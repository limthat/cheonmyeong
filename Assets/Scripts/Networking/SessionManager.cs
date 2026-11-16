using System;
using System.Threading.Tasks;
using CheonMyeong.Data;
using CheonMyeong.UI;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheonMyeong.Networking
{
    /// <summary>
    /// Multiplayer Services Session API로 호스트 세션 생성 / 참가 코드로 참가를
    /// 처리한다. WithRelayNetwork()를 쓰면 Session SDK가 내부적으로 Lobby +
    /// Relay를 묶어서 처리하고, 세션이 준비되면 NGO NetworkManager를 찾아
    /// host/client로 자동 시작해준다 - 직접 NetworkManager.StartHost() /
    /// StartClient()를 호출할 필요가 없다(공식 문서 기준, 2.4 버전).
    ///
    /// 기획 확정(261013, UI 전반 구현 1순위) - 임시 OnGUI 디버그 버튼은 제거했다.
    /// 이제 ChoiceMapScreen(방 만들기/참가 코드 입력)과 WaitRoomScreen(초대 코드
    /// 표시)이 HostAsync/JoinAsync/JoinCode를 직접 호출·참조한다.
    ///
    /// 261115 연결 끊김 보완 (기획서 4장 "연결 끊김 - 남은 사람이 혼자 계속, 나간 사람도 그때까지 주운 엽전은 받는다"):
    /// - 방 인원은 2명(maxPlayers). 판이 시작되면(게임 씬 로드 시작) 세션을 잠가 코드로 새로 못 들어오게 하고,
    ///   게임 씬을 내리면 다시 연다. 잠그기 전에 끼어든 접속은 서버가 이유와 함께 끊는다(DisconnectClient).
    /// - 호스트: 동료가 들어오거나 나가면 알림(NoticeToast). 판 중이면 "혼자 계속" 안내 - 투표 · 신통력 선택 정리는 PartyProgress가 한다.
    /// - 호스트가 '나가기'를 누르면 동료에게 이유("호스트가 방을 나갔어요")를 보낸 뒤 세션을 정리한다.
    /// - 참가자: 호스트가 나가거나 연결이 끊기면 세션을 정리하고 로비로 + 이유 알림. 엽전은 PlayerProgress.OnNetworkDespawn이 정산.
    /// - 끊김 감지 시간: UnityTransport Disconnect Timeout(씬 값 10초).
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [Tooltip("261115 - 방 최대 인원(기획서: 2인 협동).")]
        [Min(1)] [SerializeField] private int maxPlayers = 2;

        private const float ShutdownWaitSeconds = 3f;
        private const float ReasonFlushSeconds = 0.3f;

        private const string ReasonHostLeft = "호스트가 방을 나갔어요.";
        private const string ReasonRoomFull = "방이 가득 찼어요.";
        private const string ReasonInProgress = "이미 게임이 진행 중인 방이에요.";

        private ISession currentSession;
        private NetworkManager subscribedManager;
        private bool leaving;
        private bool gameplayActive;
        private GameplaySceneLoader subscribedLoader;

        /// <summary>261110 추가 - 나가기(ESC 메뉴 · 결과 화면)에서 세션을 정리하려고 찾는다.</summary>
        public static SessionManager Instance { get; private set; }

        /// <summary>참가 코드. 세션이 없으면 null - WaitRoomScreen이 매 프레임 읽어서 표시한다.</summary>
        public string JoinCode => currentSession?.Code;

        /// <summary>호스트/참가 작업이 진행 중인지 - UI가 중복 클릭을 막는 데 쓸 수 있다.</summary>
        public bool IsBusy { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] SessionManager가 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
            }
            Instance = this;
        }

        private void Start()
        {
            subscribedManager = NetworkManager.Singleton;
            if (subscribedManager != null)
            {
                subscribedManager.OnClientStopped += HandleClientStopped;
                subscribedManager.OnClientConnectedCallback += HandleClientConnected;
                subscribedManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }
            subscribedLoader = GameplaySceneLoader.Instance;
            if (subscribedLoader != null)
            {
                subscribedLoader.OnGameplaySceneLoadStarted += HandleGameplayLoadStarted;
                subscribedLoader.OnGameplaySceneUnloaded += HandleGameplayUnloaded;
            }
        }

        private void OnDestroy()
        {
            if (subscribedManager != null)
            {
                subscribedManager.OnClientStopped -= HandleClientStopped;
                subscribedManager.OnClientConnectedCallback -= HandleClientConnected;
                subscribedManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                subscribedManager = null;
            }
            if (subscribedLoader != null)
            {
                subscribedLoader.OnGameplaySceneLoadStarted -= HandleGameplayLoadStarted;
                subscribedLoader.OnGameplaySceneUnloaded -= HandleGameplayUnloaded;
                subscribedLoader = null;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 261110 추가 - "나가기"로 로비에 돌아갈 때 호출. 이전엔 Gameplay 씬만 내리고 세션 · NetworkManager가 그대로 살아 있어서
        /// 로비 → 출정 → 방 만들기에서 HostAsync가 실패했다(이미 호스트로 돌고 있음).
        /// 순서: 세션 나가기(ISession.LeaveAsync - Relay 네트워크면 NetworkManager도 멈춘다) → 아직 돌면 Shutdown →
        /// 멈출 때까지 대기 → 남은 Gameplay 씬을 이 PC에서 내린다. 호스트가 나가면 동료는 끊김 처리(HandleClientStopped)로 로비에 온다.
        /// </summary>
        public async Task LeaveAsync()
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            IsBusy = true;
            try
            {
                ISession session = currentSession;
                currentSession = null;
                gameplayActive = false;
                await DisconnectPartnersAsync(ReasonHostLeft);
                if (session != null)
                {
                    try
                    {
                        await session.LeaveAsync();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CheonMyeong] 세션 나가기(LeaveAsync) 중 오류: {e.Message}");
                    }
                }
                await ShutdownNetworkAsync();
                UnloadGameplaySceneLocally();
            }
            finally
            {
                leaving = false;
                IsBusy = false;
            }
        }

        /// <summary>호스트가 나가기 전에 동료들에게 이유를 보내고 끊는다(동료 화면에 "호스트가 방을 나갔어요"가 뜬다).</summary>
        private static async Task DisconnectPartnersAsync(string reason)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening || manager.ShutdownInProgress)
            {
                return;
            }
            bool any = false;
            // ConnectedClientsIds는 끊는 동안 바뀌므로 복사본으로 돈다(2명 방이라 할당은 작다).
            ulong[] ids = new ulong[manager.ConnectedClientsIds.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = manager.ConnectedClientsIds[i];
            }
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] != NetworkManager.ServerClientId)
                {
                    manager.DisconnectClient(ids[i], reason);
                    any = true;
                }
            }
            if (!any)
            {
                return;
            }
            // 이유 메시지가 실제로 나갈 시간을 잠깐 준다.
            float until = Time.realtimeSinceStartup + ReasonFlushSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                await Task.Yield();
            }
        }

        private static async Task ShutdownNetworkAsync()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }
            if (manager.IsListening && !manager.ShutdownInProgress)
            {
                manager.Shutdown();
            }
            float deadline = Time.realtimeSinceStartup + ShutdownWaitSeconds;
            while (manager != null && (manager.ShutdownInProgress || manager.IsListening) && Time.realtimeSinceStartup < deadline)
            {
                await Task.Yield();
            }
        }

        /// <summary>네트워크가 멈춘 뒤 남아 있는 Gameplay 씬(NGO가 로드한 추가 씬)을 이 PC에서 내린다.</summary>
        private static void UnloadGameplaySceneLocally()
        {
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        /// <summary>
        /// 내가 나가기를 누르지 않았는데 네트워크가 멈췄다(호스트가 나감 · 연결 끊김) - 세션을 정리하고 로비로.
        /// 방 만들기/참가 화면(ChoiceMap)에서 참가에 실패한 경우는 화면을 그대로 둔다.
        /// </summary>
        private void HandleClientStopped(bool wasHost)
        {
            if (leaving)
            {
                return;
            }
            // 이유는 Shutdown 정리 전에 읽어 둔다(서버가 DisconnectClient(id, reason)으로 보낸 문장).
            string reason = subscribedManager != null ? subscribedManager.DisconnectReason : null;
            _ = HandleUnexpectedStopAsync(wasHost, reason);
        }

        private async Task HandleUnexpectedStopAsync(bool wasHost, string reason)
        {
            await LeaveAsync();
            UIFlowManager flow = UIFlowManager.Instance;
            string screen = flow != null ? flow.CurrentScreen : null;
            bool joining = screen == UIFlowManager.ChoiceMap;
            if (flow != null && screen != UIFlowManager.Lobby && !joining)
            {
                flow.ShowScreen(UIFlowManager.Lobby);
            }

            if (!string.IsNullOrEmpty(reason))
            {
                NoticeToast.Show(joining ? "방에 들어가지 못했어요. " + reason : reason);
            }
            else if (!joining)
            {
                NoticeToast.Show(wasHost
                    ? "네트워크가 멈춰서 로비로 돌아왔어요."
                    : "호스트와 연결이 끊겨서 로비로 돌아왔어요.");
            }
        }

        // ---------------- 261115 호스트 쪽: 입장 · 퇴장

        private void HandleClientConnected(ulong clientId)
        {
            NetworkManager manager = subscribedManager;
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId)
            {
                return;
            }
            if (manager.ConnectedClientsIds.Count > maxPlayers)
            {
                manager.DisconnectClient(clientId, ReasonRoomFull);
                return;
            }
            if (gameplayActive)
            {
                manager.DisconnectClient(clientId, ReasonInProgress);
                return;
            }
            NoticeToast.Show("동료가 방에 들어왔어요.");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            NetworkManager manager = subscribedManager;
            if (leaving || manager == null || !manager.IsServer || manager.ShutdownInProgress || clientId == NetworkManager.ServerClientId)
            {
                return;
            }
            NoticeToast.Show(gameplayActive
                ? "동료와 연결이 끊겼어요. 혼자 계속합니다."
                : "동료가 방을 나갔어요.");
        }

        private void HandleGameplayLoadStarted(string sceneName)
        {
            gameplayActive = true;
            _ = SetSessionLockedAsync(true);
        }

        private void HandleGameplayUnloaded(string sceneName)
        {
            gameplayActive = false;
            _ = SetSessionLockedAsync(false);
        }

        /// <summary>호스트만 - 판 중엔 세션을 잠가 참가 코드로 새로 못 들어오게 한다.</summary>
        private async Task SetSessionLockedAsync(bool locked)
        {
            ISession session = currentSession;
            if (session == null || !session.IsHost)
            {
                return;
            }
            try
            {
                IHostSession host = session.AsHost();
                if (host.IsLocked == locked)
                {
                    return;
                }
                host.IsLocked = locked;
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CheonMyeong] 세션 {(locked ? "잠금" : "잠금 해제")} 실패: {e.Message}");
            }
        }

        /// <summary>이전 판의 세션 · 네트워크가 남아 있으면 먼저 정리한다(방 만들기 · 참가 실패 방지).</summary>
        private async Task LeaveStaleAsync()
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool networkRunning = manager != null && (manager.IsListening || manager.ShutdownInProgress);
            if (currentSession != null || networkRunning)
            {
                await LeaveAsync();
            }
        }

        public async Task HostAsync()
        {
            await LeaveStaleAsync();
            IsBusy = true;
            try
            {
                await GameServicesBootstrap.Instance.InitializeAsync();

                SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
                currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

                Debug.Log($"[CheonMyeong] 세션 생성 완료 (Id: {currentSession.Id}, Code: {currentSession.Code})");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task JoinAsync(string code)
        {
            await LeaveStaleAsync();
            IsBusy = true;
            try
            {
                await GameServicesBootstrap.Instance.InitializeAsync();

                currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

                Debug.Log($"[CheonMyeong] 세션 참가 완료: {currentSession.Id}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 261014 추가 - Play 모드 종료 시 콘솔에 뜨던
        /// "GameObjectsNetcodeNetworkHandler.StopAsync: Failed to stop session: session was
        /// never started" 경고 보완. 원인: 이 프로젝트 어디에도 세션을 정리하는 코드가 없어서
        /// (currentSession을 만들기만 하고 끝낼 때 아무것도 안 호출함), Play 모드를 끄면 Unity
        /// Services Multiplayer SDK가 내부적으로 자체 정리를 시도하다 이 경고를 남긴 것으로
        /// 보인다(SDK 내부 소스는 파일 접근 한계로 끝까지 확인은 못 했음 - 확인 못 함). Unity
        /// 공식 문서("Best practices for Joining and Leaving multiple Sessions")가 세션을 끝낼
        /// 때의 유일한 정석 경로로 명시한 ISession.LeaveAsync()를 OnApplicationQuit에서 호출해서
        /// 세션이 존재하면 정상적으로 정리하게 한다 - 에디터에서 Play 모드를 끌 때도, 실제
        /// 빌드에서 앱을 종료할 때도 OnApplicationQuit은 동일하게 호출된다. 메모리 누수 예방
        /// 차원에서도(세션/Relay 리소스를 정리 없이 그냥 버려두지 않음) 맞는 방향이라 적용함.
        /// 기존 HostAsync/JoinAsync 동작은 전혀 건드리지 않았다 - 순수 추가.
        /// </summary>
        private async void OnApplicationQuit()
        {
            if (currentSession == null)
            {
                return;
            }

            try
            {
                await currentSession.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CheonMyeong] 세션 정리(LeaveAsync) 중 오류: {e.Message}");
            }
            finally
            {
                currentSession = null;
            }
        }
    }
}
