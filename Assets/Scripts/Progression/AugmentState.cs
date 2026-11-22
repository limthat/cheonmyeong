using System;
using Unity.Netcode;

namespace CheonMyeong.Progression
{
    /// <summary>261104 - 신통력 칸 하나(왼쪽부터 고른 순서).</summary>
    public struct AugmentSlot : INetworkSerializable, IEquatable<AugmentSlot>
    {
        public byte Id;
        public byte Level;
        /// <summary>261118 - 원귀와의 거래: 이번 단계에서 뽑힌 보상 번호(0부터). 다른 신통력은 0.</summary>
        public byte Variant;

        public AugmentId Augment => (AugmentId)Id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Variant);
        }

        public bool Equals(AugmentSlot other) => Id == other.Id && Level == other.Level && Variant == other.Variant;
    }

    /// <summary>261104 - 무한 스탯을 고른 횟수.</summary>
    public struct AugmentStack : INetworkSerializable, IEquatable<AugmentStack>
    {
        public byte Id;
        public ushort Count;

        public AugmentId Augment => (AugmentId)Id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Count);
        }

        public bool Equals(AugmentStack other) => Id == other.Id && Count == other.Count;
    }

    /// <summary>261104 - 이번 레벨업에 제시된 카드 한 장. Level = 고르면 그 칸이 될 단계(일회용 · 무한은 1).</summary>
    public struct AugmentOffer : INetworkSerializable, IEquatable<AugmentOffer>
    {
        public byte Id;
        public byte Level;

        public AugmentId Augment => (AugmentId)Id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Level);
        }

        public bool Equals(AugmentOffer other) => Id == other.Id && Level == other.Level;
    }

    /// <summary>
    /// 261104 - 신통력이 캐릭터 기준 스탯에 더하는 값(합산 결과). 비율은 기준값 대비(0.1 = +10%).
    /// 모든 화면이 같은 칸 목록에서 같은 값을 계산한다(서버 판정 · 화면 연출이 어긋나지 않게).
    /// 261118 - 기획서 v0.9.9 공통 20종 · 저주 보상에 맞춰 칸을 늘림. 5단계 특별 능력 · 협동 · 일회용 지속 효과는 CharacterKit.Augments.
    /// </summary>
    public struct AugmentModifiers
    {
        public float MaxHealthBonus;
        public float CurseMaxHealthLoss;  // 261122 - 원귀와의 거래로 바친 최대 체력 비율(MaxHealthBonus에 이미 빠져 있음, 체력 바 잠긴 칸 표시용)
        public float MoveSpeedBonus;
        public float RegenPercent;        // 최대 체력 %/초 (0.0015 = 0.15%)
        public int ShieldBonus;           // 호신부 최대 횟수
        public float AttackBonus;
        public float AttackSpeedBonus;
        public float MeleeRangeBonus;     // 칸
        public float RangedRangeBonus;    // 칸(저주 보상)
        public float CritChanceBonus;
        public float CritDamageBonus;     // 배율에 더함(+0.1)
        public float CritMultiplierFloor; // 0이면 없음(저주 보상 "치명 피해 2.0")
        public bool CritAlways;           // 저주 5단계 "치명 100% 고정"
        public int ExtraSwings;
        public float ArcBonus;
        public float FollowUpSlashRatio;
        public float ProjectileSpeedBonus;
        public int ExtraProjectiles;
        public float Lifesteal;           // 넋 흡수(준 피해 비율)
        public float AreaBonus;           // 너울
        public float PickupRangeBonus;    // 넋 부르기
        public float KnockbackBonus;      // 칸
        public float GrowthBonus;         // 깨달음(내가 주운 영혼)
        public float GoldGainBonus;       // 복주머니
        public float PurifyBonus;         // 씻김굿(웨이브 끝 원한 감소)
        public float CurseDamageTaken;    // 원귀와의 거래 3단계부터
        public float ShieldRechargeSeconds; // 0이면 기본(20초)
        public bool ShieldFillToMax;      // 저주 5단계 "방어막 최대 5"
        public int CardBonus;             // 산통 흔들기 5단계: 선택지 +1
        public float LastSwingBonus;      // 칼춤 5단계
    }
}
