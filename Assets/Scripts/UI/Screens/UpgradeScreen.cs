using System;
using System.Collections.Generic;
using CheonMyeong.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// UPGRADE(신통력 고르기) 화면 - 레벨업 시 뜨는 오버레이.
    /// 261104 - 기획서 v0.9.4 07장 신통력 연결(서버가 뽑은 내 카드 3장, 준비 완료 → PickAugmentRpc, 10초, 리롤권).
    /// 261109 - 아트 카드 디자인([아트] 천명_UI_에셋 · Test_Art_SampleScene) 연결. 카드 안 이름으로 찾는다:
    /// - NameText: [분류] 이름 / DescText: 설명 / Star_0~4: 단계 별(채운 별 = 고르면 될 단계, 일회용 · 무한은 숨김)
    /// - Icon: 신통력 아이콘(Sintong_Icon-Sheet). 261110 - augmentIcons 표로 카탈로그 id ↔ 아이콘을 잇는다. 표에 없으면 숨긴다.
    /// - RerollButton: 그 카드 한 장만 리롤권 1장으로 다시 뽑는다(아트 디자인이 카드마다 버튼).
    /// 카드 배경이 아트 그림이라 선택 표시는 색을 어둡게 칠하지 않고, 고른 카드를 살짝 키우고 나머지를 흐리게 한다.
    /// 261109 - 리롤권이 없으면(기본 0장, 5웨이브 억쇠 격퇴 때 +1) 카드 리롤 버튼을 확실히 어둡게 하고, 버튼 구석에 남은 장수(×N)를 띄운다.
    /// 아트 구조가 없는 예전 와이어프레임이면 예전처럼 첫 글자칸에 전부 쓰고, 리롤은 3장 전부 다시 뽑는다.
    /// </summary>
    public class UpgradeScreen : MonoBehaviour
    {
        [SerializeField] private Text timerText;
        [SerializeField] private Transform cardsContainer;
        [SerializeField] private Button readyButton;

        [Tooltip("카드 밖 리롤 버튼(전부 다시 뽑기). 비워두면 카드 밖에서 \"RerollButton\"을 찾는다. 카드마다 리롤 버튼이 있으면 쓰지 않는다.")]
        [SerializeField] private Button rerollButton;

        [Tooltip("비워두면 자식 중 \"RerollCountText\"를 찾는다.")]
        [SerializeField] private Text rerollCountText;

        [Serializable]
        public struct AugmentIcon
        {
            public AugmentId id;
            public Sprite sprite;
        }

        [Tooltip("261110 - 신통력 id → 카드 아이콘(Sintong_Icon-Sheet). 아이콘 시트는 기획서 v0.9.6 이름 기준이라 v0.9.4 카탈로그와는 뜻이 가장 가까운 아이콘으로 이었다.")]
        [SerializeField] private AugmentIcon[] augmentIcons = Array.Empty<AugmentIcon>();

        private const string TitleLabel = "레벨업! 신통력을 고르세요";
        private const string ReadyButtonDefaultLabel = "✓ 준비 완료";
        private const string ReadyButtonWaitingLabel = "대기 중... (동료를 기다리는 중)";
        private const int StarCount = 5;
        private const float SelectedScale = 1.04f;
        private static readonly Color DimmedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color RerollDisabledColor = new Color(0.35f, 0.35f, 0.38f, 1f);

        private class CardView
        {
            public Transform Root;
            public Button Button;
            public Image Background;
            public Color BackgroundColor;
            public Text NameText;
            public Text DescText;
            public Image Icon;
            public Image[] Stars;
            public Button RerollButton;
            public TMP_Text RerollCount;
        }

        private readonly List<CardView> cards = new List<CardView>();
        private Sprite starFilled;
        private Sprite starEmpty;
        private bool perCardReroll;

        private int selectedCardIndex = -1;
        private Text readyButtonLabel;

        private int shownRound = -1;
        private int shownOfferVersion = -1;
        private int shownSeconds = -1;
        private int shownTickets = -1;
        private bool shownPicked;

        private void Awake()
        {
            Transform title = FindDeep(transform, "TitleText");
            if (title != null && title.TryGetComponent(out Text titleText))
            {
                titleText.text = TitleLabel;
            }

            if (readyButton != null)
            {
                readyButtonLabel = readyButton.GetComponentInChildren<Text>();
                readyButton.onClick.AddListener(HandleReadyClicked);
            }

            BindCards();

            if (rerollCountText == null)
            {
                Transform found = FindDeep(transform, "RerollCountText");
                if (found != null)
                {
                    found.TryGetComponent(out rerollCountText);
                }
            }
            if (!perCardReroll)
            {
                if (rerollButton == null)
                {
                    Transform found = FindDeepOutside(transform, "RerollButton", cardsContainer);
                    if (found != null)
                    {
                        found.TryGetComponent(out rerollButton);
                    }
                }
                if (rerollButton == null && rerollCountText != null)
                {
                    rerollButton = rerollCountText.gameObject.AddComponent<Button>();
                    rerollButton.targetGraphic = rerollCountText;
                    rerollCountText.raycastTarget = true;
                }
                if (rerollButton != null)
                {
                    rerollButton.onClick.AddListener(HandleRerollAllClicked);
                }
            }
        }

        private void BindCards()
        {
            if (cardsContainer == null)
            {
                return;
            }
            for (int i = 0; i < cardsContainer.childCount; i++)
            {
                Transform root = cardsContainer.GetChild(i);
                if (!root.TryGetComponent(out Button button) || !root.TryGetComponent(out Image background))
                {
                    continue;
                }
                var view = new CardView
                {
                    Root = root,
                    Button = button,
                    Background = background,
                    BackgroundColor = background.color,
                    NameText = FindComponent<Text>(root, "NameText"),
                    DescText = FindComponent<Text>(root, "DescText"),
                    Icon = FindComponent<Image>(root, "Icon"),
                    RerollButton = FindComponent<Button>(root, "RerollButton")
                };
                if (view.NameText == null)
                {
                    view.NameText = root.GetComponentInChildren<Text>(true);
                }
                var stars = new Image[StarCount];
                bool hasStars = true;
                for (int s = 0; s < StarCount; s++)
                {
                    stars[s] = FindComponent<Image>(root, "Star_" + s);
                    hasStars &= stars[s] != null;
                }
                if (hasStars)
                {
                    view.Stars = stars;
                    // 아트 시안: 왼쪽 첫 별만 채운 1단계 모습 - 채운 별 · 빈 별 그림을 여기서 가져간다.
                    if (starFilled == null && stars[0].sprite != null && stars[1].sprite != null)
                    {
                        starFilled = stars[0].sprite;
                        starEmpty = stars[1].sprite;
                    }
                }
                if (view.Icon != null)
                {
                    view.Icon.gameObject.SetActive(false);
                }

                int index = cards.Count;
                button.onClick.AddListener(() => HandleCardClicked(index));
                if (view.RerollButton != null)
                {
                    view.RerollButton.onClick.AddListener(() => HandleRerollCardClicked(index));
                    ColorBlock colors = view.RerollButton.colors;
                    colors.disabledColor = RerollDisabledColor;
                    view.RerollButton.colors = colors;
                    view.RerollCount = view.RerollButton.GetComponentInChildren<TMP_Text>(true);
                    if (view.RerollCount != null)
                    {
                        view.RerollCount.alignment = TextAlignmentOptions.BottomRight;
                        view.RerollCount.fontSize = 22f;
                        view.RerollCount.margin = new Vector4(0f, 0f, 8f, 4f);
                    }
                    perCardReroll = true;
                }
                cards.Add(view);
            }
        }

        private void OnDestroy()
        {
            if (readyButton != null)
            {
                readyButton.onClick.RemoveListener(HandleReadyClicked);
            }
            if (rerollButton != null)
            {
                rerollButton.onClick.RemoveListener(HandleRerollAllClicked);
            }
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Button != null)
                {
                    cards[i].Button.onClick.RemoveAllListeners();
                }
                if (cards[i].RerollButton != null)
                {
                    cards[i].RerollButton.onClick.RemoveAllListeners();
                }
            }
        }

        private void OnEnable()
        {
            shownRound = -1;
            shownOfferVersion = -1;
            shownSeconds = -1;
            shownTickets = -1;
            ResetSelection();
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            PartyProgress party = PartyProgress.Instance;
            PlayerProgress me = PlayerProgress.Local;
            if (party == null || me == null)
            {
                return;
            }

            if (shownRound != party.ChoiceRound || shownOfferVersion != me.OfferVersion)
            {
                shownRound = party.ChoiceRound;
                shownOfferVersion = me.OfferVersion;
                ResetSelection();
                FillCards(me);
            }

            int seconds = Mathf.CeilToInt(party.ChoiceSecondsLeft);
            if (seconds != shownSeconds && timerText != null)
            {
                shownSeconds = seconds;
                timerText.text = "남은 시간 " + seconds + "초";
            }

            bool picked = me.HasPicked;
            int tickets = me.RerollTickets;
            if (tickets != shownTickets || picked != shownPicked)
            {
                shownTickets = tickets;
                shownPicked = picked;
                bool canReroll = tickets > 0 && !picked;
                if (rerollCountText != null)
                {
                    rerollCountText.text = !perCardReroll && canReroll ? "↻ 리롤권×" + tickets : "리롤권×" + tickets;
                }
                if (rerollButton != null)
                {
                    rerollButton.interactable = canReroll;
                }
                for (int i = 0; i < cards.Count; i++)
                {
                    bool has = i < me.OfferCount;
                    cards[i].Button.interactable = !picked && has;
                    if (cards[i].RerollButton != null)
                    {
                        cards[i].RerollButton.interactable = canReroll && has;
                    }
                    if (cards[i].RerollCount != null)
                    {
                        cards[i].RerollCount.text = "×" + tickets;
                    }
                }
                if (readyButton != null)
                {
                    readyButton.interactable = !picked && selectedCardIndex >= 0;
                }
                if (readyButtonLabel != null)
                {
                    readyButtonLabel.text = picked ? ReadyButtonWaitingLabel : ReadyButtonDefaultLabel;
                }
            }
        }

        private void FillCards(PlayerProgress me)
        {
            AugmentCatalogSO catalog = PlayerProgress.Catalog;
            for (int i = 0; i < cards.Count; i++)
            {
                CardView card = cards[i];
                bool has = i < me.OfferCount;
                card.Root.gameObject.SetActive(has);
                card.Button.interactable = has && !me.HasPicked;
                if (has)
                {
                    FillCard(card, catalog, me.GetOffer(i));
                }
            }
        }

        private void FillCard(CardView card, AugmentCatalogSO catalog, AugmentOffer offer)
        {
            AugmentEntry entry = catalog.Find(offer.Augment);
            if (entry == null)
            {
                if (card.NameText != null)
                {
                    card.NameText.text = "?";
                }
                return;
            }

            string tag;
            switch (entry.kind)
            {
                case AugmentKind.Exclusive: tag = "[전용]"; break;
                case AugmentKind.OneTime: tag = "[일회용]"; break;
                case AugmentKind.Infinite: tag = "[무한]"; break;
                default: tag = "[공통]"; break;
            }
            if (card.Icon != null)
            {
                Sprite icon = FindIcon(offer.Augment);
                card.Icon.sprite = icon;
                card.Icon.preserveAspect = true;
                card.Icon.gameObject.SetActive(icon != null);
            }

            bool leveled = entry.kind == AugmentKind.Common || entry.kind == AugmentKind.Exclusive;
            bool showStars = leveled && card.Stars != null && starFilled != null;

            if (card.Stars != null)
            {
                for (int s = 0; s < card.Stars.Length; s++)
                {
                    card.Stars[s].gameObject.SetActive(showStars);
                    if (showStars)
                    {
                        card.Stars[s].sprite = s < offer.Level ? starFilled : starEmpty;
                    }
                }
            }

            string name = leveled && !showStars ? entry.displayName + " Lv." + offer.Level : entry.displayName;
            if (card.DescText != null)
            {
                if (card.NameText != null)
                {
                    card.NameText.text = tag + " " + name;
                }
                card.DescText.text = entry.Describe(offer.Level);
            }
            else if (card.NameText != null)
            {
                card.NameText.text = tag + " " + name + "\n\n" + entry.Describe(offer.Level);
            }
        }

        private void HandleCardClicked(int index)
        {
            PlayerProgress me = PlayerProgress.Local;
            if (me == null || me.HasPicked || index >= me.OfferCount)
            {
                return;
            }
            selectedCardIndex = index;
            RefreshCardHighlights();
            if (readyButton != null)
            {
                readyButton.interactable = true;
            }
        }

        private void HandleReadyClicked()
        {
            if (selectedCardIndex < 0 || PartyProgress.Instance == null)
            {
                return;
            }
            PartyProgress.Instance.PickAugmentRpc(selectedCardIndex);
            readyButton.interactable = false;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = ReadyButtonWaitingLabel;
            }
        }

        private void HandleRerollCardClicked(int index)
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.RerollAugmentCardRpc(index);
            }
        }

        private void HandleRerollAllClicked()
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.RerollAugmentsRpc();
            }
        }

        private void ResetSelection()
        {
            selectedCardIndex = -1;
            RefreshCardHighlights();
            if (readyButton != null)
            {
                readyButton.interactable = false;
            }
            shownTickets = -1;
        }

        private void RefreshCardHighlights()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardView card = cards[i];
                bool selected = i == selectedCardIndex;
                bool dimmed = selectedCardIndex >= 0 && !selected;
                card.Background.color = dimmed ? card.BackgroundColor * DimmedColor : card.BackgroundColor;
                card.Root.localScale = selected ? Vector3.one * SelectedScale : Vector3.one;
            }
        }

        private Sprite FindIcon(AugmentId id)
        {
            for (int i = 0; i < augmentIcons.Length; i++)
            {
                if (augmentIcons[i].id == id)
                {
                    return augmentIcons[i].sprite;
                }
            }
            return null;
        }

        private static T FindComponent<T>(Transform root, string childName) where T : Component
        {
            Transform found = FindDeep(root, childName);
            return found != null && found.TryGetComponent(out T component) ? component : null;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            return FindDeepOutside(root, childName, null);
        }

        private static Transform FindDeepOutside(Transform root, string childName, Transform skip)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == skip)
                {
                    continue;
                }
                if (child.name == childName)
                {
                    return child;
                }
                Transform deeper = FindDeepOutside(child, childName, skip);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }
    }
}
