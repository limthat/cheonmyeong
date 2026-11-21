using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261121 - 어린 억쇠 '빗자루 회오리' 연출: 프레임을 반복 재생하며 direction으로 distance만큼 곧게 날아가다 사라진다.
    /// 판정은 서버(EorinEoksoeBoss)가 같은 속도 · 거리로 따로 계산하고, 이건 각 화면 로컬 그림이다. 풀링.
    /// </summary>
    public class BossProjectileFx : MonoBehaviour
    {
        private static readonly Stack<BossProjectileFx> Pool = new Stack<BossProjectileFx>();
        private static Transform poolRoot;

        private SpriteRenderer spriteRenderer;
        private Sprite[] frames;
        private Vector2 origin;
        private Vector2 direction;
        private float speed;
        private float life;
        private float framesPerSecond;
        private float elapsed;

        public static void Play(Sprite[] frames, Vector2 origin, Vector2 direction, float speed, float distance, float framesPerSecond, float scale)
        {
            if (frames == null || frames.Length == 0 || speed <= 0f || distance <= 0f)
            {
                return;
            }
            BossProjectileFx fx = null;
            while (fx == null && Pool.Count > 0)
            {
                fx = Pool.Pop();
            }
            if (fx == null)
            {
                fx = Create();
            }
            fx.frames = frames;
            fx.origin = origin;
            fx.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.left;
            fx.speed = speed;
            fx.life = distance / speed;
            fx.framesPerSecond = Mathf.Max(1f, framesPerSecond);
            fx.elapsed = 0f;
            fx.spriteRenderer.sprite = frames[0];
            fx.spriteRenderer.color = Color.white;
            fx.transform.position = new Vector3(origin.x, origin.y, 0f);
            fx.transform.localScale = new Vector3(scale, scale, 1f);
            fx.gameObject.SetActive(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            poolRoot = null;
        }

        private static BossProjectileFx Create()
        {
            if (poolRoot == null)
            {
                Pool.Clear();
                poolRoot = new GameObject("BossProjectileFxPool").transform;
                DontDestroyOnLoad(poolRoot.gameObject);
            }
            var go = new GameObject("BossProjectileFx");
            go.transform.SetParent(poolRoot, false);
            var fx = go.AddComponent<BossProjectileFx>();
            fx.spriteRenderer = go.AddComponent<SpriteRenderer>();
            fx.spriteRenderer.sortingOrder = 20;
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
            Vector2 p = origin + direction * (speed * elapsed);
            transform.position = new Vector3(p.x, p.y, 0f);
            spriteRenderer.sprite = frames[Mathf.FloorToInt(elapsed * framesPerSecond) % frames.Length];
            float fade = Mathf.Clamp01((life - elapsed) / 0.2f);
            spriteRenderer.color = new Color(1f, 1f, 1f, fade);
        }
    }
}
