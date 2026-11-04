using System.Collections.Generic;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// UPGRADE(신통력 고르기) 화면 - 레벨업 시 뜨는 오버레이.
    /// 261104 수정 - 기획서 v0.9.4 07장 신통력 연결:
    /// - 카드 3장은 서버가 뽑은 내 카드(PlayerProgress.GetOffer)를 보여준다.
    /// - 카드를 고르고 "준비 완료"를 누르면 PartyProgress.PickAugmentRpc로 서버에 알린다. 고르는 순간 적용된다.
    /// - 둘 다 고르면(또는 10초가 지나면 무작위로) 서버가 재개하고, isPaused가 풀리면 이 화면이 닫힌다.
    /// - 리롤권이 있으면 "다시 뽑기"로 내 카드 3장을 새로 뽑는다.
    /// 와이어프레임 UI는 아트가 잡는 중이라 프리팹 구조는 건드리지 않고, 리롤 버튼이 없으면 실행 중에만 만든다.
    /// </summary>
    public class UpgradeScreen : MonoBehaviour
    {
        [SerializeField] private Text timerText;
        [SerializeField] private Transform cardsContainer;
        [SerializeField] private Button readyButton;

        [Tooltip("비워두면 자식 중 \"RerollButton\"을 찾고, 없으면 실행 중에 만든다.")]
        [SerializeField] private Button rerollButton;

        [Tooltip("비워두면 자식 중 \"RerollCountText\"를 찾는다(없어도 됨 - 리롤 버튼 라벨에 같이 표시).")]
        [SerializeField] private Text rerollCountText;

        private static readonly Color CardNormalColor = new Color(0.14f, 0.14f, 0.18f, 0.95f);
        private static readonly Color CardSelectedColor = new Color(0.46f, 0.37f, 0.15f, 0.95f);

        private const string TitleLabel = "레벨업! 신통력을 고르세요";
        private const string ReadyButtonDefaultLabel = "✓ 준비 완료";
        private const string ReadyButtonWaitingLabel = "대기 중... (동료를 기다리는 중)";

        private readonly List<Image> cardBackgrounds = new List<Image>();
        private readonly List<Button> cardButtons = new List<Button>();
        private readonly List<Text> cardTexts = new List<Text>();

        private int selectedCardIndex = -1;
        private Text readyButtonLabel;
        private Text rerollButtonLabel;

        // 바뀐 것만 다시 그리기 위한 캐시(매 프레임 문자열 할당 방지).
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

            if (cardsContainer != null)
            {
                for (int i = 0; i < cardsContainer.childCount; i++)
                {
                    Transform cardTransform = cardsContainer.GetChild(i);
                    Button cardButton = cardTransform.GetComponent<Button>();
                    Image cardBackground = cardTransform.GetComponent<Image>();
                    if (cardButton == null || cardBackground == null)
                    {
                        continue;
                    }

                    int capturedIndex = cardButtons.Count;
                    cardButton.onClick.AddListener(() => HandleCardClicked(capturedIndex));
                    cardButtons.Add(cardButton);
                    cardBackgrounds.Add(cardBackground);
                    cardTexts.Add(cardTransform.GetComponentInChildren<Text>(true));
                }
            }

            if (rerollCountText == null)
            {
                Transform found = FindDeep(transform, "RerollCountText");
                if (found != null)
                {
                    found.TryGetComponent(out rerollCountText);
                }
            }
            if (rerollButton == null)
            {
                Transform found = FindDeep(transform, "RerollButton");
                if (found != null)
                {
                    found.TryGetComponent(out rerollButton);
                }
            }
            if (rerollButton == null)
            {
                rerollButton = CreateRerollButton(transform);
            }
            if (rerollButton != null)
            {
                rerollButtonLabel = rerollButton.GetComponentInChildren<Text>();
                rerollButton.onClick.AddListener(HandleRerollClicked);
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
                rerollButton.onClick.RemoveListener(HandleRerollClicked);
            }
            for (int i = 0; i < cardButtons.Count; i++)
            {
                if (cardButtons[i] != null)
                {
                    cardButtons[i].onClick.RemoveAllListeners();
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

            // 새 라운드(연속 레벨업) 또는 리롤로 카드가 바뀌면 선택을 풀고 다시 그린다.
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
                string ticketLabel = "리롤권 ×" + tickets;
                if (rerollCountText != null)
                {
                    rerollCountText.text = ticketLabel;
                }
                if (rerollButtonLabel != null)
                {
                    rerollButtonLabel.text = "↻ 다시 뽑기 (" + tickets + ")";
                }
                if (rerollButton != null)
                {
                    rerollButton.interactable = tickets > 0 && !picked;
                }
                for (int i = 0; i < cardButtons.Count; i++)
                {
                    cardButtons[i].interactable = !picked && i < me.OfferCount;
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
            for (int i = 0; i < cardButtons.Count; i++)
            {
                bool has = i < me.OfferCount;
                cardButtons[i].gameObject.SetActive(has);
                cardButtons[i].interactable = has && !me.HasPicked;
                if (!has || cardTexts[i] == null)
                {
                    continue;
                }
                cardTexts[i].text = DescribeCard(catalog, me.GetOffer(i));
            }
        }

        private static string DescribeCard(AugmentCatalogSO catalog, AugmentOffer offer)
        {
            AugmentEntry entry = catalog.Find(offer.Augment);
            if (entry == null)
            {
                return "?";
            }
            string tag;
            switch (entry.kind)
            {
                case AugmentKind.Exclusive: tag = "[전용]"; break;
                case AugmentKind.OneTime: tag = "[일회용]"; break;
                case AugmentKind.Infinite: tag = "[무한]"; break;
                default: tag = "[공통]"; break;
            }
            bool leveled = entry.kind == AugmentKind.Common || entry.kind == AugmentKind.Exclusive;
            string name = leveled ? entry.displayName + " Lv." + offer.Level : entry.displayName;
            return tag + " " + name + "\n\n" + entry.Describe(offer.Level);
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

        private void HandleRerollClicked()
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
            shownTickets = -1; // 버튼 상태를 다음 Refresh에서 다시 계산
        }

        private void RefreshCardHighlights()
        {
            for (int i = 0; i < cardBackgrounds.Count; i++)
            {
                cardBackgrounds[i].color = i == selectedCardIndex ? CardSelectedColor : CardNormalColor;
            }
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform deeper = FindDeep(child, childName);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }

        /// <summary>와이어프레임에 리롤 버튼이 없을 때만 - 화면 왼쪽 위에 임시 버튼(아트가 RerollButton을 넣으면 그걸 쓴다).</summary>
        private static Button CreateRerollButton(Transform parent)
        {
            var go = new GameObject("RerollButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -56f);
            rect.sizeDelta = new Vector2(180f, 40f);
            go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.26f, 0.95f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "↻ 다시 뽑기";
            return go.GetComponent<Button>();
        }
    }
}
