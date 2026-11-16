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
    /// </summary>
    public static class CharacterProgressStore
    {
        private const string UnlockKeyPrefix = "CheonMyeong.Hero.Unlocked.";
        private const string SinnaerimKeyPrefix = "CheonMyeong.Sinnaerim.";

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
        }
    }
}
