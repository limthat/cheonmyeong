using CheonMyeong.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// 261121 추가 - 기획서 v1.2 「UI 버튼 · 바」 바 규칙(오방색 한 색씩).
    /// - 체력(나 · 동료): 적 #B42C3B. 두 사람은 색이 아니라 얼굴 · 이름으로 구분(P2도 적). 잔상 백 · 0.3초, 30% 이하 천천히 깜빡(Juice).
    /// - 호신부: 백 #FCF6F6 - 체력 바 위 작은 네모 칸, 1회 = 1칸, 쓰면 빈 칸(테두리만). 예전 "호신부 N" 글자는 뺐다.
    /// - 영혼(경험치): 청 #2D518B.
    /// - 원한 4칸: 먹 #080710, 넘긴 단계 칸은 테두리 짙은 적 #882131, 폭주면 4칸 전부 짙은 적 테두리.
    /// - 보스: 어린 억쇠는 황(BossHealthBarView), 억쇠 최종전은 예섭 요청대로 3겹 색 유지.
    /// 씬 파일은 건드리지 않고 켜질 때 색만 바꾼다(아트 틀 · 배치는 그대로).
    /// </summary>
    public partial class InGameHudScreen
    {
        private static readonly Color HpRed = new Color32(0xB4, 0x2C, 0x3B, 0xFF);
        private static readonly Color DeepRed = new Color32(0x88, 0x21, 0x31, 0xFF);
        private static readonly Color HanjiWhite = new Color32(0xFC, 0xF6, 0xF6, 0xFF);
        private static readonly Color SoulBlue = new Color32(0x2D, 0x51, 0x8B, 0xFF);
        private static readonly Color Ink = new Color32(0x08, 0x07, 0x10, 0xFF);
        private static readonly Color PipEmpty = new Color32(0x61, 0x47, 0x3A, 0xFF); // 빈 곳 짙은 갈 #61473A
        private const float LowHealthRatio = 0.3f;
        private const float LowHealthBlinkPeriod = 1.2f;
        private const float PipSize = 14f;
        private const float PipGap = 4f;
        private const float PipBorder = 2f;
        private const float PipAreaWidth = 120f; // 글자 오른쪽 끝을 이만큼 줄여 네모 칸 자리로(글자와 안 겹치게)
        private const int MaxPips = 8;

        private RectTransform[] pipRoots = new RectTransform[2];
        private Image[][] pipFills = new Image[2][];
        private Image[][] pipFrames = new Image[2][];
        private Color[] grudgeFrameColors;
        private int grudgeBorderStage = -1;

        private void BarsAwake()
        {
            if (p1HealthFillImage != null) p1HealthFillImage.color = HpRed;
            if (p2HealthFillImage != null) p2HealthFillImage.color = HpRed;
            if (expFillImage != null) expFillImage.color = SoulBlue;
            if (grudgeSegmentImages != null)
            {
                grudgeSegmentFullColors = new Color[grudgeSegmentImages.Length];
                grudgeFrameColors = new Color[grudgeSegmentImages.Length];
                for (int i = 0; i < grudgeSegmentImages.Length; i++)
                {
                    grudgeSegmentFullColors[i] = Ink;
                    grudgeFrameColors[i] = grudgeSegmentImages[i] != null ? grudgeSegmentImages[i].color : Color.white;
                }
            }
            BuildShieldPips(0, p1HealthText);
            BuildShieldPips(1, p2HealthText);
        }

        /// <summary>체력 글자 줄 오른쪽 끝에 호신부 네모 칸 자리를 만든다(글자 칸은 그만큼 줄여 겹치지 않게).</summary>
        private void BuildShieldPips(int slot, Text label)
        {
            if (label == null)
            {
                return;
            }
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMax = new Vector2(labelRect.offsetMax.x - PipAreaWidth, labelRect.offsetMax.y);

            var rootGo = new GameObject("ShieldPips", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(labelRect.parent, false);
            root.anchorMin = labelRect.anchorMin;
            root.anchorMax = labelRect.anchorMax;
            root.pivot = new Vector2(0f, 0.5f);
            // 줄인 글자 칸 바로 오른쪽, 같은 줄 높이.
            root.offsetMin = new Vector2(labelRect.offsetMax.x + 6f, labelRect.offsetMin.y);
            root.offsetMax = new Vector2(labelRect.offsetMax.x + 6f + PipAreaWidth, labelRect.offsetMax.y);
            root.SetSiblingIndex(labelRect.GetSiblingIndex() + 1);
            pipRoots[slot] = root;
            pipFills[slot] = new Image[MaxPips];
            pipFrames[slot] = new Image[MaxPips];
            for (int i = 0; i < MaxPips; i++)
            {
                var frameGo = new GameObject("Pip" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var frameRect = (RectTransform)frameGo.transform;
                frameRect.SetParent(root, false);
                frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0.5f);
                frameRect.pivot = new Vector2(0f, 0.5f);
                frameRect.sizeDelta = new Vector2(PipSize, PipSize);
                frameRect.anchoredPosition = new Vector2(i * (PipSize + PipGap), 0f);
                Image frame = frameGo.GetComponent<Image>();
                frame.color = HanjiWhite; // 테두리(백)
                frame.raycastTarget = false;

                var fillGo = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var fillRect = (RectTransform)fillGo.transform;
                fillRect.SetParent(frameRect, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = new Vector2(PipBorder, PipBorder);
                fillRect.offsetMax = new Vector2(-PipBorder, -PipBorder);
                Image fill = fillGo.GetComponent<Image>();
                fill.raycastTarget = false;
                fill.color = PipEmpty;

                pipFrames[slot][i] = frame;
                pipFills[slot][i] = fill;
                frameGo.SetActive(false);
            }
        }

        private void UpdateShieldPips(int slot, HealthSlotCache cache)
        {
            Image[] fills = pipFills[slot];
            if (fills == null)
            {
                return;
            }
            PlayerHealth player = slot < PlayerHealth.ActivePlayers.Count ? PlayerHealth.ActivePlayers[slot] : null;
            CharacterKit kit = null;
            if (player != null)
            {
                player.TryGetComponent(out kit);
            }
            int capacity = kit != null && player != null && !player.IsDead ? Mathf.Min(MaxPips, kit.ShieldCapacity) : 0;
            int charges = kit != null ? Mathf.Min(capacity, kit.ShieldCharges) : 0;
            if (cache.ShieldCapacity == capacity && cache.PipCharges == charges)
            {
                return;
            }
            cache.ShieldCapacity = capacity;
            cache.PipCharges = charges;
            for (int i = 0; i < fills.Length; i++)
            {
                GameObject pip = pipFrames[slot][i].gameObject;
                bool show = i < capacity;
                if (pip.activeSelf != show)
                {
                    pip.SetActive(show);
                }
                if (show)
                {
                    // 찬 칸 = 백으로 꽉, 쓴 칸 = 테두리만(안은 빈 곳 색).
                    fills[i].color = i < charges ? HanjiWhite : PipEmpty;
                }
            }
        }

        /// <summary>원한 칸 테두리 - 넘긴 단계 칸은 짙은 적, 폭주(마지막 칸)에 들어가면 4칸 전부 짙은 적.</summary>
        private void ApplyGrudgeBorders(int activeStage)
        {
            if (grudgeSegmentImages == null || grudgeFrameColors == null || activeStage == grudgeBorderStage)
            {
                return;
            }
            grudgeBorderStage = activeStage;
            bool rampage = activeStage >= grudgeSegmentImages.Length - 1;
            for (int i = 0; i < grudgeSegmentImages.Length; i++)
            {
                Image segment = grudgeSegmentImages[i];
                if (segment == null)
                {
                    continue;
                }
                bool framed = segment.sprite != null && segment.type == Image.Type.Sliced && segment.sprite.border.sqrMagnitude > 0f;
                if (!framed)
                {
                    continue; // 틀 없는 임시 칸은 Juice가 칠한 흐린 바탕 그대로
                }
                segment.color = rampage || i < activeStage ? DeepRed : grudgeFrameColors[i];
            }
        }
    }
}
