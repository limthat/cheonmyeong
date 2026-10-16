using CheonMyeong.Data;
using CheonMyeong.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// REWARD(결과) 화면 - "봉인 성공/실패", 플레이어별 처치 수·획득 엽전, "다시
    /// 하기"(같은 맵 MapStory부터 재도전) / "나가기"(Lobby로). 와이어프레임 캡션
    /// 기준 실패해도 주운 엽전은 보존되는 규칙이지만, 재화 시스템 자체가 아직
    /// 없어서(우선순위 2번) 수치 표시는 placeholder로만 둔다.
    ///
    /// 씬 분리(261013) 반영 - 둘 다 호스트 쪽에서 지금 로드돼 있는 Gameplay 씬을
    /// 먼저 내린다(적 풀/타이머 등을 수동으로 리셋할 필요 없이 씬 자체를 내렸다 올리는
    /// 걸로 깨끗하게 초기화). "다시 하기"는 언로드가 끝나는 순간(OnGameplaySceneUnloaded)
    /// 같은 씬을 다시 로드하고, 그 로드가 끝나면 LoadingScreen이 알아서 MapStory로 넘겨준다.
    /// </summary>
    public class RewardScreen : MonoBehaviour
    {
        [SerializeField] private Text resultTitleText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button exitButton;

        private bool pendingRetryReload;

        private void Awake()
        {
            if (retryButton != null)
            {
                retryButton.onClick.AddListener(OnRetryPressed);
            }
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitPressed);
            }
        }

        private void OnEnable()
        {
            if (GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneUnloaded += HandleGameplaySceneUnloaded;
            }

            if (resultTitleText != null && string.IsNullOrEmpty(resultTitleText.text))
            {
                resultTitleText.text = "봉인 성공 (결과 판정 로직 미구현)";
            }
        }

        private void OnDisable()
        {
            if (GameplaySceneLoader.Instance != null)
            {
                GameplaySceneLoader.Instance.OnGameplaySceneUnloaded -= HandleGameplaySceneUnloaded;
            }
        }

        private void OnRetryPressed()
        {
            pendingRetryReload = true;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Loading);
            UnloadCurrentGameplayScene();
        }

        private void OnExitPressed()
        {
            pendingRetryReload = false;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
            UnloadCurrentGameplayScene();
        }

        private static void UnloadCurrentGameplayScene()
        {
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                GameplaySceneLoader.Instance?.HostUnloadGameplayScene(sceneName);
            }
        }

        private void HandleGameplaySceneUnloaded(string sceneName)
        {
            if (!pendingRetryReload)
            {
                return; // "나가기"로 내린 경우 - 다시 로드할 필요 없음
            }
            pendingRetryReload = false;

            string mapSceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(mapSceneName))
            {
                GameplaySceneLoader.Instance?.HostLoadGameplayScene(mapSceneName);
            }
        }

        /// <summary>웨이브 시스템이 생기면 실제 성공/실패 결과로 호출할 자리.</summary>
        public void SetResult(bool success)
        {
            if (resultTitleText != null)
            {
                resultTitleText.text = success ? "봉인 성공" : "봉인 실패";
            }
        }
    }
}
