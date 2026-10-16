using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 캐릭터 기준 스탯 데이터. 하영을 100% 기준으로 두고, 다른 캐릭터는
    /// 이 값을 비율로 조정한 별도 에셋으로 만든다.
    ///
    /// 2026-10-13 기준: 하영의 기준 스탯(maxHealth/moveSpeed/attackPower/
    /// attacksPerSecond/attackRange)은 공식 스탯 시트(스탯 천명_캐릭터_261012.xlsx,
    /// Sheet1)의 수치로 마이그레이션됨(기획 확정 - CheonMyeongPrototypeSetup.
    /// MigrateHayoungStatsToOfficialSheet 참고). attackArcDegrees/
    /// attackAwarenessRangeMultiplier는 해당 시트에 없는 프로토타입 전용 값이라
    /// 여전히 미정이며 플레이 테스트로 조정한다. 8명 캐릭터 중 하영 외 나머지는
    /// MVP 범위 "미정"이라 아직 별도 에셋을 만들지 않았다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterStats", menuName = "CheonMyeong/Character Stats")]
    public class CharacterStatsSO : ScriptableObject
    {
        [Header("식별")]
        public string characterId;
        public string displayName;

        [Header("기준 스탯 (하영 기준 - 공식 스탯 시트 261012 값)")]
        [Tooltip("최대 체력")]
        public float maxHealth = 150f;

        [Tooltip("초당 이동 속도 (유닛/초)")]
        public float moveSpeed = 5f;

        [Header("기본 무기 스탯")]
        [Tooltip("공격 1회당 피해량")]
        public float attackPower = 20f;

        [Tooltip("초당 공격 횟수 (공속). 값이 클수록 빠르다 - 공식 스탯 시트의 AtkSpeed\n" +
                 "컬럼과 같은 방향/단위로 맞춤(시트 원문: \"초당 공격 횟수로 해석, 값이\n" +
                 "클수록 빠름\"). 이전엔 attackInterval(초 단위 간격, 작을수록 빠름)이었는데\n" +
                 "시트와 방향이 반대라 혼동 위험이 있어 필드 자체를 시트와 동일한 의미로\n" +
                 "교체함 - 실제 대기시간(초)이 필요한 곳(PlayerAutoAttack)에서는\n" +
                 "1f / attacksPerSecond로 변환해서 쓴다(0으로 나누기 방지 가드 필수).")]
        public float attacksPerSecond = 0.8f;

        [Tooltip("공격 판정 범위 (유닛). 원형 판정 반지름으로 우선 사용하고,\n" +
                 "부채꼴/사거리+폭 등 형태는 추후 확정 시 바꾼다.")]
        public float attackRange = 1f;

        [Tooltip("자동공격 '대기' 판정용 배수. attackRange에 이 배수를 곱한 반경 안에\n" +
                 "적이 하나도 없으면 애초에 공격을 시도하지 않는다. 실제 타격 판정도\n" +
                 "이 반경을 그대로 쓰므로(PlayerAutoAttack.Attack 참고), 이 값이 곧\n" +
                 "'몬스터 인지거리' = '실제 공격 사거리'다 - 1보다 크게 두면 attackRange보다\n" +
                 "실질적으로 사거리가 늘어나는 셈이라(기획 지시로 롤백한 attackRange가\n" +
                 "무의미해짐), 1배(= attackRange와 동일)를 기본값으로 둔다.\n" +
                 "코드에 상수로 박아두지 않고 캐릭터별로 조정 가능하게 여기 둠.")]
        public float attackAwarenessRangeMultiplier = 1f;

        [Tooltip("공격 판정의 부채꼴 전체 각도(도). 조준 방향을 중심으로 좌우 절반씩\n" +
                 "적용된다(예: 90 = 조준 방향 기준 ±45도).\n" +
                 "참고: 이 필드가 추가되기 전에는 방향 판정 자체가 없어 OverlapCircle로만\n" +
                 "판정해 사실상 360도 전방위였음 - 기획 지시(\"공격 폭이 너무 넓다, 지금\n" +
                 "거의 180도인데 반으로 줄이자\")에 따라 90도를 기본값으로 새로 설정함.")]
        public float attackArcDegrees = 90f;

        [Header("자동줍기 (Magnet, 261013 추가)")]
        [Tooltip("드랍 아이템(골드/경험치)이 자동으로 끌려오기 시작하는 반경(유닛) - 공식\n" +
                 "스탯 시트의 Magnet 컬럼에 해당하는 자리. 정확한 수치는 밸런스 미정이라\n" +
                 "플레이스홀더값(LootPickup.cs가 이 반경으로 끌어당김 여부를 판정한다).")]
        public float magnetRadius = 2.5f;
    }
}
