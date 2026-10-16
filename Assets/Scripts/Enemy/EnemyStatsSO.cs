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

        [Header("피격/사망 연출 (값은 미정 - 느낌 보고 조정)")]
        [Tooltip("피격 시 점멸(깜빡임) 횟수. 1회 = 꺼짐→켜짐 한 번 왕복.")]
        public int hitFlashBlinkCount = 2;

        [Tooltip("점멸 한 단계(켜짐 또는 꺼짐)가 유지되는 시간(초).")]
        public float hitFlashBlinkInterval = 0.08f;

        [Tooltip("체력이 0 이하가 된 뒤 실제로 풀에 반환(소멸)되기까지 그 자리에 멈춰\n" +
                 "있는 경직 시간(초). 이 동안은 이동/추적/접촉 피해 판정을 모두 멈춘다\n" +
                 "(기획 지시: 사망 시 경직 이후 소멸).")]
        public float deathStaggerDuration = 0.3f;

        [Header("처치 보상 (261013 추가 - 값은 미정, 플레이스홀더)")]
        [Tooltip("이 적을 처치하면 사망 위치에 드랍되는 엽전(골드) 양.")]
        public int goldReward = 1;

        [Tooltip("이 적을 처치하면 사망 위치에 드랍되는 경험치 양.")]
        public int expReward = 1;
    }
}
