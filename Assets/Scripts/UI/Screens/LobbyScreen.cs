using CheonMyeong.Data;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// LOBBY 화면 - wireframe-reference_261013.md 참고. 상단 플레이어 카드/엽전,
    /// 좌측 스킬트리 열기, 우측 스토리 보관함 열기, 하단 "출격" 버튼, 설정 버튼.
    /// 중앙 로비 마당에서는 선택된 캐릭터를 움직일 수 있다(LobbyAvatar).
    /// 261101 - 스킬트리/스토리/출격 버튼 대신 건물(LobbyBuilding)로 연다. 버튼 필드는 비어 있으면 건너뛴다.
    ///
    /// 261027 수정 - 플레이어 고유 레벨은 없다(예섭 확정 - 레벨은 판 안의 공유 레벨뿐)라 상단 카드는
    /// 선택된 캐릭터 이름만 보여준다. 엽전은 판 밖 지갑(PlayerWallet) 값을 그대로 보여주고, 값이
    /// 바뀌면(판 종료 입금, 해금/노드 구매) 이벤트로 바로 갱신한다. 엽전 아이콘이 옆에 있어 숫자만 표시한다. characterCaptionText/portraitImage는 선택 사항(비어 있으면 건너뜀).
    /// </summary>
    public class LobbyScreen : MonoBehaviour
    {
        [SerializeField] private Text playerInfoText;
        [SerializeField] private Text characterCaptionText;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Text goldText;
        [SerializeField] private Button sortieButton;
        [SerializeField] private Button skillTreeButton;
        [SerializeField] private Button storyStorageButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private CharacterCatalogSO characterCatalog;

        [Header("261101 - 건물 로비 (큰 전각 = 게임 시작, 상점 = 스킬 · 캐릭터, 게시판 = 스토리 저장소)")]
        [SerializeField] private LobbyBuilding sortieBuilding;
        [SerializeField] private LobbyBuilding skillTreeBuilding;
        [SerializeField] private LobbyBuilding storyStorageBuilding;

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

            // 건물은 이 화면의 자식이라 수명이 같다 - 따로 구독 해제하지 않아도 남는 참조가 없다.
            if (sortieBuilding != null)
            {
                sortieBuilding.Activated += OpenChoiceMap;
            }
            if (skillTreeBuilding != null)
            {
                skillTreeBuilding.Activated += OpenSkillTree;
            }
            if (storyStorageBuilding != null)
            {
                storyStorageBuilding.Activated += OpenStoryStorage;
            }
        }

        private static void OpenChoiceMap()
        {
            UIFlowManager.Instance.ShowScreen(UIFlowManager.ChoiceMap);
        }

        private static void OpenSkillTree()
        {
            UIFlowManager.Instance.ShowScreen(UIFlowManager.CharSkillTree);
        }

        private static void OpenStoryStorage()
        {
            UIFlowManager.Instance.ShowScreen(UIFlowManager.StoryStorage);
        }

        private void OnEnable()
        {
            PlayerWallet.YeopjeonChanged += HandleYeopjeonChanged;
            RefreshPlayerInfo();
        }

        private void OnDisable()
        {
            PlayerWallet.YeopjeonChanged -= HandleYeopjeonChanged;
        }

        public void RefreshPlayerInfo()
        {
            CurrentRun.EnsureCharacterSelected(characterCatalog);
            CharacterCatalogSO.CharacterEntry selected = CurrentRun.SelectedCharacter;
            if (playerInfoText != null)
            {
                playerInfoText.text = selected != null ? selected.displayName : "하영";
            }
            if (characterCaptionText != null)
            {
                characterCaptionText.text = selected != null ? selected.weaponCaption : string.Empty;
            }
            if (portraitImage != null)
            {
                Sprite portrait = selected != null && selected.lobbyIdleFrames != null && selected.lobbyIdleFrames.Length > 0
                    ? selected.lobbyIdleFrames[0]
                    : null;
                portraitImage.sprite = portrait;
                portraitImage.enabled = portrait != null;
            }
            HandleYeopjeonChanged(PlayerWallet.Yeopjeon);
        }

        private void HandleYeopjeonChanged(int amount)
        {
            if (goldText != null)
            {
                goldText.text = amount.ToString("N0");
            }
        }
    }
}
