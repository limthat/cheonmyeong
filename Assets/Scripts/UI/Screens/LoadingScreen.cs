using CheonMyeong.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// LOADING 화면 - 중앙 로고 + 하단 TIP 문구. "무엇을 기다렸다가" 다음 화면(MapStory)으로
    /// 넘어가는지는 씬 분리 작업(261013)으로 실제 신호가 생겼다 - GameplaySceneLoader가
    /// Gameplay 씬 로드를 "이 클라이언트에서" 완료했다고 알려주는 순간(OnGameplaySceneLoaded)
    /// 자동으로 MapStory로 넘어간다. 호스트/참가자 둘 다 각자 로컬로 이 이벤트를 받으므로
    /// 누가 호스트인지 몰라도 동일하게 동작한다.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        // 로딩 TIP - wireframe-reference_261013.md에 있는 문구 그대로. 원한 게이지
        // 시스템 자체는 미정이지만, 이 문구는 와이어프레임에 이미 적혀 있던 텍스트라
        // 그대로 옮겼다(새로 지어낸 게 아님).
        private static readonly string[] Tips =
        {
            "놓친 원귀는 사라지지 않는다. 원한이 되어 억쇠를 키운다."
        };

        [SerializeField] private Text tipText;
        [SerializeField] private Text progressText;
        [Tooltip("261125 - 로딩이 빨리 끝나도 이 시간(초)만큼은 로딩 화면을 보여준다.")]
        [SerializeField, Min(0f)] private float minimumShowSeconds = 3f;

        private float shownAt;
        private bool sceneReady;
        private bool advanced;

        private void OnEnable()
        {
            shownAt = Time.unscaledTime;
            sceneReady = false;
            advanced = false;
            if (tipText != null)
            {
                tipText.text = Tips[Random.Range(0, Tips.Length)];
            }
            if (progressText != null)
            {
                progressText.text = "불러오는 중...";
            }

            if (GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneLoaded += HandleGameplaySceneLoaded;
            }
        }

        private void Update()
        {
            // 261114 - 참가자가 방장보다 늦게 로딩 화면에 들어온 경우(로드 완료 이벤트가 이미 지나감) 기다리지 않고 바로 넘어간다.
            // 방장은 자기가 로드/재로드를 시작한 쪽이라 항상 이벤트를 기다린다(다시 하기 땐 옛 씬이 아직 남아 있을 수 있음).
            GameplaySceneLoader loader = GameplaySceneLoader.Instance;
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (loader != null && !string.IsNullOrEmpty(loader.LoadedGameplayScene)
                && network != null && network.IsConnectedClient && !network.IsServer)
            {
                sceneReady = true;
            }

            if (sceneReady && !advanced && Time.unscaledTime - shownAt >= minimumShowSeconds)
            {
                AdvanceToStory();
            }
        }

        private void OnDisable()
        {
            if (GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneLoaded -= HandleGameplaySceneLoaded;
            }
        }

        private void HandleGameplaySceneLoaded(string sceneName)
        {
            sceneReady = true;
        }

        /// <summary>0~1 진행률 - 실제 로딩 진행 신호가 생기면 외부에서 호출.</summary>
        public void SetProgress(float value01)
        {
            if (progressText != null)
            {
                progressText.text = $"불러오는 중... {Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
            }
        }

        /// <summary>MapStory로 전환 - 위 HandleGameplaySceneLoaded가 정상 경로로 호출하지만, 공개로 둬서 디버그/수동 전환에도 쓸 수 있게 한다.</summary>
        public void AdvanceToStory()
        {
            advanced = true;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.MapStory);
        }
    }
}
