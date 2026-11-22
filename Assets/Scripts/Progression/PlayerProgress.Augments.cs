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
                Destroy(hudView);
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
                Destroy(hudView);
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
                    bool max = slot.Level >= catalog.maxLevel;
                    switch (slot.Augment)
                    {
                        case AugmentId.MaxHealth: m.MaxHealthBonus += v / 100f; break;
                        case AugmentId.MoveSpeed: m.MoveSpeedBonus += v / 100f; break;
                        case AugmentId.Regen: m.RegenPercent += v / 100f; break;
                        case AugmentId.Armor: m.ShieldBonus += Mathf.RoundToInt(v); break;
                        case AugmentId.AttackPower: m.AttackBonus += v / 100f; break;
                        case AugmentId.Lifesteal: m.Lifesteal += v / 100f; break;
                        case AugmentId.AttackSpeed: m.AttackSpeedBonus += v / 100f; break;
                        case AugmentId.MeleeRange: m.MeleeRangeBonus += v; break;
                        case AugmentId.Area: m.AreaBonus += v / 100f; break;
                        case AugmentId.ProjectileCount:
                            m.ExtraProjectiles += Mathf.RoundToInt(v);
                            // 흩날림: Lv2 · Lv4 공격력 +5%(칼춤과 같은 규칙)
                            if (slot.Level >= 2) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            if (slot.Level >= 4) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            break;
                        case AugmentId.PickupRange: m.PickupRangeBonus += v / 100f; break;
                        case AugmentId.Knockback: m.KnockbackBonus += v; break;
                        case AugmentId.Growth: m.GrowthBonus += v / 100f; break;
                        case AugmentId.Curse:
                            m.MaxHealthBonus -= v / 100f;
                            m.CurseMaxHealthLoss += v / 100f;
                            m.CurseDamageTaken += CurseTable.DamageTaken[Mathf.Clamp(slot.Level - 1, 0, CurseTable.DamageTaken.Length - 1)];
                            if (CurseTable.TryGet(slot.Level, slot.Variant, out CurseReward r))
                            {
                                m.AttackBonus += r.Attack;
                                m.AttackSpeedBonus += r.AttackSpeed;
                                m.RegenPercent += r.Regen;
                                m.Lifesteal += r.Lifesteal;
                                m.CritChanceBonus += r.Crit;
                                m.CritDamageBonus += r.CritDamage;
                                m.CritMultiplierFloor = Mathf.Max(m.CritMultiplierFloor, r.CritFloor);
                                m.GrowthBonus += r.Growth;
                                m.GoldGainBonus += r.Gold;
                                m.PurifyBonus += r.Purify;
                                m.MeleeRangeBonus += r.MeleeRange;
                                m.RangedRangeBonus += r.RangedRange;
                                m.ShieldBonus += r.Shield;
                                m.CritAlways |= r.CritAlways;
                                m.ShieldFillToMax |= r.ShieldFill;
                                if (r.ShieldRecharge > 0f) m.ShieldRechargeSeconds = r.ShieldRecharge;
                            }
                            break;
                        case AugmentId.Reroll:
                            if (max) m.CardBonus += 1; // 네 번째 산가지
                            break;
                        case AugmentId.CritChance: m.CritChanceBonus += v / 100f; break;
                        case AugmentId.CritDamage: m.CritDamageBonus += v / 100f; break;
                        case AugmentId.MultiSwing:
                            m.ExtraSwings += Mathf.RoundToInt(v);
                            if (slot.Level >= 2) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            if (slot.Level >= 4) m.AttackBonus += catalog.multiSwingAttackBonusPerStep;
                            if (max) m.LastSwingBonus = 0.5f; // 마지막 춤사위
                            break;
                        case AugmentId.GoldGain: m.GoldGainBonus += v / 100f; break;
                        case AugmentId.Cleanse: m.PurifyBonus += v / 100f; break;
                        case AugmentId.HayoungWideSlash: m.ArcBonus += v / 100f; break;
                        case AugmentId.ProjectileSpeed: m.ProjectileSpeedBonus += v / 100f; break;
                        case AugmentId.HayoungFollowUpSlash: m.FollowUpSlashRatio = v / 100f; break;
                    }
                }
            }
            modifiers = m;
            augmentVersion++;
            AugmentsChanged?.Invoke();
        }

        /// <summary>261118 - 이 칸의 저주(원귀와의 거래) 보상 설명(HUD · 카드용). 없으면 빈 문자열.</summary>
        public string CurseRewardText
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    AugmentSlot slot = augmentSlots[i];
                    if (slot.Augment == AugmentId.Curse && CurseTable.TryGet(slot.Level, slot.Variant, out CurseReward r))
                    {
                        return r.Text;
                    }
                }
                return string.Empty;
            }
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

        /// <summary>
        /// 261109 - 서버 전용. 리롤권 1장으로 index번 카드 한 장만 다시 뽑는다(지금 나와 있는 카드와 겹치지 않게).
        /// 바꿀 후보가 없으면 리롤권을 쓰지 않고 false.
        /// </summary>
        public bool ServerTryRerollCard(int index)
        {
            if (!IsServer || hasPicked.Value || rerollTickets.Value <= 0 || index < 0 || index >= augmentOffers.Count)
            {
                return false;
            }
            bool allMaxed = ServerBuildCandidates();
            for (int i = 0; i < augmentOffers.Count; i++)
            {
                RemoveById(CandidateBuffer, augmentOffers[i].Id);
                RemoveById(OneTimeBuffer, augmentOffers[i].Id);
            }
            if (!TryTakeWeighted(allMaxed, out AugmentOffer replacement))
            {
                return false;
            }
            rerollTickets.Value--;
            augmentOffers[index] = replacement;
            return true;
        }

        private static void RemoveById(List<AugmentOffer> list, byte id)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Id == id)
                {
                    list.RemoveAt(i);
                }
            }
        }

        private static bool TryTakeRandom(List<AugmentOffer> source, out AugmentOffer offer)
        {
            if (source.Count == 0)
            {
                offer = default;
                return false;
            }
            int index = UnityEngine.Random.Range(0, source.Count);
            offer = source[index];
            source.RemoveAt(index);
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

        /// <summary>261114 - 서버 전용. 카드 없이 신통력을 바로 준다(신내림 관문: 전용 신통력 하나를 Lv1로 들고 시작).</summary>
        public void ServerGrantAugment(AugmentId id)
        {
            if (IsServer)
            {
                ServerApply(id);
            }
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
            if (entry.Leveled)
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
                AugmentSlot slot;
                if (index >= 0)
                {
                    slot = augmentSlots[index];
                    slot.Level = (byte)Mathf.Min(catalog.maxLevel, slot.Level + 1);
                }
                else if (augmentSlots.Count < catalog.maxSlots)
                {
                    slot = new AugmentSlot { Id = (byte)id, Level = 1 };
                }
                else
                {
                    return;
                }
                if (id == AugmentId.Curse)
                {
                    // 원귀와의 거래: 단계가 오를 때마다 그 단계 보상 중 1개를 완전 랜덤으로(이전 보상은 사라짐).
                    CurseReward[] row = CurseTable.Rewards[Mathf.Clamp(slot.Level - 1, 0, CurseTable.Rewards.Length - 1)];
                    slot.Variant = (byte)UnityEngine.Random.Range(0, row.Length);
                    if (row[slot.Variant].Rerolls > 0)
                    {
                        ServerAddRerollTickets(row[slot.Variant].Rerolls);
                    }
                }
                else if (id == AugmentId.Reroll)
                {
                    ServerAddRerollTickets(1); // 산통 흔들기: 누적 +n회 = 고를 때마다 1회
                }
                if (index >= 0)
                {
                    augmentSlots[index] = slot;
                }
                else
                {
                    augmentSlots.Add(slot);
                }
            }
            else if (entry.kind == AugmentKind.OneTime)
            {
                ServerExecuteOneTime(entry);
            }

            RecomputeModifiers();
            if (playerHealth != null)
            {
                playerHealth.ServerOnMaxHealthChanged(previousMax);
            }
        }

        /// <summary>일회용 - 칸을 차지하지 않고 고르는 순간 효과(기획서 v0.9.9 일회용 15종).</summary>
        private void ServerExecuteOneTime(AugmentEntry entry)
        {
            float value = entry.ValueAt(1);
            TryGetComponent(out CharacterKit kit);
            switch (entry.id)
            {
                case AugmentId.RiceBall:
                case AugmentId.Cheongsim:
                case AugmentId.HerbalTea:
                    if (playerHealth != null)
                    {
                        playerHealth.ServerHealByRatio(value / 100f);
                    }
                    if (kit != null)
                    {
                        kit.ServerApplyOneTime(entry.id); // 청심환 무적 · 약탕 둔화
                    }
                    break;
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
                case AugmentId.CoinPouch:
                    AddGold(Mathf.RoundToInt(value));
                    break;
                case AugmentId.CoinBundle:
                    AddGold(Mathf.RoundToInt(value));
                    if (PartyProgress.Instance != null)
                    {
                        PartyProgress.Instance.ServerAddGrudge(10);
                    }
                    break;
                case AugmentId.SoulHandful:
                    if (PartyProgress.Instance != null)
                    {
                        PartyProgress.Instance.ServerAddExpRatioOfNext(value / 100f);
                    }
                    break;
                case AugmentId.SoulJar:
                    if (playerHealth != null)
                    {
                        playerHealth.ServerLoseCurrentHealthRatio(value / 100f);
                    }
                    if (PartyProgress.Instance != null)
                    {
                        PartyProgress.Instance.ServerDebugLevelUp(1);
                    }
                    break;
                case AugmentId.HolyWater:
                    if (PartyProgress.Instance != null)
                    {
                        PartyProgress.Instance.ServerReduceGrudge(Mathf.RoundToInt(value));
                    }
                    break;
                case AugmentId.Yut:
                    ServerAddRerollTickets(Mathf.RoundToInt(value));
                    break;
                case AugmentId.HoneyCake:
                case AugmentId.Talisman:
                case AugmentId.TalismanBundle:
                case AugmentId.Makgeolli:
                case AugmentId.StrawShoes:
                    if (kit != null)
                    {
                        kit.ServerApplyOneTime(entry.id);
                    }
                    break;
            }
        }

        /// <summary>
        /// 기획서 v0.9.9 카드 규칙:
        /// - 3장(산통 흔들기 5단계면 4장). 261122 - 한 장마다 일회용 확률을 따로 두고(TryTakeWeighted) 나머지는 공통 · 협동 · 전용(지원 피드백).
        /// - 이미 5단계 · 무기 종류가 안 맞는 · 조건이 안 맞는 카드는 빠진다.
        /// - 6칸이 차면 가진 신통력의 다음 단계와 일회용만, 전부 5단계면 일회용만. 한 번에 같은 카드는 두 장 안 나온다.
        /// </summary>
        private void ServerRollOffers()
        {
            AugmentCatalogSO catalog = Catalog;
            bool allMaxed = ServerBuildCandidates();

            augmentOffers.Clear();
            int cards = Mathf.Max(1, catalog.cardsPerChoice + modifiers.CardBonus);
            for (int c = 0; c < cards; c++)
            {
                if (TryTakeWeighted(allMaxed, out AugmentOffer offer))
                {
                    augmentOffers.Add(offer);
                }
            }
        }

        /// <summary>
        /// 261122 - 카드 한 장 뽑기: 일회용 확률(catalog.oneTimeCardChance · 6칸 차면 oneTimeCardChanceSlotsFull)로 종류를 먼저 정하고
        /// 그 안에서 같은 확률. 그 종류가 비었으면 다른 쪽에서. 전부 5단계면 일회용만. 뽑힌 카드는 버퍼에서 빠져 겹치지 않는다.
        /// </summary>
        private bool TryTakeWeighted(bool allMaxed, out AugmentOffer offer)
        {
            if (allMaxed)
            {
                return TryTakeRandom(OneTimeBuffer, out offer);
            }
            AugmentCatalogSO catalog = Catalog;
            float chance = augmentSlots.Count >= catalog.maxSlots ? catalog.oneTimeCardChanceSlotsFull : catalog.oneTimeCardChance;
            bool oneTime = UnityEngine.Random.value < chance;
            List<AugmentOffer> first = oneTime ? OneTimeBuffer : CandidateBuffer;
            List<AugmentOffer> second = oneTime ? CandidateBuffer : OneTimeBuffer;
            return TryTakeRandom(first, out offer) || TryTakeRandom(second, out offer);
        }

        /// <summary>
        /// 카드 후보를 종류별 버퍼(CandidateBuffer = 칸 차지하는 카드 · OneTimeBuffer = 일회용)에 채운다. 6칸 전부 5단계면 true.
        /// </summary>
        private bool ServerBuildCandidates()
        {
            AugmentCatalogSO catalog = Catalog;
            CharacterStatsSO stats = playerHealth != null ? playerHealth.Stats : null;
            bool isRanged = stats != null && stats.isRanged;
            string characterId = stats != null ? stats.characterId : null;
            bool hasTeammate = PlayerCharacter.FindTeammate(gameObject) != null;
            bool lowHealth = playerHealth != null && playerHealth.MaxHealth > 0f
                             && playerHealth.CurrentHealth / playerHealth.MaxHealth <= catalog.healCardHealthRatio;
            int grudge = PartyProgress.Instance != null ? PartyProgress.Instance.Grudge : 0;

            CandidateBuffer.Clear();
            OneTimeBuffer.Clear();

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
                if (entry.kind == AugmentKind.OneTime)
                {
                    if (IsHealCard(entry.id) && !lowHealth)
                    {
                        continue; // 회복 카드는 체력 70% 이하일 때만
                    }
                    if (entry.id == AugmentId.HolyWater && grudge < catalog.holyWaterMinGrudge)
                    {
                        continue; // 정화수는 원한 5 이상일 때만
                    }
                    OneTimeBuffer.Add(new AugmentOffer { Id = (byte)entry.id, Level = 1 });
                    continue;
                }
                if (!entry.Leveled || !IsEligible(entry, isRanged, characterId))
                {
                    continue;
                }
                if (entry.kind == AugmentKind.Coop && !hasTeammate)
                {
                    continue; // 혼자면 협동 카드는 조건이 안 맞음
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
            }
            return allMaxed;
        }

        private static bool IsHealCard(AugmentId id) =>
            id == AugmentId.RiceBall || id == AugmentId.HerbalTea || id == AugmentId.Prayer || id == AugmentId.HoneyCake || id == AugmentId.Cheongsim;

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
