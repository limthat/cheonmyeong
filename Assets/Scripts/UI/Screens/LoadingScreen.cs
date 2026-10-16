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

        private void OnEnable()
        {
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

        private void OnDisable()
        {
            if (GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneLoaded -= HandleGameplaySceneLoaded;
            }
        }

        private void HandleGameplaySceneLoaded(string sceneName)
        {
            AdvanceToStory();
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
            UIFlowManager.Instance.ShowScreen(UIFlowManager.MapStory);
        }
    }
}
