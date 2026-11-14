using CheonMyeong.Player;
using CheonMyeong.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI.Screens
{
    /// <summary>
    /// 261110 추가 - HUD 손맛(로컬 연출 전용, 서버 값은 건드리지 않는다).
    ///
    /// 체력(피격):
    /// - 지연 감소 바(chip): 실제 체력은 바로 줄고, 뒤의 흰 바가 잠깐 머물렀다가 따라 내려간다 → 얼마나 깎였는지 보인다.
    /// - 바 흔들림 + 채움 색 흰 번쩍.
    /// - 내 캐릭터만: 화면 가장자리 붉은 비네트(맞은 비율만큼 진하게, 서서히 사라짐), 크게 맞으면(최대 체력의 12% 이상) 카메라 흔들림.
    /// - 회복은 흰 바가 바로 맞춰진다(연출 없음).
    /// 히트스톱(Time.timeScale)은 2인 동기화 게임이라 넣지 않았다.
    ///
    /// 원한 게이지:
    /// - 4칸(맑음/짙음/한/폭주)이 실제 원한 수에 따라 칸 안에서 비율대로 차오른다. 칸 구간 = PartyProgress.GrudgeThresholds.
    ///   폭주 칸은 끝이 없으니 (마지막 문턱 - 그 전 문턱)만큼을 한 칸으로 본다.
    /// - 표시값은 실제 값을 지수 감쇠로 따라간다(숫자 · 게이지 같이).
    /// - GrudgeAbsorbFx가 HoldGrudgeDisplay로 잠깐 멈춰두고, 구슬이 닿을 때마다 OnGrudgeOrbArrived → 풀림 + 펀치 + 흰 번쩍.
    /// 칸 Fill · 흰 바 · 비네트는 실행 중에 만들고(아트 씬 파일과 안 겹치게) 텍스처는 OnDestroy에서 해제한다.
    /// </summary>
    public partial class InGameHudScreen
    {
        private const float ChipHoldSeconds = 0.4f;
        private const float ChipDrainPerSecond = 0.6f;
        private const float BarShakeSeconds = 0.25f;
        private const float BarShakePixels = 6f;
        private const float FillFlashSeconds = 0.12f;
        private const float VignetteMaxAlpha = 0.55f;
        private const float VignetteFadePerSecond = 1.4f;
        private const float BigHitRatio = 0.12f;
        private static readonly Color ChipColor = new Color(1f, 0.93f, 0.85f, 0.9f);
        private static readonly Color VignetteColor = new Color(0.75f, 0.05f, 0.05f, 1f);

        private const float GrudgeFollowSharpness = 8f;
        private const float GrudgeMaxHoldSeconds = 3f;
        private const float GrudgePunchSeconds = 0.18f;
        private const float GrudgePunchScale = 0.12f;
        private const float GrudgeFlashSeconds = 0.2f;
        private const float GrudgeSegmentBackAlpha = 0.25f;
        private static readonly int[] DefaultGrudgeThresholds = { 200, 500, 1000 };

        private class HealthJuice
        {
            public Image Fill;
            public Image Chip;
            public RectTransform ShakeTarget;
            public Vector2 ShakeBase;
            public Color FillColor;
            public float LastRatio = -1f;
            public float ChipRatio;
            public float HoldLeft;
            public float ShakeLeft;
            public float FlashLeft;
        }

        private readonly HealthJuice[] healthJuice = { new HealthJuice(), new HealthJuice() };
        private Image vignette;
        private Texture2D vignetteTexture;
        private Sprite vignetteSprite;
        private float vignetteAlpha;

        private RectTransform[] grudgeFills;
        private RectTransform grudgeGauge;
        private Image grudgeFlash;
        private float grudgeTarget;
        private float grudgeShown;
        private float grudgeHoldLeft;
        private float grudgePunchLeft;
        private float grudgeFlashLeft;
        private bool grudgeSnapNext = true;
        private int lastDrawnGrudge = int.MinValue;

        /// <summary>원한 게이지(4칸의 부모) - 흡수 구슬이 날아갈 곳.</summary>
        public RectTransform GrudgeGaugeRect => grudgeGauge;

        private void JuiceAwake()
        {
            SetupHealthJuice(healthJuice[0], p1HealthFillImage);
            SetupHealthJuice(healthJuice[1], p2HealthFillImage);
            SetupVignette();
            SetupGrudgeFills();
        }

        private void JuiceDestroy()
        {
            if (vignetteSprite != null)
            {
                Destroy(vignetteSprite);
            }
            if (vignetteTexture != null)
            {
                Destroy(vignetteTexture);
            }
        }

        private void JuiceResetGrudge()
        {
            grudgeTarget = 0f;
            grudgeShown = 0f;
            grudgeHoldLeft = 0f;
            grudgePunchLeft = 0f;
            grudgeFlashLeft = 0f;
            grudgeSnapNext = true;
            lastDrawnGrudge = int.MinValue;
            if (grudgeGauge != null)
            {
                grudgeGauge.localScale = Vector3.one;
            }
            DrawGrudge();

            for (int i = 0; i < healthJuice.Length; i++)
            {
                HealthJuice juice = healthJuice[i];
                juice.LastRatio = -1f;
                juice.ShakeLeft = 0f;
                juice.FlashLeft = 0f;
                juice.HoldLeft = 0f;
                if (juice.ShakeTarget != null)
                {
                    juice.ShakeTarget.anchoredPosition = juice.ShakeBase;
                }
                if (juice.Fill != null)
                {
                    juice.Fill.color = juice.FillColor;
                }
            }
            vignetteAlpha = 0f;
            if (vignette != null)
            {
                vignette.enabled = false;
            }
        }

        private void SetGrudgeTarget(int value)
        {
            grudgeTarget = value;
            if (grudgeSnapNext || value < grudgeShown)
            {
                // 첫 값(켜질 때 당겨온 값) · 새 판(감소)은 바로 맞춘다.
                grudgeSnapNext = false;
                grudgeShown = value;
                grudgeHoldLeft = 0f;
                DrawGrudge();
            }
        }

        /// <summary>흡수 연출 동안 게이지를 멈춰둔다(최대 3초). 구슬이 닿으면 OnGrudgeOrbArrived가 푼다.</summary>
        public void HoldGrudgeDisplay(float seconds)
        {
            grudgeHoldLeft = Mathf.Min(GrudgeMaxHoldSeconds, Mathf.Max(grudgeHoldLeft, seconds));
        }

        /// <summary>흡수 구슬 하나가 게이지에 닿았다 - 멈춤 해제 + 펀치 + 흰 번쩍.</summary>
        public void OnGrudgeOrbArrived()
        {
            grudgeHoldLeft = 0f;
            grudgePunchLeft = GrudgePunchSeconds;
            grudgeFlashLeft = GrudgeFlashSeconds;
        }

        private void JuiceUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < healthJuice.Length; i++)
            {
                UpdateHealthJuice(healthJuice[i], i, dt);
            }
            UpdateVignette(dt);
            UpdateGrudge(dt);
        }

        // ───────── 체력 ─────────

        private static void SetupHealthJuice(HealthJuice juice, Image fill)
        {
            if (fill == null)
            {
                return;
            }
            juice.Fill = fill;
            juice.FillColor = fill.color;

            var chipGo = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var chipRect = (RectTransform)chipGo.transform;
            var fillRect = fill.rectTransform;
            chipRect.SetParent(fillRect.parent, false);
            chipRect.SetSiblingIndex(fillRect.GetSiblingIndex()); // Fill 바로 뒤(먼저 그려짐)
            chipRect.anchorMin = fillRect.anchorMin;
            chipRect.anchorMax = fillRect.anchorMax;
            chipRect.pivot = fillRect.pivot;
            chipRect.offsetMin = fillRect.offsetMin;
            chipRect.offsetMax = fillRect.offsetMax;

            Image chip = chipGo.GetComponent<Image>();
            chip.sprite = fill.sprite;
            chip.type = fill.type;
            chip.fillMethod = fill.fillMethod;
            chip.fillOrigin = fill.fillOrigin;
            chip.fillAmount = fill.fillAmount;
            chip.pixelsPerUnitMultiplier = fill.pixelsPerUnitMultiplier;
            chip.color = ChipColor;
            chip.raycastTarget = false;
            juice.Chip = chip;

            // 흔들 대상 = 바 전체(Fill의 바 루트: 바/Mask/Fill 구조라 두 단계 위, 없으면 한 단계 위).
            Transform barRoot = fillRect.parent != null && fillRect.parent.parent != null ? fillRect.parent.parent : fillRect.parent;
            juice.ShakeTarget = barRoot as RectTransform;
            if (juice.ShakeTarget != null)
            {
                juice.ShakeBase = juice.ShakeTarget.anchoredPosition;
            }
        }

        private void UpdateHealthJuice(HealthJuice juice, int playerIndex, float dt)
        {
            if (juice.Fill == null)
            {
                return;
            }

            PlayerHealth player = playerIndex < PlayerHealth.ActivePlayers.Count ? PlayerHealth.ActivePlayers[playerIndex] : null;
            float ratio = player != null && !player.IsDead && player.MaxHealth > 0f
                ? Mathf.Clamp01(player.CurrentHealth / player.MaxHealth)
                : 0f;

            if (juice.LastRatio < 0f)
            {
                juice.ChipRatio = ratio; // 처음 - 연출 없이 맞춘다
            }
            else if (ratio > juice.LastRatio)
            {
                juice.ChipRatio = Mathf.Max(juice.ChipRatio, ratio); // 회복 - 흰 바가 바로 따라붙는다
            }
            else if (ratio < juice.LastRatio)
            {
                float lost = juice.LastRatio - ratio;
                juice.HoldLeft = ChipHoldSeconds;
                juice.ShakeLeft = BarShakeSeconds;
                juice.FlashLeft = FillFlashSeconds;
                if (player != null && player.IsOwner)
                {
                    vignetteAlpha = Mathf.Min(VignetteMaxAlpha, vignetteAlpha + Mathf.Lerp(0.2f, VignetteMaxAlpha, lost / BigHitRatio));
                    if (lost >= BigHitRatio)
                    {
                        PlayerCameraRig.Shake(0.15f, 0.18f);
                    }
                }
            }
            juice.LastRatio = ratio;

            // 흰 바: 잠깐 머물렀다가 따라 내려감.
            if (juice.HoldLeft > 0f)
            {
                juice.HoldLeft -= dt;
            }
            else if (juice.ChipRatio > ratio)
            {
                juice.ChipRatio = Mathf.MoveTowards(juice.ChipRatio, ratio, ChipDrainPerSecond * dt);
            }
            if (juice.Chip != null && !Mathf.Approximately(juice.Chip.fillAmount, juice.ChipRatio))
            {
                juice.Chip.fillAmount = juice.ChipRatio;
            }

            // 바 흔들림(감쇠).
            if (juice.ShakeTarget != null)
            {
                if (juice.ShakeLeft > 0f)
                {
                    juice.ShakeLeft -= dt;
                    float strength = BarShakePixels * Mathf.Max(0f, juice.ShakeLeft / BarShakeSeconds);
                    juice.ShakeTarget.anchoredPosition = juice.ShakeBase + new Vector2(
                        (Mathf.PerlinNoise(Time.unscaledTime * 40f, 0f) - 0.5f) * 2f * strength,
                        (Mathf.PerlinNoise(0f, Time.unscaledTime * 40f) - 0.5f) * 2f * strength);
                    if (juice.ShakeLeft <= 0f)
                    {
                        juice.ShakeTarget.anchoredPosition = juice.ShakeBase;
                    }
                }
            }

            // 채움 색 흰 번쩍.
            if (juice.FlashLeft > 0f)
            {
                juice.FlashLeft -= dt;
                float t = Mathf.Max(0f, juice.FlashLeft / FillFlashSeconds);
                juice.Fill.color = Color.Lerp(juice.FillColor, Color.white, t);
            }
        }

        private void SetupVignette()
        {
            const int size = 64;
            vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "HitVignette"
            };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f; // 0 = 가운데, 1 = 모서리
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            vignetteTexture.SetPixels32(pixels);
            vignetteTexture.Apply(false, true); // CPU 사본 해제
            vignetteSprite = Sprite.Create(vignetteTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));

            var go = new GameObject("HitVignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.SetAsFirstSibling(); // HUD 글자 · 바 뒤
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            vignette = go.GetComponent<Image>();
            vignette.sprite = vignetteSprite;
            vignette.raycastTarget = false;
            vignette.color = new Color(VignetteColor.r, VignetteColor.g, VignetteColor.b, 0f);
            vignette.enabled = false;
        }

        private void UpdateVignette(float dt)
        {
            if (vignette == null)
            {
                return;
            }
            if (vignetteAlpha <= 0f)
            {
                if (vignette.enabled)
                {
                    vignette.enabled = false;
                }
                return;
            }
            vignetteAlpha = Mathf.Max(0f, vignetteAlpha - VignetteFadePerSecond * dt);
            if (!vignette.enabled)
            {
                vignette.enabled = true;
            }
            vignette.color = new Color(VignetteColor.r, VignetteColor.g, VignetteColor.b, vignetteAlpha);
        }

        // ───────── 원한 게이지 ─────────

        private void SetupGrudgeFills()
        {
            if (grudgeSegmentImages == null || grudgeSegmentImages.Length == 0)
            {
                return;
            }
            grudgeFills = new RectTransform[grudgeSegmentImages.Length];
            for (int i = 0; i < grudgeSegmentImages.Length; i++)
            {
                Image segment = grudgeSegmentImages[i];
                if (segment == null)
                {
                    continue;
                }
                if (grudgeGauge == null)
                {
                    grudgeGauge = segment.rectTransform.parent as RectTransform;
                }
                Color full = grudgeSegmentFullColors != null && i < grudgeSegmentFullColors.Length
                    ? grudgeSegmentFullColors[i]
                    : segment.color;

                var go = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(segment.rectTransform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Image fill = go.GetComponent<Image>();
                fill.sprite = segment.sprite;
                fill.type = segment.type;
                fill.color = full;
                fill.raycastTarget = false;
                grudgeFills[i] = rect;

                // 칸 바탕은 같은 색을 흐리게 - 어느 단계 칸인지는 보이게.
                segment.color = new Color(full.r, full.g, full.b, full.a * GrudgeSegmentBackAlpha);
            }

            if (grudgeGauge != null)
            {
                var flashGo = new GameObject("GrudgeFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var flashRect = (RectTransform)flashGo.transform;
                flashRect.SetParent(grudgeGauge, false);
                flashRect.SetAsLastSibling();
                flashRect.anchorMin = Vector2.zero;
                flashRect.anchorMax = Vector2.one;
                flashRect.offsetMin = new Vector2(-2f, -2f);
                flashRect.offsetMax = new Vector2(2f, 2f);
                grudgeFlash = flashGo.GetComponent<Image>();
                grudgeFlash.raycastTarget = false;
                grudgeFlash.color = new Color(1f, 1f, 1f, 0f);
                grudgeFlash.enabled = false;
            }
        }

        private void UpdateGrudge(float dt)
        {
            if (grudgeHoldLeft > 0f)
            {
                grudgeHoldLeft -= dt;
            }
            else if (!Mathf.Approximately(grudgeShown, grudgeTarget))
            {
                grudgeShown = Mathf.Lerp(grudgeShown, grudgeTarget, 1f - Mathf.Exp(-GrudgeFollowSharpness * dt));
                if (Mathf.Abs(grudgeTarget - grudgeShown) < 0.5f)
                {
                    grudgeShown = grudgeTarget;
                }
                DrawGrudge();
            }

            if (grudgeGauge != null && grudgePunchLeft > 0f)
            {
                grudgePunchLeft -= dt;
                float t = Mathf.Max(0f, grudgePunchLeft / GrudgePunchSeconds);
                grudgeGauge.localScale = Vector3.one * (1f + GrudgePunchScale * Mathf.Sin(t * Mathf.PI));
                if (grudgePunchLeft <= 0f)
                {
                    grudgeGauge.localScale = Vector3.one;
                }
            }

            if (grudgeFlash != null)
            {
                if (grudgeFlashLeft > 0f)
                {
                    grudgeFlashLeft -= dt;
                    grudgeFlash.enabled = true;
                    grudgeFlash.color = new Color(1f, 1f, 1f, 0.7f * Mathf.Max(0f, grudgeFlashLeft / GrudgeFlashSeconds));
                }
                else if (grudgeFlash.enabled)
                {
                    grudgeFlash.enabled = false;
                }
            }
        }

        private void DrawGrudge()
        {
            int shown = Mathf.RoundToInt(grudgeShown);
            if (shown == lastDrawnGrudge)
            {
                return;
            }
            lastDrawnGrudge = shown;
            DrawGrudgeText(shown);

            if (grudgeFills == null)
            {
                return;
            }
            float lower = 0f;
            for (int i = 0; i < grudgeFills.Length; i++)
            {
                float upper = SegmentUpper(i, lower);
                float ratio = upper > lower ? Mathf.Clamp01((grudgeShown - lower) / (upper - lower)) : 0f;
                RectTransform fill = grudgeFills[i];
                if (fill != null && !Mathf.Approximately(fill.anchorMax.x, ratio))
                {
                    fill.anchorMax = new Vector2(ratio, 1f);
                }
                lower = upper;
            }
        }

        /// <summary>i번째 칸의 끝값. 마지막 칸(폭주)은 (마지막 문턱 - 그 전 문턱)만큼을 한 칸으로 본다.</summary>
        private static float SegmentUpper(int index, float lower)
        {
            System.Collections.Generic.IReadOnlyList<int> thresholds =
                PartyProgress.Instance != null && PartyProgress.Instance.GrudgeThresholds != null && PartyProgress.Instance.GrudgeThresholds.Count > 0
                    ? PartyProgress.Instance.GrudgeThresholds
                    : DefaultGrudgeThresholds;
            if (index < thresholds.Count)
            {
                return thresholds[index];
            }
            int last = thresholds[thresholds.Count - 1];
            int previous = thresholds.Count >= 2 ? thresholds[thresholds.Count - 2] : 0;
            return lower + Mathf.Max(1, last - previous);
        }
    }
}
