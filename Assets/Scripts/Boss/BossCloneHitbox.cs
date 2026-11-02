using CheonMyeong.Combat;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 서버에만 있는 분신 판정 몸통. 플레이어 공격(PlayerAutoAttack)이 Enemy 레이어 콜라이더에서 IDamageable을 찾으므로,
    /// 분신을 원귀처럼 맞을 수 있게 해 주고 받은 피해는 보스 쪽 분신 체력으로 넘긴다.
    /// </summary>
    public class BossCloneHitbox : MonoBehaviour, IDamageable
    {
        public EoksoeBoss Owner { get; set; }

        public void TakeDamage(float amount)
        {
            if (Owner != null)
            {
                Owner.ServerDamageClone(amount);
            }
        }
    }
}
