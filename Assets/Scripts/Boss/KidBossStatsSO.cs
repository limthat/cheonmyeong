using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261121 추가 - 어린 억쇠(5웨이브 중간 보스) 수치. 기획서 v1.2 9-1 · [스탯] 엑셀 「어린 억쇠」 시트.
    /// 원한과 상관없이 고정(맑음 고정). 거리 = 칸(1칸 = 1유닛), 시간 = 초, 이동 속도는 새 단위 ÷ 60 = 유닛/초.
    /// "제안" · "미정"은 기획서에 확정값이 없는 임시값이라 이 에셋에서 고치면 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "EorinEoksoe", menuName = "CheonMyeong/Kid Boss Stats")]
    public class KidBossStatsSO : ScriptableObject
    {
        [Header("기본")]
        public string displayName = "어린 억쇠";
        [Tooltip("체력 - 기획서 3,000(1칸).")]
        [Min(1f)] public float maxHealth = 3000f;
        [Tooltip("이동 속도(유닛/초) - 엑셀 160(제안) ÷ 60.")]
        [Min(0f)] public float moveSpeed = 160f / 60f;
        [Tooltip("몸 반지름(유닛) - 공격 사거리를 몸 가장자리부터 잰다. 128px · PPU 100 그림 기준.")]
        [Min(0f)] public float bodyRadius = 0.35f;
        [Tooltip("261122 - 크기 배율(예섭 요청 1.75배). 그림 · 그림자 · 몸 판정 · 잡은 사람 거리가 같이 커지고, 공격 사거리는 몸 가장자리부터라 그만큼 늘어난다.")]
        [Min(0.5f)] public float sizeScale = 1.75f;
        [Tooltip("등장 연출 시간(초).")]
        [Min(0f)] public float appearSeconds = 1f;

        [Header("시전 속도 · 원한 (261122 예섭 요청)")]
        [Tooltip("스킬 시전 속도 배율 - 모든 예고 · 준비 모션(기본 공격 · 배지기 · 비행 이륙 · 회오리)이 이 배만큼 빨라진다. 1 = 기획서 값. 261123 - 1.5 → 1.1(예섭: 3분의 1쯤 느리게).")]
        [Min(0.5f)] public float castSpeed = 1.1f;
        [Tooltip("원한 단계(맑음 · 짙음 · 한 · 폭주)별 체력 배율 - 어린 억쇠는 조금씩만. 기획서 원문은 '원한 영향 없음'(예섭 요청으로 바꿈).")]
        public float[] healthByStage = { 1f, 1.5f, 2.5f, 4f }; // 261125 - 기획서 v1.3 조정안
        [Tooltip("원한 단계별 피해 배율(모든 스킬).")]
        public float[] damageByStage = { 1f, 1.5f, 2.5f, 4f }; // 261125 - 기획서 v1.3 조정안
        [Tooltip("원한 단계별 이동 속도 배율.")]
        public float[] speedByStage = { 1f, 1.02f, 1.04f, 1.06f };

        [Header("통통 뛰는 이동(외다리 - 억쇠와 같은 방식, 더 가볍게)")]
        [Min(0f)] public float hopHeight = 0.22f;
        [Min(0.1f)] public float hopsPerSecond = 3.2f;
        [Range(0.3f, 1f)] public float hopAirFraction = 0.7f;
        [Range(0f, 0.5f)] public float landingSquash = 0.15f;

        [Header("기본 공격 · 빗자루 후려치기")]
        [Tooltip("앞쪽 부채꼴 범위(칸) - 몸 가장자리부터.")]
        [Min(0f)] public float basicRange = 1.5f;
        [Tooltip("부채꼴 각도(도) - 미정.")]
        [Range(10f, 360f)] public float basicArcDegrees = 120f;
        [Min(0f)] public float basicDamage = 12f;
        [Min(0f)] public float basicCooldown = 1.8f;
        [Tooltip("맞으면 밀려나는 거리(칸).")]
        [Min(0f)] public float basicKnockbackTiles = 1f;
        [Tooltip("붉은 예고 → 타격(초). ATK 타격 프레임(18)이 이 순간에 오도록 모션을 늘린다 - 제안.")]
        [Min(0.1f)] public float basicWindup = 0.55f;
        [Min(0f)] public float basicRecovery = 0.3f;

        [Header("배지기 (작게)")]
        [Tooltip("바닥 붉은 원 예고(초) - 기획서 1초.")]
        [Min(0.2f)] public float grabWindupSeconds = 1f;
        [Tooltip("잡기 사거리(칸) - 미정. 내던지는 거리 = 이 값 × grabThrowMultiplier.")]
        [Min(0.1f)] public float grabRange = 1.5f;
        [Min(0f)] public float grabThrowMultiplier = 2f;
        [Tooltip("회전 수(바퀴).")]
        [Min(0f)] public float grabRotations = 0.5f;
        [Tooltip("제압 시간(초).")]
        [Min(0f)] public float grabSuppressSeconds = 2f;
        [Tooltip("내던지기 피해.")]
        [Min(0f)] public float grabThrowDamage = 15f;
        [Min(0f)] public float grabCooldown = 20f;
        [Tooltip("첫 쿨타임(초) - 미정.")]
        [Min(0f)] public float grabFirstCooldown = 10f;
        [Min(0.05f)] public float grabSecondsPerTurn = 0.6f;
        [Tooltip("잡힌 사람이 몸에서 떨어진 거리(유닛).")]
        [Min(0f)] public float grabHoldRadius = 0.55f;

        [Header("빗자루 비행")]
        [Tooltip("비행 시간(초) - 3번 꺾으니 4구간으로 나눈다.")]
        [Min(0.5f)] public float flySeconds = 4f;
        [Min(0)] public int flyTurns = 3;
        [Tooltip("꺾기 전 다음 방향 화살표가 먼저 뜨는 시간(초).")]
        [Min(0f)] public float flyWarnSeconds = 0.6f;
        [Tooltip("화살표 폭 · 길이(칸) - 한 구간에 날아가는 거리 = 길이.")]
        [Min(0.1f)] public float flyArrowWidth = 1.5f;
        [Min(0.5f)] public float flyArrowLength = 6f;
        [Min(0f)] public float flyContactDamage = 14f;
        [Min(0f)] public float flyKnockbackTiles = 2f;
        [Tooltip("부딪힘 판정 반지름(유닛).")]
        [Min(0.1f)] public float flyHitRadius = 0.6f;
        [Tooltip("먼지 줄 남는 시간(초) · 밟으면 이동 속도 감소.")]
        [Min(0f)] public float dustSeconds = 2f;
        [Range(0f, 0.9f)] public float dustSlow = 0.2f;
        [Tooltip("먼지 한 덩이 반지름(유닛).")]
        [Min(0.1f)] public float dustRadius = 0.45f;
        [Tooltip("내린 뒤 숨 고르기(초) - 때릴 기회.")]
        [Min(0f)] public float flyRestSeconds = 1f;
        [Min(0f)] public float flyCooldown = 18f;
        [Min(0f)] public float flyFirstCooldown = 6f;

        [Header("빗자루 회오리")]
        [Min(1)] public int tornadoCount = 3;
        [Tooltip("회오리 사이 각도(도) - 부채꼴 - 제안.")]
        [Range(0f, 60f)] public float tornadoSpreadDegrees = 25f;
        [Tooltip("바닥 화살표 예고(초).")]
        [Min(0.1f)] public float tornadoWarnSeconds = 1f;
        [Tooltip("직진 거리(칸).")]
        [Min(0.5f)] public float tornadoDistance = 6f;
        [Tooltip("날아가는 속도(유닛/초) - 제안.")]
        [Min(0.5f)] public float tornadoSpeed = 5f;
        [Min(0.1f)] public float tornadoHitRadius = 0.6f;
        [Min(0f)] public float tornadoDamage = 10f;
        [Min(0f)] public float tornadoKnockbackTiles = 2f;
        [Min(0f)] public float tornadoCooldown = 12f;
        [Min(0f)] public float tornadoFirstCooldown = 4f;

        [Header("5웨이브 결과 - 기획서")]
        [Min(0)] public int defeatRerollTickets = 1;
        [Min(0)] public int defeatSoul = 125;
        [Min(0)] public int defeatGold = 20;
        [Min(0)] public int failGrudge = 250;
    }
}
