using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261121 - 어린 억쇠 '빗자루 비행' 먼지 줄(로컬 연출). 바닥에 갈색 먼지 덩이를 깔고 lifetime 동안 옅어진다.
    /// 감속 판정은 서버(EorinEoksoeBoss)가 같은 간격 · 반지름 · 시간으로 한다. 풀링(4초 비행 ≈ 60덩이).
    /// </summary>
    public class BossDustFx : MonoBehaviour
    {
        private static readonly Color DustColor = new Color(0.55f, 0.40f, 0.27f, 0.55f);
        private static readonly Stack<BossDustFx> Pool = new Stack<BossDustFx>();
        private static Transform poolRoot;

        private SpriteRenderer spriteRenderer;
        private float life;
        private float elapsed;
        private float size;

        public static void Play(Vector2 position, float radius, float lifetime)
        {
            BossDustFx fx = null;
            while (fx == null && Pool.Count > 0)
            {
                fx = Pool.Pop();
            }
            if (fx == null)
            {
                fx = Create();
            }
            fx.life = Mathf.Max(0.1f, lifetime);
            fx.elapsed = 0f;
            fx.size = radius * 2.2f;
            fx.transform.position = new Vector3(position.x + Random.Range(-0.08f, 0.08f), position.y + Random.Range(-0.08f, 0.08f), 0f);
            fx.Apply();
            fx.gameObject.SetActive(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            poolRoot = null;
        }

        private static BossDustFx Create()
        {
            if (poolRoot == null)
            {
                Pool.Clear();
                poolRoot = new GameObject("BossDustFxPool").transform;
                DontDestroyOnLoad(poolRoot.gameObject);
            }
            var go = new GameObject("BossDustFx");
            go.transform.SetParent(poolRoot, false);
            var fx = go.AddComponent<BossDustFx>();
            fx.spriteRenderer = go.AddComponent<SpriteRenderer>();
            fx.spriteRenderer.sprite = EoksoeBoss.GetSoftCircleSprite();
            fx.spriteRenderer.sortingOrder = -48; // 바닥 예고(-50) 위, 그림자(-40) 아래
            return fx;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= life)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
                return;
            }
            Apply();
        }

        private void Apply()
        {
            float t = elapsed / life;
            float grow = 0.7f + 0.3f * Mathf.Clamp01(t * 4f);
            transform.localScale = new Vector3(size * grow, size * 0.6f * grow, 1f);
            Color c = DustColor;
            c.a *= 1f - t * t;
            spriteRenderer.color = c;
        }
    }
}
