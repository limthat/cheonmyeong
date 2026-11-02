using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261102 추가 - 억쇠 타격 이펙트(Eoksoe_FX-Sheet: 초승달 베기 5프레임 / 땅 터짐 4프레임)를 한 번 재생하고 사라지는 연출.
    /// 각 화면에서 로컬로만 만든다(서버가 Rpc로 위치 · 방향 · 크기를 알려줌). 오브젝트는 풀링해서 매번 새로 만들지 않는다.
    /// </summary>
    public class BossFx : MonoBehaviour
    {
        private static readonly Stack<BossFx> Pool = new Stack<BossFx>();
        private static Transform poolRoot;

        private SpriteRenderer spriteRenderer;
        private Sprite[] frames;
        private float framesPerSecond;
        private float elapsed;
        private Color baseColor;

        public static void Play(Sprite[] frames, Vector2 position, float angleDegrees, float scale, float framesPerSecond, int sortingOrder, Color color, bool flipY)
        {
            Play(frames, position, angleDegrees, new Vector2(scale, scale), framesPerSecond, sortingOrder, color, flipY);
        }

        /// <summary>가로(그림 x) · 세로(그림 y) 배율을 따로 주는 버전 - 판정 범위의 두 축에 각각 맞출 때.</summary>
        public static void Play(Sprite[] frames, Vector2 position, float angleDegrees, Vector2 scale, float framesPerSecond, int sortingOrder, Color color, bool flipY)
        {
            if (frames == null || frames.Length == 0 || scale.x <= 0f || scale.y <= 0f)
            {
                return;
            }

            BossFx fx = null;
            while (fx == null && Pool.Count > 0)
            {
                fx = Pool.Pop();
            }
            if (fx == null)
            {
                fx = Create();
            }

            fx.frames = frames;
            fx.framesPerSecond = Mathf.Max(1f, framesPerSecond);
            fx.elapsed = 0f;
            fx.baseColor = color;
            fx.spriteRenderer.sprite = frames[0];
            fx.spriteRenderer.color = color;
            fx.spriteRenderer.sortingOrder = sortingOrder;
            fx.spriteRenderer.flipY = flipY;
            fx.transform.SetPositionAndRotation(new Vector3(position.x, position.y, 0f), Quaternion.Euler(0f, 0f, angleDegrees));
            fx.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            fx.gameObject.SetActive(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            poolRoot = null;
        }

        private static BossFx Create()
        {
            if (poolRoot == null)
            {
                Pool.Clear();
                poolRoot = new GameObject("BossFxPool").transform;
                DontDestroyOnLoad(poolRoot.gameObject);
            }
            var go = new GameObject("BossFx");
            go.transform.SetParent(poolRoot, false);
            var fx = go.AddComponent<BossFx>();
            fx.spriteRenderer = go.AddComponent<SpriteRenderer>();
            return fx;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            int index = Mathf.FloorToInt(elapsed * framesPerSecond);
            if (index >= frames.Length)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
                return;
            }
            spriteRenderer.sprite = frames[index];
            if (index == frames.Length - 1)
            {
                float t = elapsed * framesPerSecond - index;
                Color c = baseColor;
                c.a *= 1f - t;
                spriteRenderer.color = c;
            }
        }
    }
}
