using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 적 스탯 데이터. CharacterStatsSO와 같은 패턴 - 수치는 전부 미정이라
    /// 플레이스홀더 값만 넣어뒀다. 밸런스 확정 전까지 이 값들로 테스트한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemyStats", menuName = "CheonMyeong/Enemy Stats")]
    public class EnemyStatsSO : ScriptableObject
    {
        public string enemyId;
        public string displayName;

        [Header("미정 - 플레이스홀더 값")]
        public float maxHealth = 20f;
        public float moveSpeed = 2f;
        public float contactDamage = 5f;
        public float contactDamageInterval = 1f;
    }
}
