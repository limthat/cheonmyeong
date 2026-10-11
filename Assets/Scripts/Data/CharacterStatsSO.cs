using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 캐릭터 기준 스탯 데이터. 하영을 100% 기준으로 두고, 다른 캐릭터는
    /// 이 값을 비율로 조정한 별도 에셋으로 만든다.
    /// 수치는 전부 미정 상태이며 프로토타입 플레이 테스트로 조정한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterStats", menuName = "CheonMyeong/Character Stats")]
    public class CharacterStatsSO : ScriptableObject
    {
        [Header("식별")]
        public string characterId;
        public string displayName;

        [Header("기준 스탯 (전부 미정 - 프로토타입에서 조정)")]
        [Tooltip("최대 체력")]
        public float maxHealth = 100f;

        [Tooltip("초당 이동 속도 (유닛/초)")]
        public float moveSpeed = 5f;

        [Header("기본 무기 스탯")]
        [Tooltip("공격 1회당 피해량")]
        public float attackPower = 10f;

        [Tooltip("공격 사이 간격 (초)")]
        public float attackInterval = 1f;

        [Tooltip("공격 판정 범위 (유닛). 원형 판정 반지름으로 우선 사용하고,\n" +
                 "부채꼴/사거리+폭 등 형태는 추후 확정 시 바꾼다.")]
        public float attackRange = 2f;
    }
}
