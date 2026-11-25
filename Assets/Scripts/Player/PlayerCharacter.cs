using System;
using CheonMyeong.Data;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261114 추가 - 이 플레이어가 어떤 캐릭터인지(8명 중). 플레이어 프리팹은 하나고, 캐릭터는 실행 중에 입힌다.
    /// - 오너가 자기 선택(CurrentRun.SelectedCharacter, 영웅 화면 "선택하기")과 신내림 연 칸(PlayerPrefs)을 NetworkVariable에 쓴다.
    /// - 전원이 값이 바뀔 때 스탯(이동 · 체력 · 공격) · 모션 · 이펙트 · 투사체 그림을 바꾼다. 판정은 원래대로 서버만.
    /// - 신내림 마스크는 서버의 노드 효과(SinnaerimEffects)가 읽는다.
    /// - 261116 영력 레벨도 오너가 쓴다(PlayerPrefs). 체력 · 공격력 · 공격 속도 기본값에 SpiritLevel 배율을 곱한다(PlayerHealth · PlayerAutoAttack이 읽음).
    /// 둘이 같은 캐릭터여도 된다(기획서 4장).
    /// </summary>
    public class PlayerCharacter : NetworkBehaviour
    {
        [SerializeField] private CharacterCatalogSO catalog;

        private readonly NetworkVariable<int> characterIndex = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<int> sinnaerimMask = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<int> spiritLevel = new NetworkVariable<int>(
            SpiritLevel.MinLevel, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerAutoAttack attack;
        private PlayerMovement movement;
        private PlayerHealth health;
        private PlayerAnimator animatorView;
        private PlayerAttackEffect attackEffect;
        private PlayerProjectiles projectiles;
        private AttackRangeDebugVisual debugVisual;
        private int appliedIndex = int.MinValue;

        public CharacterCatalogSO Catalog => catalog;
        public CharacterCatalogSO.CharacterEntry Entry { get; private set; }
        public CharacterStatsSO Stats => Entry != null ? Entry.stats : null;
        public string CharacterId => Entry != null ? Entry.characterId : string.Empty;
        public int SinnaerimMask => sinnaerimMask.Value;
        public int Level => spiritLevel.Value;

        /// <summary>261116 - 이 캐릭터 공격 소리 이름(SoundKeys.AttackKey). 캐릭터를 입힐 때 한 번 만든다.</summary>
        public string AttackSoundKey { get; private set; }
        public float HealthScale => SpiritLevel.HealthScale(spiritLevel.Value);
        public float AttackScale => SpiritLevel.AttackScale(spiritLevel.Value, CharacterId);
        public float AttackSpeedScale => SpiritLevel.AttackSpeedScale(spiritLevel.Value, CharacterId);

        /// <summary>캐릭터(또는 신내림)가 바뀌어 적용이 끝났을 때(전원).</summary>
        public event Action CharacterApplied;

        private void Awake()
        {
            TryGetComponent(out attack);
            TryGetComponent(out movement);
            TryGetComponent(out health);
            TryGetComponent(out animatorView);
            TryGetComponent(out attackEffect);
            TryGetComponent(out projectiles);
            TryGetComponent(out debugVisual);
        }

        public override void OnNetworkSpawn()
        {
            characterIndex.OnValueChanged += HandleIndexChanged;
            sinnaerimMask.OnValueChanged += HandleMaskChanged;
            spiritLevel.OnValueChanged += HandleLevelChanged;
            if (IsOwner)
            {
                CurrentRun.CharacterChanged += HandleLocalSelectionChanged;
                CharacterProgressStore.Changed += HandleLocalProgressChanged;
                CurrentRun.EnsureCharacterSelected(catalog);
                WriteOwnerSelection(CurrentRun.SelectedCharacter);
            }
            Apply(characterIndex.Value);
        }

        public override void OnNetworkDespawn()
        {
            characterIndex.OnValueChanged -= HandleIndexChanged;
            sinnaerimMask.OnValueChanged -= HandleMaskChanged;
            spiritLevel.OnValueChanged -= HandleLevelChanged;
            CurrentRun.CharacterChanged -= HandleLocalSelectionChanged;
            CharacterProgressStore.Changed -= HandleLocalProgressChanged;
        }

        /// <summary>261116 - 접속 중에 신내림 · 영력이 바뀌면(로비에서 수련 등) 지금 캐릭터 값을 다시 쓴다.</summary>
        private void HandleLocalProgressChanged(string characterId)
        {
            if (IsOwner && IsSpawned && Entry != null && Entry.characterId == characterId)
            {
                sinnaerimMask.Value = CharacterProgressStore.SinnaerimMask(characterId);
                spiritLevel.Value = CharacterProgressStore.Level(characterId);
            }
        }

        private void HandleLevelChanged(int previous, int current)
        {
            // 서버: 최대 체력이 바뀌었으니 현재 체력도 맞춘다(늘면 그만큼 채움).
            if (IsServer && health != null && health.Stats != null)
            {
                float previousMax = health.MaxHealth / SpiritLevel.HealthScale(current) * SpiritLevel.HealthScale(previous);
                health.ServerOnMaxHealthChanged(previousMax);
            }
            CharacterApplied?.Invoke();
        }

        private void HandleLocalSelectionChanged(CharacterCatalogSO.CharacterEntry entry)
        {
            if (IsOwner && IsSpawned)
            {
                WriteOwnerSelection(entry);
            }
        }

        private void WriteOwnerSelection(CharacterCatalogSO.CharacterEntry entry)
        {
            int index = catalog != null ? catalog.IndexOf(entry) : -1;
            if (index < 0)
            {
                index = 0;
            }
            CharacterCatalogSO.CharacterEntry chosen = catalog != null ? catalog.At(index) : null;
            sinnaerimMask.Value = chosen != null ? CharacterProgressStore.SinnaerimMask(chosen.characterId) : 0;
            spiritLevel.Value = chosen != null ? CharacterProgressStore.Level(chosen.characterId) : SpiritLevel.MinLevel;
            characterIndex.Value = index;
        }

        private void HandleIndexChanged(int previous, int current)
        {
            Apply(current);
        }

        private void HandleMaskChanged(int previous, int current)
        {
            CharacterApplied?.Invoke();
        }

        private void Apply(int index)
        {
            if (catalog == null)
            {
                return;
            }
            if (index < 0)
            {
                index = 0; // 오너 값이 오기 전에는 첫 캐릭터(하영)로 둔다
            }
            if (index == appliedIndex)
            {
                return;
            }
            CharacterCatalogSO.CharacterEntry entry = catalog.At(index);
            if (entry == null || entry.stats == null)
            {
                return;
            }
            appliedIndex = index;
            Entry = entry;
            AttackSoundKey = CheonMyeong.Audio.SoundKeys.AttackKey(entry.characterId);
            CharacterStatsSO stats = entry.stats;
            if (movement != null)
            {
                movement.SetStats(stats);
            }
            if (attack != null)
            {
                attack.SetStats(stats);
            }
            if (health != null)
            {
                health.SetStats(stats);
            }
            if (debugVisual != null)
            {
                debugVisual.SetStats(stats);
            }
            if (animatorView != null)
            {
                animatorView.SetCharacter(entry);
            }
            if (attackEffect != null)
            {
                attackEffect.SetCharacter(stats, stats.IsProjectile ? null : entry.effectFrames);
            }
            if (projectiles != null)
            {
                projectiles.SetSprite(entry.projectileSprite);
            }
            CharacterApplied?.Invoke();
        }

        /// <summary>261114 - 화면의 P1 = 방장(서버 클라이언트), P2 = 참가자. 대기실 · 결과 화면이 같은 기준을 쓴다. 없으면 null.</summary>
        public static PlayerCharacter FindBySlot(int slot)
        {
            bool wantHost = slot == 0;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || !player.IsSpawned)
                {
                    continue;
                }
                bool isHost = player.OwnerClientId == NetworkManager.ServerClientId;
                if (isHost == wantHost && player.TryGetComponent(out PlayerCharacter pc))
                {
                    return pc;
                }
            }
            return null;
        }

        /// <summary>동료(나 말고 접속한 다른 플레이어). 없으면 null. 2인 협동이라 첫 번째 다른 사람.</summary>
        public static PlayerHealth FindTeammate(GameObject self)
        {
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth other = PlayerHealth.ActivePlayers[i];
                if (other != null && other.gameObject != self)
                {
                    return other;
                }
            }
            return null;
        }
    }
}
