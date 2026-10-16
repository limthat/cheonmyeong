using CheonMyeong.Data;
using CheonMyeong.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// ESC(일시 메뉴) - "계속하기"/"설정"/"나가기(로비로)". 와이어프레임 캡션
    /// "온라인이라 게임은 멈추지 않음"을 그대로 따른다 - 그래서 이 스크립트는
    /// Time.timeScale을 절대 건드리지 않는다(UI만 덮어 보여주는 순수 오버레이).
    ///
    /// "나가기"는 씬 분리(261013) 반영으로 호스트 쪽 Gameplay 씬은 내려주지만,
    /// 실제로 네트워크 세션 자체에서 빠져나오는 처리(NetworkManager.Shutdown 등,
    /// 중간 이탈 시 동료 쪽 처리 포함)는 아직 없음(2인 이상 네트워크 테스트 항목과
    /// 함께 나중에 추가해야 함, 확인 필요 - 임의로 끊지 않음).
    /// </summary>
    public class EscMenuScreen : MonoBehaviour
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        private void Awake()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(() => UIFlowManager.Instance.CloseOverlay());
            }
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => UIFlowManager.Instance.OpenOverlay(UIFlowManager.Settings));
            }
            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitPressed);
            }
        }

        private static void OnExitPressed()
        {
            // TODO(네트워크 세션 종료): NetworkManager.Singleton.Shutdown() 등 실제 세션
            // 이탈 처리가 필요하지만, 중간 이탈 시 동료 쪽 처리 규칙이 미정이라 세션은
            // 그대로 두고 Gameplay 씬만 내린다 - 확정되면 여기 채울 것.
            string sceneName = CurrentRun.SelectedMap != null ? CurrentRun.SelectedMap.gameplaySceneName : null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                GameplaySceneLoader.Instance?.HostUnloadGameplayScene(sceneName);
            }
            UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby);
        }
    }
}
