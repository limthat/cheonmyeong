using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261101 추가 - 화면 위 보스 체력 바. 인게임 HUD 화면 안에 실행 중에 직접 만든다(에디터 빌드 메뉴 없이도 동작).
    /// - 칸(최종전 3칸 × 8,400): 지금 깎이는 칸만 바로 보여주고, 남은 칸 수는 오른쪽에 "×N"으로 표시.
    /// - 피해 잔상: 맞으면 빨간 바는 바로 줄고, 뒤의 밝은 바가 잠깐 멈췄다가 따라 줄어든다(요즘 보스 체력 바의 흔한 연출).
    /// - 분신 체력: 정신병 차원이 열려 있으면 아래에 작은 보라색 바를 하나 더 보인다.
    /// 바 길이는 Image 대신 RectTransform 앵커로 조절해서 스프라이트 없이도 정확히 줄어든다.
    /// </summary>
    public class BossHealthBarView : MonoBehaviour
    {
        private const float ChipDelay = 0.35f;
        private const float ChipSpeed = 1.2f; // 초당 칸 비율

        private RectTransform fill;
        private RectTransform chip;
        private RectTransform cloneRoot;
        private RectTransform cloneFill;
        private Text nameText;
        private Text barsText;
        private float shownRatio = 1f;
        private float chipRatio = 1f;
        private float chipTimer;
        private int lastBarIndex = -1;

        // 261114 - 틀 그림(Bar-Sheet_10)에서 빈 칸 위 · 아래로 보이는 나무 두께(원본 픽셀). 글자를 이만큼 띄운다.
        private const float FrameWoodPixels = 9f;

        public static BossHealthBarView Create(Transform hudRoot)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 261114 - 아트 보스 바 틀(HUD의 BossBarFrame, Bar-Sheet_10 + 위 덮개 Overlay)이 있으면 그 틀 안쪽 칸을 채운다.
            Transform template = FindDeep(hudRoot, "BossBarFrame");
            if (template is RectTransform frameRoot && frameRoot.TryGetComponent(out Image frameImage))
            {
                return CreateFromFrame(frameRoot, frameImage, font);
            }

            RectTransform root = NewRect(hudRoot, "BossHealthBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            root.sizeDelta = new Vector2(760f, 30f);
            root.anchoredPosition = new Vector2(0f, -222f);
            var view = root.gameObject.AddComponent<BossHealthBarView>();

            Image frame = AddImage(root, new Color(0.05f, 0.03f, 0.05f, 0.9f));
            frame.raycastTarget = false;
            RectTransform inner = NewRect(root, "Inner", Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            AddImage(inner, new Color(0.18f, 0.06f, 0.08f, 1f));

            view.chip = NewRect(inner, "Chip", Vector2.zero, Vector2.one);
            AddImage(view.chip, new Color(1f, 0.85f, 0.6f, 0.9f));
            view.fill = NewRect(inner, "Fill", Vector2.zero, Vector2.one);
            AddImage(view.fill, new Color(0.78f, 0.1f, 0.12f, 1f));

            view.nameText = AddText(NewRect(root, "Name", new Vector2(0f, 1f), new Vector2(1f, 1f)), font, 20, TextAnchor.LowerCenter);
            ((RectTransform)view.nameText.transform).offsetMin = new Vector2(0f, 2f);
            ((RectTransform)view.nameText.transform).offsetMax = new Vector2(0f, 30f);
            view.barsText = AddText(NewRect(root, "Bars", new Vector2(1f, 0f), new Vector2(1f, 1f)), font, 20, TextAnchor.MiddleLeft);
            ((RectTransform)view.barsText.transform).offsetMin = new Vector2(10f, 0f);
            ((RectTransform)view.barsText.transform).offsetMax = new Vector2(70f, 0f);

            view.cloneRoot = NewRect(root, "Clone", new Vector2(0.25f, 0f), new Vector2(0.75f, 0f));
            view.cloneRoot.offsetMin = new Vector2(0f, -22f);
            view.cloneRoot.offsetMax = new Vector2(0f, -8f);
            AddImage(view.cloneRoot, new Color(0.08f, 0.04f, 0.1f, 0.9f));
            view.cloneFill = NewRect(view.cloneRoot, "Fill", Vector2.zero, Vector2.one);
            view.cloneFill.offsetMin = new Vector2(2f, 2f);
            view.cloneFill.offsetMax = new Vector2(-2f, -2f);
            AddImage(view.cloneFill, new Color(0.6f, 0.35f, 0.85f, 1f));
            view.cloneRoot.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
            return view;
        }

        private static BossHealthBarView CreateFromFrame(RectTransform root, Image frameImage, Font font)
        {
            var view = root.gameObject.AddComponent<BossHealthBarView>();
            frameImage.raycastTarget = false;
            Transform overlay = root.Find("Overlay");

            // 261114 - 틀의 실제 빈 칸(SlicedHole 여백)에 꽉 맞춘다. 없으면 9분할 테두리 기준.
            RectTransform inner = NewRect(root, "Inner", Vector2.zero, Vector2.one);
            Vector4 hole = root.TryGetComponent(out CheonMyeong.UI.SlicedHole marker) ? marker.holePixels : Vector4.zero;
            CheonMyeong.UI.SlicedHole.Fit(frameImage, inner, hole);
            AddImage(inner, new Color(0.18f, 0.06f, 0.08f, 1f));
            view.chip = NewRect(inner, "Chip", Vector2.zero, Vector2.one);
            AddImage(view.chip, new Color(1f, 0.85f, 0.6f, 0.9f));
            view.fill = NewRect(inner, "Fill", Vector2.zero, Vector2.one);
            AddImage(view.fill, new Color(0.78f, 0.1f, 0.12f, 1f));
            // 261114 - 덮개(Bar-Sheet_11)는 칸 왼쪽을 막아서 바가 덜 차 보였다 - 켜져 있을 때만 쓴다(씬에선 꺼둠).
            if (overlay != null && overlay.gameObject.activeSelf)
            {
                overlay.SetAsLastSibling();
                if (overlay.TryGetComponent(out Image overlayImage))
                {
                    overlayImage.raycastTarget = false;
                }
            }

            // 글자 · 분신 바: 틀은 배율(아트 3.2배)이 걸려 있어서 그 안에 그대로 두면 글자도 커진다 -
            // 배율을 되돌린 칸(Labels)을 틀과 같은 화면 크기로 만들고 그 안쪽 칸(Area) 기준으로 붙인다.
            float scale = Mathf.Max(0.0001f, root.localScale.x);
            RectTransform labels = NewRect(root, "Labels", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            labels.sizeDelta = root.rect.size * scale;
            labels.localScale = Vector3.one / scale;
            RectTransform area = NewRect(labels, "Area", Vector2.zero, Vector2.one);
            area.offsetMin = inner.offsetMin * scale;
            area.offsetMax = inner.offsetMax * scale;

            // 261114 - 글자가 나무 틀과 겹치지 않게: 이름은 틀 위, 칸 수(×3)는 틀 오른쪽 끝 밖, 분신 바는 틀 아래.
            float spritePixel = scale / Mathf.Max(0.0001f, frameImage.pixelsPerUnit * frameImage.pixelsPerUnitMultiplier);
            float woodAbove = FrameWoodPixels * spritePixel;
            float rightEnd = -area.offsetMax.x;
            view.nameText = AddText(NewRect(area, "Name", new Vector2(0f, 1f), new Vector2(1f, 1f)), font, 24, TextAnchor.LowerCenter);
            ((RectTransform)view.nameText.transform).offsetMin = new Vector2(0f, woodAbove);
            ((RectTransform)view.nameText.transform).offsetMax = new Vector2(0f, woodAbove + 32f);
            view.barsText = AddText(NewRect(area, "Bars", new Vector2(1f, 0f), new Vector2(1f, 1f)), font, 24, TextAnchor.MiddleLeft);
            ((RectTransform)view.barsText.transform).offsetMin = new Vector2(rightEnd + 8f, -20f);
            ((RectTransform)view.barsText.transform).offsetMax = new Vector2(rightEnd + 80f, 20f);

            view.cloneRoot = NewRect(area, "Clone", new Vector2(0.25f, 0f), new Vector2(0.75f, 0f));
            view.cloneRoot.offsetMin = new Vector2(0f, -woodAbove - 18f);
            view.cloneRoot.offsetMax = new Vector2(0f, -woodAbove - 4f);
            AddImage(view.cloneRoot, new Color(0.08f, 0.04f, 0.1f, 0.9f));
            view.cloneFill = NewRect(view.cloneRoot, "Fill", Vector2.zero, Vector2.one);
            view.cloneFill.offsetMin = new Vector2(2f, 2f);
            view.cloneFill.offsetMax = new Vector2(-2f, -2f);
            AddImage(view.cloneFill, new Color(0.6f, 0.35f, 0.85f, 1f));
            view.cloneRoot.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
            return view;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform deeper = FindDeep(child, childName);
                if (deeper != null)
                {
                    return deeper;
                }
            }
            return null;
        }

        /// <summary>매 프레임 보스가 부른다. health/maxHealth는 전체, bars는 칸 수(1 = 칸 없음).</summary>
        public void Refresh(string title, float health, float maxHealth, int bars, bool cloneVisible, float cloneRatio)
        {
            bars = Mathf.Max(1, bars);
            float perBar = maxHealth / bars;
            int barIndex = health <= 0f ? 0 : Mathf.Clamp(Mathf.CeilToInt(health / perBar) - 1, 0, bars - 1);
            float ratio = perBar > 0f ? Mathf.Clamp01((health - barIndex * perBar) / perBar) : 0f;

            if (barIndex != lastBarIndex)
            {
                // 칸이 넘어가면 잔상을 새 칸 기준으로 다시 채운다.
                lastBarIndex = barIndex;
                chipRatio = ratio;
                shownRatio = ratio;
            }
            if (ratio < shownRatio)
            {
                chipTimer = ChipDelay;
            }
            shownRatio = ratio;

            if (chipTimer > 0f)
            {
                chipTimer -= Time.deltaTime;
            }
            else if (chipRatio > shownRatio)
            {
                chipRatio = Mathf.Max(shownRatio, chipRatio - ChipSpeed * Time.deltaTime);
            }
            chipRatio = Mathf.Max(chipRatio, shownRatio);

            fill.anchorMax = new Vector2(shownRatio, 1f);
            chip.anchorMax = new Vector2(chipRatio, 1f);
            if (nameText.text != title)
            {
                nameText.text = title;
            }
            string barsLabel = bars > 1 ? $"×{barIndex + 1}" : string.Empty;
            if (barsText.text != barsLabel)
            {
                barsText.text = barsLabel;
            }

            if (cloneRoot.gameObject.activeSelf != cloneVisible)
            {
                cloneRoot.gameObject.SetActive(cloneVisible);
            }
            if (cloneVisible)
            {
                cloneFill.anchorMax = new Vector2(Mathf.Clamp01(cloneRatio), 1f);
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
                lastBarIndex = -1;
            }
        }

        private static RectTransform NewRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text AddText(RectTransform rt, Font font, int size, TextAnchor anchor)
        {
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(1f, 0.95f, 0.85f, 1f);
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Outline outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            return text;
        }
    }
}
