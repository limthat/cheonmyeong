using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.Audio
{
    /// <summary>
    /// 261115 - UICanvas 아래 모든 버튼에 클릭 소리를 붙인다(시작할 때 한 번, 꺼진 화면 포함). 이름에 Back이 들어간 버튼은 '뒤로' 소리.
    /// 261117 - 마우스를 올렸을 때 소리(UiHoverSound)도 같이 붙인다.
    /// 실행 중에 새로 만들어지는 버튼은 해당 화면 코드에서 GameAudio.Play(SoundKeys.UiClick)를 직접 부르면 된다.
    /// </summary>
    public sealed class UiButtonSounds : MonoBehaviour
    {
        private void Start()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                bool back = buttons[i].name.IndexOf("Back", System.StringComparison.OrdinalIgnoreCase) >= 0;
                buttons[i].onClick.AddListener(back ? (UnityEngine.Events.UnityAction)PlayBack : PlayClick);
                if (!buttons[i].TryGetComponent(out UiHoverSound _))
                {
                    buttons[i].gameObject.AddComponent<UiHoverSound>();
                }
            }
        }

        private static void PlayClick()
        {
            GameAudio.Play(SoundKeys.UiClick);
        }

        private static void PlayBack()
        {
            GameAudio.Play(SoundKeys.UiBack);
        }
    }
}
