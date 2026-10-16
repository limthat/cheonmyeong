using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// CHAR_SKILLTREE 화면 - 좌측 캐릭터 8명 리스트(하영만 해금) / 중앙 선택 캐릭터
    /// 미리보기 / 우측 스킬트리 노드 그래프.
    ///
    /// 와이어프레임 사용자 확인(261013) 기준: 8캐릭터 로스터는 확정(하영만 구현 대상,
    /// 나머지 7명은 이름만 있는 추후 개발분), 스킬트리 "구조"(노드 종류/분기/밸런스)는
    /// 미정 - 그래서 우측 노드 그래프는 레이아웃만 자리를 잡아두고(skillTreeContainer),
    /// 실제 노드 로직은 만들지 않는다(팀 확정 후 구현).
    ///
    /// rosterContainer 밑에는 캐릭터 수만큼 Button 자식이 이름순(하영 먼저)으로 들어있고,
    /// 이 스크립트가 Awake에서 자식들을 순서대로 읽어 인덱스 0(하영)만 선택 가능하게,
    /// 나머지는 잠김으로 처리한다 - 캐릭터가 늘어도(미래) 이 스크립트를 고칠 필요 없음.
    /// </summary>
    public class CharSkillTreeScreen : MonoBehaviour
    {
        private static readonly string[] RosterNames =
        {
            "하영", "한주원", "박규혁", "이아라", "최환조", "신지현", "임훈", "심여레"
        };

        [SerializeField] private Button backButton;
        [SerializeField] private Transform rosterContainer;
        [SerializeField] private Text previewNameText;
        [SerializeField] private Text previewDescText;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.Lobby));
            }

            if (rosterContainer != null)
            {
                for (int i = 0; i < rosterContainer.childCount; i++)
                {
                    Button rosterButton = rosterContainer.GetChild(i).GetComponent<Button>();
                    if (rosterButton == null)
                    {
                        continue;
                    }

                    int capturedIndex = i; // 클로저 변수 캡처 버그 방지용 로컬 복사
                    bool unlocked = capturedIndex == 0; // 하영만 해금 - 나머지는 추후 개발(확정, 261013)
                    rosterButton.interactable = true; // 잠긴 캐릭터도 눌러서 "잠김" 안내는 볼 수 있게 둔다
                    rosterButton.onClick.AddListener(() => OnRosterClicked(capturedIndex, unlocked));
                }
            }

            SelectCharacter(0);
        }

        private void OnRosterClicked(int index, bool unlocked)
        {
            if (!unlocked)
            {
                Debug.Log($"[CheonMyeong] '{RosterNames[index]}'는 추후 개발 예정 캐릭터 - 아직 선택할 수 없습니다.");
                return;
            }

            SelectCharacter(index);
        }

        private void SelectCharacter(int index)
        {
            if (previewNameText != null)
            {
                previewNameText.text = index == 0 ? "하영 · 사인검" : RosterNames[index];
            }
            if (previewDescText != null)
            {
                previewDescText.text = index == 0
                    ? "근접·다수 | 패시브: 동료를 지키는 결계"
                    : "추후 개발 예정 - 상세 미정";
            }
        }
    }
}
