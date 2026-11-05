using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261105 수정 - 인게임 HUD에 이미 있는 신통력 칸(AugmentSlotsContainer/AugmentSlot_0~5)을 채운다. 새 UI는 만들지 않는다.
    /// - AugmentSlot_i/StageDots: 단계(●=찬 단계, ○=빈 단계, 5단계는 MAX).
    /// - AugmentSlot_i/NameText: 신통력 이름. 와이어프레임에 없으면 그 칸 안에만 텍스트 하나를 붙인다.
    /// - AugmentSlot_i/Ring: 전용 신통력이면 테두리 색을 바꾼다.
    /// - AugmentSlotsCaption: 필요 없어서(예섭) 실행 중엔 숨긴다.
    /// 이름으로 찾기 때문에 아트가 모양을 바꿔도 이름만 같으면 그대로 붙는다.
    /// </summary>
    public class AugmentHudView : MonoBehaviour
    {
        private const string ContainerName = "AugmentSlotsContainer";
        private const string SlotPrefix = "AugmentSlot_";
        private const string DotsName = "StageDots";
        private const string NameTextName = "NameText";
        private const string RingName = "Ring";
        private const string CaptionName = "AugmentSlotsCaption";

        private static readonly Color ExclusiveRingColor = new Color(0.55f, 0.75f, 1f, 1f);

        private PlayerProgress owner;
        private Text[] dotTexts;
        private Text[] nameTexts;
        private Image[] rings;
        private Color[] ringDefaultColors;
        private readonly System.Text.StringBuilder builder = new System.Text.StringBuilder(64);

        public static AugmentHudView Create(Transform hudRoot, PlayerProgress owner)
        {
            var view = hudRoot.gameObject.AddComponent<AugmentHudView>();
            view.Bind(hudRoot, owner);
            return view;
        }

        private void Bind(Transform hudRoot, PlayerProgress progress)
        {
            owner = progress;
            int slots = Mathf.Max(1, PlayerProgress.Catalog.maxSlots);
            dotTexts = new Text[slots];
            nameTexts = new Text[slots];
            rings = new Image[slots];
            ringDefaultColors = new Color[slots];

            Transform container = FindDeep(hudRoot, ContainerName);
            if (container == null)
            {
                Debug.LogWarning("[CheonMyeong] HUD에서 " + ContainerName + "를 못 찾아 신통력 칸을 표시하지 않습니다.");
            }
            else
            {
                for (int i = 0; i < slots; i++)
                {
                    Transform slot = FindDeep(container, SlotPrefix + i);
                    if (slot == null)
                    {
                        continue;
                    }
                    Transform dots = FindDeep(slot, DotsName);
                    if (dots != null)
                    {
                        dots.TryGetComponent(out dotTexts[i]);
                    }
                    Transform ring = FindDeep(slot, RingName);
                    if (ring != null && ring.TryGetComponent(out rings[i]))
                    {
                        ringDefaultColors[i] = rings[i].color;
                    }
                    Transform nameTransform = FindDeep(slot, NameTextName);
                    if (nameTransform != null)
                    {
                        nameTransform.TryGetComponent(out nameTexts[i]);
                    }
                    else
                    {
                        nameTexts[i] = CreateNameText(slot, dotTexts[i]);
                    }
                }
            }

            Transform caption = FindDeep(hudRoot, CaptionName);
            if (caption != null)
            {
                caption.gameObject.SetActive(false);
                AlignToBottom(container as RectTransform, caption as RectTransform);
            }

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
            if (owner == null || dotTexts == null)
            {
                return;
            }
            AugmentCatalogSO catalog = PlayerProgress.Catalog;
            int maxLevel = Mathf.Max(1, catalog.maxLevel);

            for (int i = 0; i < dotTexts.Length; i++)
            {
                bool has = i < owner.SlotCount;
                AugmentSlot slot = has ? owner.GetSlot(i) : default;
                AugmentEntry entry = has ? catalog.Find(slot.Augment) : null;

                if (dotTexts[i] != null)
                {
                    if (entry != null && slot.Level >= maxLevel)
                    {
                        dotTexts[i].text = "MAX";
                    }
                    else
                    {
                        builder.Clear();
                        int level = entry != null ? slot.Level : 0;
                        for (int d = 0; d < maxLevel; d++)
                        {
                            builder.Append(d < level ? '●' : '○');
                        }
                        dotTexts[i].text = builder.ToString();
                    }
                }
                if (nameTexts[i] != null)
                {
                    nameTexts[i].text = entry != null ? entry.displayName : string.Empty;
                }
                if (rings[i] != null)
                {
                    rings[i].color = entry != null && entry.kind == AugmentKind.Exclusive ? ExclusiveRingColor : ringDefaultColors[i];
                }
            }
        }

        /// <summary>
        /// 261105 - 캡션을 숨긴 자리만큼 칸 줄을 아래로 내린다(높이 유지). 아래 여백 = 왼쪽 여백.
        /// </summary>
        private static void AlignToBottom(RectTransform container, RectTransform caption)
        {
            if (container == null || caption == null || container.parent != caption.parent)
            {
                return;
            }
            float anchorShift = container.anchorMin.y - caption.anchorMin.y;
            if (anchorShift <= 0f)
            {
                return;
            }
            container.anchorMin -= new Vector2(0f, anchorShift);
            container.anchorMax -= new Vector2(0f, anchorShift);
            float offsetShift = container.offsetMin.x - container.offsetMin.y;
            container.offsetMin += new Vector2(0f, offsetShift);
            container.offsetMax += new Vector2(0f, offsetShift);
        }

        private static Text CreateNameText(Transform slot, Text styleSource)
        {
            var go = new GameObject(NameTextName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(slot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 20f);
            rect.offsetMax = new Vector2(-4f, -4f);
            Text text = go.GetComponent<Text>();
            text.font = styleSource != null && styleSource.font != null
                ? styleSource.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = styleSource != null ? styleSource.color : Color.white;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
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
    }
}
