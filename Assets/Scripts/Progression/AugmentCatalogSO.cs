using System;
using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Progression
{
    public enum AugmentKind : byte
    {
        Common,
        Exclusive,
        OneTime,
        Infinite, // 261118 - 기획서 v0.9.6에서 삭제(값은 네트워크 호환을 위해 남김)
        Coop,     // 261118 - 협동 신통력 6종
    }

    public enum AugmentTarget : byte
    {
        All,
        Melee,
        Ranged,
    }

    /// <summary>
    /// 신통력 종류. 값은 네트워크로 byte 하나로 보내므로 바꾸지 말 것(새 항목은 뒤에 추가).
    /// </summary>
    public enum AugmentId : byte
    {
        None = 0,
        MaxHealth = 1,
        MoveSpeed = 2,
        Regen = 3,
        Armor = 4,
        AttackPower = 5,
        ProjectileSpeed = 6,
        AttackSpeed = 7,
        MeleeRange = 8,
        CooldownReduction = 9,
        ProjectileCount = 10,
        PickupRange = 11,
        Luck = 12,
        Growth = 13,
        Curse = 14,
        Reroll = 15,
        CritChance = 16,
        CritDamage = 17,
        MultiSwing = 18,
        // 261118 - 기획서 v0.9.9 공통 20종 중 새로 생긴 스탯
        Lifesteal = 19,   // 넋 거두기
        Area = 20,        // 너울
        Knockback = 21,   // 물렀거라
        GoldGain = 22,    // 복주머니
        Cleanse = 23,     // 씻김굿

        HayoungWideSlash = 40,
        HayoungFollowUpSlash = 41,
        // 261114 - 기획서 v0.9.7 6장 ⑤ 전용 신통력 16종(캐릭터당 2종). 위 두 개(옛 하영 전용)는 꺼둔다.
        HayoungGeomgi = 42,
        HayoungAsPlanned = 43,
        JuwonByeoksabu = 44,
        JuwonGyeolbakbu = 45,
        ParkDangpa = 46,
        ParkJeokjang = 47,
        AraReturningKnife = 48,
        AraChilseong = 49,
        HwanjoCheoljeon = 50,
        HwanjoGakgung = 51,
        JihyunBaesujin = 52,
        JihyunManwol = 53,
        HunCharge = 54,
        HunHorseback = 55,
        YeoreWhirl = 56,
        YeoreTemper = 57,

        // 261118 - 협동 6종
        CoopBack = 60,    // 등 맡기기
        CoopPair = 61,    // 짝패
        CoopMeal = 62,    // 한솥밥
        CoopRope = 63,    // 금줄
        CoopCheer = 64,   // 얼쑤
        CoopRush = 65,    // 한달음

        Prayer = 80,
        SoulHarvest = 81,
        CoinPouch = 82,
        Purify = 83,
        // 261118 - 일회용 15종(기도 · 엽전 주머니는 위 값 그대로)
        RiceBall = 84,       // 주먹밥
        HerbalTea = 85,      // 약탕
        HoneyCake = 86,      // 꿀떡
        Cheongsim = 87,      // 청심환
        CoinBundle = 88,     // 엽전 꾸러미
        SoulHandful = 89,    // 영혼 한 줌
        SoulJar = 90,        // 영혼 단지
        Talisman = 91,       // 부적 한 장
        TalismanBundle = 92, // 부적 다발
        Makgeolli = 93,      // 막걸리
        StrawShoes = 94,     // 새 짚신
        HolyWater = 95,      // 정화수
        Yut = 96,            // 윷 한 판

        InfiniteAttack = 100,
        InfiniteAttackSpeed = 101,
        InfiniteCrit = 102,
        InfiniteMaxHealth = 103,
        InfiniteMoveSpeed = 104,
    }

    [Serializable]
    public class AugmentEntry
    {
        public AugmentId id;
        public string displayName;
        public AugmentKind kind;
        public AugmentTarget target;
        [Tooltip("전용 신통력만 - CharacterStatsSO.characterId와 같아야 그 캐릭터 카드에 나온다.")]
        public string characterId;
        [Tooltip("카드 설명. {0} = 그 단계까지의 누적값(Lv), 일회용 · 무한은 values[0].")]
        public string descriptionFormat;
        [Tooltip("Lv1~5 누적값(기획서 표 그대로). 일회용 · 무한은 [0]만 쓴다.")]
        public float[] values = new float[5];
        [Tooltip("끄면 카드에 안 나온다. 기획서가 모호하거나 아직 쓸 수 없는 항목.")]
        public bool enabled = true;
        [Tooltip("꺼둔 이유(기록용).")]
        public string disabledReason;

        public float ValueAt(int level)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }
            return values[Mathf.Clamp(level - 1, 0, values.Length - 1)];
        }

        /// <summary>칸을 차지하고 단계가 오르는 종류(공통 · 전용 · 협동).</summary>
        public bool Leveled => kind == AugmentKind.Common || kind == AugmentKind.Exclusive || kind == AugmentKind.Coop;

        public string Describe(int level)
        {
            float value = kind == AugmentKind.OneTime || kind == AugmentKind.Infinite ? ValueAt(1) : ValueAt(level);
            string format = string.IsNullOrEmpty(descriptionFormat) ? "{0}" : descriptionFormat;
            return string.Format(format, value.ToString("0.##"));
        }
    }

    /// <summary>
    /// 261118 - 원귀와의 거래(저주) 단계 보상 하나. 기획서 v0.9.9 7장 「저주 · 원귀와의 거래」 표 그대로.
    /// 단계가 오르면 이전 보상은 사라지고 새 단계 보상 1개가 랜덤으로 붙는다(교체 방식).
    /// </summary>
    public readonly struct CurseReward
    {
        public readonly string Text;
        public readonly float Attack, AttackSpeed, Regen, Lifesteal, Crit, CritDamage, CritFloor, Growth, Gold, Purify, MeleeRange, RangedRange, ShieldRecharge;
        public readonly int Shield, Rerolls;
        public readonly bool CritAlways, ShieldFill;

        public CurseReward(string text, float attack = 0f, float attackSpeed = 0f, float regen = 0f, float lifesteal = 0f,
                           float crit = 0f, float critDamage = 0f, float critFloor = 0f, float growth = 0f, float gold = 0f,
                           float purify = 0f, float meleeRange = 0f, float rangedRange = 0f, int shield = 0, int rerolls = 0,
                           bool critAlways = false, bool shieldFill = false, float shieldRecharge = 0f)
        {
            Text = text; Attack = attack; AttackSpeed = attackSpeed; Regen = regen; Lifesteal = lifesteal; Crit = crit;
            CritDamage = critDamage; CritFloor = critFloor; Growth = growth; Gold = gold; Purify = purify;
            MeleeRange = meleeRange; RangedRange = rangedRange; Shield = shield; Rerolls = rerolls;
            CritAlways = critAlways; ShieldFill = shieldFill; ShieldRecharge = shieldRecharge;
        }
    }

    public static class CurseTable
    {
        /// <summary>단계별 받는 피해 증가(1~5단계).</summary>
        public static readonly float[] DamageTaken = { 0f, 0f, 0.10f, 0.20f, 0.30f };

        /// <summary>[단계-1][번호]. 체력 재생 · 넋 흡수는 비율(0.006 = 0.6%/초).</summary>
        public static readonly CurseReward[][] Rewards =
        {
            new[]
            {
                new CurseReward("공격력 +10% · 방어막 +1", attack: 0.10f, shield: 1),
                new CurseReward("체력 재생 0.6%/초", regen: 0.006f),
                new CurseReward("넋 흡수 3%", lifesteal: 0.03f),
                new CurseReward("치명 +10% · 치명 피해 +0.3", crit: 0.10f, critDamage: 0.3f),
            },
            new[]
            {
                new CurseReward("공격력 +25% · 공격 속도 +5%", attack: 0.25f, attackSpeed: 0.05f),
                new CurseReward("체력 재생 0.8%/초 · 넋 흡수 3%", regen: 0.008f, lifesteal: 0.03f),
                new CurseReward("리롤 +3 · 경험치 +30%", rerolls: 3, growth: 0.30f),
            },
            new[]
            {
                new CurseReward("공격력 +60%", attack: 0.60f),
                new CurseReward("넋 흡수 8% · 공격력 +25%", lifesteal: 0.08f, attack: 0.25f),
                new CurseReward("엽전 +50% · 원한 정화 -10%", gold: 0.50f, purify: -0.10f),
                new CurseReward("방어막 +3 · 체력 재생 1.2%/초", shield: 3, regen: 0.012f),
            },
            new[]
            {
                new CurseReward("공격력 +100% · 공격 속도 +20%", attack: 1.00f, attackSpeed: 0.20f),
                new CurseReward("치명 +40% · 치명 피해 2.0 · 공격력 +60%", crit: 0.40f, critFloor: 2.0f, attack: 0.60f),
                new CurseReward("사거리 근접 +2 / 원거리 +1 · 공격력 +80%", meleeRange: 2f, rangedRange: 1f, attack: 0.80f),
            },
            new[]
            {
                new CurseReward("공격력 +300%", attack: 3.00f),
                new CurseReward("공격 속도 +100% · 공격력 +100%", attackSpeed: 1.00f, attack: 1.00f),
                new CurseReward("치명 100% 고정 · 치명 피해 2.0 · 공격력 +100%", critAlways: true, critFloor: 2.0f, attack: 1.00f),
                new CurseReward("방어막 최대 5 · 충전 10초 · 공격력 +100%", shieldFill: true, shieldRecharge: 10f, attack: 1.00f),
            },
        };

        public static bool TryGet(int level, int variant, out CurseReward reward)
        {
            if (level < 1 || level > Rewards.Length)
            {
                reward = default;
                return false;
            }
            CurseReward[] row = Rewards[level - 1];
            reward = row[Mathf.Clamp(variant, 0, row.Length - 1)];
            return true;
        }
    }

    /// <summary>
    /// 261104 추가 - 신통력(증강) 목록과 규칙 수치. 261118 - 기획서 v0.9.9 7장(공통 20 · 협동 6 · 일회용 15 · 전용 16)으로 갱신.
    /// 에셋을 안 만들어도 <see cref="Default"/>가 같은 기본값을 코드로 만들어 쓴다(두 사람 화면이 같은 값을 쓰도록).
    /// 수치를 바꾸고 싶으면 'CheonMyeong/Setup/Create Augment Catalog'로 에셋을 만들어 고친다.
    /// </summary>
    [CreateAssetMenu(fileName = "Augments", menuName = "CheonMyeong/Augment Catalog")]
    public class AugmentCatalogSO : ScriptableObject
    {
        [Header("규칙 - 기획서 07장")]
        [Min(1)] public int maxSlots = 6;
        [Min(1)] public int maxLevel = 5;
        [Min(1)] public int cardsPerChoice = 3;
        [Tooltip("선택 제한 시간(초) - 지나면 남은 사람은 무작위로 고른다.")]
        [Min(1f)] public float choiceSeconds = 10f;
        [Tooltip("넋 부르기(획득 범위 %)의 기준 거리(칸). 기본 자석 범위가 0이라 % 기준이 없어서, 예전 기본값 2.5칸을 기준으로 둔다.")]
        [Min(0f)] public float pickupRangeBase = 2.5f;
        [Tooltip("일회용 회복 카드(주먹밥 · 약탕 · 기도 · 꿀떡 · 청심환)가 나오는 체력 비율 - 기획서 70% 이하.")]
        [Range(0f, 1f)] public float healCardHealthRatio = 0.7f;
        [Tooltip("정화수가 나오는 최소 원한 - 기획서 5.")]
        [Min(0)] public int holyWaterMinGrudge = 5;

        [Header("세부 수치 - 기획서")]
        [Tooltip("연속 공격 휘두르기 간격(초) - 기획서 0.15초.")]
        [Min(0.01f)] public float multiSwingInterval = 0.15f;
        [Tooltip("연속 공격 Lv2 · Lv4에서 각각 더해지는 공격력 - 기획서 +5%.")]
        [Min(0f)] public float multiSwingAttackBonusPerStep = 0.05f;
        [Tooltip("연속 베기(하영 전용)가 첫 베기 뒤 나가는 간격(초) - 기획서에 없음, 연속 공격과 같게 둠.")]
        [Min(0.01f)] public float followUpSlashDelay = 0.15f;

        public List<AugmentEntry> entries = new List<AugmentEntry>();

        private static AugmentCatalogSO defaultCatalog;

        /// <summary>에셋이 연결 안 됐을 때 쓰는 코드 기본값(기획서 v0.9.4).</summary>
        public static AugmentCatalogSO Default
        {
            get
            {
                if (defaultCatalog == null)
                {
                    defaultCatalog = CreateInstance<AugmentCatalogSO>();
                    defaultCatalog.name = "Augments(Default)";
                    defaultCatalog.hideFlags = HideFlags.DontSave;
                    defaultCatalog.entries = BuildDefaultEntries();
                }
                return defaultCatalog;
            }
        }

        public AugmentEntry Find(AugmentId id)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].id == id)
                {
                    return entries[i];
                }
            }
            return null;
        }

        public float ValueAt(AugmentId id, int level)
        {
            AugmentEntry entry = Find(id);
            return entry != null ? entry.ValueAt(level) : 0f;
        }

        public static List<AugmentEntry> BuildDefaultEntries()
        {
            var list = new List<AugmentEntry>
            {
                // ───── 공통 20종(기획서 v0.9.9 7장, 값 = 그 단계까지의 누적값) ─────
                Common(AugmentId.MaxHealth, "밥심", AugmentTarget.All, "최대 체력 +{0}% · 5단계 「든든한 한 끼」 웨이브 시작 때 체력 25% 회복", 10, 20, 30, 40, 50),
                Common(AugmentId.MoveSpeed, "축지법", AugmentTarget.All, "이동 속도 +{0}% · 5단계 「줄행랑」 체력 30% 이하일 때 이동 속도 +20%", 5, 10, 15, 20, 25),
                Common(AugmentId.Regen, "숨 고르기", AugmentTarget.All, "초마다 최대 체력의 {0}% 회복 · 5단계 「깊은 숨」 3초 동안 안 맞으면 2배", 0.15f, 0.3f, 0.45f, 0.6f, 0.75f),
                Common(AugmentId.Armor, "호신부", AugmentTarget.All, "방어막 최대 +{0}회 (1회 = 공격 1번을 막고 0.5초 무적) · 5단계 「소지」 막으면 주변 2칸 밀쳐내고 공격력 50% 피해", 1, 1, 2, 2, 3),
                Common(AugmentId.AttackPower, "서슬", AugmentTarget.All, "공격력 +{0}% · 5단계 「베어 넘김」 처치하면 2초 동안 공격력 +15%", 10, 20, 30, 40, 50),
                Common(AugmentId.Lifesteal, "넋 거두기", AugmentTarget.All, "준 피해의 {0}% 회복 (한 번 공격에 1마리분) · 5단계 「넋 한 줌」 처치마다 체력 1 회복", 1, 2, 3, 4, 5),
                Common(AugmentId.AttackSpeed, "신들린 손", AugmentTarget.All, "공격 속도 +{0}% · 5단계 「신명」 공격 20번마다 3초 동안 공격 속도 +30%", 8, 16, 24, 32, 40),
                Common(AugmentId.MeleeRange, "한 뼘 더", AugmentTarget.Melee, "근접 사거리 +{0}칸 · 5단계 「끝자락」 사거리 바깥 30%에 맞은 원귀 피해 +20%", 0.2f, 0.4f, 0.6f, 0.8f, 1f),
                Common(AugmentId.Area, "너울", AugmentTarget.All, "공격 범위 +{0}% · 5단계 「큰 물결」 5번째 공격마다 공격 범위 2배", 10, 20, 30, 40, 50),
                Common(AugmentId.ProjectileCount, "흩날림", AugmentTarget.Ranged, "투사체 +{0}개 (Lv2 · Lv4 공격력 +5%) · 5단계 「물수제비」 25% 확률로 근처 원귀에게 한 번 더 튕김", 1, 1, 2, 2, 3),
                Common(AugmentId.PickupRange, "넋 부르기", AugmentTarget.All, "획득 범위 +{0}% · 5단계 「초혼」 30초마다 맵의 영혼을 전부 끌어옴", 20, 40, 60, 80, 100),
                Common(AugmentId.Knockback, "물렀거라", AugmentTarget.All, "넉백 +{0}칸 · 5단계 「호통」 밀려난 원귀 0.5초 기절", 0.1f, 0.2f, 0.3f, 0.4f, 0.5f),
                Common(AugmentId.Growth, "깨달음", AugmentTarget.All, "내가 주운 영혼 +{0}% · 5단계 「득도」 레벨업할 때 체력 10% 회복", 8, 16, 24, 32, 40),
                Common(AugmentId.Curse, "원귀와의 거래", AugmentTarget.All, "최대 체력 {0}%를 바치고 단계 보상 1개를 랜덤으로 받음 (3단계부터 받는 피해 증가)", 10, 25, 40, 60, 80),
                Common(AugmentId.Reroll, "산통 흔들기", AugmentTarget.All, "리롤 +{0}회 · 5단계 「네 번째 산가지」 선택지 3장 → 4장", 1, 2, 3, 4, 5),
                Common(AugmentId.CritChance, "매의 눈", AugmentTarget.All, "치명타 확률 +{0}% · 5단계 「급살」 치명타에 맞은 원귀 3초 동안 받는 피해 +10%", 3, 6, 9, 12, 15),
                Common(AugmentId.CritDamage, "벽력", AugmentTarget.All, "치명타 피해 +{0}% · 5단계 「벼락 갈래」 치명타 때 주변 1칸에 공격력 30% 번개", 10, 20, 30, 40, 50),
                Common(AugmentId.MultiSwing, "칼춤", AugmentTarget.Melee, "한 번에 {0}번 더 휘두름 (Lv2 · Lv4 공격력 +5%) · 5단계 「마지막 춤사위」 마지막 휘두르기 피해 +50%", 1, 1, 2, 2, 3),
                Common(AugmentId.GoldGain, "복주머니", AugmentTarget.All, "엽전 획득 +{0}% · 5단계 「덤」 웨이브를 끝까지 살아남으면 엽전 +5", 10, 20, 30, 40, 50),
                Common(AugmentId.Cleanse, "씻김굿", AugmentTarget.All, "웨이브 끝 원한 -{0}% · 5단계 「한풀이」 원한 단계가 오르면 그 웨이브 동안 둘 다 공격력 +10%", 5, 10, 15, 20, 25),
                Disabled(Common(AugmentId.ProjectileSpeed, "투사체 속도", AugmentTarget.Ranged, "투사체 속도 +{0}%", 10, 20, 30, 40, 50), "기획서 v0.9.6: 삭제"),
                Disabled(Common(AugmentId.CooldownReduction, "쿨다운 감소", AugmentTarget.All, "재사용 대기시간 -{0}%", 5, 10, 15, 20, 25), "기획서 v0.9.6: 삭제"),
                Disabled(Common(AugmentId.Luck, "행운", AugmentTarget.All, "좋은 신통력 확률 +{0}%", 10, 20, 30, 40, 50), "기획서 v0.9.6: 삭제"),

                // ───── 협동 6종(동료가 쓰러지면 꺼짐 · 공명: 둘 다 같은 카드면 둘 다 +50%) ─────
                Coop(AugmentId.CoopBack, "등 맡기기", "동료가 6칸 안이면 받는 피해 -{0}% · 5단계 「액땜」 동료가 쓰러질 공격을 내 호신부로 대신 막음 (웨이브당 1회)", 3, 6, 9, 12, 15),
                Coop(AugmentId.CoopPair, "짝패", "동료가 6칸 안이면 공격력 +{0}% · 5단계 「합공」 둘이 같은 원귀를 2초 안에 때리면 그 원귀 받는 피해 +20%", 4, 8, 12, 16, 20),
                Coop(AugmentId.CoopMeal, "한솥밥", "내가 회복한 양의 {0}%를 동료도 회복 · 5단계 「밥상」 웨이브 시작 때 둘 다 체력 15% 회복", 10, 20, 30, 40, 50),
                Coop(AugmentId.CoopRope, "금줄", "두 사람 사이 금줄(최대 10칸)에 닿은 원귀에게 초당 공격력 {0}% · 5단계 「금줄 치기」 닿은 원귀 이동 속도 -30%", 10, 20, 30, 40, 50),
                Coop(AugmentId.CoopCheer, "얼쑤", "동료가 처치하면 3초 동안 공격 속도 +{0}% (최대 5중첩) · 5단계 「굿판」 둘이 10초 안에 30마리 → 5초 동안 둘 다 공격 속도 +20%", 2, 4, 6, 8, 10),
                Coop(AugmentId.CoopRush, "한달음", "동료와 10칸 넘게 떨어지면 동료 쪽 이동 속도 +{0}% · 5단계 「곁으로」 3초 지나면 동료 곁으로 순간이동 (30초마다)", 10, 20, 30, 40, 50),

                Disabled(Exclusive(AugmentId.HayoungWideSlash, "넓은 베기", "hayoung", "사인검 베기 각도 +{0}%", 10, 20, 30, 40, 50),
                         "기획서 v0.9.7: 하영 전용은 검기 · 계획대로로 바뀜"),
                Disabled(Exclusive(AugmentId.HayoungFollowUpSlash, "연속 베기", "hayoung", "같은 방향으로 한 번 더 벤다 (피해 {0}%)", 50, 65, 80, 95, 110),
                         "기획서 v0.9.7: 하영 전용은 검기 · 계획대로로 바뀜"),
                // 261114 - 기획서 v0.9.7 6장 ⑤ (값 = 그 단계까지의 누적값). 효과는 CharacterKit.
                Exclusive(AugmentId.HayoungGeomgi, "검기", "hayoung", "최대 체력 -30% · 베기가 검기로 날아감 (8칸) · 피해 {0}% · 5단계 파사", 70, 80, 90, 100, 110),
                Exclusive(AugmentId.HayoungAsPlanned, "계획대로", "hayoung", "「완벽주의」 공격력 +{0}% · 5단계 완벽한 하루", 14, 18, 22, 26, 30),
                Exclusive(AugmentId.JuwonByeoksabu, "벽사부", "hanjuwon", "부적이 원귀를 {0}명 뚫고 지나감 · 5단계 꿰뚫는 낙인", 1, 2, 3, 4, 5),
                Exclusive(AugmentId.JuwonGyeolbakbu, "결박부", "hanjuwon", "맞은 원귀 이동 속도 -{0}% (1초) · 5단계 발목 잡기", 10, 15, 20, 25, 30),
                Exclusive(AugmentId.ParkDangpa, "당파 찌르기", "parkgyuhyeok", "찌르기가 세 갈래로 · 옆 갈래 피해 {0}% · 5단계 삼지일격", 30, 40, 50, 60, 70),
                Exclusive(AugmentId.ParkJeokjang, "적장 사냥", "parkgyuhyeok", "중형 원귀 · 보스 피해 +{0}% · 5단계 지휘", 10, 20, 30, 40, 50),
                Exclusive(AugmentId.AraReturningKnife, "돌아오는 신칼", "leeara", "신칼이 돌아오며 한 번 더 · 왕복 피해 {0}% · 5단계 길흉 가르기", 50, 60, 70, 80, 100),
                Exclusive(AugmentId.AraChilseong, "칠성의 점괘", "leeara", "「오늘의 운세」 길 확률 {0}% · 5단계 대길", 58, 66, 74, 82, 90),
                Exclusive(AugmentId.HwanjoCheoljeon, "철전", "choihwanjo", "화살이 원귀를 {0}명 관통 · 5단계 편전", 1, 2, 3, 4, 5),
                Exclusive(AugmentId.HwanjoGakgung, "각궁", "choihwanjo", "사거리 +{0}칸 · 투사체 속도 +{0}0% · 5단계 먼 바람", 1, 2, 3, 4, 5),
                Exclusive(AugmentId.JihyunBaesujin, "배수진", "sinjihyeon", "「물러서지 않는다」 마리당 -{0}% · 5단계 한 번 한 약속", 2.2f, 2.4f, 2.6f, 2.8f, 3f),
                Exclusive(AugmentId.JihyunManwol, "만월 베기", "sinjihyeon", "5번째 공격마다 주변 전체를 벤다 (피해 {0}%) · 5단계 청룡 회오리", 100, 120, 140, 160, 200),
                Exclusive(AugmentId.HunCharge, "철기 돌격", "imhun", "Space 충전 후 돌진 (피해 {0}%) · 5단계 말발굽", 100, 130, 160, 190, 250),
                Exclusive(AugmentId.HunHorseback, "말 위에서", "imhun", "이동 중 공격 속도 +{0}% · 5단계 질주", 6, 12, 18, 24, 30),
                Exclusive(AugmentId.YeoreWhirl, "휘몰이", "simyeore", "4번째 바람마다 회오리 2초 (초당 피해 {0}%) · 5단계 돌개바람", 20, 30, 40, 50, 60),
                Exclusive(AugmentId.YeoreTemper, "불같은 성미", "simyeore", "「화나면 제일 무섭다」 공격력 +{0}% · 5단계 밥은 먹고 싸워야지", 25, 30, 35, 40, 45),

                // ───── 일회용 15종(칸 차지 없음) ─────
                OneTime(AugmentId.RiceBall, "주먹밥", "체력 {0}% 회복", 30),
                OneTime(AugmentId.HerbalTea, "약탕", "체력 {0}% 회복 · 대가: 5초 동안 이동 속도 -30%", 60),
                OneTime(AugmentId.Prayer, "기도", "둘 다 체력 {0}% 회복", 20),
                OneTime(AugmentId.HoneyCake, "꿀떡", "10초 동안 초마다 체력 {0}% 회복 (총 30%)", 3),
                OneTime(AugmentId.Cheongsim, "청심환", "체력 {0}% 회복 + 3초 동안 무적", 15),
                OneTime(AugmentId.CoinPouch, "엽전 주머니", "엽전 +{0}", 30),
                OneTime(AugmentId.CoinBundle, "엽전 꾸러미", "엽전 +{0} · 대가: 원한 +10", 60),
                OneTime(AugmentId.SoulHandful, "영혼 한 줌", "다음 레벨까지 필요한 영혼의 {0}% 채움 (공유)", 50),
                OneTime(AugmentId.SoulJar, "영혼 단지", "바로 레벨업 · 대가: 내 현재 체력 -{0}%", 20),
                OneTime(AugmentId.Talisman, "부적 한 장", "호신부 {0}회 즉시 충전 (최대치를 넘으면 이번 웨이브만)", 1),
                OneTime(AugmentId.TalismanBundle, "부적 다발", "호신부 최대치까지 충전 · 대가: 10초 동안 공격력 -{0}%", 20),
                OneTime(AugmentId.Makgeolli, "막걸리", "20초 동안 공격력 +{0}%", 30),
                OneTime(AugmentId.StrawShoes, "새 짚신", "이번 웨이브 동안 이동 속도 +{0}%", 20),
                OneTime(AugmentId.HolyWater, "정화수", "쌓인 원한 -{0}", 20),
                OneTime(AugmentId.Yut, "윷 한 판", "리롤 +{0} (바로 쓸 수 있음)", 2),
                Disabled(OneTime(AugmentId.SoulHarvest, "넋 거두기(옛)", "맵의 영혼을 전부 끌어옴", 0), "기획서 v0.9.6: 일회용 15종으로 교체"),
                Disabled(OneTime(AugmentId.Purify, "부정 씻기", "주변 {0}칸 원귀 정화", 6), "기획서 v0.9.6: 일회용 15종으로 교체"),

                Disabled(Infinite(AugmentId.InfiniteAttack, "무한 · 공격력", "고를 때마다 공격력 +{0}%", 3), "기획서 v0.9.6: 무한 카드 삭제"),
                Disabled(Infinite(AugmentId.InfiniteAttackSpeed, "무한 · 공격 속도", "고를 때마다 공격 속도 +{0}%", 2), "기획서 v0.9.6: 무한 카드 삭제"),
                Disabled(Infinite(AugmentId.InfiniteCrit, "무한 · 치명타 확률", "고를 때마다 치명타 확률 +{0}%p", 1), "기획서 v0.9.6: 무한 카드 삭제"),
                Disabled(Infinite(AugmentId.InfiniteMaxHealth, "무한 · 최대 체력", "고를 때마다 최대 체력 +{0}%", 4), "기획서 v0.9.6: 무한 카드 삭제"),
                Disabled(Infinite(AugmentId.InfiniteMoveSpeed, "무한 · 이동 속도", "고를 때마다 이동 속도 +{0}%", 1), "기획서 v0.9.6: 무한 카드 삭제"),
            };
            return list;
        }

        private static AugmentEntry Common(AugmentId id, string name, AugmentTarget target, string format, params float[] values)
        {
            return new AugmentEntry { id = id, displayName = name, kind = AugmentKind.Common, target = target, descriptionFormat = format, values = values };
        }

        private static AugmentEntry Exclusive(AugmentId id, string name, string characterId, string format, params float[] values)
        {
            return new AugmentEntry
            {
                id = id, displayName = name, kind = AugmentKind.Exclusive, target = AugmentTarget.All,
                characterId = characterId, descriptionFormat = format, values = values,
            };
        }

        private static AugmentEntry Coop(AugmentId id, string name, string format, params float[] values)
        {
            return new AugmentEntry { id = id, displayName = name, kind = AugmentKind.Coop, target = AugmentTarget.All, descriptionFormat = format, values = values };
        }

        private static AugmentEntry OneTime(AugmentId id, string name, string format, float value)
        {
            return new AugmentEntry { id = id, displayName = name, kind = AugmentKind.OneTime, descriptionFormat = format, values = new[] { value } };
        }

        private static AugmentEntry Infinite(AugmentId id, string name, string format, float value)
        {
            return new AugmentEntry { id = id, displayName = name, kind = AugmentKind.Infinite, descriptionFormat = format, values = new[] { value } };
        }

        private static AugmentEntry Disabled(AugmentEntry entry, string reason)
        {
            entry.enabled = false;
            entry.disabledReason = reason;
            return entry;
        }
    }
}
