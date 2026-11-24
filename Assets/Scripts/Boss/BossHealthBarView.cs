using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261101 추가 - 화면 위 보스 체력 바. 인게임 HUD 화면 안에 실행 중에 직접 만든다(에디터 빌드 메뉴 없이도 동작).
    /// - 칸(최종전 3칸 × 8,400): 지금 깎이는 칸만 바로 보여주고, 남은 칸 수는 오른쪽에 "×N"으로 표시.
    /// - 피해 잔상: 맞으면 빨간 바는 바로 줄고, 뒤의 밝은 바가 잠깐 멈췄다가 따라 줄어든다(요즘 보스 체력 바의 흔한 연출).
    /// - 261119 겹 체력(로스트아크식): 칸마다 색이 다르고, 지금 칸이 줄어든 자리 뒤로 다음 칸 색이 보인다. 마지막 칸은 어두운 바탕.
    /// - 분신 체력: 261119 - 정신병 차원이 열려 있으면 본 체력 바 위를 보라색 분신 바로 덮어 보인다.
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
        // 261123 - 지원 피드백: 보스 체력 수치 표기(전체 체력 "현재 / 최대"). 값이 바뀔 때만 글자를 새로 만든다.
        private Text valueText;
        private int shownValue = int.MinValue;
        private int shownMaxValue = int.MinValue;
        private float shownRatio = 1f;
        private float chipRatio = 1f;
        private float chipTimer;
        private int lastBarIndex = -1;
        private bool fromFrame;
        private bool released;
        private RectTransform frameInner;
        private RectTransform frameLabels;
        private Image backImage;
        private Image fillImage;
        private int shownColorIndex = -1;
        // 261121 - 어린 억쇠: 같은 틀 자리에 머리 + 빗자루 그림(Bar-Sheet_12)으로 바꿔 쓰고, 1칸 바는 황(기획서 v1.2).
        private Image frameImageRef;
        private Sprite originalFrameSprite;
        private bool frameSpriteSwapped;
        private bool hasSingleBarColor;
        private Color singleBarColor;

        // 261119 - 바 아래에 보스가 있으면 바를 반투명(30%)하게 - 바가 보스를 가리지 않게.
        private const float CoveredAlpha = 0.3f;
        private const float FadeSpeed = 4f; // 초당 알파 변화
        private CanvasGroup group;
        private bool bossBoundsValid;
        private Bounds bossBounds;
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>261119 - 보스 몸의 월드 경계(Refresh 전에 매 프레임).</summary>
        public void SetBossBounds(bool valid, Bounds bounds)
        {
            bossBoundsValid = valid;
            bossBounds = bounds;
        }

        private void UpdateCoverFade()
        {
            if (group == null && !TryGetComponent(out group))
            {
                group = gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            float target = IsBossUnderBar() ? CoveredAlpha : 1f;
            if (!Mathf.Approximately(group.alpha, target))
            {
                group.alpha = Mathf.MoveTowards(group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
            }
        }

        /// <summary>바(화면 오버레이 캔버스 = 월드 좌표가 곧 화면 픽셀)와 보스의 화면 위치가 겹치는지.</summary>
        private bool IsBossUnderBar()
        {
            Camera cam = Camera.main;
            if (!bossBoundsValid || cam == null)
            {
                return false;
            }
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            ((RectTransform)transform).GetWorldCorners(Corners);
            Vector2 barMin = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[0]);
            Vector2 barMax = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[2]);
            Vector2 bossMin = cam.WorldToScreenPoint(bossBounds.min);
            Vector2 bossMax = cam.WorldToScreenPoint(bossBounds.max);
            return bossMax.x >= barMin.x && bossMin.x <= barMax.x && bossMax.y >= barMin.y && bossMin.y <= barMax.y;
        }

        // 261119 - 칸 색(위에서부터 깎이는 순서, 마지막이 적). 오방색 청 → 황 → 적, 칸이 더 많으면 앞에서 되풀이.
        private static readonly Color[] LayerColors =
        {
            new Color(0.78f, 0.10f, 0.12f, 1f), // 마지막 칸(×1) 적
            new Color(0.86f, 0.60f, 0.12f, 1f), // ×2 황
            new Color(0.20f, 0.45f, 0.82f, 1f), // ×3 청
        };
        private static readonly Color EmptyBackColor = new Color(0.18f, 0.06f, 0.08f, 1f);
        private static readonly Color CloneColor = new Color(0.6f, 0.35f, 0.85f, 1f);

        private static Color LayerColor(int barIndex) => LayerColors[Mathf.Abs(barIndex) % LayerColors.Length];

        // 261114 - 틀 그림(Bar-Sheet_10)에서 빈 칸 위 · 아래로 보이는 나무 두께(원본 픽셀). 글자를 이만큼 띄운다.
        private const float FrameWoodPixels = 9f;

        public static BossHealthBarView Create(Transform hudRoot)
        {
            return Create(hudRoot, null, Vector4.zero, null);
        }

        /// <summary>
        /// 261121 - frameSprite가 있으면 씬 틀(BossBarFrame)의 그림을 잠시 바꿔 쓰고(빈 칸 여백 = hole, 원본 픽셀 왼 · 아래 · 오른 · 위),
        /// singleColor가 있으면 1칸 바를 그 색으로 칠한다. Release 때 원래 그림으로 되돌린다.
        /// </summary>
        public static BossHealthBarView Create(Transform hudRoot, Sprite frameSprite, Vector4 hole, Color? singleColor)
        {
            Font font = CheonMyeong.UI.GameFontSet.Get(CheonMyeong.UI.FontRole.Title);
            BossHealthBarView created = CreateInternal(hudRoot, font, frameSprite, hole);
            if (created != null && singleColor.HasValue)
            {
                created.hasSingleBarColor = true;
                created.singleBarColor = singleColor.Value;
            }
            return created;
        }

        private static BossHealthBarView CreateInternal(Transform hudRoot, Font font, Sprite frameSprite, Vector4 holeOverride)
        {

            // 261114 - 아트 보스 바 틀(HUD의 BossBarFrame, Bar-Sheet_10 + 위 덮개 Overlay)이 있으면 그 틀 안쪽 칸을 채운다.
            Transform template = FindDeep(hudRoot, "BossBarFrame");
            if (template is RectTransform frameRoot && frameRoot.TryGetComponent(out Image frameImage))
            {
                if (frameRoot.TryGetComponent(out BossHealthBarView existing) && !existing.released)
                {
                    return existing;
                }
                Sprite original = frameImage.sprite;
                bool swapped = frameSprite != null && frameSprite != original;
                if (swapped)
                {
                    frameImage.sprite = frameSprite;
                }
                BossHealthBarView fromFrameView = CreateFromFrame(frameRoot, frameImage, font, swapped ? holeOverride : (Vector4?)null);
                fromFrameView.frameImageRef = frameImage;
                fromFrameView.originalFrameSprite = original;
                fromFrameView.frameSpriteSwapped = swapped;
                return fromFrameView;
            }

            // 261117 - 틀이 없을 때의 임시 바도 아트 틀과 같은 자리(왼쪽 위 플레이어 체력 바 아래)에 둔다.
            RectTransform root = NewRect(hudRoot, "BossHealthBar", new Vector2(0f, 1f), new Vector2(0f, 1f));
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(475f, 24f);
            root.anchoredPosition = new Vector2(24f, -250f);
            var view = root.gameObject.AddComponent<BossHealthBarView>();

            Image frame = AddImage(root, new Color(0.05f, 0.03f, 0.05f, 0.9f));
            frame.raycastTarget = false;
            RectTransform inner = NewRect(root, "Inner", Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            view.BuildLayers(inner);

            view.nameText = AddText(NewRect(root, "Name", new Vector2(0f, 1f), new Vector2(1f, 1f)), font, 20, TextAnchor.LowerCenter);
            ((RectTransform)view.nameText.transform).offsetMin = new Vector2(0f, 2f);
            ((RectTransform)view.nameText.transform).offsetMax = new Vector2(0f, 30f);
            view.barsText = AddText(NewRect(root, "Bars", new Vector2(1f, 0f), new Vector2(1f, 1f)), font, 20, TextAnchor.MiddleLeft);
            ((RectTransform)view.barsText.transform).offsetMin = new Vector2(10f, 0f);
            ((RectTransform)view.barsText.transform).offsetMax = new Vector2(70f, 0f);
            view.valueText = AddText(NewRect(inner, "Value", Vector2.zero, Vector2.one), font, 12, TextAnchor.MiddleCenter);

            root.gameObject.SetActive(false);
            return view;
        }

        /// <summary>261119 - 칸 안쪽: 바탕(다음 칸 색) · 잔상 · 지금 칸 · 분신 덮개(보라).</summary>
        private void BuildLayers(RectTransform inner)
        {
            backImage = AddImage(inner, EmptyBackColor);
            chip = NewRect(inner, "Chip", Vector2.zero, Vector2.one);
            AddImage(chip, new Color(1f, 0.85f, 0.6f, 0.9f));
            fill = NewRect(inner, "Fill", Vector2.zero, Vector2.one);
            fillImage = AddImage(fill, LayerColor(0));
            cloneRoot = NewRect(inner, "Clone", Vector2.zero, Vector2.one);
            AddImage(cloneRoot, new Color(0.10f, 0.05f, 0.14f, 1f));
            cloneFill = NewRect(cloneRoot, "Fill", Vector2.zero, Vector2.one);
            AddImage(cloneFill, CloneColor);
            cloneRoot.gameObject.SetActive(false);
        }

        private static BossHealthBarView CreateFromFrame(RectTransform root, Image frameImage, Font font, Vector4? holeOverride)
        {
            var view = root.gameObject.AddComponent<BossHealthBarView>();
            view.fromFrame = true;
            frameImage.raycastTarget = false;
            Transform overlay = root.Find("Overlay");

            // 261114 - 틀의 실제 빈 칸(SlicedHole 여백)에 꽉 맞춘다. 없으면 9분할 테두리 기준.
            RectTransform inner = NewRect(root, "Inner", Vector2.zero, Vector2.one);
            Vector4 hole = holeOverride ?? (root.TryGetComponent(out CheonMyeong.UI.SlicedHole marker) ? marker.holePixels : Vector4.zero);
            CheonMyeong.UI.SlicedHole.Fit(frameImage, inner, hole);
            view.frameInner = inner;
            view.BuildLayers(inner);
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
            view.frameLabels = labels;
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
            // 261123 - 칸 가운데 체력 수치. 칸 높이(화면 px)에 맞춰 12의 배수 크기(Galmuri11)로.
            float areaHeight = labels.sizeDelta.y + area.offsetMax.y - area.offsetMin.y;
            int valueSize = areaHeight >= 30f ? 24 : 12;
            view.valueText = AddText(NewRect(area, "Value", Vector2.zero, Vector2.one), font, valueSize, TextAnchor.MiddleCenter);

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
            UpdateCoverFade();
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
            if (shownColorIndex != barIndex && fillImage != null)
            {
                // 지금 칸 색 + 그 뒤로 보이는 다음 칸 색(마지막 칸이면 어두운 바탕).
                shownColorIndex = barIndex;
                fillImage.color = bars == 1 && hasSingleBarColor ? singleBarColor : LayerColor(barIndex);
                backImage.color = barIndex > 0 ? LayerColor(barIndex - 1) : EmptyBackColor;
            }
            if (nameText.text != title)
            {
                nameText.text = title;
            }
            int value = Mathf.Max(0, Mathf.CeilToInt(health));
            int maxValue = Mathf.Max(1, Mathf.RoundToInt(maxHealth));
            if (valueText != null && (value != shownValue || maxValue != shownMaxValue))
            {
                shownValue = value;
                shownMaxValue = maxValue;
                valueText.text = $"{value:N0} / {maxValue:N0}";
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

        /// <summary>
        /// 261117 - 보스가 사라질 때. 씬 틀을 쓰는 경우 틀(BossBarFrame)은 남기고 이 바가 붙인 칸 · 글자 · 컴포넌트만 걷어
        /// 다음 보스가 같은 틀을 다시 쓸 수 있게 한다. 임시 바면 통째로 지운다.
        /// </summary>
        public void Release()
        {
            released = true;
            if (!fromFrame)
            {
                Destroy(gameObject);
                return;
            }
            if (frameInner != null)
            {
                Destroy(frameInner.gameObject);
            }
            if (frameLabels != null)
            {
                Destroy(frameLabels.gameObject);
            }
            if (group != null)
            {
                group.alpha = 1f; // 다음 보스가 같은 틀을 쓸 때 투명한 채로 시작하지 않게
            }
            if (frameSpriteSwapped && frameImageRef != null)
            {
                frameImageRef.sprite = originalFrameSprite; // 261121 - 어린 억쇠 틀 → 억쇠 틀로 되돌림
            }
            gameObject.SetActive(false);
            Destroy(this);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
                lastBarIndex = -1;
                shownColorIndex = -1;
                shownValue = int.MinValue;
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
            text.fontStyle = FontStyle.Normal;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Outline outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            return text;
        }
    }
}
