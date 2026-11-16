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
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4; // 인원수 미정 - 임시값

        private const float ShutdownWaitSeconds = 3f;

        private ISession currentSession;
        private NetworkManager subscribedManager;
        private bool leaving;

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
            }
        }

        private void OnDestroy()
        {
            if (subscribedManager != null)
            {
                subscribedManager.OnClientStopped -= HandleClientStopped;
                subscribedManager = null;
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
            _ = HandleUnexpectedStopAsync();
        }

        private async Task HandleUnexpectedStopAsync()
        {
            await LeaveAsync();
            UIFlowManager flow = UIFlowManager.Instance;
            if (flow == null)
            {
                return;
            }
            string screen = flow.CurrentScreen;
            if (screen != UIFlowManager.Lobby && screen != UIFlowManager.ChoiceMap)
            {
                flow.ShowScreen(UIFlowManager.Lobby);
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
