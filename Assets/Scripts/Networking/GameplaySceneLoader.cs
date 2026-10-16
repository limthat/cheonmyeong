using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheonMyeong.Networking
{
    /// <summary>
    /// 씬 분리(261013, 예섭 지시) - 로비/대기실류 UI는 이 오브젝트가 있는 Bootstrap 씬에
    /// 영구히 두고, 실제 플레이 내용(적 풀/스포너/맵 지형)은 맵별 Gameplay 씬으로 분리해
    /// 대기실 "START"에서 호스트가 추가 로드(Additive)한다.
    ///
    /// NGO API 확인(공식 문서, netcode 2.x 기준, 2026-10-13 웹 검색으로 확인):
    /// NetworkSceneManager.LoadScene(sceneName, LoadSceneMode)/UnloadScene(Scene)은 둘 다
    /// SceneEventProgressStatus를 반환하고, 실제 완료 시점은 개별 델리게이트가 아니라
    /// NetworkSceneManager.OnSceneEvent(SceneEvent) 콜백 하나로 전부 들어온다 - 그 안의
    /// SceneEvent.SceneEventType이 LoadEventCompleted/UnloadEventCompleted일 때가
    /// "접속해 있는 모든 클라이언트가 전부 끝낸" 시점이다(SceneEvent.ClientsThatCompleted
    /// 참고). 이 콜백은 호스트/클라이언트 양쪽 모두에서 각자 로컬로 발생하므로, 이 스크립트가
    /// 양쪽 모두에 떠 있기만 하면(Bootstrap 씬에 있으니 당연히 그럼) 서버/클라이언트 구분 없이
    /// 똑같이 "지금 내 화면에서 Loading→MapStory로 넘어가도 된다"는 신호로 쓸 수 있다.
    ///
    /// NetworkManager의 Inspector "Enable Scene Management" 체크박스가 꺼져 있으면 이 전부가
    /// 동작하지 않는다 - GameplaySceneSplitSetup 에디터 메뉴가 이 값을 true로 맞춰준다.
    /// Build Settings(Scenes In Build)에 Gameplay 씬이 등록돼 있어야 하는 것도 확인됨 -
    /// 등록 안 돼 있으면 LoadScene이 InvalidSceneName 상태를 반환한다(아래 에러 로그 참고).
    /// </summary>
    public class GameplaySceneLoader : MonoBehaviour
    {
        public static GameplaySceneLoader Instance { get; private set; }

        /// <summary>이 씬의 로드가 "내 쪽에서" 완료됐을 때(호스트/클라이언트 각자 로컬로 발생).</summary>
        public event Action<string> OnGameplaySceneLoaded;
        /// <summary>이 씬의 언로드가 완료됐을 때.</summary>
        public event Action<string> OnGameplaySceneUnloaded;

        // NetworkManager.SceneManager(NetworkSceneManager)는 NetworkManager가 실제로
        // 시작(StartHost/StartClient/StartServer)된 뒤에야 생성된다(공식 문서 확인, 261013 수정) -
        // 그 전에는 null이라, 씬이 로드되자마자 도는 OnEnable에서 곧바로 구독하면
        // NullReferenceException이 난다(실기 테스트로 확인됨). 그래서 OnEnable에서 바로
        // 구독하지 않고, IsListening이 true가 되는 순간(호스트/클라이언트 공통)까지 기다렸다가
        // 딱 한 번만 구독한다.
        private bool isSubscribedToSceneEvents;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] GameplaySceneLoader가 씬에 두 개 이상 있습니다 - 하나만 남기고 제거하세요.");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (listening && !isSubscribedToSceneEvents)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
                isSubscribedToSceneEvents = true;
            }
            else if (!listening && isSubscribedToSceneEvents)
            {
                // 호스트/클라이언트가 멈췄다 - 이미 사라진 SceneManager에서 구독 해제를
                // 시도하면 또 NRE가 날 수 있어 시도하지 않는다(NGO가 셧다운 시 내부적으로
                // 정리함). 플래그만 내려서 다음에 다시 시작되면 재구독되게 한다.
                isSubscribedToSceneEvents = false;
            }
        }

        private void OnDisable()
        {
            if (isSubscribedToSceneEvents && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            isSubscribedToSceneEvents = false;
        }

        /// <summary>호스트 전용 - Gameplay 씬을 추가(Additive) 로드한다. 참가자가 호출하면 무시됨.</summary>
        public void HostLoadGameplayScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !IsHostReady())
            {
                return;
            }

            if (SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                Debug.LogWarning($"[CheonMyeong] '{sceneName}'은 이미 로드돼 있어서 다시 로드하지 않습니다 - 먼저 언로드부터 하세요.");
                return;
            }

            SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[CheonMyeong] '{sceneName}' 로드를 시작하지 못했습니다: {status} - " +
                                "Build Settings(Scenes In Build)에 이 씬이 등록돼 있는지 확인하세요.");
            }
        }

        /// <summary>호스트 전용 - 로드돼 있는 Gameplay 씬을 내린다(다시 하기/나가기에서 사용). 참가자가 호출하면 무시됨.</summary>
        public void HostUnloadGameplayScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !IsHostReady())
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.isLoaded)
            {
                return; // 이미 내려가 있으면 조용히 무시 - 중복 호출 방어
            }

            SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.UnloadScene(scene);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[CheonMyeong] '{sceneName}' 언로드를 시작하지 못했습니다: {status}");
            }
        }

        private static bool IsHostReady()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
            {
                // 참가자(클라이언트)는 씬 로드/언로드 권한이 없다 - 정상적인 상황이라 경고까지만.
                Debug.LogWarning("[CheonMyeong] 씬 로드/언로드는 호스트(서버)만 할 수 있습니다.");
                return false;
            }
            return true;
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.LoadEventCompleted:
                    OnGameplaySceneLoaded?.Invoke(sceneEvent.SceneName);
                    break;
                case SceneEventType.UnloadEventCompleted:
                    OnGameplaySceneUnloaded?.Invoke(sceneEvent.SceneName);
                    break;
            }
        }
    }
}
