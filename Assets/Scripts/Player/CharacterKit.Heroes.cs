using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 261114 - 캐릭터별 효과(기획서 v0.9.7 6-1 ~ 6-8). 수치는 기획서 표 그대로, 기획서에 없는 지속 시간은 주석에 (제안)으로 표시.
    /// </summary>
    public partial class CharacterKit
    {
        private const string Hayoung = "hayoung";
        private const string Juwon = "hanjuwon";
        private const string Park = "parkgyuhyeok";
        private const string Ara = "leeara";
        private const string Hwanjo = "choihwanjo";
        private const string Jihyun = "sinjihyeon";
        private const string Hun = "imhun";
        private const string Yeore = "simyeore";

        private const int TagAwaken = 1;
        private const int TagGeomgi = 2;
        private const int TagTornado = 3;
        private const int TagDangpaLeft = 4;
        private const int TagDangpaRight = 5;
        private const int TagCollision = 6;
        private const int TagManwol = 7;
        private const int TagCharge = 8;

        // 공통 각성 쿨타임(판 시작 후 한 번 다 차야 첫 발동).
        private float awakenTimer;

        // 하영
        private float perfectSince;
        private bool perfectGraceUsed;
        private bool perfectWasActive;
        private int geomgiCount;
        private float allyHitSpeedUntil;

        // 박규혁
        private float generalUntil;
        private float generalTauntTimer;
        private float vanguardTauntTimer;
        private static bool redirectingDamage;
        private float commandUntil;
        private int dangpaMask;
        private Collider2D dangpaCenter;
        private Collider2D dangpaLeft;
        private Collider2D dangpaRight;

        // 이아라
        private bool fortuneGood;
        private int forcedCrits;
        private bool shieldCritReady;

        // 신지현
        private int manwolCount;
        private float manwolSecondAt = -1f;
        private float loyaltyUntil;
        private float eonwolAllyUntil;

        // 임훈
        private bool allyDownThisWave;
        private float horsebackMovingSince = -1f;
        private bool charging;
        private float chargeStart;
        private float chargeCooldownUntil;
        private float chargeEndAt = -1f;
        private Vector2 chargeFrom;
        private Vector2 chargeDir;
        private float chargeDistance;
        private float chargeRatio;
        private float chargeAllyBuffUntil;

        // 심여레
        private int windCount;
        private bool angerWasActive;
        private float angerHealReadyAt;

        private struct Tornado
        {
            public bool Alive;
            public Vector2 Position;
            public float Until;
            public float Radius;
            public float Tick;
            public float DamageScale;
        }

        private readonly Tornado[] tornadoes = new Tornado[4];

        private void ResetHeroState()
        {
            awakenTimer = 0f;
            perfectSince = Time.time;
            perfectGraceUsed = false;
            perfectWasActive = false;
            geomgiCount = 0;
            allyHitSpeedUntil = 0f;
            generalUntil = 0f;
            commandUntil = 0f;
            fortuneGood = false;
            forcedCrits = 0;
            shieldCritReady = false;
            manwolCount = 0;
            manwolSecondAt = -1f;
            loyaltyUntil = 0f;
            eonwolAllyUntil = 0f;
            allyDownThisWave = false;
            horsebackMovingSince = -1f;
            charging = false;
            chargeEndAt = -1f;
            chargeCooldownUntil = 0f;
            chargeAllyBuffUntil = 0f;
            windCount = 0;
            angerWasActive = false;
            angerHealReadyAt = 0f;
            for (int i = 0; i < tornadoes.Length; i++)
            {
                tornadoes[i].Alive = false;
            }
        }

        private void ExclusivePair(out AugmentId a, out AugmentId b)
        {
            switch (heroId)
            {
                case Hayoung: a = AugmentId.HayoungGeomgi; b = AugmentId.HayoungAsPlanned; return;
                case Juwon: a = AugmentId.JuwonByeoksabu; b = AugmentId.JuwonGyeolbakbu; return;
                case Park: a = AugmentId.ParkDangpa; b = AugmentId.ParkJeokjang; return;
                case Ara: a = AugmentId.AraReturningKnife; b = AugmentId.AraChilseong; return;
                case Hwanjo: a = AugmentId.HwanjoCheoljeon; b = AugmentId.HwanjoGakgung; return;
                case Jihyun: a = AugmentId.JihyunBaesujin; b = AugmentId.JihyunManwol; return;
                case Hun: a = AugmentId.HunCharge; b = AugmentId.HunHorseback; return;
                case Yeore: a = AugmentId.YeoreWhirl; b = AugmentId.YeoreTemper; return;
                default: a = AugmentId.None; b = AugmentId.None; return;
            }
        }

        // ───────── 신내림 갈래 수치표 ─────────

        private void NodeTable(out NodeValues v)
        {
            v = default;
            switch (heroId)
            {
                case Hayoung:
                    v.BaseAlly.MaxHealth = 0.05f; v.VanguardHealth = 0.10f; v.VanguardHitAlly.Attack = 0.03f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.DamageTaken = -0.05f;
                    v.Guard2Self.Regen = 0.002f; v.Guard2Ally.Regen = 0.0015f; v.Guard2AllyAlways = true;
                    v.Raid1Self.MoveSpeed = 0.10f; v.Raid1Ally.MoveSpeed = 0.10f; v.Raid2Self.AttackSpeed = 0.08f;
                    break;
                case Juwon:
                    v.BaseAlly.ProjectileSpeed = 0.10f; v.VanguardHealth = 0.10f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.Attack = 0.05f;
                    v.Guard2Self.AttackSpeed = 0.08f;
                    v.Raid1Self.Range = 1f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.Attack = 0.10f;
                    break;
                case Park:
                    v.BaseAlly.MaxHealth = 0.05f; v.VanguardHealth = 0.15f; v.VanguardHitAlly.Attack = 0.03f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.MaxHealth = 0.08f;
                    v.Guard2Self.Regen = 0.002f; v.Guard2Ally.Regen = 0.0015f; v.Guard2AllyAlways = true;
                    v.Raid1Self.MoveSpeed = 0.12f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.BossDamage = 0.10f;
                    break;
                case Ara:
                    v.BaseAlly.Crit = 0.02f; v.VanguardHealth = 0.15f; v.VanguardHitAlly.Crit = 0.03f;
                    v.Guard1Self.Crit = 0.04f; v.Guard1Ally.Crit = 0.05f;
                    v.Guard2Self.CritDamage = 0.1f; v.Guard2Ally.CritDamage = 0.1f; v.Guard2AllyAlways = true;
                    v.Raid1Self.MoveSpeed = 0.10f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.CritDamage = 0.2f;
                    break;
                case Hwanjo:
                    v.BaseAlly.ProjectileSpeed = 0.10f; v.VanguardHealth = 0.15f; v.VanguardHitAlly.MoveSpeed = 0.10f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.MoveSpeed = 0.05f;
                    v.Guard2Self.AttackSpeed = 0.08f;
                    v.Raid1Self.Range = 2f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.Attack = 0.10f;
                    break;
                case Jihyun:
                    v.BaseAlly.AttackSpeed = 0.03f; v.VanguardHealth = 0.10f; v.VanguardHitAlly.AttackSpeed = 0.05f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.AttackSpeed = 0.05f;
                    v.Guard2Self.Regen = 0.002f; v.Guard2Ally.Regen = 0.0015f; v.Guard2AllyAlways = true;
                    v.Raid1Self.Area = 0.10f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.AttackSpeed = 0.08f;
                    break;
                case Hun:
                    v.BaseAlly.Range = 0.2f; v.VanguardHealth = 0.10f; v.VanguardHitAlly.Attack = 0.03f;
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.RangeRatio = 0.08f;
                    v.Guard2Ally.Regen = 0.0015f; v.Guard2AllyAlways = true; // 나: 동료 쪽으로 갈 때 이속(ComputeHero)
                    v.Raid1Self.MoveSpeed = 0.10f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.Pierce = 1;
                    break;
                case Yeore:
                    v.BaseAlly.MaxHealth = 0.05f; v.VanguardHealth = 0.15f; // 맞으면 동료 체력 1% 회복(OnDamagedBeforeRecord)
                    v.Guard1Self.Attack = 0.08f; v.Guard1Ally.Regen = 0.002f;
                    v.Guard2Self.Regen = 0.002f; v.Guard2Ally.Regen = 0.0015f; v.Guard2AllyAlways = true;
                    v.Raid1Self.KnockbackScale = 0.20f; v.Raid1Ally.MoveSpeed = 0.08f; v.Raid2Self.AttackSpeed = 0.08f;
                    break;
            }
        }

        // ───────── 매 프레임 효과(고유 패시브 · 전용 신통력 · 각성 상태) ─────────

        private void ComputeHero(ref Bonus me, ref Bonus ally, ref bool active)
        {
            switch (heroId)
            {
                case Hayoung:
                {
                    // 완벽주의: 10초(원칙대로 8초) 동안 안 맞으면 공격력 +10%(계획대로 14~30%).
                    float need = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) ? 8f : 10f;
                    bool on = Time.time - perfectSince >= need;
                    if (on && !perfectWasActive)
                    {
                        perfectGraceUsed = false;
                    }
                    perfectWasActive = on;
                    if (on)
                    {
                        int lv = Lv(AugmentId.HayoungAsPlanned);
                        me.Attack += lv > 0 ? Val(AugmentId.HayoungAsPlanned, lv) / 100f : 0.10f;
                        active = true;
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA))
                    {
                        me.Crit += 0.05f;
                        ally.Crit += 0.02f;
                    }
                    if (Time.time < allyHitSpeedUntil)
                    {
                        me.MoveSpeed += 0.15f; // 원칙대로(동료): 동료 피격 시 내 이속 +15% (3초, 제안)
                    }
                    break;
                }
                case Juwon:
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA))
                    {
                        me.Attack -= 0.10f; // 겹부적
                    }
                    me.Pierce += Mathf.RoundToInt(Val(AugmentId.JuwonByeoksabu, Lv(AugmentId.JuwonByeoksabu)));
                    break;
                case Park:
                {
                    float threshold = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) ? 0.4f : 0.3f;
                    if (teammate != null && !teammate.IsDeadServerSafe && AllyHealthRatio <= threshold)
                    {
                        me.DamageTaken -= 0.20f;
                        if (MovingToward(teammate.transform.position))
                        {
                            me.MoveSpeed += 0.15f;
                        }
                        if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA))
                        {
                            ally.Attack += 0.05f;
                        }
                        active = true;
                    }
                    me.BossDamage += Val(AugmentId.ParkJeokjang, Lv(AugmentId.ParkJeokjang)) / 100f;
                    if (Time.time < commandUntil)
                    {
                        ally.BossDamage += 0.10f; // 지휘
                    }
                    if (Time.time < generalUntil)
                    {
                        me.Attack += 0.30f;
                        ally.DamageTaken -= 0.20f;
                        active = true;
                    }
                    break;
                }
                case Ara:
                {
                    bool goodTeam = fortuneGood && Lv(AugmentId.AraChilseong) >= 5;
                    if (fortuneGood)
                    {
                        me.Crit += 0.10f;
                        if (goodTeam)
                        {
                            ally.Crit += 0.10f; // 대길
                        }
                    }
                    else if (lastWave > 0)
                    {
                        if (!Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA))
                        {
                            me.DamageTaken += 0.10f;
                        }
                        ally.Crit += 0.10f;
                    }
                    active = fortuneGood;
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && AllyNear)
                    {
                        ally.Shield += 1; // 액막이(동료)
                    }
                    if (forcedCrits > 0 && AllyNear)
                    {
                        ally.Crit += 0.10f; // 북두칠성 발동 중
                    }
                    break;
                }
                case Hwanjo:
                {
                    int lv = Lv(AugmentId.HwanjoCheoljeon);
                    me.Pierce += Mathf.RoundToInt(Val(AugmentId.HwanjoCheoljeon, lv));
                    me.Range += Val(AugmentId.HwanjoGakgung, Lv(AugmentId.HwanjoGakgung));
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB))
                    {
                        me.AttackSpeed += 0.10f;
                        me.Attack -= 0.05f;
                        if (AllyNear)
                        {
                            ally.AttackSpeed += 0.03f;
                        }
                    }
                    break;
                }
                case Jihyun:
                {
                    int lv = Lv(AugmentId.JihyunBaesujin);
                    float per = lv > 0 ? Val(AugmentId.JihyunBaesujin, lv) / 100f : 0.02f;
                    float max = lv > 0 ? 0.20f + 0.02f * lv : 0.20f;
                    int near = CountEnemiesAround(transform.position, 4f);
                    float reduce = Mathf.Min(max, near * per);
                    me.DamageTaken -= reduce;
                    active = near > 0;
                    if (lv >= 5 && teammate != null && teammateDistance <= 4f)
                    {
                        ally.DamageTaken -= reduce * 0.5f; // 한 번 한 약속
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA))
                    {
                        if (near >= 5)
                        {
                            me.Attack += 0.15f;
                        }
                        if (AllyNear)
                        {
                            ally.Attack += 0.05f;
                        }
                    }
                    if (Time.time < loyaltyUntil)
                    {
                        me.AttackSpeed += 0.15f; // 의리
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB))
                    {
                        ally.DamageTaken -= 0.10f; // 의리(동료): 동료 받는 피해 10%를 내가 대신(OnTeammateDamaged에서 나에게 줌)
                    }
                    if (Time.time < eonwolAllyUntil)
                    {
                        ally.DamageTaken -= 0.20f;
                    }
                    break;
                }
                case Hun:
                {
                    if (teammateKit != null && Time.time - teammateKit.LastDamagedTime < 3f)
                    {
                        me.MoveSpeed += 0.20f;
                        active = true;
                    }
                    if (teammate != null && teammate.IsDeadServerSafe && InCombat)
                    {
                        allyDownThisWave = true;
                    }
                    if (allyDownThisWave)
                    {
                        me.Attack += 0.20f;
                        active = true;
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.Guard2) && teammate != null && MovingToward(teammate.transform.position))
                    {
                        me.MoveSpeed += 0.15f;
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && AllyNear)
                    {
                        me.DamageTaken -= 0.08f;
                        ally.DamageTaken -= 0.04f;
                    }
                    bool moving = movement != null && movement.MoveInput.sqrMagnitude > 0.01f;
                    int horse = Lv(AugmentId.HunHorseback);
                    if (moving)
                    {
                        if (horsebackMovingSince < 0f)
                        {
                            horsebackMovingSince = Time.time;
                        }
                        me.AttackSpeed += Val(AugmentId.HunHorseback, horse) / 100f;
                        if (horse >= 5 && Time.time - horsebackMovingSince > 3f)
                        {
                            me.MoveSpeed += 0.15f; // 질주
                        }
                    }
                    else
                    {
                        horsebackMovingSince = -1f;
                    }
                    if (charging)
                    {
                        me.MoveSpeed -= 0.50f; // 정신집중
                    }
                    if (Time.time < chargeAllyBuffUntil)
                    {
                        ally.MoveSpeed += 0.15f; // 단련된 집중(동료)
                    }
                    break;
                }
                case Yeore:
                {
                    bool angry = teammate != null && !teammate.IsDeadServerSafe && AllyHealthRatio <= 0.5f;
                    if (angry)
                    {
                        int lv = Lv(AugmentId.YeoreTemper);
                        me.Attack += lv > 0 ? Val(AugmentId.YeoreTemper, lv) / 100f : 0.20f;
                        me.KnockbackScale += 0.30f;
                        active = true;
                        if (!angerWasActive && lv >= 5 && Time.time >= angerHealReadyAt)
                        {
                            angerHealReadyAt = Time.time + 30f;
                            teammate.ServerHealByRatio(0.15f); // 밥은 먹고 싸워야지
                        }
                    }
                    angerWasActive = angry;
                    break;
                }
            }
        }

        private bool MovingToward(Vector3 target)
        {
            if (movement == null)
            {
                return false;
            }
            Vector2 move = movement.MoveInput;
            if (move.sqrMagnitude < 0.01f)
            {
                return false;
            }
            Vector2 to = (Vector2)target - (Vector2)transform.position;
            return to.sqrMagnitude > 0.25f && Vector2.Dot(move.normalized, to.normalized) > 0.5f;
        }

        /// <summary>261122 - 검기 베기가 날아가는 거리(칸). 기본 공격 인식 범위도 여기에 맞춘다.</summary>
        private const float GeomgiRange = 8f;
        private const float GeomgiPasaRange = 20f;

        /// <summary>
        /// 261122 - 기본 공격 인식 반지름 하한(0 = 없음). 하영이 검기를 배우면 베기가 8칸 날아가므로 근접 사거리가 아니라
        /// 8칸 안에 적이 있으면 휘두른다(예섭 요청). 파사(5번째 20칸)는 가끔 나가는 한 발이라 인식은 8칸 기준.
        /// </summary>
        public float AttackDetectRange => heroId == Hayoung && Lv(AugmentId.HayoungGeomgi) > 0 ? GeomgiRange : 0f;

        private float HeroMaxHealthBonus() => heroId == Hayoung && Lv(AugmentId.HayoungGeomgi) > 0 ? -0.30f : 0f;

        private float HeroProjectileSpeedBonus() =>
            heroId == Hwanjo ? Val(AugmentId.HwanjoGakgung, Lv(AugmentId.HwanjoGakgung)) * 0.10f : 0f;

        private int HeroExtraTargets() => heroId == Juwon && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) ? 1 : 0;

        private Transform HeroTargetBias()
        {
            // 한주원 수호 2(동료) · 겹부적(동료): 동료 주변 원귀를 먼저 지정.
            if (heroId == Juwon && teammate != null && !teammate.IsDeadServerSafe
                && (Has(CharacterCatalogSO.SinnaerimSlot.Guard2) || Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA)))
            {
                return teammate.transform;
            }
            return null;
        }

        private int HeroExtraShield() => heroId == Ara && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) ? 1 : 0;

        private float HealingScaleNow()
        {
            if (heroId == Yeore && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && health != null && health.MaxHealth > 0f
                && health.CurrentHealth / health.MaxHealth <= 0.3f)
            {
                return 2f; // 씻김
            }
            return 1f;
        }

        // ───────── 사건 ─────────

        private void OnWaveStart()
        {
            allyDownThisWave = false;
            if (heroId == Ara)
            {
                // 오늘의 운세: 웨이브 시작마다 점괘(길 50%, 칠성의 점괘 58~90%).
                int lv = Lv(AugmentId.AraChilseong);
                float chance = lv > 0 ? Val(AugmentId.AraChilseong, lv) / 100f : 0.5f;
                fortuneGood = Random.value < chance;
                FxRpc(fortuneGood ? (byte)FxKind.FortuneGood : (byte)FxKind.FortuneBad, transform.position, 1.2f, 1f);
            }
        }

        private void OnShieldBlocked()
        {
            if (heroId == Ara && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB))
            {
                shieldCritReady = true; // 액막이: 호신부로 막으면 다음 공격 치명 1회 확정
            }
        }

        private void OnDamagedBeforeRecord(float amount)
        {
            if (heroId == Hayoung)
            {
                // 완벽주의: 맞으면 초기화(계획대로 5단계: 켜져 있을 때 첫 피격 1회는 유지).
                bool on = perfectWasActive;
                if (on && Lv(AugmentId.HayoungAsPlanned) >= 5 && !perfectGraceUsed)
                {
                    perfectGraceUsed = true;
                }
                else
                {
                    perfectSince = Time.time;
                }
            }
            if (heroId == Yeore && Has(CharacterCatalogSO.SinnaerimSlot.Vanguard2) && teammate != null)
            {
                teammate.ServerHealByRatio(0.01f); // 바람막이 2(동료)
            }
            // 동료 쪽 반응(하영 원칙대로 · 신지현 의리) - 내가 맞았다는 걸 동료 키트에 알린다.
            if (teammateKit != null)
            {
                teammateKit.OnTeammateDamaged(amount);
            }
        }

        private void OnTeammateDamaged(float amount)
        {
            if (redirectingDamage)
            {
                return; // 대신 받은 피해가 다시 동료에게 돌아가지 않게
            }
            if (heroId == Hayoung && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB))
            {
                allyHitSpeedUntil = Time.time + 3f;
            }
            if (heroId == Jihyun && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB))
            {
                loyaltyUntil = Time.time + 2f;
                // 동료가 받는 피해 10%를 내가 대신 받음(동료 쪽은 AllyAura -10%로 이미 줄었다).
                if (health != null && amount > 0f)
                {
                    redirectingDamage = true;
                    health.TakeDamage(amount * 0.10f / 0.90f);
                    redirectingDamage = false;
                }
            }
        }

        // ───────── 피해 계산(캐릭터별) ─────────

        private void HeroModifyOutgoing(ref OutgoingHit hit)
        {
            switch (heroId)
            {
                case Ara:
                    if (forcedCrits > 0 || shieldCritReady)
                    {
                        hit.ForceCrit = true;
                    }
                    break;
                case Hwanjo:
                {
                    // 바람을 읽다: 8칸(바람결 6칸) 넘게 떨어진 원귀 +15%, 각궁 5단계: 12칸 넘으면 +30%.
                    float limit = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) ? 6f : 8f;
                    bool far = hit.Distance > limit;
                    bool nearAlly = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) && teammate != null
                                    && Vector2.Distance(teammate.transform.position, hit.Target.transform.position) <= 3f;
                    if (far || nearAlly)
                    {
                        float bonus = Lv(AugmentId.HwanjoGakgung) >= 5 && hit.Distance > 12f ? 0.30f : 0.15f;
                        hit.Damage *= 1f + bonus;
                    }
                    if (Lv(AugmentId.HwanjoCheoljeon) >= 5 && hit.HitIndex > 0)
                    {
                        hit.Damage *= 1f + 0.10f * hit.HitIndex; // 편전
                    }
                    break;
                }
                case Park:
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && !hit.IsProjectile)
                    {
                        hit.Knockback = Mathf.Max(hit.Knockback, 1f); // 밀어내기
                    }
                    break;
            }
        }

        private void HeroOnHitLanded(in OutgoingHit hit, bool killed)
        {
            Enemy enemy = hit.Enemy;
            switch (heroId)
            {
                case Juwon:
                    if (enemy != null && !killed)
                    {
                        float seconds = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) ? 7f : 5f;
                        if (hit.HitIndex == 0 || Lv(AugmentId.JuwonByeoksabu) >= 5 || hit.Tag == TagAwaken)
                        {
                            enemy.ServerBrand(gameObject, seconds); // 주사 낙인
                        }
                        int bind = Lv(AugmentId.JuwonGyeolbakbu);
                        if (bind > 0)
                        {
                            enemy.ServerSlow(Val(AugmentId.JuwonGyeolbakbu, bind) / 100f, 1f);
                            if (bind >= 5 && ++enemy.ServerBindCount % 3 == 0)
                            {
                                enemy.ServerStun(1f); // 발목 잡기
                            }
                        }
                        if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1))
                        {
                            enemy.ServerTaunt(transform, 2f);
                        }
                        if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard2))
                        {
                            enemy.ServerMarkVulnerable(gameObject, 0.05f, 2f); // (2초, 제안)
                        }
                    }
                    break;
                case Park:
                    if (hit.Tag == 0 || hit.Tag == TagDangpaLeft || hit.Tag == TagDangpaRight)
                    {
                        RecordDangpa(hit.Tag, hit.Target);
                    }
                    if (Lv(AugmentId.ParkJeokjang) >= 5 && IsBoss(hit.Target))
                    {
                        commandUntil = Time.time + 5f; // 지휘
                    }
                    if (enemy != null && !killed && Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && hit.Knockback > 0f)
                    {
                        enemy.ServerMarkVulnerable(gameObject, 0.10f, 2f);
                    }
                    break;
                case Ara:
                    if (hit.Crit && forcedCrits > 0)
                    {
                        forcedCrits--;
                    }
                    shieldCritReady = false;
                    if (enemy != null && !killed && Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1))
                    {
                        enemy.ServerTaunt(transform, 2f);
                    }
                    break;
                case Hwanjo:
                    if (enemy != null && !killed)
                    {
                        if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1))
                        {
                            enemy.ServerTaunt(transform, 2f);
                        }
                        if (hit.Tag == TagAwaken)
                        {
                            enemy.ServerMarkVulnerable(gameObject, 0.15f, 3f); // 십 리 화살(동료)
                        }
                    }
                    break;
                case Yeore:
                    if (enemy != null && !killed)
                    {
                        if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1))
                        {
                            enemy.ServerTaunt(transform, 2f);
                        }
                        if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) && hit.Tag != TagCollision && hit.Knockback > 0f)
                        {
                            CollisionHits(enemy, hit.Direction);
                        }
                    }
                    if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceB) && teammate != null && !teammate.IsDeadServerSafe
                        && AllyHealthRatio <= 0.3f)
                    {
                        teammate.ServerHealAmount(hit.FinalDamage * 0.03f); // 씻김(동료)
                    }
                    break;
            }
        }

        /// <summary>부딪히는 바람: 밀려난 원귀가 다른 원귀와 부딪히면 그 원귀에게 피해 50%(+ 동료 피해 +10%).</summary>
        private void CollisionHits(Enemy pushed, Vector2 direction)
        {
            Vector2 ahead = (Vector2)pushed.transform.position + direction.normalized * 0.6f;
            int n = attack.ServerOverlapEnemies(ahead, 0.6f, overlap);
            attack.NextHitTag = TagCollision;
            for (int i = 0; i < n; i++)
            {
                if (overlap[i].gameObject == pushed.gameObject || !overlap[i].TryGetComponent(out Enemy other) || other.IsDying)
                {
                    continue;
                }
                attack.ServerDamageTarget(overlap[i], 0.5f, direction, 0f, false, hitFeedback);
                other.ServerMarkVulnerable(gameObject, 0.10f, 2f);
            }
            attack.NextHitTag = 0;
        }

        // ───────── 공격 바꾸기 ─────────

        /// <summary>서버 - 기본 공격 타격 프레임. true면 원래 공격 대신 처리했다.</summary>
        public bool ServerTryOverrideAttack(Vector2 aimDir)
        {
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = AimDirection();
            }
            aimDir.Normalize();
            switch (heroId)
            {
                case Hayoung:
                {
                    int lv = Lv(AugmentId.HayoungGeomgi);
                    if (lv <= 0 || projectiles == null)
                    {
                        return false;
                    }
                    // 검기: 베기가 날아감(사거리 8칸, 관통 2·2·3·3·4마리), 5단계 파사: 5번째는 관통 제한 없이 끝까지.
                    geomgiCount++;
                    bool pasa = lv >= 5 && geomgiCount % 5 == 0;
                    int hits = lv >= 5 ? 4 : lv >= 3 ? 3 : 2;
                    var p = new PlayerProjectiles.ShotParams
                    {
                        Speed = 14f,
                        Range = pasa ? GeomgiPasaRange : GeomgiRange,
                        Pierce = pasa ? -1 : hits - 1,
                        DamageScale = Val(AugmentId.HayoungGeomgi, lv) / 100f,
                        Radius = 0.7f,
                        Scale = 0.9f,
                        UseAlt = true,
                        Tag = TagGeomgi,
                    };
                    projectiles.ServerSpawnShot(transform.position, aimDir, p, null);
                    return true;
                }
                case Park:
                    dangpaMask = 0;
                    dangpaCenter = dangpaLeft = dangpaRight = null;
                    return false;
            }
            return false;
        }

        /// <summary>서버 - 근접 기본 공격 판정 직후.</summary>
        public void ServerAfterMeleeAttack(Vector2 aimDir)
        {
            if (aimDir.sqrMagnitude < 0.0001f)
            {
                aimDir = AimDirection();
            }
            aimDir.Normalize();
            switch (heroId)
            {
                case Park:
                {
                    int lv = Lv(AugmentId.ParkDangpa);
                    if (lv <= 0)
                    {
                        return;
                    }
                    // 당파 찌르기: 가운데 + 양옆 갈래(값 = 옆 갈래 피해).
                    float side = Val(AugmentId.ParkDangpa, lv) / 100f;
                    attack.NextHitTag = TagDangpaLeft;
                    attack.ServerResolveMelee(Quaternion.Euler(0f, 0f, 22f) * aimDir, side);
                    attack.NextHitTag = TagDangpaRight;
                    attack.ServerResolveMelee(Quaternion.Euler(0f, 0f, -22f) * aimDir, side);
                    attack.NextHitTag = 0;
                    if (lv >= 5 && dangpaMask == 7 && dangpaCenter != null && dangpaCenter == dangpaLeft && dangpaCenter == dangpaRight
                        && IsBossOrElite(dangpaCenter))
                    {
                        attack.ServerDamageTarget(dangpaCenter, 0.5f, aimDir, 0f, false, hitFeedback); // 삼지일격
                        attack.ServerFlushHitFeedback(hitFeedback);
                    }
                    break;
                }
                case Jihyun:
                {
                    int lv = Lv(AugmentId.JihyunManwol);
                    if (lv <= 0)
                    {
                        return;
                    }
                    manwolCount++;
                    if (manwolCount % 5 == 0)
                    {
                        ManwolSpin(Val(AugmentId.JihyunManwol, lv) / 100f);
                        if (lv >= 5)
                        {
                            manwolSecondAt = Time.time + 0.35f; // 청룡 회오리: 두 바퀴
                        }
                    }
                    break;
                }
            }
        }

        private void ManwolSpin(float scale)
        {
            attack.NextHitTag = TagManwol;
            attack.ServerHitAround(attack.HitRadius, scale, 0f);
            attack.NextHitTag = 0;
            FxRpc((byte)FxKind.Spin, transform.position, 0.35f, attack.HitRadius);
        }

        private void RecordDangpa(int tag, Collider2D target)
        {
            if (tag == 0 && dangpaCenter == null)
            {
                dangpaCenter = target;
                dangpaMask |= 1;
            }
            else if (tag == TagDangpaLeft && target == dangpaCenter)
            {
                dangpaLeft = target;
                dangpaMask |= 2;
            }
            else if (tag == TagDangpaRight && target == dangpaCenter)
            {
                dangpaRight = target;
                dangpaMask |= 4;
            }
        }

        /// <summary>서버 - 원거리 기본 공격 방향(최환조 수호 2 동료 쪽 원귀 · 심여레 회오리 카운트).</summary>
        public Vector2 ServerAdjustAim(Vector2 aimDir)
        {
            switch (heroId)
            {
                case Hwanjo:
                    if (Has(CharacterCatalogSO.SinnaerimSlot.Guard2) && teammate != null && !teammate.IsDeadServerSafe)
                    {
                        // 엄호 사격 2(동료): 동료 주변(3칸) 원귀를 먼저 쏨 - 사거리 안이면.
                        int n = attack.ServerOverlapEnemies(teammate.transform.position, 3f, overlap);
                        float range = attack.HitRadius;
                        for (int i = 0; i < n; i++)
                        {
                            Vector2 to = (Vector2)overlap[i].transform.position - (Vector2)transform.position;
                            if (to.magnitude <= range && overlap[i].TryGetComponent(out Enemy e) && !e.IsDying)
                            {
                                return to;
                            }
                        }
                    }
                    break;
                case Yeore:
                {
                    int lv = Lv(AugmentId.YeoreWhirl);
                    if (lv > 0 && ++windCount % 4 == 0)
                    {
                        // 휘몰이: 4번째 바람마다 회오리(2초, 돌개바람 4초 · 1.5배).
                        Vector2 dir = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : AimDirection();
                        float dist = Mathf.Min(5f, Vector2.Distance(attack.AimPosition, transform.position));
                        SpawnTornado((Vector2)transform.position + dir * Mathf.Max(2f, dist), lv);
                    }
                    break;
                }
            }
            return aimDir;
        }

        private void SpawnTornado(Vector2 position, int lv)
        {
            float seconds = lv >= 5 ? 4f : 2f;
            float radius = lv >= 5 ? 2.25f : 1.5f;
            for (int i = 0; i < tornadoes.Length; i++)
            {
                if (tornadoes[i].Alive)
                {
                    continue;
                }
                tornadoes[i] = new Tornado
                {
                    Alive = true,
                    Position = position,
                    Until = Time.time + seconds,
                    Radius = radius,
                    Tick = 0f,
                    DamageScale = Val(AugmentId.YeoreWhirl, lv) / 100f,
                };
                FxRpc((byte)FxKind.Tornado, position, seconds, radius);
                return;
            }
        }

        // ───────── 서버 시간 진행(각성 · 회오리 · 돌진) ─────────

        private void TickHero(float dt)
        {
            // 각성: 판 시작 후 쿨타임이 한 번 다 차야 첫 발동, 줄이는 수단 없음(하영만 동료 6칸 안이면 24초).
            if (Has(CharacterCatalogSO.SinnaerimSlot.Awaken))
            {
                awakenTimer += dt;
                float cooldown = AwakenCooldown();
                if (awakenTimer >= cooldown && TryAwaken())
                {
                    awakenTimer = 0f;
                }
            }

            if (Time.time < generalUntil)
            {
                generalTauntTimer -= dt;
                if (generalTauntTimer <= 0f)
                {
                    generalTauntTimer = 0.5f;
                    TauntAround(4f, 1f);
                }
            }

            // 선봉 1 "내 주변 3칸 원귀는 나를 먼저 노림"(하영 · 박규혁 · 신지현 · 임훈).
            if (Has(CharacterCatalogSO.SinnaerimSlot.Vanguard1)
                && (heroId == Hayoung || heroId == Park || heroId == Jihyun || heroId == Hun))
            {
                vanguardTauntTimer -= dt;
                if (vanguardTauntTimer <= 0f)
                {
                    vanguardTauntTimer = 0.5f;
                    TauntAround(3f, 1f);
                }
            }

            if (manwolSecondAt > 0f && Time.time >= manwolSecondAt)
            {
                manwolSecondAt = -1f;
                ManwolSpin(Val(AugmentId.JihyunManwol, Lv(AugmentId.JihyunManwol)) / 100f);
            }

            TickTornadoes(dt);
            TickCharge();
        }

        private float AwakenCooldown()
        {
            switch (heroId)
            {
                case Hayoung: return AllyNear ? 24f : 30f;
                case Juwon: return 25f;
                case Ara: return 20f;
                case Jihyun: return 20f;
                case Hun: return 40f;
                default: return 30f;
            }
        }

        private bool TryAwaken()
        {
            Vector2 aim = AimDirection();
            switch (heroId)
            {
                case Hayoung:
                {
                    // 벽사의 일섬: 앞으로 3칸(검기 상태면 8칸) 뻗는 일섬, 피해 300%.
                    bool geomgi = Lv(AugmentId.HayoungGeomgi) > 0;
                    var p = new PlayerProjectiles.ShotParams
                    {
                        Speed = 30f, Range = geomgi ? 8f : 3f, Pierce = -1, DamageScale = 3f, Radius = 0.9f, Scale = 1.3f,
                        UseAlt = true, Tag = TagAwaken,
                    };
                    projectiles?.ServerSpawnShot(transform.position, aim, p, null);
                    return true;
                }
                case Juwon:
                {
                    // 법을 부르는 부적: 원귀 10마리에 부적(200%) + 주사 낙인, 동료 6칸 안이면 동료 주변부터.
                    if (projectiles == null)
                    {
                        return false;
                    }
                    Transform previousBias = projectiles.TargetBias;
                    projectiles.TargetBias = AllyNear ? teammate.transform : null;
                    int found = projectiles.FindNearestTargets(transform.position, 12f, 10);
                    projectiles.TargetBias = previousBias;
                    if (found == 0)
                    {
                        return false;
                    }
                    PlayerProjectiles.ShotParams p = projectiles.DefaultParams(attack.Stats);
                    p.DamageScale = 2f;
                    p.Pierce = 0;
                    p.Range = 14f;
                    p.Tag = TagAwaken;
                    for (int i = 0; i < found; i++)
                    {
                        Transform target = projectiles.TargetAt(i).transform;
                        projectiles.ServerSpawnShot(transform.position, (Vector2)target.position - (Vector2)transform.position, p, target);
                    }
                    return true;
                }
                case Park:
                    generalUntil = Time.time + 5f; // 장군신 강림
                    generalTauntTimer = 0f;
                    FxRpc((byte)FxKind.Pulse, transform.position, 0.5f, 4f);
                    return true;
                case Ara:
                    forcedCrits = 5; // 북두칠성
                    FxRpc((byte)FxKind.FortuneGood, transform.position, 1f, 1f);
                    return true;
                case Hwanjo:
                {
                    // 십 리 화살: 화면 끝까지 꿰뚫는 화살(400%, 관통 무제한).
                    if (projectiles == null)
                    {
                        return false;
                    }
                    PlayerProjectiles.ShotParams p = projectiles.DefaultParams(attack.Stats);
                    p.DamageScale = 4f;
                    p.Pierce = -1;
                    p.Range = 22f;
                    p.Speed *= 1.5f;
                    p.Scale *= 1.5f;
                    p.Tag = TagAwaken;
                    projectiles.ServerSpawnShot(transform.position, aim, p, null);
                    return true;
                }
                case Jihyun:
                {
                    // 언월 진격: 앞으로 4칸 밀고 나가며 벰(200%, 1칸 밀침), 동료 6칸 안이면 3초간 동료 받는 피해 -20%.
                    Vector2 from = transform.position;
                    movement?.ServerDash(aim, 4f, 0.3f);
                    HitAlongSegment(from, from + aim * 4f, 1.2f, 2f, 1f, TagAwaken);
                    if (AllyNear)
                    {
                        eonwolAllyUntil = Time.time + 3f;
                    }
                    FxRpc((byte)FxKind.Spin, from + aim * 2f, 0.3f, 2f);
                    return true;
                }
                case Hun:
                {
                    // 구원의 창: 동료 체력 30% 이하면 동료 옆으로 순간이동, 주변 3칸 밀쳐냄 · 동료 2초 무적(40초마다).
                    if (teammate == null || teammate.IsDeadServerSafe || AllyHealthRatio > 0.3f || movement == null)
                    {
                        return false;
                    }
                    Vector2 side = (Vector2)teammate.transform.position + Vector2.right * 0.8f;
                    movement.ServerTeleport(side);
                    attack.ServerHitAround(3f, 0f, 2f);
                    teammate.ServerInvulnerableUntil = Time.time + 2f;
                    FxRpc((byte)FxKind.Pulse, side, 0.5f, 3f);
                    return true;
                }
                case Yeore:
                    // 산신 강림: 주변 4칸 원귀를 크게 밀어냄, 동료가 어디 있든 둘 다 최대 체력 10% 회복.
                    attack.ServerHitAround(4f, 0f, 3f);
                    health?.ServerHealByRatio(0.10f);
                    if (teammate != null)
                    {
                        teammate.ServerHealByRatio(0.10f);
                    }
                    FxRpc((byte)FxKind.Pulse, transform.position, 0.6f, 4f);
                    return true;
            }
            return false;
        }

        private void TickTornadoes(float dt)
        {
            for (int i = 0; i < tornadoes.Length; i++)
            {
                if (!tornadoes[i].Alive)
                {
                    continue;
                }
                ref Tornado t = ref tornadoes[i];
                if (Time.time >= t.Until)
                {
                    t.Alive = false;
                    continue;
                }
                t.Tick -= dt;
                if (t.Tick > 0f)
                {
                    continue;
                }
                t.Tick = 0.25f;
                int n = attack.ServerOverlapEnemies(t.Position, t.Radius, overlap);
                attack.NextHitTag = TagTornado;
                for (int k = 0; k < n; k++)
                {
                    if (!overlap[k].TryGetComponent(out Enemy enemy) || enemy.IsDying)
                    {
                        continue;
                    }
                    Vector2 toCenter = t.Position - (Vector2)enemy.transform.position;
                    if (toCenter.sqrMagnitude > 0.04f)
                    {
                        enemy.ServerKnockback(toCenter, Mathf.Min(0.5f, toCenter.magnitude)); // 끌어당김
                    }
                    attack.ServerDamageTarget(overlap[k], t.DamageScale * 0.25f, -toCenter, 0f, false, hitFeedback); // 초당 피해 × 0.25초
                }
                attack.NextHitTag = 0;
                attack.ServerFlushHitFeedback(hitFeedback);
            }
        }

        // ───────── 임훈 철기 돌격(능동형, Space) ─────────

        private void OwnerInputUpdate()
        {
            if (heroId != Hun || Keyboard.current == null || progress == null || progress.LevelOf(AugmentId.HunCharge) <= 0)
            {
                return;
            }
            // 261116 - 설정에서 바꿀 수 있는 '기술' 키(KeyBindings, 기본 Space).
            if (CheonMyeong.Input.KeyBindings.WasPressed(CheonMyeong.Input.GameAction.Skill))
            {
                ChargeStartRpc();
            }
            else if (CheonMyeong.Input.KeyBindings.WasReleased(CheonMyeong.Input.GameAction.Skill))
            {
                ChargeReleaseRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void ChargeStartRpc()
        {
            if (heroId != Hun || Lv(AugmentId.HunCharge) <= 0 || Time.time < chargeCooldownUntil || !Alive || Paused)
            {
                return;
            }
            charging = true;
            chargeStart = Time.time;
        }

        [Rpc(SendTo.Server)]
        private void ChargeReleaseRpc()
        {
            if (!charging)
            {
                return;
            }
            charging = false;
            int lv = Lv(AugmentId.HunCharge);
            float maxCharge = Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) ? 1f : 1.5f;
            chargeRatio = Mathf.Clamp01((Time.time - chargeStart) / maxCharge);
            if (chargeRatio < 0.05f || movement == null)
            {
                return;
            }
            float maxDistance = 3f + 0.5f * (lv - 1);
            chargeDistance = maxDistance * chargeRatio;
            chargeDir = AimDirection();
            chargeFrom = transform.position;
            float seconds = 0.25f;
            movement.ServerDash(chargeDir, chargeDistance, seconds);
            chargeEndAt = Time.time + seconds;
            chargeCooldownUntil = Time.time + 8f; // 대기 8초(제안)
        }

        private void TickCharge()
        {
            if (chargeEndAt < 0f || Time.time < chargeEndAt)
            {
                return;
            }
            chargeEndAt = -1f;
            int lv = Lv(AugmentId.HunCharge);
            Vector2 to = transform.position;
            // 지나가는 원귀 모두 타격 - 값 = 최대 거리 기준 피해(거리 비례).
            HitAlongSegment(chargeFrom, to, 0.9f, Val(AugmentId.HunCharge, lv) / 100f * chargeRatio, 0f, TagCharge);
            if (lv >= 5 && chargeRatio >= 0.99f)
            {
                attack.ServerHitAround(1.5f, 0f, 1.5f); // 말발굽
            }
            if (Has(CharacterCatalogSO.SinnaerimSlot.ChoiceA) && teammate != null
                && Vector2.Distance(to, teammate.transform.position) <= 3f)
            {
                chargeAllyBuffUntil = Time.time + 3f;
            }
        }

        // ───────── 연출(전원) ─────────

        private enum FxKind : byte
        {
            Spin,
            Tornado,
            Pulse,
            FortuneGood,
            FortuneBad
        }

        private struct FxInstance
        {
            public bool Alive;
            public FxKind Kind;
            public float Time;
            public float Duration;
            public float Scale;
            public SpriteRenderer Renderer;
        }

        private readonly FxInstance[] fx = new FxInstance[4];

        [Rpc(SendTo.ClientsAndHost)]
        private void FxRpc(byte kind, Vector2 position, float seconds, float scale)
        {
            if (character == null || character.Entry == null)
            {
                return;
            }
            Sprite sprite = (FxKind)kind == FxKind.Tornado ? character.Entry.projectileSprite
                : character.Entry.effectFrames != null && character.Entry.effectFrames.Length > 0 ? character.Entry.effectFrames[0]
                : character.Entry.projectileSprite;
            if (sprite == null)
            {
                return;
            }
            int slot = 0;
            for (int i = 0; i < fx.Length; i++)
            {
                if (!fx[i].Alive)
                {
                    slot = i;
                    break;
                }
            }
            SpriteRenderer sr = fx[slot].Renderer;
            if (sr == null)
            {
                var go = new GameObject("CharacterFx");
                go.transform.SetParent(transform.parent, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 19;
            }
            sr.sprite = sprite;
            sr.enabled = true;
            sr.color = (FxKind)kind == FxKind.FortuneBad ? new Color(0.6f, 0.6f, 1f, 1f)
                : (FxKind)kind == FxKind.FortuneGood ? new Color(1f, 0.9f, 0.4f, 1f) : Color.white;
            sr.transform.position = position;
            float size = Mathf.Max(0.0001f, sprite.bounds.size.y);
            fx[slot] = new FxInstance
            {
                Alive = true,
                Kind = (FxKind)kind,
                Time = 0f,
                Duration = Mathf.Max(0.1f, seconds),
                Scale = scale * 2f / size,
                Renderer = sr,
            };
        }

        private void UpdateClientFx(float dt)
        {
            for (int i = 0; i < fx.Length; i++)
            {
                if (!fx[i].Alive)
                {
                    continue;
                }
                ref FxInstance f = ref fx[i];
                f.Time += dt;
                float u = f.Time / f.Duration;
                if (u >= 1f)
                {
                    f.Alive = false;
                    f.Renderer.enabled = false;
                    continue;
                }
                Transform t = f.Renderer.transform;
                switch (f.Kind)
                {
                    case FxKind.Spin:
                        t.rotation = Quaternion.Euler(0f, 0f, -720f * u);
                        t.localScale = Vector3.one * f.Scale * 0.5f;
                        break;
                    case FxKind.Tornado:
                        t.rotation = Quaternion.Euler(0f, 0f, -900f * f.Time);
                        t.localScale = Vector3.one * f.Scale * 0.5f;
                        break;
                    default:
                        t.rotation = Quaternion.identity;
                        t.localScale = Vector3.one * f.Scale * (0.3f + 0.7f * u) * 0.5f;
                        break;
                }
                Color c = f.Renderer.color;
                c.a = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
                f.Renderer.color = c;
            }
        }

        public override void OnDestroy()
        {
            DestroyAugmentFx();
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i].Renderer != null)
                {
                    Destroy(fx[i].Renderer.gameObject);
                }
            }
            base.OnDestroy();
        }
    }
}
