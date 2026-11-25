using System;
using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 261114 추가 - 아웃게임 영구 성장 저장소(각자 자기 PC, PlayerPrefs). 기획서 v0.9.7 7장:
    /// - 영웅 해금: 엽전으로 산다(하영은 기본 보유). 가격은 CharacterEntry.unlockPrice.
    /// - 신내림: 캐릭터마다 11칸, 아래 기본부터 위 각성까지 엽전으로 연다. 갈래(선봉 · 수호 · 유격)는 하나만, 택1(A · B)도 하나만.
    /// 엽전 자체는 PlayerWallet(Progression)이 갖고 있어서, 여기선 "무엇을 열었나"만 저장한다(돈 처리는 호출부가 먼저 한다).
    /// 저장 형식: 해금 = 키 하나에 0/1, 신내림 = 캐릭터마다 비트마스크 int 하나(11비트).
    /// 261116 - 영력(캐릭터 레벨, 기획서 v0.9.9 7장): 캐릭터마다 1~30, 저장 = 키 하나에 int(없으면 1). 규칙은 SpiritLevel.
    /// </summary>
    public static class CharacterProgressStore
    {
        private const string UnlockKeyPrefix = "CheonMyeong.Hero.Unlocked.";
        private const string SinnaerimKeyPrefix = "CheonMyeong.Sinnaerim.";
        private const string LevelKeyPrefix = "CheonMyeong.SpiritLevel.";

        /// <summary>해금 · 신내림이 바뀌었을 때(characterId). 구독한 쪽은 반드시 해제할 것.</summary>
        public static event Action<string> Changed;

        public static bool IsUnlocked(CharacterCatalogSO.CharacterEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            return entry.isUnlocked || entry.unlockPrice <= 0 || PlayerPrefs.GetInt(UnlockKeyPrefix + entry.characterId, 0) == 1;
        }

        /// <summary>엽전 차감이 끝난 뒤 부른다.</summary>
        public static void MarkUnlocked(CharacterCatalogSO.CharacterEntry entry)
        {
            if (entry == null || IsUnlocked(entry))
            {
                return;
            }
            PlayerPrefs.SetInt(UnlockKeyPrefix + entry.characterId, 1);
            PlayerPrefs.Save();
            Changed?.Invoke(entry.characterId);
        }

        public static int SinnaerimMask(string characterId)
        {
            return string.IsNullOrEmpty(characterId) ? 0 : PlayerPrefs.GetInt(SinnaerimKeyPrefix + characterId, 0);
        }

        public static bool IsOpen(string characterId, CharacterCatalogSO.SinnaerimSlot slot)
        {
            return (SinnaerimMask(characterId) & (1 << (int)slot)) != 0;
        }

        public static bool IsOpen(int mask, CharacterCatalogSO.SinnaerimSlot slot) => (mask & (1 << (int)slot)) != 0;

        public enum NodeState
        {
            Open,
            Available,
            NeedsPrevious,
            BranchLocked
        }

        /// <summary>칸 상태. 규칙: 기본 → 갈래 1칸(셋 중 하나) → 같은 갈래 2칸 → 관문 → 택1(둘 중 하나) → 각성.</summary>
        public static NodeState StateOf(int mask, CharacterCatalogSO.SinnaerimSlot slot)
        {
            if (IsOpen(mask, slot))
            {
                return NodeState.Open;
            }
            bool baseOpen = IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Base);
            switch (slot)
            {
                case CharacterCatalogSO.SinnaerimSlot.Base:
                    return NodeState.Available;
                case CharacterCatalogSO.SinnaerimSlot.Vanguard1:
                case CharacterCatalogSO.SinnaerimSlot.Guard1:
                case CharacterCatalogSO.SinnaerimSlot.Raid1:
                    if (BranchOf(mask) >= 0)
                    {
                        return NodeState.BranchLocked;
                    }
                    return baseOpen ? NodeState.Available : NodeState.NeedsPrevious;
                case CharacterCatalogSO.SinnaerimSlot.Vanguard2:
                case CharacterCatalogSO.SinnaerimSlot.Guard2:
                case CharacterCatalogSO.SinnaerimSlot.Raid2:
                {
                    var first = (CharacterCatalogSO.SinnaerimSlot)((int)slot - 1);
                    if (BranchOf(mask) >= 0 && !IsOpen(mask, first))
                    {
                        return NodeState.BranchLocked;
                    }
                    return IsOpen(mask, first) ? NodeState.Available : NodeState.NeedsPrevious;
                }
                case CharacterCatalogSO.SinnaerimSlot.Gate:
                    return BranchComplete(mask) ? NodeState.Available : NodeState.NeedsPrevious;
                case CharacterCatalogSO.SinnaerimSlot.ChoiceA:
                case CharacterCatalogSO.SinnaerimSlot.ChoiceB:
                {
                    var other = slot == CharacterCatalogSO.SinnaerimSlot.ChoiceA
                        ? CharacterCatalogSO.SinnaerimSlot.ChoiceB
                        : CharacterCatalogSO.SinnaerimSlot.ChoiceA;
                    if (IsOpen(mask, other))
                    {
                        return NodeState.BranchLocked;
                    }
                    return IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Gate) ? NodeState.Available : NodeState.NeedsPrevious;
                }
                case CharacterCatalogSO.SinnaerimSlot.Awaken:
                    return IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.ChoiceA) || IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.ChoiceB)
                        ? NodeState.Available
                        : NodeState.NeedsPrevious;
                default:
                    return NodeState.NeedsPrevious;
            }
        }

        /// <summary>고른 갈래: 0 = 선봉, 1 = 수호, 2 = 유격, -1 = 아직 없음.</summary>
        public static int BranchOf(int mask)
        {
            if (IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Vanguard1))
            {
                return 0;
            }
            if (IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Guard1))
            {
                return 1;
            }
            if (IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Raid1))
            {
                return 2;
            }
            return -1;
        }

        private static bool BranchComplete(int mask)
        {
            return IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Vanguard2)
                   || IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Guard2)
                   || IsOpen(mask, CharacterCatalogSO.SinnaerimSlot.Raid2);
        }

        /// <summary>엽전 차감이 끝난 뒤 부른다. 열 수 없는 칸이면 false(아무것도 안 바꿈).</summary>
        public static bool TryMarkOpen(string characterId, CharacterCatalogSO.SinnaerimSlot slot)
        {
            int mask = SinnaerimMask(characterId);
            if (StateOf(mask, slot) != NodeState.Available)
            {
                return false;
            }
            mask |= 1 << (int)slot;
            PlayerPrefs.SetInt(SinnaerimKeyPrefix + characterId, mask);
            PlayerPrefs.Save();
            Changed?.Invoke(characterId);
            return true;
        }

        /// <summary>261116 - 영력 레벨(1~30). 해금 전이어도 1.</summary>
        public static int Level(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return SpiritLevel.MinLevel;
            }
            return Mathf.Clamp(PlayerPrefs.GetInt(LevelKeyPrefix + characterId, SpiritLevel.MinLevel), SpiritLevel.MinLevel, SpiritLevel.MaxLevel);
        }

        public enum LevelUpResult
        {
            Done,
            Locked,
            MaxLevel,
            NotEnoughYeopjeon
        }

        /// <summary>
        /// 261116 - 영력 수련: 해금된 캐릭터만, 30 미만일 때, 엽전(지금 레벨 × 10)을 내고 1 올린다.
        /// spend = 엽전 차감(PlayerWallet.TrySpendYeopjeon). Data가 Progression을 모르게 호출부가 넘긴다.
        /// </summary>
        public static LevelUpResult TryLevelUp(CharacterCatalogSO.CharacterEntry entry, Func<int, bool> spend)
        {
            if (entry == null || !IsUnlocked(entry))
            {
                return LevelUpResult.Locked;
            }
            int level = Level(entry.characterId);
            if (level >= SpiritLevel.MaxLevel)
            {
                return LevelUpResult.MaxLevel;
            }
            if (spend == null || !spend(SpiritLevel.Cost(level)))
            {
                return LevelUpResult.NotEnoughYeopjeon;
            }
            PlayerPrefs.SetInt(LevelKeyPrefix + entry.characterId, level + 1);
            PlayerPrefs.Save();
            Changed?.Invoke(entry.characterId);
            return LevelUpResult.Done;
        }

        /// <summary>261114 - 신내림 화면에서 지금 보고 있는 캐릭터(화면이 닫히면 null). 개발자 치트가 초기화 대상을 고를 때 쓴다.</summary>
        public static string FocusedCharacterId { get; set; }

        /// <summary>261114 개발자 모드 치트 전용 - 그 캐릭터의 신내림 칸을 전부 닫는다(엽전은 돌려주지 않음).</summary>
        public static void DebugResetSinnaerim(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return;
            }
            PlayerPrefs.DeleteKey(SinnaerimKeyPrefix + characterId);
            PlayerPrefs.Save();
            Changed?.Invoke(characterId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            FocusedCharacterId = null;
        }
    }

    /// <summary>
    /// 261116 - 영력(靈力, 캐릭터 레벨) 규칙. 1~30레벨, 비용 = 지금 레벨 × 10 엽전(합계 4,350).
    /// 261125 - 기획서 v1.3 「밸런스 조정안 · 확정(26.11.23)」 반영:
    /// - 체력(공통): 레벨당 +3%(2~10) · +3%(11~20) · +2.5%(21~30) → 30레벨 ×1.82.
    /// - 공격 속도: 다수형 고정 · 원거리 단일 +1.5%/레벨 · 근접 단일 +0.6%/레벨.
    /// - DPS 배수(10 · 20 · 30레벨): 단일형 ×1.35 · 1.7 · 2.1 / 다수형 ×1.3 · 1.6 · 2.0. 공격력 배율 = DPS 배수 ÷ 공격 속도 배율(사이 레벨은 직선).
    /// - 달성 보너스: 10 호신부 +1 · 20 치명 +5% · 30 호신부 +1.
    /// - 인게임 신통력의 % 보너스는 이 배율이 적용된 기본 스탯에 곱한다.
    /// </summary>
    public static class SpiritLevel
    {
        public enum GrowthType { MeleeMulti, RangedMulti, MeleeSingle, RangedSingle }

        public const int MinLevel = 1;
        public const int MaxLevel = 30;
        public const int CostPerLevel = 10;

        private static readonly float[] HealthPerLevelByBand = { 0.03f, 0.03f, 0.025f };
        private static readonly float[] MultiDps = { 1.3f, 1.6f, 2.0f };
        private static readonly float[] SingleDps = { 1.35f, 1.7f, 2.1f };
        private const float MeleeSingleSpeedPerLevel = 0.006f;
        private const float RangedSingleSpeedPerLevel = 0.015f;

        public static int Cost(int level) => Mathf.Clamp(level, MinLevel, MaxLevel) * CostPerLevel;

        public static GrowthType TypeOf(string characterId)
        {
            switch (characterId)
            {
                case "parkgyuhyeok":
                case "imhun":
                    return GrowthType.MeleeSingle;
                case "leeara":
                case "choihwanjo":
                    return GrowthType.RangedSingle;
                case "hanjuwon":
                case "simyeore":
                    return GrowthType.RangedMulti;
                default:
                    return GrowthType.MeleeMulti;
            }
        }

        public static float HealthScale(int level)
        {
            int l = Mathf.Clamp(level, MinLevel, MaxLevel);
            float scale = 1f;
            scale += HealthPerLevelByBand[0] * (Mathf.Min(l, 10) - 1);
            scale += HealthPerLevelByBand[1] * Mathf.Clamp(l - 10, 0, 10);
            scale += HealthPerLevelByBand[2] * Mathf.Clamp(l - 20, 0, 10);
            return scale;
        }

        public static float AttackSpeedScale(int level, string characterId)
        {
            int steps = Mathf.Clamp(level, MinLevel, MaxLevel) - MinLevel;
            switch (TypeOf(characterId))
            {
                case GrowthType.MeleeSingle: return 1f + MeleeSingleSpeedPerLevel * steps;
                case GrowthType.RangedSingle: return 1f + RangedSingleSpeedPerLevel * steps;
                default: return 1f;
            }
        }

        public static float DpsScale(int level, string characterId)
        {
            GrowthType type = TypeOf(characterId);
            float[] anchors = type == GrowthType.MeleeSingle || type == GrowthType.RangedSingle ? SingleDps : MultiDps;
            int l = Mathf.Clamp(level, MinLevel, MaxLevel);
            if (l <= 10)
            {
                return Mathf.Lerp(1f, anchors[0], (l - 1) / 9f);
            }
            if (l <= 20)
            {
                return Mathf.Lerp(anchors[0], anchors[1], (l - 10) / 10f);
            }
            return Mathf.Lerp(anchors[1], anchors[2], (l - 20) / 10f);
        }

        public static float AttackScale(int level, string characterId) => DpsScale(level, characterId) / AttackSpeedScale(level, characterId);

        public static int ShieldBonus(int level) => (level >= 10 ? 1 : 0) + (level >= 30 ? 1 : 0);

        public static float CritBonus(int level) => level >= 20 ? 0.05f : 0f;
    }
}
