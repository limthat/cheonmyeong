using System;
using CheonMyeong.Combat;
using CheonMyeong.Data;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 기본 적. 플레이어를 향해 직선 추적 이동하고(경로탐색 없음 - 프로토타입 단계),
    /// 닿으면 주기적으로 접촉 피해를 준다. 죽을 때 Destroy하지 않고 풀로 반환한다 -
    /// 수백 마리 단위 스폰/소멸이 반복되므로 GC 압박을 피하기 위함.
    ///
    /// 콜라이더는 Trigger로 둔다 - 적끼리, 적과 플레이어가 서로 물리적으로 밀어내면
    /// 수백 마리가 뭉쳤을 때 서로 막혀서 멈추거나(지금 겪은 문제), 물리 연산 비용이
    /// 급격히 늘어난다. 서바이버즈류에서 흔히 쓰는 방식대로 다들 서로 통과하고,
    /// 닿아있는 동안 주기적으로 피해만 주는 쪽으로 처리한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Enemy : MonoBehaviour, IDamageable
    {
        [SerializeField] private EnemyStatsSO stats;

        private Rigidbody2D rb;
        private Transform target;
        private float currentHealth;
        private float contactTimer;

        /// <summary>풀로 반환될 때 호출. EnemyPool이 구독한다.</summary>
        public event Action<Enemy> OnDespawn;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        /// <summary>풀에서 꺼내질 때(Get) 호출 - 상태 초기화.</summary>
        public void Spawn(Vector2 position, Transform chaseTarget)
        {
            transform.position = position;
            target = chaseTarget;
            currentHealth = stats != null ? stats.maxHealth : 1f;
            contactTimer = 0f;
            gameObject.SetActive(true);
        }

        private void FixedUpdate()
        {
            if (target == null || stats == null)
            {
                return;
            }

            Vector2 toTarget = (Vector2)target.position - rb.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                rb.linearVelocity = toTarget.normalized * stats.moveSpeed;
            }
        }

        public void TakeDamage(float amount)
        {
            currentHealth -= amount;
            if (currentHealth <= 0f)
            {
                Despawn();
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (stats == null)
            {
                return;
            }

            contactTimer -= Time.fixedDeltaTime;
            if (contactTimer > 0f)
            {
                return;
            }

            // 다른 적은 제외 - IDamageable을 구현하고 있어서 그냥 두면 적끼리도 서로 때림.
            if (other.GetComponent<Enemy>() != null)
            {
                return;
            }

            if (other.TryGetComponent(out IDamageable damageable))
            {
                damageable.TakeDamage(stats.contactDamage);
                contactTimer = stats.contactDamageInterval;
            }
        }

        private void Despawn()
        {
            rb.linearVelocity = Vector2.zero;
            target = null;
            gameObject.SetActive(false);
            OnDespawn?.Invoke(this);
        }
    }
}
