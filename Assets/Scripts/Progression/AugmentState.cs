using System;
using Unity.Netcode;

namespace CheonMyeong.Progression
{
    /// <summary>261104 - 신통력 칸 하나(왼쪽부터 고른 순서).</summary>
    public struct AugmentSlot : INetworkSerializable, IEquatable<AugmentSlot>
    {
        public byte Id;
        public byte Level;

        public AugmentId Augment => (AugmentId)Id;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Level);
        }

        public bool Equals(AugmentSlot other) => Id == other.Id && Level == other.Level;
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
    /// </summary>
    public struct AugmentModifiers
    {
        public float MaxHealthBonus;
        public float MoveSpeedBonus;
        public float RegenPerSecond;
        public float Armor;
        public float AttackBonus;
        public float AttackSpeedBonus;
        public float MeleeRangeBonus;
        public float CritChanceBonus;
        public int ExtraSwings;
        public float ArcBonus;
        public float FollowUpSlashRatio;
    }
}
