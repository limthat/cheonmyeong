using CheonMyeong.Combat;
using CheonMyeong.Data;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영의 체력. CharacterStatsSO.maxHealth를 그대로 쓴다(이미 확정된 필드라
    /// 새로 미정 항목을 만든 건 아님). 사망 시 처리(리스폰/게임오버/UI)는
    /// 아직 기획이 없어서 최소한으로만: 이동/공격 비활성화 + 로그.
    /// 추후 체력 UI, 사망 연출, 리스폰 규칙이 정해지면 OnDeath 쪽만 확장하면 된다.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private CharacterStatsSO stats;

        private float currentHealth;
        private bool isDead;

        private void Awake()
        {
            currentHealth = stats != null ? stats.maxHealth : 1f;
        }

        public float CurrentHealth => currentHealth;
        public float MaxHealth => stats != null ? stats.maxHealth : 1f;
        public bool IsDead => isDead;

        public void TakeDamage(float amount)
        {
            if (isDead || stats == null)
            {
                return;
            }

            currentHealth -= amount;

            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                Die();
            }
        }

        private void Die()
        {
            isDead = true;

            // 사망 처리 미정 - 우선 이동/공격만 멈춘다.
            if (TryGetComponent(out PlayerMovement movement))
            {
                movement.enabled = false;
            }
            if (TryGetComponent(out PlayerAutoAttack attack))
            {
                attack.enabled = false;
            }

            Debug.Log("[CheonMyeong] 하영 사망 (리스폰/게임오버 처리는 미정 - 현재는 정지만 함).");
        }
    }
}
