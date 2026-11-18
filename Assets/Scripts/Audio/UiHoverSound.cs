using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CheonMyeong.Audio
{
    /// <summary>
    /// 261117 - 버튼에 마우스를 올리면 작은 톡(SFX_UI_Hover). UiButtonSounds가 시작할 때 버튼마다 붙인다.
    /// 누를 수 없는 버튼은 소리 없음. 같은 버튼 위에서 반복 재생되지 않게 들어올 때 한 번만.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiHoverSound : MonoBehaviour, IPointerEnterHandler
    {
        private Selectable selectable;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable())
            {
                return;
            }
            GameAudio.Play(SoundKeys.UiHover);
        }
    }
}
