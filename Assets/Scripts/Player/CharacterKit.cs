using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261114 추가 - 캐릭터마다 다른 인게임 효과 한 곳(기획서 v0.9.7 6장):
    /// ① 고유 패시브 8종 ② 전용 신통력 16종(5단계 특별 능력 포함) ③ 신내림 노드 효과(나 · 동료) ④ 각성(고정 쿨타임 자동 발동)
    /// ⑤ 호신부(횟수제 방어막: 1회 = 공격 1번을 0으로 막고 0.5초 무적 · 20초마다 1회 충전 · 웨이브 시작 시 전부 충전 · 최대 5).
    ///
    /// 구조:
    /// - 서버가 매 프레임 "내 효과(self)"와 "동료에게 주는 효과(AllyAura)"를 계산하고, 내 최종값 = self + 동료의 AllyAura 를
    ///   이동 · 공격 · 체력 컴포넌트의 Extra 값에 넣는다(서로 덮어쓰지 않게 동료 효과는 상대 키트가 읽어 간다).
    /// - 피해 계산은 PlayerAutoAttack.ServerDamageTarget → IPlayerCombatHook(이 컴포넌트)에서 바꾼다.
    /// - 판정 · 상태는 전부 서버. 클라이언트는 패시브 켜짐 · 호신부 수(복제)와 연출 Rpc만 받는다.
    /// 캐릭터별 내용은 CharacterKit.Heroes.cs. 매 프레임 할당 없음.
    /// </summary>
    public partial class CharacterKit : NetworkBehaviour, IPlayerCombatHook
    {
        private const float NearAllyDistance = 6f;
        private const float FarAllyDistance = 10f;
        private const float ShieldRechargeSeconds = 20f;
        private const float ShieldInvulnerableSeconds = 0.5f;
        private const int ShieldMax = 5;
        private const float HitBuffSeconds = 3f;

        /// <summary>한 프레임의 효과 합(비율은 0.1 = 10%).</summary>
        public struct Bonus
        {
            public float MoveSpeed;
            public float AttackSpeed;
            public float Attack;
            public float Crit;
            public float CritDamage;
            public float DamageTaken;      // + 늘어남, - 줄어듦
            public float Regen;            // 최대 체력 %/초 (0.002 = 0.2%)
            public float Range;            // 칸
            public float RangeRatio;       // 기본 사거리 대비
            public float Area;
            public float MaxHealth;
            public float ProjectileSpeed;
            public float KnockbackScale;
            public float BossDamage;
            public float SoulGain;
            public int Shield;
            public int Pierce;

            public void Add(in Bonus b)
            {
                MoveSpeed += b.MoveSpeed; AttackSpeed += b.AttackSpeed; Attack += b.Attack; Crit += b.Crit; CritDamage += b.CritDamage;
                DamageTaken += b.DamageTaken; Regen += b.Regen; Range += b.Range; RangeRatio += b.RangeRatio; Area += b.Area;
                MaxHealth += b.MaxHealth; ProjectileSpeed += b.ProjectileSpeed; KnockbackScale += b.KnockbackScale;
                BossDamage += b.BossDamage; SoulGain += b.SoulGain; Shield += b.Shield; Pierce += b.Pierce;
            }
        }

        private readonly NetworkVariable<bool> passiveActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> shieldCharges = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerCharacter character;
        private PlayerAutoAttack attack;
        private PlayerMovement movement;
        private PlayerHealth health;
        private PlayerProgress progress;
        private PlayerProjectiles projectiles;

        private Bonus self;
        private Bonus total;
        /// <summary>서버 전용 - 이번 프레임 동료에게 주는 효과(동료 키트가 읽는다).</summary>
        public Bonus AllyAura { get; private set; }

        private PlayerHealth teammate;
        private CharacterKit teammateKit;
        private float teammateDistance = float.MaxValue;
        private string heroId = string.Empty;
        private int mask;

        private int lastPhase = -1;
        private int lastWave = -1;
        private float shieldTimer;
        private float soulCarry;
        private float lastMaxHealthBonus;

        /// <summary>서버 전용 - 마지막으로 실제 피해를 받은 시각(호신부로 막은 건 제외).</summary>
        public float LastDamagedTime { get; private set; } = -999f;
        /// <summary>서버 전용 - 마지막으로 호신부가 막은 시각.</summary>
        public float LastShieldBlockTime { get; private set; } = -999f;

        public bool PassiveActive => passiveActive.Value;
        public int ShieldCharges => shieldCharges.Value;
        public string PassiveName => character != null && character.Entry != null ? character.Entry.passiveName : string.Empty;

        private void Awake()
        {
            TryGetComponent(out character);
            TryGetComponent(out attack);
            TryGetComponent(out movement);
            TryGetComponent(out health);
            TryGetComponent(out progress);
            TryGetComponent(out projectiles);
        }

        public override void OnNetworkSpawn()
        {
            if (character != null)
            {
                character.CharacterApplied += HandleCharacterApplied;
            }
            HandleCharacterApplied();
            if (IsServer && health != null)
            {
                health.ServerDamageFilter = ServerShieldFilter;
                health.ServerDamaged += HandleServerDamaged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (character != null)
            {
                character.CharacterApplied -= HandleCharacterApplied;
            }
            if (health != null)
            {
                health.ServerDamaged -= HandleServerDamaged;
                if (health.ServerDamageFilter == ServerShieldFilter)
                {
                    health.ServerDamageFilter = null;
                }
            }
        }

        private void HandleCharacterApplied()
        {
            heroId = character != null ? character.CharacterId : string.Empty;
            mask = character != null ? character.SinnaerimMask : 0;
            ResetHeroState();
            if (attack != null)
            {
                attack.RefreshCombatHooks();
            }
            if (projectiles != null && character != null && character.Entry != null)
            {
                Sprite[] fx = character.Entry.effectFrames;
                projectiles.SetAltSprite(fx != null && fx.Length > 0 ? fx[0] : null);
            }
        }

        private bool Has(CharacterCatalogSO.SinnaerimSlot slot) => CharacterProgressStore.IsOpen(mask, slot);

        private int Lv(AugmentId id) => progress != null ? progress.LevelOf(id) : 0;

        private static float Val(AugmentId id, int level) => level > 0 ? PlayerProgress.Catalog.ValueAt(id, level) : 0f;

        private bool Paused => PartyProgress.Instance != null && PartyProgress.Instance.IsPaused;

        private bool InCombat => WaveManager.Instance != null && (WaveManager.Instance.IsWaveRunning || WaveManager.Instance.IsFinalBattle);

        private bool AllyNear => teammate != null && !teammate.IsDeadServerSafe && teammateDistance <= NearAllyDistance;

        private bool AllyFar => teammate != null && teammateDistance >= FarAllyDistance;

        private float AllyHealthRatio =>
            teammate != null && !teammate.IsDeadServerSafe && teammate.MaxHealth > 0f ? teammate.CurrentHealth / teammate.MaxHealth : 1f;

        private bool Alive => health != null && !health.IsDeadServerSafe;

        // ───────── 서버 매 프레임 ─────────

        private void Update()
        {
            if (IsOwner)
            {
                OwnerInputUpdate();
            }
            UpdateClientFx(Time.deltaTime);
            if (!IsServer || !IsSpawned || attack == null || attack.Stats == null)
            {
                return;
            }

            teammate = PlayerCharacter.FindTeammate(gameObject);
            teammateKit = null;
            if (teammate != null)
            {
                teammate.TryGetComponent(out teammateKit);
                teammateDistance = Vector2.Distance(transform.position, teammate.transform.position);
            }
            else
            {
                teammateDistance = float.MaxValue;
            }

            DetectWaveStart();
            float dt = Paused ? 0f : Time.deltaTime;
            if (dt > 0f && InCombat && Alive)
            {
                TickShield(dt);
                TickHero(dt);
            }

            self = default;
            Bonus aura = default;
            bool active = false;
            if (Alive)
            {
                ComputeNodes(ref self, ref aura);
                ComputeHero(ref self, ref aura, ref active);
            }
            AllyAura = aura;
            total = self;
            if (teammateKit != null && Alive)
            {
                total.Add(teammateKit.AllyAura);
            }
            ApplyTotals();
            if (passiveActive.Value != active)
            {
                passiveActive.Value = active;
            }
        }

        private void DetectWaveStart()
        {
            WaveManager waves = WaveManager.Instance;
            if (waves == null)
            {
                return;
            }
            int phase = waves.Phase;
            int wave = waves.CurrentWave;
            bool started = (phase == WaveManager.PhaseRunning || phase == WaveManager.PhaseFinalBattle)
                           && (phase != lastPhase || wave != lastWave);
            lastPhase = phase;
            lastWave = wave;
            if (!started)
            {
                return;
            }
            if (wave <= 1 && phase == WaveManager.PhaseRunning)
            {
                OnRunStart();
            }
            shieldCharges.Value = (byte)ShieldMaxCount();
            shieldTimer = 0f;
            OnWaveStart();
        }

        private void OnRunStart()
        {
            ResetHeroState();
            soulCarry = 0f;
            // 신내림 관문: 전용 신통력 둘 중 하나를 Lv1로 들고 시작(기획서 - 고르는 방식이 없어 무작위).
            if (Has(CharacterCatalogSO.SinnaerimSlot.Gate) && progress != null)
            {
                ExclusivePair(out AugmentId a, out AugmentId b);
                if (a != AugmentId.None && Lv(a) == 0 && Lv(b) == 0)
                {
                    progress.ServerGrantAugment(Random.value < 0.5f ? a : b);
                }
            }
        }

        private void ApplyTotals()
        {
            CharacterStatsSO stats = attack.Stats;
            AugmentModifiers mods = progress != null ? progress.Modifiers : default;
            attack.ExtraAttackSpeed = total.AttackSpeed;
            attack.RangeBonus = total.Range + stats.attackRange * total.RangeRatio;
            attack.AreaBonus = total.Area;
            attack.MeleePierceBonus = total.Pierce;
            if (movement != null)
            {
                movement.ExtraMoveSpeed = total.MoveSpeed;
            }
            if (health != null)
            {
                health.DamageTakenScale = Mathf.Max(0f, 1f + total.DamageTaken);
                health.ExtraRegenPercent = total.Regen;
                health.HealingScale = HealingScaleNow();
                float maxBonus = total.MaxHealth + HeroMaxHealthBonus();
                if (!Mathf.Approximately(maxBonus, lastMaxHealthBonus))
                {
                    float previousMax = health.MaxHealth;
                    lastMaxHealthBonus = maxBonus;
                    health.ExtraMaxHealthBonus = maxBonus;
                    health.ServerOnMaxHealthChanged(previousMax);
                }
            }
            if (projectiles != null)
            {
                projectiles.SpeedBonus = mods.ProjectileSpeedBonus + total.ProjectileSpeed + HeroProjectileSpeedBonus();
                projectiles.ExtraProjectiles = mods.ExtraProjectiles;
                projectiles.ExtraPierce = total.Pierce;
                projectiles.KnockbackScale = total.KnockbackScale;
                projectiles.ExtraTargets = HeroExtraTargets();
                projectiles.TargetBias = HeroTargetBias();
                int returning = heroId == "leeara" ? Lv(AugmentId.AraReturningKnife) : 0;
                projectiles.ReturnDamageScale = returning > 0 ? Val(AugmentId.AraReturningKnife, returning) / 100f : 0f;
                projectiles.ReturnForceCrit = returning >= 5;
            }
        }

        // ───────── 신내림 공통 갈래(캐릭터마다 수치만 다름) ─────────

        private void ComputeNodes(ref Bonus me, ref Bonus ally)
        {
            if (mask == 0)
            {
                return;
            }
            NodeTable(out NodeValues v);
            if (Has(CharacterCatalogSO.SinnaerimSlot.Base))
            {
                me.Attack += 0.05f;
                ally.Add(v.BaseAlly);
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1))
            {
                me.MaxHealth += v.VanguardHealth;
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard2))
            {
                me.Shield += 1;
                if (Time.time - LastDamagedTime < HitBuffSeconds)
                {
                    ally.Add(v.VanguardHitAlly);
                }
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Guard1) && AllyNear)
            {
                me.Add(v.Guard1Self);
                ally.Add(v.Guard1Ally);
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Guard2))
            {
                if (AllyNear || v.Guard2SelfAlways)
                {
                    me.Add(v.Guard2Self);
                }
                if (v.Guard2AllyAlways || AllyNear)
                {
                    ally.Add(v.Guard2Ally);
                }
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Raid1) && AllyFar)
            {
                me.Add(v.Raid1Self);
                ally.Add(v.Raid1Ally);
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.Raid2))
            {
                me.SoulGain += 0.05f;
                if (AllyFar)
                {
                    me.Add(v.Raid2Self);
                }
            }
        }

        /// <summary>갈래 노드 수치 묶음. 캐릭터별 표는 Heroes.cs의 NodeTable.</summary>
        private struct NodeValues
        {
            public Bonus BaseAlly;
            public float VanguardHealth;
            public Bonus VanguardHitAlly;
            public Bonus Guard1Self;
            public Bonus Guard1Ally;
            public Bonus Guard2Self;
            public bool Guard2SelfAlways;
            public Bonus Guard2Ally;
            public bool Guard2AllyAlways;
            public Bonus Raid1Self;
            public Bonus Raid1Ally;
            public Bonus Raid2Self;
        }

        // ───────── 호신부 ─────────

        private int ShieldMaxCount()
        {
            int fromAugment = 0;
            int count = self.Shield + (teammateKit != null ? teammateKit.AllyAura.Shield : 0) + fromAugment + HeroExtraShield();
            return Mathf.Clamp(count, 0, ShieldMax);
        }

        private void TickShield(float dt)
        {
            int max = ShieldMaxCount();
            if (shieldCharges.Value > max)
            {
                shieldCharges.Value = (byte)max;
            }
            if (shieldCharges.Value >= max)
            {
                shieldTimer = 0f;
                return;
            }
            shieldTimer += dt;
            if (shieldTimer >= ShieldRechargeSeconds)
            {
                shieldTimer = 0f;
                shieldCharges.Value++;
            }
        }

        private bool ServerShieldFilter(float amount)
        {
            if (shieldCharges.Value == 0 || amount <= 0f)
            {
                return true;
            }
            shieldCharges.Value--;
            LastShieldBlockTime = Time.time;
            if (health != null)
            {
                health.ServerInvulnerableUntil = Time.time + ShieldInvulnerableSeconds;
            }
            OnShieldBlocked();
            return false;
        }

        private void HandleServerDamaged(float amount)
        {
            OnDamagedBeforeRecord(amount);
            LastDamagedTime = Time.time;
        }

        // ───────── 피해 계산 ─────────

        public void ModifyOutgoing(ref OutgoingHit hit)
        {
            hit.Damage *= 1f + total.Attack;
            hit.CritChance += total.Crit;
            hit.CritMultiplier += total.CritDamage;
            hit.Knockback *= 1f + (hit.IsProjectile ? 0f : total.KnockbackScale); // 투사체는 PlayerProjectiles가 이미 반영
            if (hit.Enemy != null)
            {
                hit.Damage *= 1f + hit.Enemy.VulnerableBonusFor(gameObject);
                if (hit.Enemy.HasBrandFromOther(gameObject))
                {
                    PopBrand(ref hit);
                }
            }
            if (total.BossDamage > 0f && IsBossOrElite(hit.Target))
            {
                hit.Damage *= 1f + total.BossDamage;
            }
            HeroModifyOutgoing(ref hit);
        }

        public void OnHitLanded(in OutgoingHit hit, bool killed)
        {
            HeroOnHitLanded(in hit, killed);
        }

        private void PopBrand(ref OutgoingHit hit)
        {
            GameObject source = hit.Enemy.ServerBrandSource;
            hit.Enemy.ServerClearBrand();
            if (source == null || !source.TryGetComponent(out PlayerAutoAttack brander) || brander.Stats == null)
            {
                return;
            }
            // 주사 낙인: 동료가 공격하면 터져서 (한주원) 공격력 50% 추가 피해.
            hit.Damage += brander.BaseAttackPower * 0.5f;
            if (source.TryGetComponent(out CharacterKit branderKit) && branderKit.Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && health != null)
            {
                health.ServerHealByRatio(0.01f); // 짙은 주사(동료): 낙인을 터뜨린 동료 체력 1% 회복
            }
        }

        public static bool IsBossOrElite(Collider2D col)
        {
            if (col == null)
            {
                return false;
            }
            if (col.GetComponentInParent<Boss.EoksoeBoss>() != null || col.GetComponent<Boss.BossCloneHitbox>() != null)
            {
                return true;
            }
            return col.TryGetComponent(out Enemy enemy) && enemy.Stats != null && enemy.Stats.maxHealth >= 100f;
        }

        public static bool IsBoss(Collider2D col) =>
            col != null && (col.GetComponentInParent<Boss.EoksoeBoss>() != null || col.GetComponent<Boss.BossCloneHitbox>() != null);

        // ───────── 영혼 ─────────

        /// <summary>서버 전용 - 내가 주운 영혼에 신내림 "+5%"를 붙인다(소수점은 모아둔다).</summary>
        public int ServerScaleSoul(int amount)
        {
            if (self.SoulGain <= 0f || amount <= 0)
            {
                return amount;
            }
            soulCarry += amount * self.SoulGain;
            int extra = Mathf.FloorToInt(soulCarry);
            soulCarry -= extra;
            return amount + extra;
        }

        // ───────── 공용 도우미 ─────────

        private readonly Collider2D[] overlap = new Collider2D[64];

        private int CountEnemiesAround(Vector2 center, float radius)
        {
            int n = attack.ServerOverlapEnemies(center, radius, overlap);
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (overlap[i].TryGetComponent(out Enemy enemy) && !enemy.IsDying)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>주변 radius 원귀가 seconds 동안 나를 노린다.</summary>
        private void TauntAround(float radius, float seconds)
        {
            int n = attack.ServerOverlapEnemies(transform.position, radius, overlap);
            for (int i = 0; i < n; i++)
            {
                if (overlap[i].TryGetComponent(out Enemy enemy) && !enemy.IsDying)
                {
                    enemy.ServerTaunt(transform, seconds);
                }
            }
        }

        private Vector2 AimDirection()
        {
            Vector2 dir = (Vector2)attack.AimPosition - (Vector2)transform.position;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.left;
        }

        /// <summary>서버 전용 - 선분(from→to) 근처 원귀를 때린다(돌진 · 언월 진격).</summary>
        private void HitAlongSegment(Vector2 from, Vector2 to, float width, float damageScale, float knockback, int tag)
        {
            Vector2 mid = (from + to) * 0.5f;
            float half = Vector2.Distance(from, to) * 0.5f + width;
            int n = attack.ServerOverlapEnemies(mid, half, overlap);
            Vector2 seg = to - from;
            float segLenSq = Mathf.Max(0.0001f, seg.sqrMagnitude);
            Vector2 dir = seg.sqrMagnitude > 0.0001f ? seg.normalized : AimDirection();
            attack.NextHitTag = tag;
            for (int i = 0; i < n; i++)
            {
                Collider2D col = overlap[i];
                if (!Boss.DeathRealm.CanInteract(gameObject, col.gameObject))
                {
                    continue;
                }
                Vector2 p = col.transform.position;
                float t = Mathf.Clamp01(Vector2.Dot(p - from, seg) / segLenSq);
                if ((from + seg * t - p).sqrMagnitude > width * width)
                {
                    continue;
                }
                attack.ServerDamageTarget(col, damageScale, dir, knockback, false, hitFeedback);
            }
            attack.NextHitTag = 0;
            attack.ServerFlushHitFeedback(hitFeedback);
        }

        private readonly System.Collections.Generic.List<Combat.HitFeedback> hitFeedback = new System.Collections.Generic.List<Combat.HitFeedback>(32);
    }
}
