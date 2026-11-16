using CheonMyeong.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261110 - 웨이브가 넘어갈 때 남은 원귀가 원한으로 흡수되는 연출(로컬 · UI 전용). 261110 재설계(원혼불 느낌).
    /// 참고: 인혼(人魂, 히토다마) · 도깨비불 · 윌오더위스프 묘사 - "실체 없이 떠다니는 둥근 빛 + 끌리는 꼬리, 일렁이며 깜빡이고,
    /// 바람에 흔들리듯 하지만 목적지로 향한다".
    /// 1) 원귀 자리에서 넋이 빠져나온다(작게 튀어나왔다 자리 잡음) → 흔들리며 곡선으로 화면 가운데로.
    /// 2) 가운데에서 소용돌이치며 맴돌고, 모인 만큼 가운데 붉은 기운이 짙어진다.
    /// 3) 하나씩 원한 게이지로 빨려 들어간다(가속, 꼬리가 길어짐). 닿을 때마다 HUD 게이지 펀치 · 번쩍 · 수치 상승.
    /// - 꼬리: 같은 경로를 조금 전 시간으로 다시 계산한 잔상(프레임 속도와 무관).
    /// - 일렁임: 넋마다 다른 펄린 노이즈로 투명도 · 크기 흔들림.
    /// - 성능: 루트에 하위 Canvas를 따로 둬서 매 프레임 위치가 바뀌어도 HUD 전체 Canvas는 다시 빌드되지 않는다.
    ///   Image는 풀링(서버가 최대 48개만 보냄), 텍스처 2장은 OnDestroy에서 해제.
    /// </summary>
    public class GrudgeAbsorbFx : MonoBehaviour
    {
        private const float HeadSize = 30f;
        private const float TrailSize = 28f;
        private const int TrailCount = 8;
        private const float TrailStep = 0.016f;
        private const float AppearSeconds = 0.18f;
        private const float GatherSeconds = 0.75f;
        private const float GatherStagger = 0.012f;
        private const float SwirlRadiusMin = 30f;
        private const float SwirlRadiusMax = 70f;
        private const float SwirlSpeed = 5f;
        private const float HoldSeconds = 0.35f;
        private const float FlySeconds = 0.5f;
        private const float FlyStagger = 0.02f;
        private const float CurveBend = 160f;
        private const float SwayPixels = 26f;
        private const float AuraSize = 180f;

        private static readonly Color TrailColor = new Color(0.8f, 0.05f, 0.08f, 1f);
        private static readonly Color AuraColor = new Color(0.55f, 0.02f, 0.05f, 1f);

        private static GrudgeAbsorbFx instance;

        private struct Orb
        {
            public RectTransform Head;
            public Image HeadImage;
            public RectTransform[] Trail;
            public Image[] TrailImages;
            public Vector2 Start;
            public Vector2 StartControl;
            public float GatherAngle;
            public float GatherRadius;
            public Vector2 FlyControl;
            public float GatherDelay;
            public float FlyBegin;
            public float Seed;
            public float Size;
            public bool Done;
        }

        private struct Sample
        {
            public bool Visible;
            public Vector2 Position;
            public float Scale;
            public int Phase; // 0 모임 · 1 맴돎 · 2 날아감
        }

        private Orb[] orbs = new Orb[0];
        private int orbCount;
        private float elapsed;
        private float flyStart;
        private Vector2 center;
        private Vector2 target;
        private bool playing;
        private Texture2D headTexture;
        private Sprite headSprite;
        private Texture2D glowTexture;
        private Sprite glowSprite;
        private RectTransform root;
        private RectTransform aura;
        private Image auraImage;
        private float auraStrength;

        /// <summary>넋 count개 연출이 끝나는 데 걸리는 시간(마지막 넋이 게이지에 닿을 때까지, 초). 서버가 신통력 화면을 미룰 때 쓴다.</summary>
        public static float TotalSeconds(int count)
        {
            if (count <= 0)
            {
                return 0f;
            }
            float start = (count - 1) * GatherStagger + GatherSeconds + HoldSeconds;
            return start + (count - 1) * FlyStagger + FlySeconds;
        }

        /// <summary>모든 화면에서 호출(WaveManager.GrudgeAbsorbFxRpc). worldPositions = 남은 원귀 위치.</summary>
        public static void Play(Vector2[] worldPositions)
        {
            InGameHudScreen hud = InGameHudScreen.Instance;
            if (worldPositions == null || worldPositions.Length == 0 || hud == null || !hud.isActiveAndEnabled)
            {
                return;
            }
            if (instance == null)
            {
                var go = new GameObject("GrudgeAbsorbFx", typeof(RectTransform));
                go.transform.SetParent(hud.transform, false);
                instance = go.AddComponent<GrudgeAbsorbFx>();
                instance.Build();
            }
            instance.Begin(hud, worldPositions);
        }

        private void Build()
        {
            root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            gameObject.AddComponent<Canvas>(); // 하위 Canvas - 이 연출만 따로 다시 빌드된다

            headTexture = CreateHeadTexture(64);
            headSprite = Sprite.Create(headTexture, new Rect(0, 0, headTexture.width, headTexture.height), new Vector2(0.5f, 0.5f));
            glowTexture = CreateGlowTexture(64);
            glowSprite = Sprite.Create(glowTexture, new Rect(0, 0, glowTexture.width, glowTexture.height), new Vector2(0.5f, 0.5f));

            aura = CreateImage("Aura", glowSprite, AuraSize, out auraImage);
            auraImage.color = new Color(AuraColor.r, AuraColor.g, AuraColor.b, 0f);
        }

        /// <summary>넋 머리: 안쪽은 검붉고, 바깥으로 짙은 핏빛 → 밝은 붉은 빛으로 번지며 사라진다. 실체 없는 덩어리라 테두리가 없다.</summary>
        private static Texture2D CreateHeadTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GrudgeWispHead"
            };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            var core = new Color(0.5f, 0.01f, 0.04f);
            var body = new Color(0.85f, 0.07f, 0.09f);
            var halo = new Color(1f, 0.35f, 0.28f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2((x - half) / half, (y - half) / half).magnitude;
                    Color c = Color.Lerp(core, body, Mathf.Clamp01(d / 0.5f));
                    c = Color.Lerp(c, halo, 0.45f * Mathf.SmoothStep(0f, 1f, (d - 0.5f) / 0.45f));
                    float dense = Mathf.Exp(-(d * d) / (2f * 0.3f * 0.3f));
                    float glow = 0.55f * Mathf.Exp(-(d * d) / (2f * 0.62f * 0.62f));
                    float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, (d - 0.85f) / 0.15f);
                    c.a = Mathf.Clamp01(Mathf.Max(dense, glow) * edgeFade);
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>흰 부드러운 원(색은 Image.color로) - 꼬리 잔상 · 가운데 기운에 같이 쓴다.</summary>
        private static Texture2D CreateGlowTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GrudgeWispGlow"
            };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2((x - half) / half, (y - half) / half).magnitude;
                    float a = Mathf.Exp(-(d * d) / (2f * 0.4f * 0.4f)) * (1f - Mathf.SmoothStep(0f, 1f, (d - 0.8f) / 0.2f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            if (headSprite != null)
            {
                Destroy(headSprite);
            }
            if (headTexture != null)
            {
                Destroy(headTexture);
            }
            if (glowSprite != null)
            {
                Destroy(glowSprite);
            }
            if (glowTexture != null)
            {
                Destroy(glowTexture);
            }
        }

        private void Begin(InGameHudScreen hud, Vector2[] worldPositions)
        {
            transform.SetAsLastSibling();
            Canvas canvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Camera worldCamera = Camera.main;

            center = root.rect.center;
            RectTransform gauge = hud.GrudgeGaugeRect;
            target = gauge != null
                ? (Vector2)root.InverseTransformPoint(gauge.TransformPoint(gauge.rect.center))
                : center + new Vector2(0f, root.rect.height * 0.4f);

            EnsurePool(worldPositions.Length);
            orbCount = worldPositions.Length;
            float lastGatherEnd = 0f;
            for (int i = 0; i < orbCount; i++)
            {
                Vector2 start = center;
                if (worldCamera != null)
                {
                    Vector3 screen = worldCamera.WorldToScreenPoint(worldPositions[i]);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, uiCamera, out start);
                }

                ref Orb orb = ref orbs[i];
                orb.Start = start;
                orb.GatherAngle = Random.value * Mathf.PI * 2f;
                orb.GatherRadius = Random.Range(SwirlRadiusMin, SwirlRadiusMax);
                orb.StartControl = BendControl(start, GatherPoint(orb));
                orb.FlyControl = BendControl(center, target);
                orb.GatherDelay = i * GatherStagger;
                orb.Seed = Random.value * 100f;
                orb.Size = Random.Range(0.8f, 1.2f);
                orb.Done = false;
                lastGatherEnd = Mathf.Max(lastGatherEnd, orb.GatherDelay + GatherSeconds);
            }

            flyStart = lastGatherEnd + HoldSeconds;
            for (int i = 0; i < orbCount; i++)
            {
                orbs[i].FlyBegin = flyStart + i * FlyStagger;
            }
            for (int i = 0; i < orbs.Length; i++)
            {
                SetOrbVisible(ref orbs[i], false);
            }

            elapsed = 0f;
            auraStrength = 0f;
            aura.anchoredPosition = center;
            playing = true;
            hud.HoldGrudgeDisplay(flyStart + FlySeconds + 0.1f);
        }

        private Vector2 GatherPoint(in Orb orb)
        {
            return center + new Vector2(Mathf.Cos(orb.GatherAngle), Mathf.Sin(orb.GatherAngle)) * orb.GatherRadius;
        }

        /// <summary>가운데를 맴도는 위치. 시간이 지날수록 반지름이 줄며 조여든다.</summary>
        private Vector2 SwirlPoint(in Orb orb, float swirlTime)
        {
            float angle = orb.GatherAngle + SwirlSpeed * swirlTime;
            float radius = orb.GatherRadius * Mathf.Lerp(1f, 0.45f, 1f - Mathf.Exp(-3f * swirlTime));
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>t초 시점의 넋 위치(꼬리는 같은 경로를 조금 전 시간으로 다시 계산한다).</summary>
        private Sample Evaluate(in Orb orb, float t)
        {
            var sample = new Sample();
            float gatherEnd = orb.GatherDelay + GatherSeconds;
            if (t < orb.GatherDelay || t >= orb.FlyBegin + FlySeconds)
            {
                return sample;
            }
            sample.Visible = true;

            if (t < gatherEnd)
            {
                float u = (t - orb.GatherDelay) / GatherSeconds;
                float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;
                Vector2 end = GatherPoint(orb);
                Vector2 position = Bezier(orb.Start, orb.StartControl, end, e);
                Vector2 dir = end - orb.Start;
                Vector2 normal = dir.sqrMagnitude > 1f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
                position += normal * (Mathf.Sin(e * Mathf.PI) * SwayPixels * Mathf.Sin(t * 7f + orb.Seed));
                sample.Position = position;
                float appear = Mathf.Clamp01((t - orb.GatherDelay) / AppearSeconds);
                sample.Scale = appear < 1f ? EaseOutBack(appear) : 1f;
                sample.Phase = 0;
                return sample;
            }

            if (t < orb.FlyBegin)
            {
                sample.Position = SwirlPoint(orb, t - gatherEnd);
                sample.Scale = 1f;
                sample.Phase = 1;
                return sample;
            }

            float f = (t - orb.FlyBegin) / FlySeconds;
            float accel = f * f;
            Vector2 from = SwirlPoint(orb, orb.FlyBegin - gatherEnd);
            sample.Position = Bezier(from, orb.FlyControl, target, accel);
            sample.Scale = Mathf.Lerp(1f, 0.45f, accel);
            sample.Phase = 2;
            return sample;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float m = x - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }

        private static Vector2 BendControl(Vector2 from, Vector2 to)
        {
            Vector2 mid = (from + to) * 0.5f;
            Vector2 dir = to - from;
            Vector2 normal = dir.sqrMagnitude > 1f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
            return mid + normal * Random.Range(-CurveBend, CurveBend);
        }

        private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }

        private RectTransform CreateImage(string objectName, Sprite sprite, float size, out Image image)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
            image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return rect;
        }

        private void EnsurePool(int count)
        {
            if (orbs.Length >= count)
            {
                return;
            }
            int old = orbs.Length;
            System.Array.Resize(ref orbs, count);
            for (int i = old; i < count; i++)
            {
                ref Orb orb = ref orbs[i];
                orb.Trail = new RectTransform[TrailCount];
                orb.TrailImages = new Image[TrailCount];
                // 꼬리를 먼저 만들어서 머리보다 뒤에 그려지게 한다.
                for (int k = TrailCount - 1; k >= 0; k--)
                {
                    orb.Trail[k] = CreateImage("Trail", glowSprite, TrailSize, out orb.TrailImages[k]);
                }
                orb.Head = CreateImage("Wisp", headSprite, HeadSize, out orb.HeadImage);
                SetOrbVisible(ref orb, false);
            }
        }

        private static void SetOrbVisible(ref Orb orb, bool visible)
        {
            if (orb.HeadImage != null && orb.HeadImage.enabled != visible)
            {
                orb.HeadImage.enabled = visible;
            }
            if (orb.TrailImages == null)
            {
                return;
            }
            for (int k = 0; k < orb.TrailImages.Length; k++)
            {
                if (orb.TrailImages[k].enabled != visible)
                {
                    orb.TrailImages[k].enabled = visible;
                }
            }
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            InGameHudScreen hud = InGameHudScreen.Instance;
            bool anyActive = false;
            int swirling = 0;

            for (int i = 0; i < orbCount; i++)
            {
                ref Orb orb = ref orbs[i];
                if (orb.Done)
                {
                    continue;
                }
                if (elapsed >= orb.FlyBegin + FlySeconds)
                {
                    orb.Done = true;
                    SetOrbVisible(ref orb, false);
                    if (hud != null)
                    {
                        hud.OnGrudgeOrbArrived();
                    }
                    continue;
                }
                anyActive = true;

                // 일렁임 - 넋마다 다른 노이즈로 밝기 · 크기가 흔들린다.
                float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(orb.Seed, elapsed * 7f);
                float breathe = 1f + 0.12f * (Mathf.PerlinNoise(orb.Seed + 13f, elapsed * 5f) - 0.5f);

                Sample head = Evaluate(orb, elapsed);
                ApplySample(orb.Head, orb.HeadImage, head, breathe * orb.Size, flicker, Color.white);
                if (head.Visible && head.Phase == 1)
                {
                    swirling++;
                }

                for (int k = 0; k < TrailCount; k++)
                {
                    float fade = 1f - (k + 1f) / (TrailCount + 1f);
                    Sample trail = Evaluate(orb, elapsed - (k + 1) * TrailStep);
                    // 꼬리: 잔상이 촘촘히 겹쳐 하나의 흐름처럼 보인다. 날아갈 때는 빨라져서 꼬리가 길어진다.
                    ApplySample(orb.Trail[k], orb.TrailImages[k], trail, Mathf.Sqrt(fade) * orb.Size, flicker * fade * 0.45f, TrailColor);
                }
            }

            // 가운데 기운 - 맴도는 넋이 많을수록 짙고 크게, 맥박처럼 숨쉰다.
            float targetStrength = orbCount > 0 ? (float)swirling / orbCount : 0f;
            auraStrength = Mathf.Lerp(auraStrength, targetStrength, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            float pulse = 1f + 0.08f * Mathf.Sin(elapsed * 9f);
            aura.localScale = Vector3.one * ((0.5f + 0.7f * auraStrength) * pulse);
            auraImage.color = new Color(AuraColor.r, AuraColor.g, AuraColor.b, 0.6f * auraStrength);
            bool auraOn = auraStrength > 0.01f;
            if (auraImage.enabled != auraOn)
            {
                auraImage.enabled = auraOn;
            }

            if (!anyActive)
            {
                playing = false;
                auraImage.enabled = false;
            }
        }

        private static void ApplySample(RectTransform rect, Image image, Sample sample, float scale, float alpha, Color tint)
        {
            if (!sample.Visible)
            {
                if (image.enabled)
                {
                    image.enabled = false;
                }
                return;
            }
            if (!image.enabled)
            {
                image.enabled = true;
            }
            rect.anchoredPosition = sample.Position;
            rect.localScale = Vector3.one * (sample.Scale * scale);
            image.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(alpha));
        }
    }
}
