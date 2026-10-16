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
    /// "동료 선택 완료"는 협동(2인) 선택 동기화가 필요한데 네트워크 쪽이 아직 없어서
    /// 지금은 누르면 바로 오버레이를 닫는다 - 실제로는 양쪽 다 고를 때까지 기다려야 함.
    /// </summary>
    public class UpgradeScreen : MonoBehaviour
    {
        [SerializeField] private Text timerText;
        [SerializeField] private Button readyButton;

        private void Awake()
        {
            if (readyButton != null)
            {
                readyButton.onClick.AddListener(HandleReadyClicked);
            }
        }

        /// <summary>
        /// 261014 추가 - "증강 선택중 게임 및 캐릭터 정지 없음" 수정의 짝. 오버레이를
        /// 닫는 것과 별개로, 서버에게 일시정지 해제를 요청해야 PartyProgress.isPaused가
        /// 실제로 풀린다(오버레이를 닫는 CloseOverlay()는 순수 로컬 UI 동작이라 네트워크
        /// 쪽 isPaused에는 전혀 영향을 주지 않는다). "둘 다 선택 완료" 동기화는 여전히
        /// 미구현이라(기존에 이미 알려진 보류 사항) 지금은 CloseOverlay()와 똑같이
        /// 누가 눌러도 즉시 재개된다.
        /// </summary>
        private void HandleReadyClicked()
        {
            if (PartyProgress.Instance != null)
            {
                PartyProgress.Instance.RequestUnpauseRpc();
            }

            UIFlowManager.Instance.CloseOverlay();
        }

        private void OnEnable()
        {
            if (timerText != null)
            {
                timerText.text = "제한시간 -- (타이머 로직 미구현)";
            }
        }
    }
}
