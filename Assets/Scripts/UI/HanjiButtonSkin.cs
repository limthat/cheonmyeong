using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261121 추가 - 기획서 v1.2 「UI 버튼 · 바」 버튼 규칙을 Btn.aseprite 4종 버튼에 입힌다(씬은 그대로, 켜질 때 붙임).
    /// - 글씨: 먹 #080710(한지 위 흰 글씨 금지). 비활성 = 갈 #916A3E.
    /// - 호버: 같은 종류의 흰 테두리 그림(Btn 2 · imprt 4 · 나무 간판 7)으로 바뀌고 1px 위로 뜬다. 누름: 1px 아래로.
    /// - 비활성: Un_Btn(5) 회색.
    /// 640×360 기준 ×3이라 그림 1px = 화면 3px. 버튼 그림은 Resources/ButtonSkin(ButtonSkinSet)에서 찾는다(아틀라스로 묶여도 안전).
    /// 호버 · 누름 소리는 UiButtonSounds/UiHoverSound가 따로 낸다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HanjiButtonSkin : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public static readonly Color Ink = new Color32(0x08, 0x07, 0x10, 0xFF);
        public static readonly Color DisabledText = new Color32(0x91, 0x6A, 0x3E, 0xFF);
        private const float ArtPixel = 3f;

        private Button button;
        private readonly List<Text> labels = new List<Text>(2);
        private readonly List<Vector2> labelBase = new List<Vector2>(2);
        private bool hover;
        private bool down;
        private bool lastInteractable = true;
        private float lastOffset = float.NaN;

        /// <summary>root 아래(꺼진 것 포함) Btn-Sheet 그림을 쓰는 버튼마다 붙인다. 이미 붙어 있으면 건너뛴다.</summary>
        public static void ApplyAll(Transform root)
        {
            ButtonSkinSet set = ButtonSkinSet.Load();
            if (root == null || set == null)
            {
                return;
            }
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button b = buttons[i];
                if (b == null || b.GetComponent<HanjiButtonSkin>() != null || !(b.targetGraphic is Image image))
                {
                    continue;
                }
                if (set.KindOf(image.sprite) < 0)
                {
                    continue;
                }
                b.gameObject.AddComponent<HanjiButtonSkin>();
            }
        }

        private void Awake()
        {
            button = GetComponent<Button>();
            ButtonSkinSet set = ButtonSkinSet.Load();
            if (button == null || set == null || !(button.targetGraphic is Image image))
            {
                enabled = false;
                return;
            }
            int kind = set.KindOf(image.sprite);
            if (kind >= 0)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = set.Hover(kind),
                    pressedSprite = set.Hover(kind),
                    selectedSprite = null, // 누른 뒤 선택 상태로 흰 테두리가 남지 않게
                    disabledSprite = set.Disabled,
                };
            }
            GetComponentsInChildren(true, labels);
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                // 버튼 바로 아래 글자만(다른 버튼 안의 글자 제외).
                if (labels[i].GetComponentInParent<Button>(true) != button)
                {
                    labels.RemoveAt(i);
                }
            }
            labelBase.Clear();
            for (int i = 0; i < labels.Count; i++)
            {
                labelBase.Add(labels[i].rectTransform.anchoredPosition);
            }
            lastInteractable = !button.IsInteractable();
            Refresh();
        }

        private void OnDisable()
        {
            hover = false;
            down = false;
            Refresh();
        }

        private void LateUpdate()
        {
            if (button != null && button.IsInteractable() != lastInteractable)
            {
                Refresh();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hover = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hover = false;
            down = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            down = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            down = false;
            Refresh();
        }

        private void Refresh()
        {
            if (button == null)
            {
                return;
            }
            bool interactable = button.IsInteractable();
            if (interactable != lastInteractable)
            {
                lastInteractable = interactable;
                for (int i = 0; i < labels.Count; i++)
                {
                    if (labels[i] != null)
                    {
                        labels[i].color = interactable ? Ink : DisabledText;
                    }
                }
            }
            float offset = !interactable ? 0f : (down ? -ArtPixel : (hover ? ArtPixel : 0f));
            if (offset.Equals(lastOffset))
            {
                return;
            }
            lastOffset = offset;
            for (int i = 0; i < labels.Count && i < labelBase.Count; i++)
            {
                if (labels[i] != null)
                {
                    labels[i].rectTransform.anchoredPosition = labelBase[i] + new Vector2(0f, offset);
                }
            }
        }
    }
}
