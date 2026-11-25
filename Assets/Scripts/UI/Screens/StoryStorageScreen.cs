using CheonMyeong.Data;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// STORY_STORAGE 화면 - 2x3 스토리 슬롯. 1번 슬롯 = "1화. 억쇠"(달밤의 폐장터 썸네일).
    /// 261125 - 해당 맵을 클리어해야 열람 가능(StoryProgressStore). 잠겨 있으면 썸네일을 어둡게 하고 클릭을 막는다.
    /// 2~6번 슬롯은 아직 내용이 없어 항상 잠김.
    /// </summary>
    public class StoryStorageScreen : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private Transform slotsContainer;

        private const int EpisodeCount = 1;
        private const string ThumbnailName = "Thumbnail";
        private const string LabelName = "Label";
        private static readonly string[] EpisodeTitles = { "1화. 억쇠" };
        private static readonly Color LockedThumbColor = new Color(0.25f, 0.25f, 0.3f, 1f);

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
                    slotButton.onClick.AddListener(() => OnSlotClicked(capturedIndex));
                }
            }
        }

        private void OnEnable()
        {
            RefreshSlots();
        }

        private static bool IsUnlocked(int index)
        {
            return index < EpisodeCount && StoryProgressStore.IsCleared(index);
        }

        private void RefreshSlots()
        {
            if (slotsContainer == null)
            {
                return;
            }

            int count = Mathf.Min(EpisodeCount, slotsContainer.childCount);
            for (int i = 0; i < count; i++)
            {
                Transform slot = slotsContainer.GetChild(i);
                bool unlocked = IsUnlocked(i);

                Transform thumb = slot.Find(ThumbnailName);
                if (thumb != null && thumb.TryGetComponent(out Image thumbImage))
                {
                    thumbImage.color = unlocked ? Color.white : LockedThumbColor;
                }

                Transform label = slot.Find(LabelName);
                if (label != null && label.TryGetComponent(out Text labelText))
                {
                    labelText.text = unlocked
                        ? EpisodeTitles[i]
                        : EpisodeTitles[i] + "\n<size=16>클리어하면 열림</size>";
                }
            }
        }

        private void OnSlotClicked(int index)
        {
            if (!IsUnlocked(index))
            {
                Debug.Log("[CheonMyeong] 아직 봉인하지 않아 잠긴 이야기입니다.");
                return;
            }

            // 261118 - 기획서 12장 "본 컷신 다시 보기": 스토리 컷신 화면을 다시 보기 모드로 연다(Space · 버튼으로 보관함 복귀).
            MapStoryScreen.ReplayFromStorage = true;
            UIFlowManager.Instance.ShowScreen(UIFlowManager.MapStory);
        }
    }
}
