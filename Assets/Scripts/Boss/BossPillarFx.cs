using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261121 - 억쇠 '원귀 부르기' 연출: 원귀가 솟아날 자리마다 푸른 넋 기둥(기획서 v1.2 "원귀 솟는 연출은 코드").
    /// 그림 없이 부드러운 원 스프라이트를 세로로 늘려 그린다 - 바닥 고리(납작한 원)가 먼저 커지고, 기둥이 솟았다가 사라진다.
    /// 각 화면에서 로컬로만 만들고 풀링한다(80마리 부르기도 오브젝트를 새로 만들지 않게).
    /// </summary>
    public class BossPillarFx : MonoBehaviour
    {
        private static readonly Color PillarColor = new Color(0.45f, 0.72f, 1f, 0.75f); // 청 계열 넋
        private static readonly Stack<BossPillarFx> Pool = new Stack<BossPillarFx>();
        private static Transform poolRoot;

        private SpriteRenderer ring;
        private SpriteRenderer pillar;
        private float rise;
        private float hold;
        private float elapsed;
        private float height;

        /// <summary>rise초 동안 차오르고(이때 원귀가 나온다) hold초 동안 옅어지며 사라진다.</summary>
        public static void Play(Vector2 position, float riseSeconds, float holdSeconds, float pillarHeight)
        {
            BossPillarFx fx = null;
            while (fx == null && Pool.Count > 0)
            {
                fx = Pool.Pop();
            }
            if (fx == null)
            {
                fx = Create();
            }
            fx.transform.position = new Vector3(position.x, position.y, 0f);
            fx.rise = Mathf.Max(0.05f, riseSeconds);
            fx.hold = Mathf.Max(0.05f, holdSeconds);
            fx.height = Mathf.Max(0.2f, pillarHeight);
            fx.elapsed = 0f;
            fx.Apply(0f);
            fx.gameObject.SetActive(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            poolRoot = null;
        }

        private static BossPillarFx Create()
        {
            if (poolRoot == null)
            {
                Pool.Clear();
                poolRoot = new GameObject("BossPillarFxPool").transform;
                DontDestroyOnLoad(poolRoot.gameObject);
            }
            var go = new GameObject("BossPillarFx");
            go.transform.SetParent(poolRoot, false);
            var fx = go.AddComponent<BossPillarFx>();
            Sprite circle = EoksoeBoss.GetSoftCircleSprite();

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(go.transform, false);
            fx.ring = ringGo.AddComponent<SpriteRenderer>();
            fx.ring.sprite = circle;
            fx.ring.sortingOrder = -45;

            var pillarGo = new GameObject("Pillar");
            pillarGo.transform.SetParent(go.transform, false);
            fx.pillar = pillarGo.AddComponent<SpriteRenderer>();
            fx.pillar.sprite = circle;
            fx.pillar.sortingOrder = 30;
            return fx;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= rise + hold)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
                return;
            }
            Apply(elapsed);
        }

        private void Apply(float t)
        {
            float grow = Mathf.Clamp01(t / rise);
            float fade = t <= rise ? 1f : 1f - Mathf.Clamp01((t - rise) / hold);
            float easeOut = 1f - (1f - grow) * (1f - grow);

            ring.transform.localScale = new Vector3(1.4f * easeOut, 0.55f * easeOut, 1f);
            Color rc = PillarColor;
            rc.a *= fade;
            ring.color = rc;

            // 기둥은 차오르는 마지막 40%에 솟는다(원귀가 나오는 순간과 맞춤).
            float shoot = Mathf.Clamp01((grow - 0.6f) / 0.4f);
            float h = height * (t <= rise ? shoot : 1f);
            float flicker = 0.9f + 0.1f * Mathf.Sin(Time.time * 25f + transform.position.x);
            pillar.transform.localScale = new Vector3(0.55f * flicker, Mathf.Max(0.001f, h), 1f);
            pillar.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            Color pc = PillarColor;
            pc.a *= fade * (t <= rise ? shoot : 1f);
            pillar.color = pc;
        }
    }
}
