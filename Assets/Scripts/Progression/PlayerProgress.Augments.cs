using System;
using System.Collections.Generic;
using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Player;
using CheonMyeong.UI;
using CheonMyeong.UI.Screens;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261104 추가 - 플레이어 한 명의 신통력(기획서 v0.9.4 07장). 플레이어 프리팹에 이미 붙은 PlayerProgress에 넣어서
    /// 프리팹을 고치지 않아도 된다.
    /// - 칸 6개 × 5단계(augmentSlots, 고른 순서대로 왼쪽부터), 무한 스탯 횟수(augmentStacks), 이번 레벨업 카드(augmentOffers).
    /// - 판정은 서버, 목록은 모두에게 복제되고 각 화면이 같은 값으로 Modifiers를 계산한다(공격 범위 이펙트 등이 판정과 맞게).
    /// - 레벨업 진행(일시정지 · 10초 · 다음 라운드)은 PartyProgress가 맡는다.
    /// </summary>
    public partial class PlayerProgress
    {
        private NetworkList<AugmentSlot> augmentSlots;
        private NetworkList<AugmentStack> augmentStacks;
        private NetworkList<AugmentOffer> augmentOffers;

        private readonly NetworkVariable<bool> hasPicked = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private AugmentModifiers modifiers;
        private int augmentVersion;
        private int offerVersion;
        private PlayerHealth playerHealth;
        private AugmentHudView hudView;

        private static readonly List<AugmentOffer> CandidateBuffer = new List<AugmentOffer>();
        private static readonly List<AugmentOffer> OneTimeBuffer = new List<AugmentOffer>();
        private static readonly List<AugmentOffer> InfiniteBuffer = new List<AugmentOffer>();

        /// <summary>칸 · 무한 스탯이 바뀔 때(모든 화면).</summary>
        public event Action AugmentsChanged;

        public AugmentModifiers Modifiers => modifiers;
        public int AugmentVersion => augmentVersion;
        public int OfferVersion => offerVersion;
        public bool HasPicked => hasPicked.Value;
        public int SlotCount => augmentSlots != null ? augmentSlots.Count : 0;
        public int StackCount => augmentStacks != null ? augmentStacks.Count : 0;
        public int OfferCount => augmentOffers != null ? augmentOffers.Count : 0;
        public AugmentSlot GetSlot(int index) => augmentSlots[index];
        public AugmentStack GetStack(int index) => augmentStacks[index];
        public AugmentOffer GetOffer(int index) => augmentOffers[index];

        public static AugmentCatalogSO Catalog =>
            PartyProgress.Instance != null ? PartyProgress.Instance.AugmentCatalog : AugmentCatalogSO.Default;

        /// <summary>이 PC의 내 플레이어 PlayerProgress(없으면 null).</summary>
        public static PlayerProgress Local
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                NetworkObject playerObject = manager != null && manager.IsListening && manager.LocalClient != null
                    ? manager.LocalClient.PlayerObject
                    : null;
                return playerObject != null && playerObject.TryGetComponent(out PlayerProgress progress) ? progress : null;
            }
        }

        private void Awake()
        {
            augmentSlots = new NetworkList<AugmentSlot>();
            augmentStacks = new NetworkList<AugmentStack>();
            augmentOffers = new NetworkList<AugmentOffer>();
            TryGetComponent(out playerHealth);
        }

        public override void OnDestroy()
        {
            if (hudView != null)
            {
                Destroy(hudView.gameObject);
            }
            base.OnDestroy();
        }

        private void AugmentsOnNetworkSpawn()
        {
            augmentSlots.OnListChanged += HandleAugmentListChanged;
            augmentStacks.OnListChanged += HandleAugmentStackChanged;
            augmentOffers.OnListChanged += HandleOfferListChanged;
            RecomputeModifiers();
        }

        private void AugmentsOnNetworkDespawn()
        {
            augmentSlots.OnListChanged -= HandleAugmentListChanged;
            augmentStacks.OnListChanged -= HandleAugmentStackChanged;
            augmentOffers.OnListChanged -= HandleOfferListChanged;
            if (hudView != null)
            {
                Destroy(hudView.gameObject);
                hudView = null;
            }
        }

        private void Update()
        {
            // 내 화면에만 신통력 칸 HUD(기획서: 왼쪽 아래 6칸). HUD가 켜진 뒤에 만든다.
            if (IsSpawned && IsOwner && hudView == null && InGameHudScreen.Instance != null)
            {
                hudView = AugmentHudView.Create(InGameHudScreen.Instance.transform, this);
            }
        }

        private void HandleAugmentListChanged(NetworkListEvent<AugmentSlot> change)
        {
            RecomputeModifiers();
        }

        private void HandleAugmentStackChanged(NetworkListEvent<AugmentStack> change)
        {
            RecomputeModifiers();
        }

        private void HandleOfferListChanged(NetworkListEvent<AugmentOffer> change)
        {
            offerVersion++;
        }

        private void RecomputeModifiers()
        {
            AugmentCatalogSO catalog = Catalog;
            var m = new AugmentModifiers();
            if (augmentSlots != null)
            {
                for (int i = 0; i < augmentSlots.Count; i++)
                {
                    AugmentSlot slot = augmentSlots[i];
                    AugmentEntry entry = catalog.Find(slot.Augment);
                    if (entry == null)
                    {
                        continue;
                    }
                    float v = entry.ValueAt(slot.Level);
                    switch (slot.Augment)
                    {
                        case AugmentId.MaxHealth: m.MaxHealthBonus += v / 100f; break;
                        case AugmentId.MoveSpeed: m.MoveSpeedBonus += v / 100f; break;
                        case AugmentId.Regen: m.RegenPerSecond += v; break;
                        case AugmentId.Armor: m.Armor += v; break;
                        case AugmentId.AttackPower: m.AttackBonus += v / 100f; break;
                        case AugmentId.AttackSpeed: m.AttackSpeedBonus += v / 100f; break;
                        case AugmentId.MeleeRange: m.MeleeRangeBonus += v; break;
                        case AugmentId.CritChance: m.CritChanceBonus += v / 100f; break;
                        case AugmentId.MultiSwing:
                            m.ExtraSwings += Mathf.RoundToInt(v);
                            if (slot.Level >= 2) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            if (slot.Level >= 4) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            break;
                        case AugmentId.HayoungWideSlash: m.ArcBonus += v / 100f; break;
                        case AugmentId.HayoungFollowUpSlash: m.FollowUpSlashRatio = v / 100f; break;
                    }
                }
            }
            if (augmentStacks != null)
            {
                for (int i = 0; i < augmentStacks.Count; i++)
                {
                    AugmentStack stack = augmentStacks[i];
                    AugmentEntry entry = catalog.Find(stack.Augment);
                    if (entry == null)
                    {
                        continue;
                    }
                    float v = entry.ValueAt(1) * stack.Count / 100f;
                    switch (stack.Augment)
                    {
                        case AugmentId.InfiniteAttack: m.AttackBonus += v; break;
                        case AugmentId.InfiniteAttackSpeed: m.AttackSpeedBonus += v; break;
                        case AugmentId.InfiniteCrit: m.CritChanceBonus += v; break;
                        case AugmentId.InfiniteMaxHealth: m.MaxHealthBonus += v; break;
                        case AugmentId.InfiniteMoveSpeed: m.MoveSpeedBonus += Mathf.Min(v, catalog.infiniteMoveSpeedCap); break;
                    }
                }
            }
            modifiers = m;
            augmentVersion++;
            AugmentsChanged?.Invoke();
        }

        /// <summary>이 칸의 신통력 단계(없으면 0).</summary>
        public int LevelOf(AugmentId id)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (augmentSlots[i].Augment == id)
                {
                    return augmentSlots[i].Level;
                }
            }
            return 0;
        }

        // =====================================================================
        // 서버
        // =====================================================================

        /// <summary>서버 전용 - 새 판 시작 때 신통력 전부 초기화.</summary>
        private void ServerResetAugments()
        {
            float previousMax = playerHealth != null ? playerHealth.MaxHealth : 0f;
            augmentSlots.Clear();
            augmentStacks.Clear();
            augmentOffers.Clear();
            hasPicked.Value = false;
            RecomputeModifiers();
            if (playerHealth != null)
            {
                playerHealth.ServerOnMaxHealthChanged(previousMax);
            }
        }

        /// <summary>서버 전용 - 레벨업 라운드 시작: 카드를 새로 뽑고 아직 안 고른 상태로.</summary>
        public void ServerBeginChoice()
        {
            if (!IsServer)
            {
                return;
            }
            hasPicked.Value = false;
            ServerRollOffers();
        }

        /// <summary>서버 전용 - 리롤권 1장을 써서 카드를 다시 뽑는다. 이미 골랐거나 리롤권이 없으면 false.</summary>
        public bool ServerTryReroll()
        {
            if (!IsServer || hasPicked.Value || rerollTickets.Value <= 0)
            {
                return false;
            }
            rerollTickets.Value--;
            ServerRollOffers();
            return true;
        }

        /// <summary>서버 전용 - index번 카드를 고른다. 이미 골랐거나 범위 밖이면 false.</summary>
        public bool ServerPick(int index)
        {
            if (!IsServer || hasPicked.Value || index < 0 || index >= augmentOffers.Count)
            {
                return false;
            }
            AugmentOffer offer = augmentOffers[index];
            hasPicked.Value = true;
            ServerApply(offer.Augment);
            return true;
        }

        /// <summary>서버 전용 - 시간이 다 됐을 때 무작위로 고른다(기획서: 10초 지나면 무작위).</summary>
        public void ServerPickRandom()
        {
            if (!IsServer || hasPicked.Value)
            {
                return;
            }
            if (augmentOffers.Count == 0)
            {
                hasPicked.Value = true;
                return;
            }
            ServerPick(UnityEngine.Random.Range(0, augmentOffers.Count));
        }

        private void ServerApply(AugmentId id)
        {
            AugmentCatalogSO catalog = Catalog;
            AugmentEntry entry = catalog.Find(id);
            if (entry == null)
            {
                return;
            }

            float previousMax = playerHealth != null ? playerHealth.MaxHealth : 0f;
            switch (entry.kind)
            {
                case AugmentKind.Common:
                case AugmentKind.Exclusive:
                {
                    // 기획서: 이미 가진 신통력이면 그 칸이 한 단계 오르고, 새 신통력은 빈 칸에 1단계로(왼쪽부터).
                    int index = -1;
                    for (int i = 0; i < augmentSlots.Count; i++)
                    {
                        if (augmentSlots[i].Augment == id)
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index >= 0)
                    {
                        AugmentSlot slot = augmentSlots[index];
                        slot.Level = (byte)Mathf.Min(catalog.maxLevel, slot.Level + 1);
                        augmentSlots[index] = slot;
                    }
                    else if (augmentSlots.Count < catalog.maxSlots)
                    {
                        augmentSlots.Add(new AugmentSlot { Id = (byte)id, Level = 1 });
                    }
                    break;
                }
                case AugmentKind.Infinite:
                {
                    int index = -1;
                    for (int i = 0; i < augmentStacks.Count; i++)
                    {
                        if (augmentStacks[i].Augment == id)
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index >= 0)
                    {
                        AugmentStack stack = augmentStacks[index];
                        stack.Count++;
                        augmentStacks[index] = stack;
                    }
                    else
                    {
                        augmentStacks.Add(new AugmentStack { Id = (byte)id, Count = 1 });
                    }
                    break;
                }
                case AugmentKind.OneTime:
                    ServerExecuteOneTime(entry);
                    break;
            }

            RecomputeModifiers();
            if (playerHealth != null)
            {
                playerHealth.ServerOnMaxHealthChanged(previousMax);
            }
        }

        /// <summary>일회용 - 칸을 차지하지 않고 고르는 순간 효과(기획서).</summary>
        private void ServerExecuteOneTime(AugmentEntry entry)
        {
            float value = entry.ValueAt(1);
            switch (entry.id)
            {
                case AugmentId.Prayer:
                    for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
                    {
                        PlayerHealth player = PlayerHealth.ActivePlayers[i];
                        if (player != null)
                        {
                            player.ServerHealByRatio(value / 100f);
                        }
                    }
                    break;
                case AugmentId.SoulHarvest:
                    if (playerHealth != null)
                    {
                        LootPickup.ServerPullAllExpTo(playerHealth);
                    }
                    break;
                case AugmentId.CoinPouch:
                    AddGold(Mathf.RoundToInt(value));
                    break;
                case AugmentId.Purify:
                {
                    int kills = Enemy.ServerPurifyAround(transform.position, value, gameObject);
                    for (int i = 0; i < kills; i++)
                    {
                        ServerAddKill();
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 기획서 카드 규칙:
        /// - 6칸이 차기 전: 공통 · 전용 · 일회용이 섞여 나온다(가진 신통력은 다음 단계로).
        /// - 6칸이 차면: 가진 신통력의 다음 단계와 일회용만.
        /// - 6칸 전부 5단계: 무한 스탯 70% · 일회용 30%.
        /// 종류 사이 비율은 기획서에 없어서 후보마다 같은 확률로 뽑는다. 한 번에 같은 카드는 두 장 안 나온다.
        /// </summary>
        private void ServerRollOffers()
        {
            AugmentCatalogSO catalog = Catalog;
            CharacterStatsSO stats = playerHealth != null ? playerHealth.Stats : null;
            bool isRanged = stats != null && stats.isRanged;
            string characterId = stats != null ? stats.characterId : null;

            CandidateBuffer.Clear();
            OneTimeBuffer.Clear();
            InfiniteBuffer.Clear();

            bool slotsFull = augmentSlots.Count >= catalog.maxSlots;
            bool allMaxed = slotsFull;
            for (int i = 0; i < augmentSlots.Count && allMaxed; i++)
            {
                if (augmentSlots[i].Level < catalog.maxLevel)
                {
                    allMaxed = false;
                }
            }

            for (int i = 0; i < catalog.entries.Count; i++)
            {
                AugmentEntry entry = catalog.entries[i];
                if (entry == null || !entry.enabled || entry.id == AugmentId.None)
                {
                    continue;
                }
                switch (entry.kind)
                {
                    case AugmentKind.OneTime:
                        OneTimeBuffer.Add(new AugmentOffer { Id = (byte)entry.id, Level = 1 });
                        break;
                    case AugmentKind.Infinite:
                        InfiniteBuffer.Add(new AugmentOffer { Id = (byte)entry.id, Level = 1 });
                        break;
                    case AugmentKind.Common:
                    case AugmentKind.Exclusive:
                    {
                        if (!IsEligible(entry, isRanged, characterId))
                        {
                            break;
                        }
                        int owned = LevelOf(entry.id);
                        if (owned > 0)
                        {
                            if (owned < catalog.maxLevel)
                            {
                                CandidateBuffer.Add(new AugmentOffer { Id = (byte)entry.id, Level = (byte)(owned + 1) });
                            }
                        }
                        else if (!slotsFull)
                        {
                            CandidateBuffer.Add(new AugmentOffer { Id = (byte)entry.id, Level = 1 });
                        }
                        break;
                    }
                }
            }

            augmentOffers.Clear();
            int cards = Mathf.Max(1, catalog.cardsPerChoice);
            if (allMaxed)
            {
                for (int c = 0; c < cards; c++)
                {
                    bool wantInfinite = UnityEngine.Random.value < catalog.infiniteShareWhenMaxed;
                    if (!TakeRandomInto(wantInfinite ? InfiniteBuffer : OneTimeBuffer))
                    {
                        TakeRandomInto(wantInfinite ? OneTimeBuffer : InfiniteBuffer);
                    }
                }
                return;
            }

            // 칸이 차기 전에도, 찬 뒤에도 일회용은 같이 섞여 나온다.
            CandidateBuffer.AddRange(OneTimeBuffer);
            OneTimeBuffer.Clear();
            for (int c = 0; c < cards; c++)
            {
                TakeRandomInto(CandidateBuffer);
            }
        }

        /// <summary>목록에서 하나를 무작위로 꺼내(목록에서 빠짐) 카드로 넣는다. 비었으면 false.</summary>
        private bool TakeRandomInto(List<AugmentOffer> source)
        {
            if (source.Count == 0)
            {
                return false;
            }
            int index = UnityEngine.Random.Range(0, source.Count);
            augmentOffers.Add(source[index]);
            source.RemoveAt(index);
            return true;
        }

        private static bool IsEligible(AugmentEntry entry, bool isRanged, string characterId)
        {
            if (entry.target == AugmentTarget.Melee && isRanged)
            {
                return false;
            }
            if (entry.target == AugmentTarget.Ranged && !isRanged)
            {
                return false;
            }
            if (entry.kind == AugmentKind.Exclusive)
            {
                return !string.IsNullOrEmpty(characterId)
                       && string.Equals(entry.characterId, characterId, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }
    }
}
