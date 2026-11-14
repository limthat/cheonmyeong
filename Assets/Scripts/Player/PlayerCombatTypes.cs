using CheonMyeong.Enemies;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261114 추가 - 서버의 피해 한 번(근접 · 투사체 공통, PlayerAutoAttack.ServerDamageTarget). 고유 패시브 · 신내림 · 전용 신통력이
    /// ModifyOutgoing에서 값을 바꾼다. Damage는 치명타 전 피해, FinalDamage는 실제로 들어간 피해.
    /// </summary>
    public struct OutgoingHit
    {
        public Collider2D Target;
        public Enemy Enemy;
        public float Damage;
        public float CritChance;
        public float CritMultiplier;
        public bool ForceCrit;
        public float Knockback;
        public Vector2 Direction;
        public float Distance;
        public bool IsProjectile;
        public bool Crit;
        public float FinalDamage;
        /// <summary>이 투사체(또는 찌르기)가 몇 번째로 맞힌 원귀인지(0 = 첫 원귀). 편전 · 꿰뚫는 낙인.</summary>
        public int HitIndex;
        /// <summary>공격 종류 표시(0 = 기본 공격). CharacterKit이 각성 · 전용 신통력 타격을 가른다.</summary>
        public int Tag;
    }

    /// <summary>
    /// 261114 추가 - 플레이어 오브젝트에 붙어 서버 피해 계산에 끼는 컴포넌트(고유 패시브 등). 서버에서만 불린다.
    /// </summary>
    public interface IPlayerCombatHook
    {
        void ModifyOutgoing(ref OutgoingHit hit);
        void OnHitLanded(in OutgoingHit hit, bool killed);
    }
}
