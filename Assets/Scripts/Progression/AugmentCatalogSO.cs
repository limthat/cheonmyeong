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
        Infinite,
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

        HayoungWideSlash = 40,
        HayoungFollowUpSlash = 41,

        Prayer = 80,
        SoulHarvest = 81,
        CoinPouch = 82,
        Purify = 83,

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

        public string Describe(int level)
        {
            float value = kind == AugmentKind.OneTime || kind == AugmentKind.Infinite ? ValueAt(1) : ValueAt(level);
            string format = string.IsNullOrEmpty(descriptionFormat) ? "{0}" : descriptionFormat;
            return string.Format(format, value.ToString("0.##"));
        }
    }

    /// <summary>
    /// 261104 추가 - 신통력(증강) 목록과 규칙 수치. 기획서 v0.9.4 07장 그대로.
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
        [Tooltip("칸이 다 차고 전부 5단계일 때 무한 카드가 나올 확률(나머지는 일회용) - 기획서 70%.")]
        [Range(0f, 1f)] public float infiniteShareWhenMaxed = 0.7f;
        [Tooltip("무한 · 이동 속도 합계 상한 - 기획서 +20%.")]
        [Range(0f, 1f)] public float infiniteMoveSpeedCap = 0.2f;

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
                Common(AugmentId.MaxHealth, "최대 체력", AugmentTarget.All, "최대 체력 +{0}%", 10, 20, 30, 40, 50),
                Common(AugmentId.MoveSpeed, "이동 속도", AugmentTarget.All, "이동 속도 +{0}%", 5, 10, 15, 20, 25),
                Common(AugmentId.Regen, "체력 재생", AugmentTarget.All, "초당 체력 {0} 회복", 0.2f, 0.4f, 0.6f, 0.8f, 1f),
                Common(AugmentId.Armor, "방어", AugmentTarget.All, "받는 피해 -{0} (최소 1은 받음)", 1, 1, 2, 2, 3),
                Common(AugmentId.AttackPower, "공격력", AugmentTarget.All, "기본 공격 피해 +{0}%", 10, 20, 30, 40, 50),
                Disabled(Common(AugmentId.ProjectileSpeed, "투사체 속도", AugmentTarget.Ranged, "투사체 속도 +{0}%", 10, 20, 30, 40, 50),
                         "원거리 캐릭터 · 투사체가 아직 없음"),
                Common(AugmentId.AttackSpeed, "공격 속도", AugmentTarget.All, "초당 공격 횟수 +{0}%", 8, 16, 24, 32, 40),
                Common(AugmentId.MeleeRange, "사거리", AugmentTarget.Melee, "근접 사거리 +{0}칸", 0.2f, 0.4f, 0.6f, 0.8f, 1f),
                Disabled(Common(AugmentId.CooldownReduction, "쿨다운 감소", AugmentTarget.All, "재사용 대기시간 -{0}%", 5, 10, 15, 20, 25),
                         "기획서: 보류(액티브 스킬 없음)"),
                Disabled(Common(AugmentId.ProjectileCount, "투사체 수", AugmentTarget.Ranged, "투사체 +{0}개", 1, 1, 2, 2, 3),
                         "원거리 캐릭터 · 투사체가 아직 없음"),
                Disabled(Common(AugmentId.PickupRange, "획득 범위", AugmentTarget.All, "끌어오는 범위 +{0}%", 20, 40, 60, 80, 100),
                         "모호: 기본 획득 범위가 0이라 % 증가의 기준이 없음"),
                Disabled(Common(AugmentId.Luck, "행운", AugmentTarget.All, "좋은 신통력 확률 +{0}%", 10, 20, 30, 40, 50),
                         "모호: '좋은 신통력'을 가를 희귀도 체계가 기획서에 없음"),
                Disabled(Common(AugmentId.Growth, "성장", AugmentTarget.All, "얻는 경험치 +{0}%", 8, 16, 24, 32, 40),
                         "모호: 영혼은 둘이 공유 - 누구에게 어떻게 적용되는지 없음"),
                Disabled(Common(AugmentId.Curse, "저주", AugmentTarget.All, "원귀 수 · 체력 +{0}%, 보상도 증가", 10, 20, 30, 40, 50),
                         "모호: 늘어나는 보상의 종류 · 양, 협동에서 누구 기준인지 없음"),
                Disabled(Common(AugmentId.Reroll, "리롤", AugmentTarget.All, "선택지 다시 뽑기 {0}회", 1, 2, 3, 4, 5),
                         "모호: 리롤권 지급인지, 레벨업마다 쓰는 무료 횟수인지 없음"),
                Common(AugmentId.CritChance, "치명타 확률", AugmentTarget.All, "치명타 확률 +{0}%p", 3, 6, 9, 12, 15),
                Disabled(Common(AugmentId.CritDamage, "치명타 피해", AugmentTarget.All, "치명타 피해 배율 +{0}%", 10, 20, 30, 40, 50),
                         "모호: 배율에 더하기(×1.5→×1.6)인지 곱하기(×1.65)인지 없음"),
                Common(AugmentId.MultiSwing, "연속 공격", AugmentTarget.Melee, "한 번에 {0}번 더 휘두름 (Lv2 · Lv4 공격력 +5%)", 1, 1, 2, 2, 3),

                Exclusive(AugmentId.HayoungWideSlash, "넓은 베기", "hayoung", "사인검 베기 각도 +{0}%", 10, 20, 30, 40, 50),
                Exclusive(AugmentId.HayoungFollowUpSlash, "연속 베기", "hayoung", "같은 방향으로 한 번 더 벤다 (피해 {0}%)", 50, 65, 80, 95, 110),

                OneTime(AugmentId.Prayer, "기도", "둘 다 최대 체력의 {0}% 회복", 30),
                OneTime(AugmentId.SoulHarvest, "넋 거두기", "맵의 영혼을 전부 끌어옴", 0),
                OneTime(AugmentId.CoinPouch, "엽전 주머니", "고른 사람 엽전 +{0}", 30),
                OneTime(AugmentId.Purify, "부정 씻기", "주변 {0}칸 원귀 정화 (처치로 계산, 보스 제외)", 6),

                Infinite(AugmentId.InfiniteAttack, "무한 · 공격력", "고를 때마다 공격력 +{0}%", 3),
                Infinite(AugmentId.InfiniteAttackSpeed, "무한 · 공격 속도", "고를 때마다 공격 속도 +{0}%", 2),
                Infinite(AugmentId.InfiniteCrit, "무한 · 치명타 확률", "고를 때마다 치명타 확률 +{0}%p", 1),
                Infinite(AugmentId.InfiniteMaxHealth, "무한 · 최대 체력", "고를 때마다 최대 체력 +{0}%", 4),
                Infinite(AugmentId.InfiniteMoveSpeed, "무한 · 이동 속도", "고를 때마다 이동 속도 +{0}% (합계 +20%까지)", 1),
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
