using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261101 추가 - 억쇠(1스테이지 최종 보스) 수치. 기획서 v0.9.3 9장 + 예섭 스킬 기획(정신병/배지기/금 나와라 뚝딱).
    /// 원한 단계별 값은 길이 4 배열(맑음 · 짙음 · 한 · 폭주) - PartyProgress.GrudgeStage(0~3)로 고른다.
    /// "미정"으로 표시한 값은 기획서에 아직 없는 임시값이라 밸런스 때 이 에셋에서 고치면 된다.
    /// 거리 단위 = 타일(1칸 = 1유닛), 시간 단위 = 초.
    /// </summary>
    [CreateAssetMenu(fileName = "Eoksoe", menuName = "CheonMyeong/Boss Stats")]
    public class BossStatsSO : ScriptableObject
    {
        public const int StageCount = 4;

        [Header("기본")]
        public string displayName = "억쇠";

        [Tooltip("5웨이브 중간 보스 체력 - 기획서 3,300(1칸).")]
        [Min(1f)] public float midMaxHealth = 3300f;

        [Tooltip("최종전 칸당 체력 - 기획서 8,400 × 3칸 = 25,200.")]
        [Min(1f)] public float finalHealthPerBar = 8400f;
        [Min(1)] public int finalBars = 3;

        [Tooltip("크기 배율 - 중간 보스는 작은 모습(1), 최종전은 2배(기획서).")]
        [Min(0.1f)] public float midScale = 1f;
        [Min(0.1f)] public float finalScale = 2f;

        [Tooltip("이동 속도(유닛/초) - 기획서 '이동 속도 150(제안)' = 150 ÷ 60 = 2.5.")]
        [Min(0f)] public float moveSpeed = 2.5f;

        [Tooltip("몸 반지름(유닛, 크기 1 기준) - 공격 사거리를 몸 가장자리부터 재려고 쓴다.")]
        [Min(0f)] public float bodyRadius = 0.6f;

        [Tooltip("등장 연출 시간(초).")]
        [Min(0f)] public float appearSeconds = 1.2f;

        [Header("통통 뛰는 이동(연출) - 외다리라 걷기 대신 콩콩 뛴다(예섭)")]
        [Min(0f)] public float hopHeight = 0.35f;
        [Min(0.1f)] public float hopsPerSecond = 2.6f;
        [Range(0f, 0.5f)] public float landingSquash = 0.18f;

        [Header("기본 공격 - 방망이 휘두르기")]
        public float[] basicDamage = { 20f, 25f, 30f, 40f };
        public float[] basicCooldown = { 2f, 1.8f, 1.6f, 1.2f };
        [Tooltip("사거리(칸) - 기획서 2칸. 몸 가장자리부터.")]
        [Min(0f)] public float basicRange = 2f;
        [Tooltip("휘두르는 부채꼴 각도(도) - 미정.")]
        [Range(10f, 360f)] public float basicArcDegrees = 120f;
        [Tooltip("붉은 예고 → 타격까지(초). 피할 시간을 주려고 ATK 모션 앞부분을 이 시간에 맞춰 늘린다 - 미정.")]
        [Min(0.1f)] public float basicWindup = 0.7f;
        [Tooltip("타격 뒤 다음 행동까지(초).")]
        [Min(0f)] public float basicRecovery = 0.35f;

        [Header("금 나와라 뚝딱")]
        [Tooltip("뿌리는 엽전 총합(개).")]
        public int[] goldCoinCount = { 5, 10, 15, 50 };
        [Tooltip("엽전 종류 뽑기 비중(동 · 은 · 금) - 미정.")]
        public int[] goldTypeWeights = { 6, 3, 1 };
        [Tooltip("주우면 얻는 엽전(동 · 은 · 금).")]
        public int[] goldCoinValue = { 1, 20, 30 };
        [Tooltip("주우면 받는 피해 = 현재 체력 × 비율(동 · 은 · 금).")]
        public float[] goldCoinDamageRatio = { 0.01f, 0.02f, 0.05f };
        [Tooltip("주우면 보스전 동안 이동 속도 감소(동 · 은 · 금).")]
        public float[] goldCoinSlow = { 0.02f, 0.03f, 0.05f };
        [Tooltip("이동 속도 감소 누적 한도 - 미정.")]
        [Range(0f, 0.95f)] public float goldSlowCap = 0.5f;
        [Min(0.1f)] public float goldPickupRadius = 0.6f;
        [Tooltip("보스에서 땅까지 날아가는 시간(초).")]
        [Min(0.05f)] public float goldFlightSeconds = 0.6f;
        [Tooltip("첫 쿨타임 / 쿨타임(초) - 미정.")]
        [Min(0f)] public float goldFirstCooldown = 18f;
        [Min(0f)] public float goldCooldown = 30f;

        [Header("배지기")]
        [Tooltip("시계 방향 회전 수(바퀴).")]
        public float[] grabRotations = { 0.5f, 2f, 3f, 6f };
        [Tooltip("잡힌 사람 제압 시간(초) - 이동 · 공격 불가.")]
        public float[] grabSuppressSeconds = { 3f, 3f, 4f, 5f };
        public float[] grabSlamDamage = { 20f, 30f, 60f, 120f };
        [Tooltip("잡기 사거리(칸) - 미정. 내려치는 거리 = 이 값 × grabThrowMultiplier.")]
        [Min(0.1f)] public float grabRange = 2f;
        [Min(0f)] public float grabThrowMultiplier = 2f;
        [Tooltip("한 바퀴 도는 데 걸리는 시간(초) - 연출용.")]
        [Min(0.05f)] public float grabSecondsPerTurn = 0.6f;
        [Tooltip("잡힌 사람이 보스 몸에서 떨어진 거리(유닛, 크기 1 기준).")]
        [Min(0f)] public float grabHoldRadius = 0.9f;
        [Tooltip("첫 쿨타임 / 쿨타임(초) - 미정.")]
        [Min(0f)] public float grabFirstCooldown = 14f;
        [Min(0f)] public float grabCooldown = 22f;

        [Header("정신병 - 분신이 한 명을 다른 차원으로")]
        [Tooltip("분신 공격력 = 본체 기본 공격 피해 × 비율.")]
        public float[] mentalCloneDamageRatio = { 0.4f, 0.5f, 0.6f, 1f };
        [Tooltip("분신 체력 = 이번 보스 최대 체력 × 비율 (5웨이브 660/990/1,485/3,300).")]
        public float[] mentalCloneHealthRatio = { 0.2f, 0.3f, 0.45f, 1f };
        [Min(0f)] public float mentalFirstCooldown = 10f;
        [Tooltip("쿨타임(초) - 예섭 기획: 지속시간이 끝난 뒤부터 돈다.")]
        public float[] mentalCooldown = { 140f, 110f, 80f, 30f };
        [Min(0.1f)] public float mentalDuration = 8f;
        [Tooltip("차원 반지름(유닛) - 끌려간 사람과 분신이 이 원 밖으로 못 나간다 - 미정.")]
        [Min(1f)] public float mentalRealmRadius = 4.5f;

        [Header("5웨이브 중간 보스 결과 - 기획서")]
        [Min(0)] public int midDefeatRerollTickets = 1;
        [Min(0)] public int midDefeatSoul = 125;
        [Min(0)] public int midDefeatGold = 20;
        [Min(0)] public int midFailGrudge = 250;

        /// <summary>원한 단계(0~3)에 맞는 값. 배열이 짧으면 마지막 값을 쓴다.</summary>
        public static float Pick(float[] values, int stage)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }
            return values[Mathf.Clamp(stage, 0, values.Length - 1)];
        }

        public static int Pick(int[] values, int stage)
        {
            if (values == null || values.Length == 0)
            {
                return 0;
            }
            return values[Mathf.Clamp(stage, 0, values.Length - 1)];
        }
    }
}
