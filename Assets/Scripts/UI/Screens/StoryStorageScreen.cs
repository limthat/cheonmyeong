using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// STORY_STORAGE 화면 - 2x3 스토리 슬롯 그리드. 1번 슬롯("제1화 · 억쇠")만 해금,
    /// 나머지는 "봉인하면 열림"(런 클리어로 해금되는 구조로 보임 - 정확한 해금 조건은
    /// 보상/결과 시스템이 들어와야 확정되므로 지금은 1번만 고정 해금 처리).
    /// </summary>
    public class StoryStorageScreen : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private Transform slotsContainer;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby));
            }

            if (slotsContainer != null)
            {
                for (int i = 0; i < slotsContainer.childCount; i++)
                {
                    Button slotButton = slotsContainer.GetChild(i).GetComponent<Button>();
                    if (slotButton == null)
                    {
                        continue;
                    }

                    int capturedIndex = i;
                    bool unlocked = capturedIndex == 0;
                    slotButton.onClick.AddListener(() => OnSlotClicked(capturedIndex, unlocked));
                }
            }
        }

        private void OnSlotClicked(int index, bool unlocked)
        {
            if (!unlocked)
            {
                Debug.Log("[CheonMyeong] 아직 봉인하지 않아 잠긴 이야기입니다.");
                return;
            }

            Debug.Log("[CheonMyeong] 제1화 · 억쇠 컷신 다시보기 - 뷰어는 아직 미구현.");
        }
    }
}
