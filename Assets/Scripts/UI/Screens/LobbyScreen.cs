using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// LOBBY 화면 - wireframe-reference_261013.md 참고. 상단 플레이어 카드/엽전,
    /// 좌측 스킬트리 열기, 우측 스토리 보관함 열기, 하단 "출격" 버튼, 설정 버튼.
    ///
    /// 엽전/레벨 수치는 아직 재화·경험치 시스템이 구현 전이라(우선순위 2번) 지금은
    /// 고정 placeholder 텍스트만 띄운다 - RefreshPlayerInfo()를 그 시스템이 생기면
    /// 외부에서 호출하도록 공개해뒀다.
    /// </summary>
    public class LobbyScreen : MonoBehaviour
    {
        [SerializeField] private Text playerInfoText;
        [SerializeField] private Text goldText;
        [SerializeField] private Button sortieButton;
        [SerializeField] private Button skillTreeButton;
        [SerializeField] private Button storyStorageButton;
        [SerializeField] private Button settingsButton;

        private void Awake()
        {
            if (sortieButton != null)
            {
                sortieButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.ChoiceMap));
            }
            if (skillTreeButton != null)
            {
                skillTreeButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.CharSkillTree));
            }
            if (storyStorageButton != null)
            {
                storyStorageButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.StoryStorage));
            }
            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(() => UIFlowManager.Instance.OpenOverlay(UIFlowManager.Settings));
            }
        }

        private void OnEnable()
        {
            RefreshPlayerInfo();
        }

        /// <summary>경험치/재화 시스템(우선순위 2번)이 들어오면 실제 값으로 교체할 자리.</summary>
        public void RefreshPlayerInfo()
        {
            if (playerInfoText != null)
            {
                playerInfoText.text = "Lv.-- · 하영 (경험치 시스템 미구현)";
            }
            if (goldText != null)
            {
                goldText.text = "엽전 -- (재화 시스템 미구현)";
            }
        }
    }
}
