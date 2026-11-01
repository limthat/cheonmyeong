using System.Collections.Generic;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// UPGRADE(증강 선택) 화면 - 레벨업 시 뜨는 오버레이. 카드 3장 레이아웃/리롤
    /// 버튼/타이머/"동료 선택 완료" 버튼은 와이어프레임 UI를 그대로 채택하기로
    /// 확정됐지만(261013), 실제 증강 종류·수치·희귀도 체계는 미정이라(보류) 카드
    /// 내용은 와이어프레임에 적혀 있던 예시 텍스트를 placeholder로만 띄운다.
    ///
    /// "준비 완료"(261027, 예섭 요청으로 "동료 선택 완료"에서 라벨 변경)는 협동(2인) 선택
    /// 동기화 - 각자 누르면 PartyProgress.RequestReadyRpc로 서버에 전달되고, 접속한 전원이
    /// 다 눌러야 실제로 재개된다(PartyProgress.HandleIsPausedChanged 참고). 그래서 버튼을
    /// 누른다고 이 화면이 바로 닫히지 않는다 - 닫힘은 오직 isPaused가 false로 바뀌는 신호로만
    /// 일어난다(UIFlowManager.Instance.CloseOverlay()를 여기서 직접 부르지 않음).
    ///
    /// 카드 선택(261027 추가) - "에초에 선택도 구현 안되어있네"(예섭 지적) 대응. 카드
    /// 3장 중 하나를 누르면 그 카드만 금색으로 하이라이트되고, 하나라도 골라야 "준비 완료"
    /// 버튼이 눌린다(처음엔 비활성). 실제 증강 효과(수치) 적용은 증강 종류·밸런스 자체가
    /// 여전히 기획 미정이라 범위 밖 - 지금은 "어떤 카드를 골랐는지"만 selectedCardIndex에
    /// 기록해둔다(다음 작업에서 실제 효과 로직이 이 값을 읽어가면 됨).
    /// </summary>
    public class UpgradeScreen : MonoBehaviour
    {
        [SerializeField] private Text timerText;
        [SerializeField] private Transform cardsContainer;
        [SerializeField] private Button readyButton;

        // UICanvasBuilder.CreateHorizontalList가 카드 배경에 쓰는 기본색과 같은 값 - 그쪽은
        // Editor 전용 어셈블리라 여기서 직접 참조할 수 없어 값만 맞춰 따로 들고 있다.
        private static readonly Color CardNormalColor = new Color(0.14f, 0.14f, 0.18f, 0.95f);
        private static readonly Color CardSelectedColor = new Color(0.46f, 0.37f, 0.15f, 0.95f); // HudPanelRingColor 금색 계열과 맞춤

        private const string ReadyButtonDefaultLabel = "✓ 준비 완료";
        private const string ReadyButtonWaitingLabel = "대기 중... (동료를 기다리는 중)";

        private readonly List<Image> cardBackgrounds = new List<Image>();
        private int selectedCardIndex = -1;
        private Text readyButtonLabel;

        private void Awake()
        {
            if (readyButton != null)
            {
                // CreateButton(UICanvasBuilder)이 버튼 아래 "Label"이라는 이름의 자식 Text를
                // 만들어둔다 - 준비 완료를 누른 뒤 "대기 중..."으로 바꿔 보여주는 용도.
                readyButtonLabel = readyButton.GetComponentInChildren<Text>();
            }

            if (cardsContainer != null)
            {
                // UICanvasBuilder.CreateHorizontalList가 만든 순서 그대로 Card_0/1/2가
                // 자식으로 들어있다 - ChoiceMapScreen의 맵/캐릭터 타일과 같은 관례
                // (GetChild(i).GetComponent&lt;Button&gt;()).
                for (int i = 0; i < cardsContainer.childCount; i++)
                {
                    Transform cardTransform = cardsContainer.GetChild(i);
                    Button cardButton = cardTransform.GetComponent<Button>();
                    Image cardBackground = cardTransform.GetComponent<Image>();
                    if (cardButton == null || cardBackground == null)
                    {
                        continue;
                    }

                    int capturedIndex = i; // 클로저 캡처용 - 루프 변수를 직접 캡처하면 전부 마지막 값을 가리키게 됨
                    cardButton.onClick.AddListener(() => HandleCardClicked(capturedIndex));
                    cardBackgrounds.Add(cardBackground);
                }
            }

            if (readyButton != null)
            {
                readyButton.onClick.AddListener(HandleReadyClicked);
            }
        }

        private void HandleCardClicked(int index)
        {
            selectedCardIndex = index;
            RefreshCardHighlights();

            if (readyButton != null)
            {
                readyButton.interactable = true;
            }
        }

        private void RefreshCardHighlights()
        {
            for (int i = 0; i < cardBackgrounds.Count; i++)
            {
                cardBackgrounds[i].color = i == selectedCardIndex ? CardSelectedColor : CardNormalColor;
            }
        }

        /// <summary>
        /// 261027 수정 - "양측 플레이어 다 준비완료면 넘어가는걸로"(예섭 요청) 대응.
        /// 더 이상 여기서 직접 CloseOverlay()를 부르지 않는다 - 서버에 RequestReadyRpc로
        /// "나 준비됨"만 알리고, 실제 오버레이 닫힘은 전원이 다 준비됐을 때 서버가
        /// isPaused를 false로 내려주는 신호(PartyProgress.HandleIsPausedChanged)로만
        /// 일어난다. 누른 사람은 버튼을 비활성화하고 라벨을 "대기 중..."으로 바꿔서
        /// 동료를 기다리는 중임을 보여준다.
        /// </summary>
        private void HandleReadyClicked()
        {
            // 261027 추가 - readyButton이 카드를 하나 고르기 전까지 interactable=false라
            // 정상 경로에서는 selectedCardIndex가 항상 -1 이상이지만, 혹시 모를 상태 꼬임에
            // 대비해 한 번 더 막는다(아무것도 안 고르고 완료되는 것 방지).
            if (selectedCardIndex < 0)
            {
                return;
            }

            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.RequestReadyRpc();
            }

            readyButton.interactable = false;
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = ReadyButtonWaitingLabel;
            }
        }

        private void OnEnable()
        {
            if (timerText != null)
            {
                timerText.text = "제한시간 -- (타이머 로직 미구현)";
            }

            // 261027 추가 - 오버레이가 다시 열릴 때마다(다음 레벨업) 이전 선택이 남아있지
            // 않도록 초기화한다. 재사용되는 패널이라 OnEnable에서 매번 리셋해야 한다.
            selectedCardIndex = -1;
            RefreshCardHighlights();
            if (readyButton != null)
            {
                readyButton.interactable = false;
            }

            // 261027 추가 - 다음 레벨업 때 지난번 "대기 중..." 라벨이 남아있지 않도록
            // 기본 라벨로 되돌린다.
            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = ReadyButtonDefaultLabel;
            }
        }
    }
}
