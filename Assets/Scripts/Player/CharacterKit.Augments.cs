using CheonMyeong.Enemies;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261118 추가 - 기획서 v0.9.9 7장 신통력 중 "상황에 따라 켜지는" 효과(서버 판정):
    /// ① 공통 20종의 5단계 특별 능력 ② 협동 6종(동료가 쓰러지면 꺼짐 · 공명 ×1.5) ③ 저주 받는 피해
    /// ④ 일회용 지속 효과(꿀떡 · 약탕 · 청심환 · 부적 · 막걸리 · 새 짚신).
    /// 늘 같은 값인 스탯(최대 체력 · 공격력 % 등)은 PlayerProgress.Modifiers가 모든 화면에서 계산한다.
    /// 지속 시간은 일시정지(신통력 선택 · ESC) 동안 흐르지 않는 augClock 기준. 매 프레임 할당 없음.
    /// 기획서에 없는 값은 (제안)으로 표시.
    /// </summary>
    public partial class CharacterKit
    {
        private const int TagRope = 20;
        private const int TagLightning = 21;
        private const int TagBounce = 22;
        private const float Resonance = 1.5f;          // 공명: 둘 다 같은 협동 카드면 +50%
        private const float RopeMaxLength = 10f;
        private const float RopeTickSeconds = 0.25f;   // (제안) 금줄 피해 간격
        private const float RopeHalfWidth = 0.4f;      // (제안) 금줄에 "닿음" 판정 폭(칸)
        // 261123 - 지원 피드백 "금줄이 몸이 아니라 머리에 걸림": 캐릭터 피벗(128px 칸 가운데)은 얼굴 높이라
        // 몸통(허리) 높이로 0.3유닛(30px, PPU 100) 내린다. 그림 · 판정 둘 다 같은 높이를 쓴다.
        private static readonly Vector2 RopeBodyOffset = new Vector2(0f, -0.3f);
        private const float BounceSearchRadius = 3f;   // (제안) 물수제비가 튕겨 갈 거리(칸)
        private const float SojiPushCells = 1f;        // (제안) 소지가 밀쳐내는 거리(칸)

        private readonly NetworkVariable<bool> ropeShown = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float augClock;
        private int basicAttackCount;
        private float sinmyeongUntil;      // 신들린 손 5단계
        private float beoUntil;            // 서슬 5단계(베어 넘김)
        private float soulCallTimer;       // 넋 부르기 5단계(초혼)
        private int lastLifestealFrame = -1;
        private int lastPartyLevel = -1;
        private int lastGrudgeStage = -1;
        private int hanpuriStacks;         // 씻김굿 5단계: 이번 웨이브에 원한 단계가 오른 횟수
        private int hanpuriWave = -1;
        private bool augChain;             // 번개 · 물수제비가 다시 번개 · 물수제비를 부르지 않게

        // 협동
        private bool aegisUsedThisWave;    // 등 맡기기 5단계(액땜)
        private float ropeTick;
        private readonly float[] cheerUntil = new float[5]; // 얼쑤 중첩(최대 5)
        private readonly float[] partyKillTimes = new float[30]; // 굿판: 둘이 10초 안에 30마리
        private int partyKillHead;
        private float gutpanUntil;
        private float farSince = -1f;      // 한달음 5단계
        private float rushReadyAt;

        // 일회용
        private float honeyUntil;
        private float herbalSlowUntil;
        private float weakUntil;           // 부적 다발 대가
        private float drunkUntil;          // 막걸리
        private int strawWave = -1;        // 새 짚신
        private int talismanExtra;         // 부적 한 장이 최대치를 넘긴 수(이번 웨이브만)

        private readonly Collider2D[] augOverlap = new Collider2D[64];
        private readonly System.Collections.Generic.List<Combat.HitFeedback> augFeedback =
            new System.Collections.Generic.List<Combat.HitFeedback>(16);

        private LineRenderer ropeLine;

        private AugmentModifiers Mods => progress != null ? progress.Modifiers : default;

        private bool Max(AugmentId id) => Lv(id) >= PlayerProgress.Catalog.maxLevel;

        private int CurrentWave => WaveManager.Instance != null ? WaveManager.Instance.CurrentWave : 0;

        /// <summary>협동 신통력이 켜져 있는지(동료가 있고 둘 다 살아 있음).</summary>
        private bool CoopOn => Alive && teammate != null && !teammate.IsDeadServerSafe;

        private int TeammateLv(AugmentId id) =>
            teammateKit != null && teammateKit.progress != null ? teammateKit.progress.LevelOf(id) : 0;

        /// <summary>협동 신통력 값(비율). 공명이면 ×1.5.</summary>
        private float CoopValue(AugmentId id)
        {
            int lv = Lv(id);
            if (lv <= 0)
            {
                return 0f;
            }
            float v = Val(id, lv) / 100f;
            return TeammateLv(id) > 0 ? v * Resonance : v;
        }

        private void ResetAugmentState()
        {
            augClock = 0f;
            basicAttackCount = 0;
            sinmyeongUntil = beoUntil = gutpanUntil = 0f;
            soulCallTimer = 0f;
            hanpuriStacks = 0;
            hanpuriWave = -1;
            aegisUsedThisWave = false;
            ropeTick = 0f;
            for (int i = 0; i < cheerUntil.Length; i++)
            {
                cheerUntil[i] = 0f;
            }
            for (int i = 0; i < partyKillTimes.Length; i++)
            {
                partyKillTimes[i] = -999f;
            }
            partyKillHead = 0;
            farSince = -1f;
            rushReadyAt = 0f;
            honeyUntil = herbalSlowUntil = weakUntil = drunkUntil = 0f;
            strawWave = -1;
            talismanExtra = 0;
            lastGrudgeStage = PartyProgress.Instance != null ? PartyProgress.Instance.GrudgeStage : 0;
            lastPartyLevel = PartyProgress.Instance != null ? PartyProgress.Instance.Level : -1;
        }

        // ───────── 매 프레임(서버) ─────────

        private void TickAugments(float dt)
        {
            augClock += dt;

            // 넋 부르기 5단계 「초혼」: 30초마다 맵의 영혼을 전부 끌어옴.
            if (Max(AugmentId.PickupRange))
            {
                soulCallTimer += dt;
                if (soulCallTimer >= 30f)
                {
                    soulCallTimer = 0f;
                    LootPickup.ServerPullAllExpTo(health);
                }
            }

            // 금줄: 두 사람 사이 줄에 닿은 원귀에게 초당 공격력 n%.
            if (Lv(AugmentId.CoopRope) > 0 && CoopOn && teammateDistance <= RopeMaxLength)
            {
                ropeTick += dt;
                if (ropeTick >= RopeTickSeconds)
                {
                    ropeTick -= RopeTickSeconds;
                    RopeHit(CoopValue(AugmentId.CoopRope) * RopeTickSeconds, Max(AugmentId.CoopRope));
                }
            }
            else
            {
                ropeTick = 0f;
            }

            // 한달음 5단계 「곁으로」: 10칸 넘게 떨어진 채 3초 → 동료 곁으로 순간이동(30초마다).
            if (Max(AugmentId.CoopRush) && CoopOn && teammateDistance > FarAllyDistance)
            {
                if (farSince < 0f)
                {
                    farSince = augClock;
                }
                if (augClock - farSince >= 3f && augClock >= rushReadyAt && movement != null)
                {
                    Vector2 toMe = (Vector2)transform.position - (Vector2)teammate.transform.position;
                    Vector2 side = (Vector2)teammate.transform.position + (toMe.sqrMagnitude > 0.0001f ? toMe.normalized : Vector2.down) * 1f;
                    movement.ServerTeleport(side);
                    rushReadyAt = augClock + 30f;
                    farSince = -1f;
                }
            }
            else
            {
                farSince = -1f;
            }
        }

        /// <summary>서버 매 프레임 - 레벨업 · 원한 단계 변화 감지(깨달음 득도 · 씻김굿 한풀이).</summary>
        private void WatchPartyAugments()
        {
            PartyProgress party = PartyProgress.Instance;
            if (party == null)
            {
                return;
            }
            int level = party.Level;
            if (lastPartyLevel >= 0 && level > lastPartyLevel && Alive && Max(AugmentId.Growth))
            {
                health.ServerHealByRatio(0.10f * (level - lastPartyLevel)); // 득도
            }
            lastPartyLevel = level;

            int stage = party.GrudgeStage;
            if (lastGrudgeStage >= 0 && stage > lastGrudgeStage && (Max(AugmentId.Cleanse) || TeammateLv(AugmentId.Cleanse) >= PlayerProgress.Catalog.maxLevel))
            {
                // 한풀이: 원한 단계가 오를 때마다 그 웨이브 동안 둘 다 공격력 +10%(웨이브 사이에 오르면 다음 웨이브).
                int wave = InCombat ? CurrentWave : CurrentWave + 1;
                if (hanpuriWave != wave)
                {
                    hanpuriWave = wave;
                    hanpuriStacks = 0;
                }
                hanpuriStacks += stage - lastGrudgeStage;
            }
            lastGrudgeStage = stage;
        }

        private void ComputeAugments(ref Bonus me)
        {
            AugmentModifiers mods = Mods;
            AugmentCatalogSO catalog = PlayerProgress.Catalog;

            me.DamageTaken += mods.CurseDamageTaken;
            me.SoulGain += mods.GrowthBonus;

            // 5단계 특별 능력
            float hpRatio = health != null && health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 1f;
            if (Max(AugmentId.MoveSpeed) && hpRatio <= 0.3f)
            {
                me.MoveSpeed += 0.20f; // 줄행랑
            }
            if (Max(AugmentId.Regen) && Time.time - LastDamagedTime >= 3f)
            {
                me.Regen += Val(AugmentId.Regen, catalog.maxLevel) / 100f; // 깊은 숨: 재생량 2배
            }
            if (augClock < beoUntil)
            {
                me.Attack += 0.15f; // 베어 넘김
            }
            if (augClock < sinmyeongUntil)
            {
                me.AttackSpeed += 0.30f; // 신명
            }
            if (hanpuriWave == CurrentWave && hanpuriStacks > 0)
            {
                me.Attack += 0.10f * hanpuriStacks; // 한풀이
            }

            // 협동(동료가 쓰러지면 꺼지고, 다음 웨이브에 부활하면 다시 켜진다)
            bool coop = CoopOn;
            if (coop)
            {
                if (AllyNear)
                {
                    me.DamageTaken -= CoopValue(AugmentId.CoopBack);
                    me.Attack += CoopValue(AugmentId.CoopPair);
                }
                float cheer = CoopValue(AugmentId.CoopCheer);
                if (cheer > 0f)
                {
                    int stacks = 0;
                    for (int i = 0; i < cheerUntil.Length; i++)
                    {
                        if (augClock < cheerUntil[i])
                        {
                            stacks++;
                        }
                    }
                    me.AttackSpeed += cheer * stacks;
                }
                if (augClock < gutpanUntil)
                {
                    me.AttackSpeed += 0.20f; // 굿판
                }
                float rush = CoopValue(AugmentId.CoopRush);
                if (rush > 0f && teammateDistance > FarAllyDistance && MovingToward(teammate.transform.position))
                {
                    me.MoveSpeed += rush;
                }
            }
            if (ropeShown.Value != (coop && Lv(AugmentId.CoopRope) > 0 && teammateDistance <= RopeMaxLength))
            {
                ropeShown.Value = !ropeShown.Value;
            }

            // 일회용 지속 효과
            if (augClock < honeyUntil)
            {
                me.Regen += 0.03f; // 꿀떡
            }
            if (augClock < herbalSlowUntil)
            {
                me.MoveSpeed -= 0.30f; // 약탕 대가
            }
            if (augClock < weakUntil)
            {
                me.Attack -= 0.20f; // 부적 다발 대가
            }
            if (augClock < drunkUntil)
            {
                me.Attack += 0.30f; // 막걸리
            }
            if (strawWave == CurrentWave)
            {
                me.MoveSpeed += 0.20f; // 새 짚신
            }
        }

        private void AugmentsOnWaveStart()
        {
            aegisUsedThisWave = false;
            if (hanpuriWave != CurrentWave)
            {
                hanpuriStacks = 0;
            }
            if (!Alive)
            {
                return;
            }
            if (Max(AugmentId.MaxHealth))
            {
                health.ServerHealByRatio(0.25f); // 든든한 한 끼
            }
            int max = PlayerProgress.Catalog.maxLevel;
            if (CoopOn && (Lv(AugmentId.CoopMeal) >= max || TeammateLv(AugmentId.CoopMeal) >= max))
            {
                health.ServerHealByRatio(0.15f); // 밥상(둘 다 - 각자 자기 몫)
            }
        }

        // ───────── 기본 공격 · 피해 ─────────

        /// <summary>서버 전용 - 기본 공격 한 번(PlayerAutoAttack.ApplyHit 시작).</summary>
        public void ServerOnBasicAttack()
        {
            basicAttackCount++;
            if (Max(AugmentId.AttackSpeed) && basicAttackCount % 20 == 0)
            {
                sinmyeongUntil = augClock + 3f; // 신명
            }
            if (attack != null)
            {
                attack.AreaBurst = Max(AugmentId.Area) && basicAttackCount % 5 == 0 ? 2f : 1f; // 큰 물결
            }
        }

        private void AugmentsModifyOutgoing(ref OutgoingHit hit)
        {
            AugmentModifiers mods = Mods;
            if (!hit.IsProjectile)
            {
                hit.Knockback += mods.KnockbackBonus; // 물렀거라(투사체는 PlayerProjectiles.KnockbackBonus)
                if (Max(AugmentId.MeleeRange) && attack != null && hit.Distance >= attack.HitRadius * 0.7f)
                {
                    hit.Damage *= 1.20f; // 끝자락
                }
            }
            Enemy enemy = hit.Enemy;
            if (enemy != null)
            {
                float now = Time.time;
                if (now < enemy.ServerExposedUntil)
                {
                    hit.Damage *= 1.10f; // 급살
                }
                // 합공: 둘이 같은 원귀를 2초 안에 때리면 그 원귀가 받는 피해 +20%.
                int max = PlayerProgress.Catalog.maxLevel;
                if (CoopOn && (Lv(AugmentId.CoopPair) >= max || TeammateLv(AugmentId.CoopPair) >= max)
                    && enemy.ServerLastHitter != null && enemy.ServerLastHitter != gameObject
                    && enemy.ServerLastHitter == teammate.gameObject && now - enemy.ServerLastHitTime <= 2f)
                {
                    enemy.ServerJointUntil = now + 2f;
                }
                if (now < enemy.ServerJointUntil)
                {
                    hit.Damage *= 1.20f;
                }
            }
        }

        private void AugmentsOnHitLanded(in OutgoingHit hit, bool killed)
        {
            AugmentModifiers mods = Mods;
            Enemy enemy = hit.Enemy;
            float now = Time.time;
            if (enemy != null)
            {
                enemy.ServerLastHitter = gameObject;
                enemy.ServerLastHitTime = now;
            }

            // 넋 거두기: 준 피해의 n% 회복, 한 번 공격(한 프레임)에 1마리분만.
            if (mods.Lifesteal > 0f && hit.Tag != TagRope && lastLifestealFrame != Time.frameCount && health != null)
            {
                lastLifestealFrame = Time.frameCount;
                health.ServerHealAmount(hit.FinalDamage * mods.Lifesteal);
            }

            if (killed)
            {
                if (Max(AugmentId.Lifesteal) && health != null)
                {
                    health.ServerHealAmount(1f); // 넋 한 줌
                }
                if (Max(AugmentId.AttackPower))
                {
                    beoUntil = augClock + 2f; // 베어 넘김
                }
                RecordPartyKill();
                if (teammateKit != null)
                {
                    teammateKit.OnTeammateKill();
                }
            }

            if (enemy != null && !killed)
            {
                if (Max(AugmentId.Knockback) && hit.Knockback > 0f)
                {
                    enemy.ServerStun(0.5f); // 호통
                }
                if (hit.Crit && Max(AugmentId.CritChance))
                {
                    enemy.ServerExposedUntil = now + 3f; // 급살(동료 공격에도)
                }
            }

            if (augChain || attack == null)
            {
                return;
            }
            // 벼락 갈래: 치명타가 터지면 주변 1칸 원귀에게 공격력 30% 번개 피해.
            if (hit.Crit && Max(AugmentId.CritDamage) && hit.Target != null)
            {
                ChainAround(hit.Target.transform.position, 1f, 0.30f, TagLightning, hit.Target, false);
            }
            // 물수제비: 투사체가 원귀를 맞히면 25% 확률로 근처 원귀에게 한 번 더 튕김.
            if (hit.IsProjectile && Max(AugmentId.ProjectileCount) && hit.Target != null && Random.value < 0.25f)
            {
                ChainAround(hit.Target.transform.position, BounceSearchRadius, 1f, TagBounce, hit.Target, true);
            }
        }

        /// <summary>center 주변 원귀에게 추가 피해(exclude 제외). nearestOnly면 가장 가까운 1마리만.</summary>
        private void ChainAround(Vector2 center, float radius, float damageScale, int tag, Collider2D exclude, bool nearestOnly)
        {
            int n = attack.ServerOverlapEnemies(center, radius, augOverlap);
            Collider2D best = null;
            float bestSq = float.MaxValue;
            augChain = true;
            int previousTag = attack.NextHitTag;
            attack.NextHitTag = tag;
            augFeedback.Clear();
            for (int i = 0; i < n; i++)
            {
                Collider2D col = augOverlap[i];
                if (col == exclude || !Boss.DeathRealm.CanInteract(gameObject, col.gameObject))
                {
                    continue;
                }
                if (nearestOnly)
                {
                    float sq = ((Vector2)col.transform.position - center).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = col;
                    }
                    continue;
                }
                Vector2 away = (Vector2)col.transform.position - center;
                attack.ServerDamageTarget(col, damageScale, away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.up, 0f, false, augFeedback);
            }
            if (best != null)
            {
                Vector2 dir = (Vector2)best.transform.position - center;
                attack.ServerDamageTarget(best, damageScale, dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up, 0f, true, augFeedback);
            }
            attack.NextHitTag = previousTag;
            attack.ServerFlushHitFeedback(augFeedback);
            augChain = false;
        }

        private void RopeHit(float damageScale, bool slow)
        {
            Vector2 from = (Vector2)transform.position + RopeBodyOffset;
            Vector2 to = (Vector2)teammate.transform.position + RopeBodyOffset;
            Vector2 seg = to - from;
            float segLenSq = Mathf.Max(0.0001f, seg.sqrMagnitude);
            Vector2 mid = (from + to) * 0.5f;
            int n = attack.ServerOverlapEnemies(mid, Mathf.Sqrt(segLenSq) * 0.5f + RopeHalfWidth, augOverlap);
            Vector2 dir = seg.sqrMagnitude > 0.0001f ? new Vector2(-seg.y, seg.x).normalized : Vector2.up;
            augChain = true;
            int previousTag = attack.NextHitTag;
            attack.NextHitTag = TagRope;
            for (int i = 0; i < n; i++)
            {
                Collider2D col = augOverlap[i];
                if (!Boss.DeathRealm.CanInteract(gameObject, col.gameObject))
                {
                    continue;
                }
                Vector2 p = col.transform.position;
                float t = Mathf.Clamp01(Vector2.Dot(p - from, seg) / segLenSq);
                if ((from + seg * t - p).sqrMagnitude > RopeHalfWidth * RopeHalfWidth)
                {
                    continue;
                }
                // 피해 숫자는 0.25초마다라 너무 많아서 띄우지 않는다(feedback null).
                attack.ServerDamageTarget(col, damageScale, dir, 0f, false, null);
                if (slow && col.TryGetComponent(out Enemy enemy) && !enemy.IsDying)
                {
                    enemy.ServerSlow(0.30f, RopeTickSeconds + 0.1f); // 금줄 치기
                }
            }
            attack.NextHitTag = previousTag;
            augChain = false;
        }

        // ───────── 처치 · 회복 · 호신부 ─────────

        private void RecordPartyKill()
        {
            partyKillTimes[partyKillHead] = augClock;
            partyKillHead = (partyKillHead + 1) % partyKillTimes.Length;
            int max = PlayerProgress.Catalog.maxLevel;
            if (!CoopOn || (Lv(AugmentId.CoopCheer) < max && TeammateLv(AugmentId.CoopCheer) < max))
            {
                return;
            }
            // 가장 오래된 기록(= 30번째 전 처치)이 10초 안이면 굿판.
            float oldest = partyKillTimes[partyKillHead];
            if (augClock - oldest <= 10f)
            {
                gutpanUntil = augClock + 5f;
                for (int i = 0; i < partyKillTimes.Length; i++)
                {
                    partyKillTimes[i] = -999f; // 다시 30마리부터
                }
            }
        }

        /// <summary>동료 키트가 부른다 - 동료가 원귀를 처치했다(얼쑤 · 굿판).</summary>
        private void OnTeammateKill()
        {
            if (Lv(AugmentId.CoopCheer) > 0 && CoopOn)
            {
                int oldest = 0;
                for (int i = 1; i < cheerUntil.Length; i++)
                {
                    if (cheerUntil[i] < cheerUntil[oldest])
                    {
                        oldest = i;
                    }
                }
                cheerUntil[oldest] = augClock + 3f;
            }
            RecordPartyKill();
        }

        private void HandleServerHealed(float amount)
        {
            // 한솥밥: 내가 회복한 양의 n%를 동료도 회복.
            float share = CoopOn ? CoopValue(AugmentId.CoopMeal) : 0f;
            if (share > 0f)
            {
                teammate.ServerHealShared(amount * share);
            }
        }

        private void AugmentsOnShieldBlocked()
        {
            if (Max(AugmentId.Armor) && attack != null)
            {
                attack.ServerHitAround(2f, 0.5f, SojiPushCells); // 소지
            }
        }

        /// <summary>
        /// 동료 키트가 부른다 - 동료가 쓰러질 공격을 내 호신부 1회로 대신 막을 수 있으면 막고 true(등 맡기기 5단계 액땜, 웨이브당 1회).
        /// </summary>
        private bool TryAegisForTeammate()
        {
            if (aegisUsedThisWave || !Max(AugmentId.CoopBack) || !Alive || shieldCharges.Value == 0)
            {
                return false;
            }
            aegisUsedThisWave = true;
            shieldCharges.Value--;
            return true;
        }

        private int AugmentShieldBonus()
        {
            AugmentModifiers mods = Mods;
            return mods.ShieldFillToMax ? ShieldMax : mods.ShieldBonus;
        }

        private float ShieldRechargeNow()
        {
            float custom = Mods.ShieldRechargeSeconds;
            return custom > 0f ? custom : ShieldRechargeSeconds;
        }

        // ───────── 일회용(PlayerProgress가 부른다) ─────────

        /// <summary>서버 전용 - 일회용 신통력의 지속 효과 · 호신부 충전.</summary>
        public void ServerApplyOneTime(AugmentId id)
        {
            if (!IsServer)
            {
                return;
            }
            switch (id)
            {
                case AugmentId.Cheongsim:
                    if (health != null)
                    {
                        health.ServerInvulnerableUntil = Mathf.Max(health.ServerInvulnerableUntil, Time.time + 3f + PauseAllowance());
                    }
                    break;
                case AugmentId.HerbalTea:
                    herbalSlowUntil = augClock + 5f;
                    break;
                case AugmentId.HoneyCake:
                    honeyUntil = augClock + 10f;
                    break;
                case AugmentId.Makgeolli:
                    drunkUntil = augClock + 20f;
                    break;
                case AugmentId.StrawShoes:
                    strawWave = InCombat ? CurrentWave : CurrentWave + 1;
                    break;
                case AugmentId.Talisman:
                {
                    int max = ShieldMaxCount();
                    if (shieldCharges.Value >= max)
                    {
                        talismanExtra++; // 최대치를 넘으면 이번 웨이브 동안만 유지
                    }
                    shieldCharges.Value = (byte)Mathf.Min(255, shieldCharges.Value + 1);
                    break;
                }
                case AugmentId.TalismanBundle:
                    shieldCharges.Value = (byte)Mathf.Max(shieldCharges.Value, ShieldMaxCount());
                    weakUntil = augClock + 10f;
                    break;
            }
        }

        /// <summary>청심환은 고르는 화면(일시정지) 동안 무적 시간이 깎이지 않게 남은 선택 시간만큼 더 준다.</summary>
        private static float PauseAllowance() =>
            PartyProgress.Instance != null && PartyProgress.Instance.IsPaused ? PlayerProgress.Catalog.choiceSeconds : 0f;

        // ───────── 금줄 그림(모든 화면) ─────────

        private void UpdateRopeVisual()
        {
            bool show = ropeShown.Value && IsSpawned;
            PlayerHealth mate = show ? PlayerCharacter.FindTeammate(gameObject) : null;
            if (mate == null)
            {
                if (ropeLine != null && ropeLine.enabled)
                {
                    ropeLine.enabled = false;
                }
                return;
            }
            if (ropeLine == null)
            {
                var go = new GameObject("GeumjulRope");
                go.transform.SetParent(transform.parent, false);
                ropeLine = go.AddComponent<LineRenderer>();
                ropeLine.useWorldSpace = true;
                ropeLine.positionCount = 2;
                ropeLine.widthMultiplier = 0.09f;
                ropeLine.numCapVertices = 2;
                ropeLine.textureMode = LineTextureMode.Stretch;
                ropeLine.sortingOrder = 18;
                ropeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ropeLine.receiveShadows = false;
                if (TryGetComponent(out SpriteRenderer sr) && sr.sharedMaterial != null)
                {
                    ropeLine.sharedMaterial = sr.sharedMaterial; // 캐릭터와 같은 스프라이트 재질(조명 · 셰이더 일치)
                }
                ropeLine.startColor = new Color(0.96f, 0.80f, 0.36f, 0.95f);
                ropeLine.endColor = new Color(0.96f, 0.80f, 0.36f, 0.95f);
            }
            ropeLine.enabled = true;
            ropeLine.SetPosition(0, transform.position + (Vector3)RopeBodyOffset);
            ropeLine.SetPosition(1, mate.transform.position + (Vector3)RopeBodyOffset);
        }

        private void DestroyAugmentFx()
        {
            if (ropeLine != null)
            {
                Destroy(ropeLine.gameObject);
                ropeLine = null;
            }
        }
    }
}
