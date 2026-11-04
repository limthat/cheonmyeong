using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261104 추가 - 인게임 HUD 왼쪽 아래 신통력 칸 6개(기획서 v0.9.4 07장 "신통력 칸").
    /// - 칸마다 이름 + 단계 점 5개(채운 점 = 단계, 5단계는 MAX). 전용은 테두리 색이 다르다.
    /// - 무한 스탯을 고른 횟수는 칸 위에 한 줄로.
    /// 와이어프레임은 아트가 잡는 중이라 프리팹을 만들지 않고 실행 중에만 만든다(씬 · 프리팹 충돌 없음).
    /// 다시 그리는 건 AugmentsChanged가 올 때만(매 프레임 X).
    /// </summary>
    public class AugmentHudView : MonoBehaviour
    {
        private static readonly Color SlotBackground = new Color(0.08f, 0.08f, 0.11f, 0.85f);
        private static readonly Color CommonBorder = new Color(0.78f, 0.62f, 0.25f, 1f);
        private static readonly Color ExclusiveBorder = new Color(0.55f, 0.75f, 1f, 1f);
        private static readonly Color EmptyBorder = new Color(0.3f, 0.3f, 0.35f, 0.8f);
        private static readonly Color DotOn = new Color(1f, 0.85f, 0.4f, 1f);
        private static readonly Color DotOff = new Color(0.25f, 0.25f, 0.3f, 1f);

        private const float SlotSize = 64f;
        private const float SlotGap = 6f;
        private const int Dots = 5;

        private PlayerProgress owner;
        private Image[] borders;
        private Text[] names;
        private Image[,] dots;
        private Text infiniteText;
        private Font font;

        public static AugmentHudView Create(Transform hudRoot, PlayerProgress owner)
        {
            var go = new GameObject("AugmentHud", typeof(RectTransform));
            go.transform.SetParent(hudRoot, false);
            var view = go.AddComponent<AugmentHudView>();
            view.Build(owner);
            return view;
        }

        private void Build(PlayerProgress progress)
        {
            owner = progress;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            AugmentCatalogSO catalog = PlayerProgress.Catalog;
            int slots = Mathf.Max(1, catalog.maxSlots);

            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = new Vector2(16f, 16f);
            root.sizeDelta = new Vector2(slots * (SlotSize + SlotGap), SlotSize + 26f);

            borders = new Image[slots];
            names = new Text[slots];
            dots = new Image[slots, Dots];

            for (int i = 0; i < slots; i++)
            {
                RectTransform border = NewRect(root, "Slot_" + i, new Vector2(i * (SlotSize + SlotGap), 0f), new Vector2(SlotSize, SlotSize));
                borders[i] = border.gameObject.AddComponent<Image>();
                borders[i].raycastTarget = false;

                RectTransform inner = NewRect(border, "Inner", new Vector2(2f, 2f), new Vector2(SlotSize - 4f, SlotSize - 4f));
                Image innerImage = inner.gameObject.AddComponent<Image>();
                innerImage.color = SlotBackground;
                innerImage.raycastTarget = false;

                RectTransform nameRect = NewRect(inner, "Name", new Vector2(2f, 14f), new Vector2(SlotSize - 8f, SlotSize - 18f));
                names[i] = AddText(nameRect, 11, TextAnchor.MiddleCenter);

                float dotSize = 7f;
                float dotsWidth = Dots * dotSize + (Dots - 1) * 2f;
                float startX = (SlotSize - 4f - dotsWidth) * 0.5f;
                for (int d = 0; d < Dots; d++)
                {
                    RectTransform dot = NewRect(inner, "Dot_" + d, new Vector2(startX + d * (dotSize + 2f), 4f), new Vector2(dotSize, dotSize));
                    Image dotImage = dot.gameObject.AddComponent<Image>();
                    dotImage.raycastTarget = false;
                    dots[i, d] = dotImage;
                }
            }

            RectTransform infiniteRect = NewRect(root, "Infinite", new Vector2(0f, SlotSize + 4f), new Vector2(root.sizeDelta.x, 20f));
            infiniteText = AddText(infiniteRect, 13, TextAnchor.LowerLeft);

            owner.AugmentsChanged += Redraw;
            Redraw();
        }

        private void OnDestroy()
        {
            if (owner != null)
            {
                owner.AugmentsChanged -= Redraw;
            }
        }

        private void Redraw()
        {
            if (owner == null || borders == null)
            {
                return;
            }
            AugmentCatalogSO catalog = PlayerProgress.Catalog;
            for (int i = 0; i < borders.Length; i++)
            {
                bool has = i < owner.SlotCount;
                AugmentSlot slot = has ? owner.GetSlot(i) : default;
                AugmentEntry entry = has ? catalog.Find(slot.Augment) : null;
                borders[i].color = entry == null ? EmptyBorder
                    : entry.kind == AugmentKind.Exclusive ? ExclusiveBorder : CommonBorder;
                names[i].text = entry == null ? string.Empty
                    : slot.Level >= catalog.maxLevel ? entry.displayName + "\nMAX" : entry.displayName;
                for (int d = 0; d < Dots; d++)
                {
                    dots[i, d].color = has && d < slot.Level ? DotOn : DotOff;
                    dots[i, d].enabled = has;
                }
            }

            if (owner.StackCount == 0)
            {
                infiniteText.text = string.Empty;
                return;
            }
            var sb = new System.Text.StringBuilder("무한 ");
            for (int i = 0; i < owner.StackCount; i++)
            {
                AugmentStack stack = owner.GetStack(i);
                AugmentEntry entry = catalog.Find(stack.Augment);
                if (entry == null)
                {
                    continue;
                }
                if (i > 0)
                {
                    sb.Append(" · ");
                }
                sb.Append(entry.displayName).Append(" ×").Append(stack.Count);
            }
            infiniteText.text = sb.ToString();
        }

        private static RectTransform NewRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private Text AddText(RectTransform rect, int size, TextAnchor anchor)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
