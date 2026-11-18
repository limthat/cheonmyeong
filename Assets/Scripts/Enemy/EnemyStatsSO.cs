using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>261119 - 중형 원귀 스킬(기획서 v1.1 8-4 ~ 8-6). 일반 원귀는 None.</summary>
    public enum EliteSkill : byte
    {
        None,
        Charge,    // 아귀: 허기 돌진
        Submerge,  // 물귀신: 웅덩이 은신
        Throw,     // 역귀: 호리병 던지기 · 독
    }

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
        [Tooltip("엽전이 떨어질 때의 개수. 기획서 5-10: 몽달귀 1 / 달걀귀신 1 / 손각시 2.")]
        public int goldReward = 1;

        [Tooltip("261027 추가 - 처치 시 엽전이 떨어질 확률(0~1). 기획서 5-10: 몽달귀 5% / 달걀귀신 10% /\n" +
                 "손각시 30%. 영혼(경험치)은 100% 떨어진다.")]
        [Range(0f, 1f)] public float goldDropChance = 1f;

        [Tooltip("처치 시 떨어지는 영혼(경험치) 가치. 기획서 5-10: 1 / 2 / 5.")]
        public int expReward = 1;

        [Header("원한(怨恨) (261027 추가)")]
        [Tooltip("웨이브가 끝날 때 이 원귀가 살아남아 있으면 쌓이는 원한 양. 기획서 5-3: \"남은 원귀 1마리 =\n" +
                 "원한 1\" - 기본 1. 보스처럼 원한 집계에서 빼야 하는 몹은 0.")]
        public float grudgeWeight = 1f;

        [Header("261119 - 중형 원귀 스킬 (기획서 v1.1 · [스탯] 「중형 원귀」 시트, 1칸 = 1유닛)")]
        public EliteSkill skill = EliteSkill.None;
        [Tooltip("이 종류가 동시에 살아 있을 수 있는 최대 수(0 = 제한 없음). 물귀신 6(기획서 제안).")]
        [Min(0)] public int maxAlive;

        [Tooltip("아귀: 이 거리 안에 플레이어가 들어오면 돌진 준비.")]
        public float chargeDetectRange = 3f;
        public float chargeDistance = 3f;
        public float chargeWindup = 0.8f;
        [Tooltip("돌진에 걸리는 시간(초) - 기획서에 없음(제안).")]
        public float chargeSeconds = 0.3f;
        public float chargeDamage = 18f;
        public float chargeKnockback = 1f;
        public float chargeWallStun = 1f;
        public float chargeCooldown = 30f;
        [Tooltip("등장 후 첫 돌진까지 대기(초). 기획서 제안: 0(바로 가능).")]
        public float chargeFirstCooldown;

        [Tooltip("물귀신: 플레이어가 이 안에 오면 솟아오른다.")]
        public float surfaceRange = 2.5f;
        [Tooltip("물귀신: 플레이어가 이 밖으로 나가면 다시 잠긴다(기획서 제안 4칸).")]
        public float submergeRange = 4f;

        [Tooltip("역귀: 이 안이면 멈춰서 호리병을 던진다.")]
        public float throwRange = 6f;
        public float throwCooldown = 5f;
        public float throwWindup = 0.8f;
        [Tooltip("호리병 속도(유닛/초). 기획서 제안 360(새 단위) ÷ 60 = 6.")]
        public float throwSpeed = 6f;
        public float poisonDamage = 1f;
        public int poisonTicks = 5;
    }
}
