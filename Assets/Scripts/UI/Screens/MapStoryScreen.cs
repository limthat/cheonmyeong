using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// MAP_STORY(스토리 컷신) 화면 - 상단 일러스트 자리 + 하단 대사창. 와이어프레임엔
    /// "두 명 모두 눌러야 넘어감" / "Space 길게 눌러 건너뛰기 0/2"가 있지만, 이건 두
    /// 플레이어의 입력을 서버가 모아서 판정하는 협동 로직이 필요하고 아직 네트워크
    /// 쪽(RPC 기반 동기화)이 없다 - 그래서 지금은 로컬 전용 "계속하기" 버튼으로
    /// 임시 대체한다(실제 컷신 콘텐츠/대사 데이터도 아직 없음). 공동 스킵 시스템이
    /// 생기면 이 버튼 로직만 교체하면 된다.
    /// </summary>
    public class MapStoryScreen : MonoBehaviour
    {
        [SerializeField] private Text speakerNameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text skipHintText;
        [SerializeField] private Button continueButton;

        private void Awake()
        {
            if (continueButton != null)
            {
                continueButton.onClick.AddListener(() => UIFlowManager.Instance.ShowScreen(UIFlowManager.InGame));
            }
        }

        private void OnEnable()
        {
            // 제1화 와이어프레임 예시 대사 - 실제 스토리 데이터 시스템이 들어오면 교체.
            if (speakerNameText != null)
            {
                speakerNameText.text = "억쇠";
            }
            if (dialogueText != null)
            {
                dialogueText.text = "한 맺힌 것들이 이렇게나 많은데… 오늘 밤도 배가 부르겠구나.";
            }
            if (skipHintText != null)
            {
                skipHintText.text = "두 명 모두 눌러야 넘어감 · Space 길게 눌러 건너뛰기 0/2 (임시: 아래 버튼으로 대체)";
            }
        }

        private void Update()
        {
            // 실제 "Space 길게 누르기 + 둘 다 입력" 판정은 네트워크 동기화가 필요해
            // 아직 없음 - 로컬 Space 단축키만 continueButton과 동일하게 동작시켜둔다.
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                UIFlowManager.Instance.ShowScreen(UIFlowManager.InGame);
            }
        }
    }
}
