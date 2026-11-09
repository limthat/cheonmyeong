using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Combat
{
    /// <summary>261108 - 타격 한 번의 연출 정보(서버 → 모든 화면).</summary>
    public struct HitFeedback : INetworkSerializable
    {
        public Vector2 Position;
        public Vector2 Direction;
        public int Damage;
        public bool Crit;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref Crit);
        }
    }

    /// <summary>
    /// 261108 추가 - 타격 연출(아트 요청): 맞은 자리에 Unity 파티클이 터지고 피해 숫자가 떠오른다.
    /// - 파티클은 시스템 하나를 두고 맞은 자리마다 Emit만 한다(타격마다 오브젝트를 만들지 않음).
    /// - 숫자는 TextMesh를 풀링해서 재사용하고, 움직임은 이 매니저 하나의 Update에서 전부 처리한다.
    /// - 각 화면에서 로컬로만 그린다(PlayerAutoAttack이 Rpc로 위치 · 피해 · 치명타를 알려줌).
    /// 261108 수정 - 파티클은 Unity 기본 파티클(Default-Particle)처럼 부드러운 동그란 알갱이로, 맞은 반대쪽으로 모래처럼 튀었다가
    /// 떨어진다(아트 요청). 동그란 그림은 실행 중에 만들고, 재질은 캐릭터 스프라이트 재질을 복사해서 쓴다
    /// (빌드에서 셰이더가 빠질 걱정 없음). 만든 텍스처 · 재질은 OnDestroy에서 지운다.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        private const int SortingOrder = 40;
        private const float NumberLifetime = 0.7f;
        private const float NumberRise = 0.9f;
        private const float NumberHeight = 0.42f;
        private const float CritNumberHeight = 0.62f;
        private const float NumberOffsetY = 0.45f;
        private const float NumberJitterX = 0.2f;
        private const int FontSize = 64;
        private const int MaxNumbers = 64;

        private static readonly Color NormalColor = new Color(1f, 0.97f, 0.88f, 1f);
        private static readonly Color CritColor = new Color(1f, 0.78f, 0.2f, 1f);
        private static readonly Color SparkColorA = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SparkColorB = new Color(0.93f, 0.85f, 0.68f, 1f);
        private const int DotTextureSize = 32;
        private const float SprayHalfAngle = 65f;

        private static CombatFeedback instance;

        private class Number
        {
            public Transform Transform;
            public TextMesh Text;
            public MeshRenderer Renderer;
            public Vector3 Start;
            public float Elapsed;
            public float BaseScale;
            public Color Color;
        }

        private readonly List<Number> active = new List<Number>(MaxNumbers);
        private readonly Stack<Number> pool = new Stack<Number>(MaxNumbers);
        private ParticleSystem sparks;
        private ParticleSystemRenderer sparksRenderer;
        private Font font;
        private bool hasMaterial;
        private Texture2D dotTexture;
        private Material sparkMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        /// <summary>맞은 대상들의 연출을 한 번에 그린다. materialSource = 파티클 재질을 빌려올 SpriteRenderer(처음 한 번만).</summary>
        public static void Play(HitFeedback[] hits, SpriteRenderer materialSource)
        {
            if (hits == null || hits.Length == 0)
            {
                return;
            }
            CombatFeedback feedback = GetOrCreate();
            feedback.TryAssignMaterial(materialSource);
            for (int i = 0; i < hits.Length; i++)
            {
                feedback.EmitSparks(hits[i]);
                feedback.ShowNumber(hits[i]);
            }
        }

        /// <summary>
        /// 261108 - 억쇠 타격 · 착지용 흙먼지(아트 시트 이펙트 대신 Unity 파티클). direction이 0이면 사방으로(땅 터짐),
        /// 아니면 그 방향 부채꼴로(휘두르기). radius에 맞춰 알갱이 수 · 퍼지는 범위가 커진다.
        /// </summary>
        public static void PlayDust(Vector2 center, float radius, Vector2 direction, Color tint, SpriteRenderer materialSource)
        {
            if (radius <= 0f)
            {
                return;
            }
            CombatFeedback feedback = GetOrCreate();
            feedback.TryAssignMaterial(materialSource);
            feedback.EmitDust(center, radius, direction, tint);
        }

        private void EmitDust(Vector2 center, float radius, Vector2 direction, Color tint)
        {
            if (!hasMaterial)
            {
                return;
            }
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 24f), 10, 70);
            bool cone = direction.sqrMagnitude > 0.0001f;
            Vector2 forward = cone ? direction.normalized : Vector2.right;
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false
            };
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = cone
                    ? (Vector2)(Quaternion.Euler(0f, 0f, Random.Range(-SprayHalfAngle, SprayHalfAngle)) * forward)
                    : Random.insideUnitCircle.normalized;
                Vector2 spawn = center + dir * (radius * Random.Range(0.2f, 0.8f));
                Vector2 velocity = dir * Random.Range(1.5f, 2.5f + radius * 2f);
                velocity.y += Random.Range(1f, 2.5f + radius);
                emit.position = new Vector3(spawn.x, spawn.y, 0f);
                emit.velocity = new Vector3(velocity.x, velocity.y, 0f);
                emit.startColor = Color.Lerp(SparkColorA, SparkColorB, Random.value) * tint;
                emit.startSize = Random.Range(0.12f, 0.24f) * Mathf.Clamp(0.7f + radius * 0.25f, 0.8f, 1.6f);
                emit.startLifetime = Random.Range(0.35f, 0.6f);
                sparks.Emit(emit, 1);
            }
        }

        private static CombatFeedback GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }
            var go = new GameObject("CombatFeedback");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CombatFeedback>();
            instance.Build();
            return instance;
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var sparkGo = new GameObject("HitSparks");
            sparkGo.transform.SetParent(transform, false);
            sparks = sparkGo.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = sparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(SparkColorA, SparkColorB);
            main.gravityModifier = 1.6f;
            main.maxParticles = 512;

            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = sparks.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.LimitVelocityOverLifetimeModule limit = sparks.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.08f;
            limit.limit = 1f;

            sparksRenderer = sparkGo.GetComponent<ParticleSystemRenderer>();
            sparksRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            sparksRenderer.sortingOrder = SortingOrder;
            sparksRenderer.enabled = false;
            sparks.Play();
        }

        private void TryAssignMaterial(SpriteRenderer source)
        {
            if (hasMaterial || source == null || source.sharedMaterial == null)
            {
                return;
            }
            dotTexture = CreateDotTexture();
            sparkMaterial = new Material(source.sharedMaterial) { name = "HitSparks (runtime)", mainTexture = dotTexture };
            sparksRenderer.sharedMaterial = sparkMaterial;
            sparksRenderer.sortingLayerID = source.sortingLayerID;
            sparksRenderer.enabled = true;
            hasMaterial = true;
        }

        private void EmitSparks(HitFeedback hit)
        {
            if (!hasMaterial)
            {
                return;
            }
            int count = hit.Crit ? 22 : 12;
            Vector2 forward = hit.Direction.sqrMagnitude > 0.0001f ? hit.Direction.normalized : Vector2.up;
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false
            };
            for (int i = 0; i < count; i++)
            {
                // 맞은 반대쪽(공격 방향)으로 부채꼴로 튀고, 살짝 위로 뜬 뒤 중력으로 떨어진다(모래 튀는 느낌).
                Vector2 dir = Quaternion.Euler(0f, 0f, Random.Range(-SprayHalfAngle, SprayHalfAngle)) * forward;
                dir.y += Random.Range(0.3f, 0.9f);
                emit.position = new Vector3(hit.Position.x + Random.Range(-0.08f, 0.08f), hit.Position.y + Random.Range(-0.08f, 0.08f), 0f);
                emit.velocity = (Vector3)(dir.normalized * Random.Range(2f, hit.Crit ? 6f : 4.5f));
                sparks.Emit(emit, 1);
            }
        }

        private void ShowNumber(HitFeedback hit)
        {
            if (hit.Damage <= 0)
            {
                return;
            }
            Number number = active.Count >= MaxNumbers ? Recycle() : (pool.Count > 0 ? pool.Pop() : CreateNumber());
            number.Text.text = hit.Crit ? hit.Damage + "!" : hit.Damage.ToString();
            number.Color = hit.Crit ? CritColor : NormalColor;
            number.Text.color = number.Color;
            number.BaseScale = (hit.Crit ? CritNumberHeight : NumberHeight) / (FontSize * 0.1f);
            number.Start = new Vector3(hit.Position.x + Random.Range(-NumberJitterX, NumberJitterX), hit.Position.y + NumberOffsetY, 0f);
            number.Elapsed = 0f;
            number.Transform.position = number.Start;
            number.Transform.localScale = Vector3.one * number.BaseScale;
            number.Transform.gameObject.SetActive(true);
            active.Add(number);
        }

        private Number Recycle()
        {
            Number oldest = active[0];
            active.RemoveAt(0);
            return oldest;
        }

        private Number CreateNumber()
        {
            var go = new GameObject("DamageNumber", typeof(MeshRenderer), typeof(TextMesh));
            go.transform.SetParent(transform, false);
            var text = go.GetComponent<TextMesh>();
            text.font = font;
            text.fontSize = FontSize;
            text.characterSize = 1f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = SortingOrder + 1;
            go.SetActive(false);
            return new Number { Transform = go.transform, Text = text, Renderer = renderer };
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Number number = active[i];
                number.Elapsed += dt;
                float t = number.Elapsed / NumberLifetime;
                if (t >= 1f)
                {
                    number.Transform.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Push(number);
                    continue;
                }
                float rise = 1f - (1f - t) * (1f - t);
                number.Transform.position = number.Start + new Vector3(0f, NumberRise * rise, 0f);
                float pop = t < 0.12f ? Mathf.Lerp(1.5f, 1f, t / 0.12f) : 1f;
                number.Transform.localScale = Vector3.one * (number.BaseScale * pop);
                Color c = number.Color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                number.Text.color = c;
            }
        }

        /// <summary>Unity 기본 파티클처럼 가운데가 진하고 가장자리가 부드럽게 사라지는 흰 동그라미.</summary>
        private static Texture2D CreateDotTexture()
        {
            var texture = new Texture2D(DotTextureSize, DotTextureSize, TextureFormat.RGBA32, false)
            {
                name = "HitDot (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[DotTextureSize * DotTextureSize];
            float half = (DotTextureSize - 1) * 0.5f;
            for (int y = 0; y < DotTextureSize; y++)
            {
                for (int x = 0; x < DotTextureSize; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01((1f - d) / 0.35f);
                    pixels[y * DotTextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (sparkMaterial != null)
            {
                Destroy(sparkMaterial);
            }
            if (dotTexture != null)
            {
                Destroy(dotTexture);
            }
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
