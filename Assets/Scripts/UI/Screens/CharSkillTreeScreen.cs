using System.Collections.Generic;
using System.Text;
using CheonMyeong.Data;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// 영웅(팀원 모집) · 신내림 화면 - 기획서 v0.9.7(261113) 12장 "영웅 구매 · 신내림 (두 화면으로 분리)".
    /// 로비 건물마다 같은 화면을 모드로 나눠 쓴다(LobbyScreen이 열기 직전에 RequestedMode를 정한다).
    ///
    /// 영웅(Recruit):
    /// - 왼쪽 8명 목록: 이름 · 무기 · "보유" 또는 해금 엽전.
    /// - 가운데 큰 그림(대기 모션) + 이름판, 오른쪽 두루마리: 기본 공격 미리보기(CharacterPreviewStage) · 고유 패시브 ·
    ///   전용 신통력 · 능력치, 맨 아래 확정 버튼 - 잠겨 있으면 "해금 · 엽전 N", 보유 중이면 "선택하기", 쓰는 중이면 "사용 중".
    /// - 선택하기 = CurrentRun.SelectCharacter(PlayerPrefs에도 저장) → 이미 접속해 있으면 내 캐릭터도 바로 바뀐다(PlayerCharacter).
    ///
    /// 신내림(SkillTree):
    /// - 가운데 노드 11칸(아래 기본 → 선봉 · 수호 · 유격 각 2칸 → 관문 → 택1 A · B → 위 각성). 아트 TreePanel의 칸 그림에 버튼을 붙인다.
    /// - 오른쪽 상세: 칸 이름 · 나 · 동료 효과 · 필요 엽전 · "열기 · N". 규칙은 CharacterProgressStore.StateOf.
    /// 캐릭터 · 노드 데이터는 전부 CharacterCatalog(기획서 6장)에서 읽는다 - 이 스크립트에 이름 · 수치 하드코딩 없음.
    /// 매 프레임 할당 없음(그림 애니메이션만 Update).
    /// </summary>
    public class CharSkillTreeScreen : MonoBehaviour
    {
        public enum Mode
        {
            Recruit,
            SkillTree
        }

        /// <summary>다음에 이 화면이 열릴 때 쓸 모드. LobbyScreen이 열기 직전에 정한다.</summary>
        public static Mode RequestedMode = Mode.Recruit;

        private static readonly Color Dark = new Color(0.18f, 0.1f, 0.06f, 1f);
        private static readonly Color Red = new Color(0.62f, 0.12f, 0.12f, 1f);
        private static readonly Color DisabledLabel = new Color(0.42f, 0.4f, 0.38f, 1f);
        // 노드 테 · 아이콘 같이 곱한다. 연 칸만 원래 색, 안 연 칸은 한눈에 어둡게(선택 표시는 색 대신 금색 테두리 - StringArtGraphic).
        private static readonly Color NodeOpen = Color.white;
        private static readonly Color NodeAvailable = new Color(0.6f, 0.58f, 0.55f, 1f);
        private static readonly Color NodeLocked = new Color(0.36f, 0.35f, 0.34f, 1f);
        private static readonly Color NodeBranchLocked = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color RosterLockedTint = new Color(0.75f, 0.72f, 0.68f, 1f);

        // 아트 TreePanel 칸 이름 → 신내림 칸(SinnaerimSlot 순서).
        private static readonly string[] TreeNodeNames =
        {
            "Base", "Image (1)", "Image (6)", "Image", "Image (5)", "Image (3)", "Image (4)", "Image (7)", "Image (8)", "Image (9)", "Image (10)"
        };

        // 신내림 칸 연결(아래 → 위). 실(StringArtGraphic)이 이 순서로 노드를 잇는다 - 규칙은 CharacterProgressStore.StateOf와 같다.
        private static readonly int[] EdgeFrom = { 0, 0, 0, 1, 3, 5, 2, 4, 6, 7, 7, 8, 9 };
        private static readonly int[] EdgeTo = { 1, 3, 5, 2, 4, 6, 7, 7, 7, 8, 9, 10, 10 };

        // 노드 테 바깥 못 자리(아트 Frame-Sheet_7~11: 150 칸에 보이는 테 반폭 51).
        private const float PegRadius = 55f;

        // 근접 = 금줄(짚), 원거리 = 오방색 끈(청 · 적 · 황 · 백 · 흑).
        private static readonly Color32[] StrawPalette =
        {
            new Color32(201, 160, 74, 255), new Color32(139, 101, 44, 255), new Color32(230, 200, 120, 255)
        };

        private static readonly Color32[] ObangPalette =
        {
            new Color32(16, 54, 150, 255), new Color32(178, 18, 24, 255), new Color32(232, 168, 0, 255),
            new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255)
        };

        private static readonly string[] SlotTitles =
        {
            "기본", "선봉 갈래 · 1칸", "선봉 갈래 · 2칸", "수호 갈래 · 1칸", "수호 갈래 · 2칸", "유격 갈래 · 1칸", "유격 갈래 · 2칸",
            "관문", "택1 · A", "택1 · B", "각성"
        };

        [SerializeField] private CharacterCatalogSO catalog;
        [SerializeField] private Button backButton;
        [SerializeField] private Transform rosterContainer;
        [SerializeField] private Text previewNameText;
        [SerializeField] private Text previewDescText;

        [Tooltip("팀원 모집 모드에서만 보이는 것. 비워두면 이름으로 찾는다(PreviewPlaceholder, RecruitPanel).")]
        [SerializeField] private GameObject[] recruitOnly;

        [Tooltip("스킬트리 모드에서만 보이는 것. 비워두면 이름으로 찾는다(SkillTreeContainer).")]
        [SerializeField] private GameObject[] skillTreeOnly;

        private Text titleText;
        private CharacterPreviewStage previewStage;
        private Image heroImage;
        private Text heroInfoText;
        private Button confirmButton;
        private Text confirmLabel;
        private Text walletText;

        private readonly List<Text> rosterLabels = new List<Text>(8);
        private readonly List<Image> rosterImages = new List<Image>(8);
        private readonly Button[] nodeButtons = new Button[CharacterCatalogSO.SinnaerimSlotCount];
        private readonly Image[] nodeImages = new Image[CharacterCatalogSO.SinnaerimSlotCount];
        private readonly Image[] nodeIcons = new Image[CharacterCatalogSO.SinnaerimSlotCount];
        private readonly byte[] nodeArtStates = new byte[CharacterCatalogSO.SinnaerimSlotCount];
        private StringArtGraphic linkArt;
        private StringArtGraphic wrapArt;
        private string artCharacterId;
        private int artMask = -1;
        private Text nodeTitleText;
        private Text nodeBodyText;
        private Button openNodeButton;
        private Text openNodeLabel;

        private int selectedIndex;
        private int selectedSlot;
        private float heroAnimTime;
        // 두루마리(DescScroll, 1080 높이에 256px 그림) 아래 줄(행 224~226) 위로 12 띄운 자리.
        private const float ScrollContentBottom = 166f;

        private readonly StringBuilder builder = new StringBuilder(256);
        private Font font;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby));
            }
            if (recruitOnly == null || recruitOnly.Length == 0)
            {
                recruitOnly = FindChildren("PreviewPlaceholder", "RecruitPanel");
            }
            if (skillTreeOnly == null || skillTreeOnly.Length == 0)
            {
                skillTreeOnly = FindChildren("SkillTreeContainer");
            }
            if (catalog == null)
            {
                LobbyScreen lobby = FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
                catalog = lobby != null ? lobby.Catalog : null;
            }

            titleText = FindDeepComponent<Text>(transform, "TitleText");
            previewStage = GetComponentInChildren<CharacterPreviewStage>(true);
            heroImage = FindDeepComponent<Image>(transform, "HeroImage");
            Text sample = previewDescText != null ? previewDescText : FindDeepComponent<Text>(transform, "PreviewDesc");
            font = sample != null ? sample.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BindRoster();
            BuildRecruitInfo();
            BuildTree();
        }

        private void OnEnable()
        {
            CharacterProgressStore.Changed += HandleProgressChanged;
            PlayerWallet.YeopjeonChanged += HandleWalletChanged;
            bool recruit = RequestedMode == Mode.Recruit;
            SetActive(recruitOnly, recruit);
            SetActive(skillTreeOnly, !recruit);
            CurrentRun.EnsureCharacterSelected(catalog);
            int current = catalog != null ? catalog.IndexOf(CurrentRun.SelectedCharacter) : 0;
            SelectCharacter(current >= 0 ? current : 0);
        }

        private void OnDisable()
        {
            CharacterProgressStore.Changed -= HandleProgressChanged;
            PlayerWallet.YeopjeonChanged -= HandleWalletChanged;
            CharacterProgressStore.FocusedCharacterId = null;
        }

        private void HandleProgressChanged(string characterId)
        {
            CharacterCatalogSO.CharacterEntry entry = Selected;
            if (entry != null && entry.characterId == characterId && CharacterProgressStore.SinnaerimMask(characterId) == 0)
            {
                selectedSlot = 0; // 초기화되면 기본 칸부터 다시
                artMask = -1; // 감는 연출 없이 실 상태만 다시 그린다
            }
            Refresh();
        }

        private void HandleWalletChanged(int value) => Refresh();

        private CharacterCatalogSO.CharacterEntry Selected => catalog != null ? catalog.At(selectedIndex) : null;

        private void OnDestroy()
        {
            CharacterProgressStore.FocusedCharacterId = null;
        }

        // ───────── 목록 ─────────

        private void BindRoster()
        {
            if (rosterContainer == null)
            {
                return;
            }
            for (int i = 0; i < rosterContainer.childCount; i++)
            {
                Transform item = rosterContainer.GetChild(i);
                int captured = i;
                if (item.TryGetComponent(out Button button))
                {
                    button.interactable = true;
                    button.onClick.AddListener(() => SelectCharacter(captured));
                }
                Text label = FindDeepComponent<Text>(item, "Label");
                if (label != null)
                {
                    // 목록 칸(Frame-Sheet_2, 400 폭): 초상화 옆 세로줄(x≈121) ~ 오른쪽 안쪽 선(x≈390) 사이에 여백을 두고
                    // 1줄 이름 · 2줄 무기와 상태로 쓴다. 한 줄로 이으면 "엽전 1,200"이 오른쪽 선에 닿았다.
                    label.supportRichText = true;
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    label.resizeTextForBestFit = false;
                    label.fontSize = 24;
                    label.alignment = TextAnchor.MiddleLeft;
                    var labelRect = (RectTransform)label.transform;
                    labelRect.anchorMin = new Vector2(0f, labelRect.anchorMin.y);
                    labelRect.anchorMax = new Vector2(1f, labelRect.anchorMax.y);
                    labelRect.offsetMin = new Vector2(140f, labelRect.offsetMin.y);
                    labelRect.offsetMax = new Vector2(-30f, labelRect.offsetMax.y);
                }
                rosterLabels.Add(label);
                item.TryGetComponent(out Image image);
                rosterImages.Add(image);
            }
        }

        private void RefreshRoster()
        {
            for (int i = 0; i < rosterLabels.Count; i++)
            {
                CharacterCatalogSO.CharacterEntry entry = catalog != null ? catalog.At(i) : null;
                if (entry == null)
                {
                    continue;
                }
                bool owned = CharacterProgressStore.IsUnlocked(entry);
                bool inUse = entry == CurrentRun.SelectedCharacter;
                if (rosterLabels[i] != null)
                {
                    builder.Clear();
                    builder.Append("<b>").Append(entry.displayName).Append("</b>\n<size=18>");
                    builder.Append(WeaponOf(entry)).Append(" · ");
                    if (inUse)
                    {
                        builder.Append("<color=#9e1f1f>사용 중</color>");
                    }
                    else if (owned)
                    {
                        builder.Append("<color=#1f3f8f>보유</color>");
                    }
                    else if (RequestedMode == Mode.Recruit)
                    {
                        builder.Append("엽전 ").Append(entry.unlockPrice.ToString("N0"));
                    }
                    else
                    {
                        builder.Append("<color=#6b6258>미해금</color>");
                    }
                    builder.Append("</size>");
                    rosterLabels[i].text = builder.ToString();
                }
                if (rosterImages[i] != null)
                {
                    Color tint = owned ? Color.white : RosterLockedTint;
                    if (i == selectedIndex)
                    {
                        tint = new Color(1f, 0.93f, 0.72f, 1f);
                    }
                    rosterImages[i].color = tint;
                }
            }
        }

        private static string WeaponOf(CharacterCatalogSO.CharacterEntry entry)
        {
            string caption = entry.weaponCaption ?? string.Empty;
            int dot = caption.IndexOf('·');
            return dot > 0 ? caption.Substring(0, dot).Trim() : caption;
        }

        private void SelectCharacter(int index)
        {
            if (catalog == null || catalog.At(index) == null)
            {
                return;
            }
            selectedIndex = index;
            CharacterProgressStore.FocusedCharacterId = catalog.At(index).characterId;
            selectedSlot = FirstInterestingSlot();
            heroAnimTime = 0f;
            if (previewStage != null)
            {
                previewStage.Show(index);
            }
            Refresh();
        }

        private void Refresh()
        {
            CharacterCatalogSO.CharacterEntry entry = Selected;
            if (entry == null)
            {
                return;
            }
            RefreshRoster();
            bool recruit = RequestedMode == Mode.Recruit;
            if (titleText != null)
            {
                titleText.text = recruit ? "영웅" : "신내림";
            }
            if (previewNameText != null)
            {
                previewNameText.text = entry.displayName + " · " + WeaponOf(entry);
            }
            if (recruit)
            {
                RefreshRecruitInfo(entry);
            }
            else
            {
                RefreshTree(entry);
            }
        }

        // ───────── 영웅(팀원 모집) ─────────

        private void BuildRecruitInfo()
        {
            Transform panel = transform.Find("RecruitPanel");
            if (panel == null)
            {
                return;
            }
            // 오른쪽 두루마리 종이 안(아래 청 · 홍 줄 y≈141~155 바로 위)에 위에서부터 능력치 → 보유 엽전 → 확정 버튼(아트 좌표 1920×1080).
            heroInfoText = CreateText(panel, "HeroInfo", new Vector2(1318f, ScrollContentBottom + 96f), new Vector2(470f, 130f), 19, TextAnchor.UpperLeft);
            walletText = CreateText(panel, "Wallet", new Vector2(1318f, ScrollContentBottom + 64f), new Vector2(470f, 28f), 18, TextAnchor.MiddleCenter);
            Button template = FindDeepComponent<Button>(panel, "AttackTab");
            confirmButton = CreateButton(panel, template, "ConfirmButton", new Vector2(1388f, ScrollContentBottom), new Vector2(330f, 60f), out confirmLabel);
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(OnConfirmClicked);
            }
        }

        private void RefreshRecruitInfo(CharacterCatalogSO.CharacterEntry entry)
        {
            CharacterStatsSO stats = entry.stats;
            if (heroInfoText != null)
            {
                builder.Clear();
                builder.Append("<color=#9e1f1f><b>고유 패시브</b></color> ").Append(entry.passiveName).Append('\n');
                builder.Append("<color=#9e1f1f><b>전용 신통력</b></color> ");
                if (entry.exclusiveNames != null)
                {
                    for (int i = 0; i < entry.exclusiveNames.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(" · ");
                        }
                        builder.Append(entry.exclusiveNames[i]);
                    }
                }
                builder.Append('\n');
                if (stats != null)
                {
                    builder.Append("<color=#9e1f1f><b>능력치</b></color> 체력 ").Append(stats.maxHealth.ToString("0"))
                        .Append(" · 공격력 ").Append(stats.attackPower.ToString("0"))
                        .Append(" · 이동 ").Append((stats.moveSpeed * 60f).ToString("0")).Append('\n')
                        .Append("공속 ").Append(stats.attacksPerSecond.ToString("0.##")).Append("회/초 · 사거리 ")
                        .Append(stats.attackRange.ToString("0.#")).Append("칸 · 치명 ").Append((stats.critChance * 100f).ToString("0")).Append('%');
                }
                heroInfoText.text = builder.ToString();
            }

            bool owned = CharacterProgressStore.IsUnlocked(entry);
            bool inUse = entry == CurrentRun.SelectedCharacter;
            int wallet = PlayerWallet.Yeopjeon;
            if (confirmLabel != null)
            {
                confirmLabel.text = inUse ? "사용 중" : owned ? "선택하기" : "해금 · 엽전 " + entry.unlockPrice.ToString("N0");
            }
            bool canConfirm = !inUse && (owned || wallet >= entry.unlockPrice);
            if (confirmButton != null)
            {
                confirmButton.interactable = canConfirm;
            }
            if (confirmLabel != null)
            {
                confirmLabel.color = canConfirm ? Red : DisabledLabel;
            }
            if (walletText != null)
            {
                walletText.text = "보유 엽전 " + wallet.ToString("N0") + (owned || wallet >= entry.unlockPrice ? string.Empty : " · 엽전이 모자랍니다");
            }
        }

        private void OnConfirmClicked()
        {
            CharacterCatalogSO.CharacterEntry entry = Selected;
            if (entry == null)
            {
                return;
            }
            if (!CharacterProgressStore.IsUnlocked(entry))
            {
                if (PlayerWallet.TrySpendYeopjeon(entry.unlockPrice))
                {
                    CharacterProgressStore.MarkUnlocked(entry);
                }
                Refresh();
                return;
            }
            CurrentRun.SelectCharacter(entry);
            Refresh();
        }

        private void Update()
        {
            // 가운데 큰 그림 - 대기 모션(IDLE 100ms).
            CharacterCatalogSO.CharacterEntry entry = Selected;
            if (heroImage == null || entry == null || entry.lobbyIdleFrames == null || entry.lobbyIdleFrames.Length == 0)
            {
                return;
            }
            heroAnimTime += Time.unscaledDeltaTime;
            Sprite frame = entry.lobbyIdleFrames[(int)(heroAnimTime * 10f) % entry.lobbyIdleFrames.Length];
            if (heroImage.sprite != frame)
            {
                heroImage.sprite = frame;
            }
            Color tint = CharacterProgressStore.IsUnlocked(entry) ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);
            if (heroImage.color != tint)
            {
                heroImage.color = tint;
            }
        }

        // ───────── 신내림 ─────────

        private void BuildTree()
        {
            Transform container = transform.Find("SkillTreeContainer");
            Transform tree = container != null ? container.Find("TreePanel") : null;
            if (tree == null)
            {
                return;
            }
            var centers = new Vector2[CharacterCatalogSO.SinnaerimSlotCount];
            for (int i = 0; i < TreeNodeNames.Length; i++)
            {
                Transform node = tree.Find(TreeNodeNames[i]);
                if (node == null || !node.TryGetComponent(out Image image))
                {
                    continue;
                }
                image.raycastTarget = true;
                nodeImages[i] = image;
                for (int c = 0; c < node.childCount; c++)
                {
                    if (node.GetChild(c).TryGetComponent(out Image icon))
                    {
                        nodeIcons[i] = icon;
                        icon.raycastTarget = false;
                        break;
                    }
                }
                Button button = node.GetComponent<Button>();
                if (button == null)
                {
                    button = node.gameObject.AddComponent<Button>();
                }
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                int slot = i;
                button.onClick.AddListener(() => SelectSlot(slot));
                nodeButtons[i] = button;
                var rect = (RectTransform)node;
                centers[i] = rect.anchoredPosition;
                rect.SetAsLastSibling();
            }

            // 실: 잇는 실은 노드 아래, 감는 실 · 못은 노드 위. 노드 글자는 아이콘과 겹쳐서 빼고 이름은 오른쪽 상세에만 쓴다.
            linkArt = CreateStringArt(tree, "StringLinks", StringArtGraphic.Layer.Links, centers);
            linkArt.transform.SetAsFirstSibling();
            wrapArt = CreateStringArt(tree, "StringWraps", StringArtGraphic.Layer.Wraps, centers);
            wrapArt.transform.SetAsLastSibling();

            // 오른쪽 상세 두루마리 - 영웅 화면과 같은 두루마리 그림을 복제해서 쓴다.
            Transform scrollSource = transform.Find("RecruitPanel/DescScroll");
            if (scrollSource != null)
            {
                GameObject scroll = Instantiate(scrollSource.gameObject, container, false);
                scroll.name = "NodeDetailScroll";
                scroll.SetActive(true);
                if (scroll.TryGetComponent(out Image scrollImage))
                {
                    scrollImage.raycastTarget = false;
                }
            }
            nodeTitleText = CreateText(container, "NodeTitle", new Vector2(1318f, 815f), new Vector2(470f, 135f), 26, TextAnchor.UpperLeft);
            nodeBodyText = CreateText(container, "NodeBody", new Vector2(1318f, ScrollContentBottom + 96f), new Vector2(470f, 545f), 21, TextAnchor.UpperLeft);
            walletTextTree = CreateText(container, "Wallet", new Vector2(1318f, ScrollContentBottom + 64f), new Vector2(470f, 28f), 18, TextAnchor.MiddleCenter);
            Button template = FindDeepComponent<Button>(transform, "AttackTab");
            openNodeButton = CreateButton(container, template, "OpenNodeButton", new Vector2(1388f, ScrollContentBottom), new Vector2(330f, 60f), out openNodeLabel);
            if (openNodeButton != null)
            {
                openNodeButton.onClick.AddListener(OnOpenNodeClicked);
            }
        }

        private Text walletTextTree;

        private int FirstInterestingSlot()
        {
            CharacterCatalogSO.CharacterEntry entry = Selected;
            if (entry == null)
            {
                return 0;
            }
            int mask = CharacterProgressStore.SinnaerimMask(entry.characterId);
            for (int i = 0; i < CharacterCatalogSO.SinnaerimSlotCount; i++)
            {
                if (CharacterProgressStore.StateOf(mask, (CharacterCatalogSO.SinnaerimSlot)i) == CharacterProgressStore.NodeState.Available)
                {
                    return i;
                }
            }
            return 0;
        }

        private void SelectSlot(int slot)
        {
            selectedSlot = slot;
            Refresh();
        }

        private void RefreshTree(CharacterCatalogSO.CharacterEntry entry)
        {
            bool owned = CharacterProgressStore.IsUnlocked(entry);
            int mask = CharacterProgressStore.SinnaerimMask(entry.characterId);
            for (int i = 0; i < CharacterCatalogSO.SinnaerimSlotCount; i++)
            {
                if (nodeImages[i] == null)
                {
                    nodeArtStates[i] = StringArtGraphic.NodeHidden;
                    continue;
                }
                var state = CharacterProgressStore.StateOf(mask, (CharacterCatalogSO.SinnaerimSlot)i);
                nodeArtStates[i] = state == CharacterProgressStore.NodeState.Open ? StringArtGraphic.NodeOpen
                    : state == CharacterProgressStore.NodeState.BranchLocked ? StringArtGraphic.NodeBlocked
                    : StringArtGraphic.NodeClosed;
                Color color = state == CharacterProgressStore.NodeState.Open ? NodeOpen
                    : state == CharacterProgressStore.NodeState.Available ? NodeAvailable
                    : state == CharacterProgressStore.NodeState.BranchLocked ? NodeBranchLocked
                    : NodeLocked;
                nodeImages[i].color = color;
                if (nodeIcons[i] != null)
                {
                    nodeIcons[i].color = color;
                }
            }
            RefreshStringArt(entry, mask);

            CharacterCatalogSO.SinnaerimNode selected = NodeOf(entry, selectedSlot);
            var selectedState = CharacterProgressStore.StateOf(mask, (CharacterCatalogSO.SinnaerimSlot)selectedSlot);
            if (nodeTitleText != null)
            {
                builder.Clear();
                builder.Append("<size=20>").Append(entry.displayName).Append("의 신내림</size>\n");
                builder.Append("<color=#9e1f1f><b>").Append(SlotTitles[selectedSlot]).Append("</b></color>\n");
                builder.Append(selected != null ? selected.nodeName : string.Empty);
                nodeTitleText.text = builder.ToString();
            }
            int wallet = PlayerWallet.Yeopjeon;
            bool shortOfMoney = selected != null && wallet < selected.cost;
            if (nodeBodyText != null && selected != null)
            {
                builder.Clear();
                builder.Append("<b>나</b>\n").Append(selected.selfEffect).Append("\n\n");
                builder.Append("<b>동료</b>\n").Append(string.IsNullOrEmpty(selected.allyEffect) ? "-" : selected.allyEffect).Append("\n\n");
                builder.Append("<b>필요 엽전</b>  ").Append(selected.cost.ToString("N0")).Append("\n\n<size=17>");
                switch (selectedState)
                {
                    case CharacterProgressStore.NodeState.Open:
                        builder.Append("이미 열었다.");
                        break;
                    case CharacterProgressStore.NodeState.BranchLocked:
                        builder.Append("갈래 · 택1은 하나만 고를 수 있다. 다른 쪽을 골라 잠겼다.");
                        break;
                    case CharacterProgressStore.NodeState.NeedsPrevious:
                        builder.Append("아래 칸을 먼저 열어야 한다.");
                        break;
                    default:
                        if (!owned)
                        {
                            builder.Append("영웅 화면에서 먼저 해금해야 한다.");
                        }
                        else if (shortOfMoney)
                        {
                            builder.Append("<color=#9e1f1f>엽전이 ").Append((selected.cost - wallet).ToString("N0")).Append(" 모자란다.</color>");
                        }
                        else
                        {
                            builder.Append("갈래(선봉 · 수호 · 유격)와 택1(A · B)은 하나만 고를 수 있다.");
                        }
                        break;
                }
                builder.Append("</size>");
                nodeBodyText.text = builder.ToString();
            }
            bool canOpen = owned && selected != null && selectedState == CharacterProgressStore.NodeState.Available && !shortOfMoney;
            if (openNodeLabel != null)
            {
                // 못 여는 이유를 버튼 글자로 바로 보여준다(예전엔 "열기 · N" 그대로 회색만 돼서 왜 안 되는지 몰랐음).
                string cost = selected != null ? selected.cost.ToString("N0") : "-";
                openNodeLabel.text = selectedState == CharacterProgressStore.NodeState.Open ? "열림"
                    : selectedState == CharacterProgressStore.NodeState.BranchLocked ? "고를 수 없음"
                    : selectedState == CharacterProgressStore.NodeState.NeedsPrevious ? "아래 칸 먼저"
                    : !owned ? "영웅 해금 필요"
                    : shortOfMoney ? "엽전 부족 · " + cost
                    : "열기 · " + cost;
                openNodeLabel.color = canOpen ? Red : DisabledLabel;
            }
            if (openNodeButton != null)
            {
                openNodeButton.interactable = canOpen;
            }
            if (walletTextTree != null)
            {
                walletTextTree.text = "보유 엽전 " + wallet.ToString("N0");
            }
        }

        /// <summary>
        /// 실 상태 갱신. 같은 캐릭터에서 칸이 새로 열렸으면 그 칸을 감는 연출을 튼다(캐릭터를 바꾸거나 화면을 처음 열 땐 연출 없음).
        /// 근접(검 · 창 · 곡도 · 삼지창) = 금줄, 원거리(부적 · 신칼 · 활 · 부채 투사체) = 오방색 끈.
        /// </summary>
        private void RefreshStringArt(CharacterCatalogSO.CharacterEntry entry, int mask)
        {
            if (linkArt == null || wrapArt == null)
            {
                return;
            }
            int wind = -2;
            if (entry.characterId == artCharacterId && artMask >= 0)
            {
                int gained = mask & ~artMask;
                if (gained != 0)
                {
                    wind = -1;
                    for (int i = 0; i < CharacterCatalogSO.SinnaerimSlotCount; i++)
                    {
                        if ((gained & (1 << i)) != 0)
                        {
                            wind = i;
                        }
                    }
                }
                else
                {
                    wind = -1;
                }
            }
            artCharacterId = entry.characterId;
            artMask = mask;
            bool ranged = entry.stats != null && entry.stats.IsProjectile;
            Color32[] palette = ranged ? ObangPalette : StrawPalette;
            linkArt.SetState(nodeArtStates, palette, !ranged, wind);
            wrapArt.SetState(nodeArtStates, palette, !ranged, wind);
            wrapArt.SetSelected(selectedSlot);
        }

        private static StringArtGraphic CreateStringArt(Transform parent, string name, StringArtGraphic.Layer layer, Vector2[] centers)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(StringArtGraphic));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var art = go.GetComponent<StringArtGraphic>();
            art.raycastTarget = false;
            art.DrawLayer = layer;
            art.SetLayout(centers, EdgeFrom, EdgeTo, PegRadius);
            return art;
        }

        private static CharacterCatalogSO.SinnaerimNode NodeOf(CharacterCatalogSO.CharacterEntry entry, int slot)
        {
            return entry.sinnaerim != null && slot >= 0 && slot < entry.sinnaerim.Length ? entry.sinnaerim[slot] : null;
        }

        private void OnOpenNodeClicked()
        {
            CharacterCatalogSO.CharacterEntry entry = Selected;
            CharacterCatalogSO.SinnaerimNode node = entry != null ? NodeOf(entry, selectedSlot) : null;
            if (node == null || !CharacterProgressStore.IsUnlocked(entry))
            {
                return;
            }
            var slot = (CharacterCatalogSO.SinnaerimSlot)selectedSlot;
            int mask = CharacterProgressStore.SinnaerimMask(entry.characterId);
            if (CharacterProgressStore.StateOf(mask, slot) != CharacterProgressStore.NodeState.Available)
            {
                return;
            }
            if (PlayerWallet.TrySpendYeopjeon(node.cost))
            {
                CharacterProgressStore.TryMarkOpen(entry.characterId, slot);
            }
            Refresh();
        }

        // ───────── 공통 ─────────

        private Text CreateText(Transform parent, string name, Vector2 absolutePosition, Vector2 size, int fontSize, TextAnchor anchor, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                // 부모(화면 전체로 늘어난 패널) 기준 왼쪽 아래 1920×1080 좌표.
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = absolutePosition;
                rect.sizeDelta = size;
            }
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Dark;
            text.alignment = anchor;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(Transform parent, Button template, string name, Vector2 absolutePosition, Vector2 size, out Text label)
        {
            label = null;
            if (template == null)
            {
                return null;
            }
            GameObject go = Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = absolutePosition;
            rect.sizeDelta = size;
            Button button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            // 복제 원본(AttackTab)의 인스펙터 onClick까지 지워 탭 동작이 섞이지 않게 한다.
            button.onClick = new Button.ButtonClickedEvent();
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(0.55f, 0.53f, 0.5f, 0.85f);
            button.colors = colors;
            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = Color.white;
            }
            label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.fontSize = 26;
                label.fontStyle = FontStyle.Bold;
                label.color = Red;
            }
            return button;
        }

        private static T FindDeepComponent<T>(Transform root, string childName) where T : Component
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName && child.TryGetComponent(out T found))
                {
                    return found;
                }
                T deeper = FindDeepComponent<T>(child, childName);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }

        private static void SetActive(GameObject[] targets, bool active)
        {
            if (targets == null)
            {
                return;
            }
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null && targets[i].activeSelf != active)
                {
                    targets[i].SetActive(active);
                }
            }
        }

        private GameObject[] FindChildren(params string[] names)
        {
            var found = new List<GameObject>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                Transform child = transform.Find(names[i]);
                if (child != null)
                {
                    found.Add(child.gameObject);
                }
            }
            return found.ToArray();
        }
    }
}
