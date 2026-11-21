using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Maps
{
    /// <summary>
    /// 261121 추가 - 당산나무 잎 반투명(맵 배치 문서 v1.5 · 기획서 26.11.20 확정): 내 캐릭터가 잎 아래에 들어가면 나무를 반투명하게.
    /// 나무는 4배 크기라 마당 대부분을 덮어서, 안 하면 보스전 중 캐릭터 · 원귀 · 바닥 예고가 가려진다. 화면마다 로컬로만 계산한다.
    /// 판정 = 그림 가로 범위 안 + 밑동(피벗)보다 위(= Y 정렬상 나무 뒤라 가려지는 곳) + 그림 위끝 아래.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class CanopyFade : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0.45f;
        [Tooltip("밑동보다 이만큼(유닛) 위부터 반투명 - 0이면 나무 뒤로 들어가자마자.")]
        [SerializeField] private float trunkHeight = 0f;
        [SerializeField] private float fadeSpeed = 4f;

        private SpriteRenderer spriteRenderer;
        private float alpha = 1f;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            float target = IsLocalPlayerUnder() ? fadedAlpha : 1f;
            if (Mathf.Approximately(alpha, target))
            {
                return;
            }
            alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
            Color c = spriteRenderer.color;
            c.a = alpha;
            spriteRenderer.color = c;
        }

        private bool IsLocalPlayerUnder()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null)
            {
                return false;
            }
            Bounds b = spriteRenderer.bounds;
            Vector2 p = player.transform.position;
            float baseY = transform.position.y;
            return p.x >= b.min.x && p.x <= b.max.x && p.y >= baseY + trunkHeight && p.y <= b.max.y;
        }
    }
}
