using CheonMyeong.Combat;
using CheonMyeong.Data;
using CheonMyeong.Input;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영의 기본 무기: 마우스 방향을 바라보며 사거리 내 적을 주기적으로
    /// 자동 공격한다. 공격 판정은 우선 원형(OverlapCircle)으로 단순화했고,
    /// 부채꼴 등 정확한 판정 형태는 "공격 범위 형태" 미정 사항이 확정되면 교체한다.
    ///
    /// 성능 메모: Physics2D.OverlapCircleNonAlloc으로 매 공격마다 배열을
    /// 새로 할당하지 않는다. 적이 수백 마리로 늘어나는 걸 감안해 공격
    /// 판정 자체도 그리드/공간분할로 옮길지는 4번 단계(적 스폰+풀링)
    /// 결과를 보고 다시 검토한다.
    /// </summary>
    public class PlayerAutoAttack : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private CharacterStatsSO stats;
        [SerializeField] private Transform facingPivot; // 바라보는 방향을 표현할 자식 오브젝트 (무기/스프라이트)
        [SerializeField] private LayerMask enemyLayer;

        private const int MaxHitsPerAttack = 32;
        private readonly Collider2D[] hitBuffer = new Collider2D[MaxHitsPerAttack];
        private ContactFilter2D contactFilter;

        private float attackTimer;

        private void Awake()
        {
            // Physics2D.OverlapCircleNonAlloc은 Unity 6000.x부터 Obsolete 처리됐다.
            // 대신 ContactFilter2D + 배열을 받는 OverlapCircle 오버로드를 쓴다 (여전히 논얼록).
            contactFilter = new ContactFilter2D();
            contactFilter.SetLayerMask(enemyLayer);
            contactFilter.useTriggers = true; // 적 콜라이더가 트리거로 구성될 수도 있어 포함
        }

        private void Update()
        {
            if (inputReader == null || stats == null)
            {
                return;
            }

            UpdateFacing();

            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                Attack();
                attackTimer = stats.attackInterval;
            }
        }

        private void UpdateFacing()
        {
            Vector3 toAim = inputReader.AimWorldPosition - transform.position;
            if (toAim.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float angle = Mathf.Atan2(toAim.y, toAim.x) * Mathf.Rad2Deg;
            Transform target = facingPivot != null ? facingPivot : transform;
            target.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void Attack()
        {
            Vector2 origin = transform.position;
            int hitCount = Physics2D.OverlapCircle(origin, stats.attackRange, contactFilter, hitBuffer);

            for (int i = 0; i < hitCount; i++)
            {
                if (hitBuffer[i].TryGetComponent(out IDamageable damageable))
                {
                    damageable.TakeDamage(stats.attackPower);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (stats == null)
            {
                return;
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, stats.attackRange);
        }
    }
}
