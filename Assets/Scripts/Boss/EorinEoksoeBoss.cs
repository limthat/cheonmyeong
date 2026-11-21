using System.Collections.Generic;
using CheonMyeong.Combat;
using CheonMyeong.Maps;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.UI.Screens;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>어린 억쇠 애니메이션 상태. 순서 = EORIN_EOKSOE-Sheet 줄 순서 = 기획서 v1.2 TAG 순서.</summary>
    public enum KidAnim : byte
    {
        Idle,
        Walk,
        Atk,
        Grab,
        Hold,
        Slam,
        Fly,
        Ride,
        Spin,
        Die,
    }

    /// <summary>
    /// 261121 추가 - 어린 억쇠(5웨이브 중간 보스, 기획서 v1.2 9-1). 억쇠에서 떨어져 나온 작은 도깨비 - 128px · 125ms · 빗자루.
    /// Gameplay 씬에 미리 놓인 NetworkObject(평소엔 숨김)이고 WaveManager가 5웨이브에 ServerBeginEncounter로 부른다.
    /// 75초(웨이브 시간) 안에 체력 0 → 웃으며 도망(격퇴), 못 하면 그대로 도망(실패 → 원한 +250). 결과는 억쇠와 같은
    /// EoksoeBoss.ServerEncounterEnded(Mid, 격퇴?)로 알린다.
    ///
    /// 스킬 4종(원한과 상관없이 고정): 빗자루 후려치기(부채꼴 1.5칸) · 배지기(작게) · 빗자루 비행(지그재그 4초, 먼지 줄) ·
    /// 빗자루 회오리(부채꼴 3개 직진). 모든 스킬은 바닥 붉은 예고(원 · 부채꼴 · 화살표)가 먼저 뜬다.
    /// 판단 · 이동 · 피해는 서버(FixedUpdate), 클라이언트는 NetworkVariable · Rpc로 그리기만 한다(억쇠와 같은 구조).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EorinEoksoeBoss : NetworkBehaviour, IDamageable
    {
        public static EorinEoksoeBoss Instance { get; private set; }

        private static readonly string[] AnimStateNames =
            { "IDLE", "WALK", "ATK", "GRAB", "HOLD", "SLAM", "FLY", "RIDE", "SPIN", "DIE" };
        private static readonly int[] AnimStateHashes = BuildHashes();

        private const float FrameSeconds = 0.125f; // 기획서 v1.2: 128px · 125ms
        private const float AtkHitFrameTime = 3 * FrameSeconds;    // 타격 18 (15~20 중 4번째)
        private const float GrabSeconds = 7 * FrameSeconds;
        private const float GrabCheckFrameTime = 4 * FrameSeconds; // 잡기 판정 25 (21~27 중 5번째)
        private const float SlamSeconds = 6 * FrameSeconds;
        private const float SlamHitSeconds = 3 * FrameSeconds;     // 놓기 35 (32~37 중 4번째)
        private const float FlyTakeoffSeconds = 6 * FrameSeconds;  // FLY 38~43
        private const float SpinSeconds = 6 * FrameSeconds;        // SPIN 45~50 한 바퀴(회오리 생성 45 = 첫 장)
        private const float DieSeconds = 11 * FrameSeconds;        // DIE(도망) 51~61
        private const float DustSpacing = 0.4f;
        private const int MaxTornadoes = 8;
        private static readonly Color BrownTint = new Color(0.62f, 0.42f, 0.26f, 1f);   // 갈색 이펙트(억쇠 SLAM_FX 대신)
        private static readonly Color BarColor = new Color(0.906f, 0.729f, 0.255f, 1f); // 황 #E7BA41 (기획서 v1.2 바 규칙)
        private static readonly Vector4 BarHolePixels = new Vector4(47f, 28f, 38f, 30f); // Bar-Sheet_12 빈 칸 여백

        [SerializeField] private KidBossStatsSO stats;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private SpriteRenderer shadowRenderer;
        [SerializeField] private Material telegraphMaterial;
        [Tooltip("EORIN_EOKSOE_FX-Sheet ATK_FX(갈색 초승달, 왼쪽을 향해 그려짐).")]
        [SerializeField] private Sprite[] slashFxFrames;
        [Tooltip("땅 터짐 - EORIN_EOKSOE_FX에 SLAM_FX 줄이 없어서 억쇠 Eoksoe_FX의 SLAM_FX를 갈색으로 칠해 쓴다.")]
        [SerializeField] private Sprite[] burstFxFrames;
        [Tooltip("회오리 그림 - SPIN 줄(몸 대신 흙먼지 토네이도).")]
        [SerializeField] private Sprite[] tornadoFrames;
        [Tooltip("체력 바 틀 - Bar-Sheet_12(머리 + 빗자루).")]
        [SerializeField] private Sprite barFrameSprite;
        [SerializeField] private Vector2 visualBaseOffset;

        // ---------- 복제 상태 ----------
        private readonly NetworkVariable<bool> visible = new NetworkVariable<bool>();
        private readonly NetworkVariable<float> health = new NetworkVariable<float>();
        private readonly NetworkVariable<float> maxHealth = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<byte> animState = new NetworkVariable<byte>();
        private readonly NetworkVariable<int> animSerial = new NetworkVariable<int>();
        private readonly NetworkVariable<float> animSpeed = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<bool> faceRight = new NetworkVariable<bool>();

        public bool IsEngaged => state != ServerState.Absent;
        public KidBossStatsSO Stats => stats;

        private enum ServerState
        {
            Absent,
            Appearing,
            Chasing,
            Attacking,
            Grabbing,
            Holding,
            Slamming,
            FlyTakeoff,
            Flying,
            FlyRest,
            TornadoWindup,
            Spinning,
            Leaving,
        }

        private struct DustPoint
        {
            public Vector2 Position;
            public float ExpireTime;
        }

        private struct Tornado
        {
            public Vector2 Origin;
            public Vector2 Direction;
            public float Elapsed;
            public int HitMask;
        }

        private Rigidbody2D rb;
        private ServerState state = ServerState.Absent;
        private float stateTime;
        private bool hitApplied;
        private bool defeated;
        private Vector2 actionDirection;
        private float basicCooldownLeft;
        private float grabCooldownLeft;
        private float flyCooldownLeft;
        private float tornadoCooldownLeft;
        private float hopClock;

        private PlayerHealth heldPlayer;
        private float holdAngle;
        private float holdTotalDegrees;
        private float holdTurned;
        private Vector2 throwFrom;
        private Vector2 throwTo;

        private int flyLeg;
        private int flyLegCount;
        private float flyLegSeconds;
        private float flySpeed;
        private Vector2 flyDirection;
        private Vector2 nextFlyDirection;
        private bool nextLegWarned;
        private int stage;
        private int flyHitMask;
        private Vector2 lastDustPosition;
        private readonly List<DustPoint> dust = new List<DustPoint>(80);
        private readonly List<Tornado> tornadoes = new List<Tornado>(MaxTornadoes);
        private Vector2 tornadoBaseDirection;

        // ---------- 클라이언트 연출 ----------
        private int playedAnimSerial = -1;
        private float currentAnimSpeed = 1f;
        private float hopPhase;
        private float squashTimer;
        private float flashTimer;
        private float hitStopTimer;
        private Vector2 lastVisualPosition;
        private bool hasLastVisualPosition;
        private float fadeAlpha;
        private BossHealthBarView healthBar;
        private Rect slashFxBounds;
        private Rect burstFxBounds;
        private bool fxBoundsReady;
        private Vector2 lastClientDustPosition;
        private bool clientDustStarted;

        private static int[] BuildHashes()
        {
            var hashes = new int[AnimStateNames.Length];
            for (int i = 0; i < hashes.Length; i++)
            {
                hashes[i] = Animator.StringToHash(AnimStateNames[i]);
            }
            return hashes;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            if (shadowRenderer != null && shadowRenderer.sprite == null)
            {
                shadowRenderer.sprite = EoksoeBoss.GetSoftCircleSprite();
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            visible.OnValueChanged += HandleVisibleChanged;
            ApplyVisible(visible.Value);
            if (IsServer)
            {
                ResetServerState();
            }
        }

        public override void OnNetworkDespawn()
        {
            visible.OnValueChanged -= HandleVisibleChanged;
            if (IsServer)
            {
                ReleaseHeldPlayer();
                ClearPlayerEffects();
            }
            ReleaseHealthBar();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            ReleaseHealthBar();
            base.OnDestroy();
        }

        // =====================================================================
        // 외부(서버) 진입점 - WaveManager
        // =====================================================================

        /// <summary>서버 전용 - 5웨이브 보스전 시작. 보스 터(박석 마당)에 나타난다.</summary>
        public void ServerBeginEncounter()
        {
            if (!IsServer || stats == null)
            {
                return;
            }
            ResetServerState();
            // 261122 - 원한 단계(시작 순간 고정)에 따라 조금씩 세진다(예섭 요청).
            stage = PartyProgress.Instance != null ? Mathf.Clamp(PartyProgress.Instance.GrudgeStage, 0, BossStatsSO.StageCount - 1) : 0;
            float hp = stats.maxHealth * BossStatsSO.Pick(stats.healthByStage, stage);
            maxHealth.Value = hp;
            health.Value = hp;

            MapBounds bounds = MapBounds.Instance;
            Vector2 spawn = bounds != null ? bounds.BossSpawnPoint : Vector2.zero;
            rb.position = spawn;
            transform.position = spawn;
            if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
            {
                networkTransform.Teleport(spawn, Quaternion.identity, transform.localScale);
            }

            basicCooldownLeft = 0.5f;
            grabCooldownLeft = stats.grabFirstCooldown;
            flyCooldownLeft = stats.flyFirstCooldown;
            tornadoCooldownLeft = stats.tornadoFirstCooldown;
            visible.Value = true;
            SetState(ServerState.Appearing, KidAnim.Idle);
            AppearRpc(spawn);
        }

        /// <summary>서버 전용 - 시간 안에 못 쫓아냈을 때(5웨이브 끝). 웃으며 도망 → 실패 결과.</summary>
        public void ServerTimeUp()
        {
            if (!IsServer || state == ServerState.Absent || state == ServerState.Leaving)
            {
                return;
            }
            defeated = false;
            StartLeaving();
        }

        /// <summary>서버 전용 - 판이 끝나거나 다시 시작할 때 결과 처리 없이 즉시 치운다.</summary>
        public void ServerForceHide()
        {
            if (IsServer)
            {
                ResetServerState();
            }
        }

        // =====================================================================
        // 피해
        // =====================================================================

        public void TakeDamage(float amount)
        {
            if (!IsServer || amount <= 0f || health.Value <= 0f)
            {
                return;
            }
            if (state == ServerState.Absent || state == ServerState.Appearing || state == ServerState.Leaving)
            {
                return;
            }
            health.Value = Mathf.Max(0f, health.Value - amount);
            FlashRpc();
            if (health.Value <= 0f)
            {
                defeated = true;
                StartLeaving();
            }
        }

        // =====================================================================
        // 서버 상태 기계
        // =====================================================================

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || state == ServerState.Absent || stats == null)
            {
                return;
            }
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            stateTime += dt;
            basicCooldownLeft -= dt;
            grabCooldownLeft -= dt;
            flyCooldownLeft -= dt;
            tornadoCooldownLeft -= dt;
            UpdateDust();
            UpdateTornadoes(dt);

            switch (state)
            {
                case ServerState.Appearing:
                    rb.linearVelocity = Vector2.zero;
                    if (stateTime >= stats.appearSeconds)
                    {
                        SetState(ServerState.Chasing, KidAnim.Idle);
                    }
                    break;
                case ServerState.Chasing:
                    UpdateChasing();
                    break;
                case ServerState.Attacking:
                    UpdateAttacking();
                    break;
                case ServerState.Grabbing:
                    UpdateGrabbing();
                    break;
                case ServerState.Holding:
                    UpdateHolding(dt);
                    break;
                case ServerState.Slamming:
                    UpdateSlamming();
                    break;
                case ServerState.FlyTakeoff:
                case ServerState.Flying:
                case ServerState.FlyRest:
                    UpdateFlying();
                    break;
                case ServerState.TornadoWindup:
                case ServerState.Spinning:
                    UpdateTornadoCast();
                    break;
                case ServerState.Leaving:
                    rb.linearVelocity = Vector2.zero;
                    if (stateTime >= DieSeconds)
                    {
                        FinishEncounter();
                    }
                    break;
            }
        }

        private void UpdateChasing()
        {
            PlayerHealth target = PlayerHealth.FindNearestAlivePlayerHealth(rb.position);
            if (target == null)
            {
                rb.linearVelocity = Vector2.zero;
                SetAnim(KidAnim.Idle);
                return;
            }
            Vector2 toTarget = (Vector2)target.transform.position - rb.position;
            float distance = toTarget.magnitude;
            Vector2 direction = distance > 0.0001f ? toTarget / distance : Vector2.left;
            Face(toTarget.x);

            if (flyCooldownLeft <= 0f)
            {
                StartFly(direction);
                return;
            }
            if (tornadoCooldownLeft <= 0f && distance <= stats.tornadoDistance + 1f)
            {
                StartTornado(direction);
                return;
            }
            if (grabCooldownLeft <= 0f && distance <= GrabReach())
            {
                SetState(ServerState.Grabbing, KidAnim.Grab, GrabCheckFrameTime / Mathf.Max(0.05f, GrabWindup));
                TelegraphRpc(rb.position, Vector2.right, GrabReach(), 360f, GrabWindup);
                SwingSoundRpc(rb.position);
                return;
            }
            if (basicCooldownLeft <= 0f && distance <= BasicReach())
            {
                actionDirection = direction;
                SetState(ServerState.Attacking, KidAnim.Atk, AtkHitFrameTime / Mathf.Max(0.05f, BasicWindup));
                TelegraphRpc(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, BasicWindup);
                return;
            }

            // 외다리 콩콩 - 공중에 뜬 동안만 앞으로(억쇠와 같은 방식).
            if ((KidAnim)animState.Value != KidAnim.Walk)
            {
                hopClock = 0f;
            }
            hopClock += Time.fixedDeltaTime * stats.hopsPerSecond;
            float cycle = hopClock - Mathf.Floor(hopClock);
            float air = Mathf.Clamp(stats.hopAirFraction, 0.3f, 1f);
            float speedFactor = cycle < air ? Mathf.PI * 0.5f / air * Mathf.Sin(Mathf.PI * cycle / air) : 0f;
            Vector2 moveDirection = direction;
            MapFlowField field = MapFlowField.Instance;
            if (field != null && distance > 3f && field.TryGetDirection(rb.position, out Vector2 flowDirection))
            {
                moveDirection = flowDirection; // 160 맵: 집 · 물을 돌아서
            }
            Vector2 velocity = moveDirection * (stats.moveSpeed * BossStatsSO.Pick(stats.speedByStage, stage) * speedFactor);
            rb.linearVelocity = field != null ? field.Steer(rb.position, velocity, Time.fixedDeltaTime) : velocity;
            SetAnim(KidAnim.Walk);
        }

        private void UpdateAttacking()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= BasicWindup)
            {
                hitApplied = true;
                DamagePlayersInSector(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, stats.basicDamage * DamageScale, stats.basicKnockbackTiles);
                SwingRpc(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees);
            }
            if (stateTime >= BasicWindup + stats.basicRecovery / CastSpeed)
            {
                basicCooldownLeft = stats.basicCooldown;
                SetState(ServerState.Chasing, KidAnim.Idle);
            }
        }

        // ---------- 배지기 (작게) ----------

        private void UpdateGrabbing()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= GrabWindup)
            {
                hitApplied = true;
                PlayerHealth target = FindGrabTarget();
                if (target != null)
                {
                    StartHold(target);
                    return;
                }
            }
            float total = GrabWindup + (GrabSeconds - GrabCheckFrameTime) / CastSpeed;
            if (stateTime >= total)
            {
                grabCooldownLeft = stats.grabCooldown * 0.5f; // 헛잡으면 절반만(억쇠와 같음 - 미정)
                SetState(ServerState.Chasing, KidAnim.Idle);
            }
        }

        private void StartHold(PlayerHealth target)
        {
            heldPlayer = target;
            Vector2 offset = (Vector2)target.transform.position - rb.position;
            holdAngle = offset.sqrMagnitude > 0.0001f ? Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg : 0f;
            holdTotalDegrees = Mathf.Max(0f, stats.grabRotations) * 360f;
            holdTurned = 0f;
            if (target.TryGetComponent(out PlayerMovement movement))
            {
                movement.ServerSetHeld(true);
                float holdSeconds = stats.grabRotations * stats.grabSecondsPerTurn + SlamSeconds;
                movement.ServerSuppress(Mathf.Max(stats.grabSuppressSeconds, holdSeconds + 0.3f));
            }
            SetState(ServerState.Holding, KidAnim.Hold);
        }

        private void UpdateHolding(float dt)
        {
            rb.linearVelocity = Vector2.zero;
            if (!IsValidHeld())
            {
                ReleaseHeldPlayer();
                grabCooldownLeft = stats.grabCooldown;
                SetState(ServerState.Chasing, KidAnim.Idle);
                return;
            }
            float step = 360f / stats.grabSecondsPerTurn * dt;
            holdAngle -= step; // 시계 방향
            holdTurned += step;
            Vector2 dir = AngleToDirection(holdAngle);
            PlaceHeld(rb.position + dir * stats.grabHoldRadius);
            Face(dir.x);
            if (holdTurned >= holdTotalDegrees)
            {
                throwFrom = heldPlayer.transform.position;
                Vector2 landing = rb.position + dir * stats.grabRange * stats.grabThrowMultiplier;
                MapBounds bounds = MapBounds.Instance;
                throwTo = bounds != null ? bounds.ClampInside(landing) : landing;
                TelegraphRpc(throwTo, Vector2.right, 0.9f, 360f, SlamHitSeconds);
                SetState(ServerState.Slamming, KidAnim.Slam);
            }
        }

        private void UpdateSlamming()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied)
            {
                if (IsValidHeld())
                {
                    float t = Mathf.Clamp01(stateTime / SlamHitSeconds);
                    PlaceHeld(Vector2.Lerp(throwFrom, throwTo, t * t * (3f - 2f * t)));
                }
                if (stateTime >= SlamHitSeconds)
                {
                    hitApplied = true;
                    if (IsValidHeld())
                    {
                        PlaceHeld(throwTo);
                        heldPlayer.TakeDamage(stats.grabThrowDamage * DamageScale);
                    }
                    ReleaseHeldPlayer();
                    ThrowImpactRpc(throwTo);
                }
            }
            if (stateTime >= SlamSeconds)
            {
                grabCooldownLeft = stats.grabCooldown;
                SetState(ServerState.Chasing, KidAnim.Idle);
            }
        }

        // ---------- 빗자루 비행 ----------

        /// <summary>
        /// FLY(떠오름) → RIDE로 4초 비행(3번 꺾음 = 4구간, 구간마다 가까운 플레이어를 노림). 각 구간 시작 flyWarnSeconds 전에 그 방향 화살표(폭 1.5 · 길이 6칸).
        /// 부딪히면 피해 + 2칸 밀림(구간마다 한 번), 지나간 길에 먼지 줄(2초, 밟으면 -20%) → 내려서 1초 숨 고르기.
        /// </summary>
        private void StartFly(Vector2 towardTarget)
        {
            rb.linearVelocity = Vector2.zero;
            flyLegCount = Mathf.Max(1, stats.flyTurns + 1);
            flyLegSeconds = stats.flySeconds / flyLegCount;
            flySpeed = stats.flyArrowLength / Mathf.Max(0.05f, flyLegSeconds);
            flyLeg = 0;
            nextFlyDirection = PickLegDirection(towardTarget);
            nextLegWarned = false;
            SetState(ServerState.FlyTakeoff, KidAnim.Fly, CastSpeed);
            FlyStartRpc(rb.position);
        }

        private void UpdateFlying()
        {
            if (state == ServerState.FlyTakeoff)
            {
                rb.linearVelocity = Vector2.zero;
                float takeoff = FlyTakeoffSeconds / CastSpeed;
                if (!nextLegWarned && stateTime >= takeoff - FlyWarn)
                {
                    nextLegWarned = true;
                    ArrowRpc(rb.position, nextFlyDirection, stats.flyArrowWidth, stats.flyArrowLength, FlyWarn);
                }
                if (stateTime >= takeoff)
                {
                    BeginLeg();
                }
                return;
            }

            if (state == ServerState.FlyRest)
            {
                rb.linearVelocity = Vector2.zero;
                if (stateTime >= stats.flyRestSeconds / CastSpeed)
                {
                    flyCooldownLeft = stats.flyCooldown;
                    SetState(ServerState.Chasing, KidAnim.Idle);
                }
                return;
            }

            // 구간 비행.
            Vector2 velocity = flyDirection * flySpeed;
            MapBounds bounds = MapBounds.Instance;
            Vector2 next = rb.position + velocity * Time.fixedDeltaTime;
            if (bounds != null && !bounds.Contains(next))
            {
                velocity = Vector2.zero; // 맵 끝에 닿으면 그 구간은 거기서 멈춘다
            }
            rb.linearVelocity = velocity;
            Face(flyDirection.x);
            DropDust();
            FlyContact();

            float legTime = stateTime;
            if (!nextLegWarned && flyLeg + 1 < flyLegCount && legTime >= flyLegSeconds - FlyWarn)
            {
                nextLegWarned = true;
                Vector2 legEnd = rb.position + flyDirection * flySpeed * Mathf.Max(0f, flyLegSeconds - legTime);
                if (bounds != null)
                {
                    legEnd = bounds.ClampInside(legEnd);
                }
                PlayerHealth target = PlayerHealth.FindNearestAlivePlayerHealth(legEnd);
                Vector2 toward = target != null ? (Vector2)target.transform.position - legEnd : -flyDirection;
                nextFlyDirection = PickLegDirection(toward.sqrMagnitude > 0.0001f ? toward : -flyDirection);
                ArrowRpc(legEnd, nextFlyDirection, stats.flyArrowWidth, stats.flyArrowLength, FlyWarn);
            }
            if (legTime >= flyLegSeconds)
            {
                flyLeg++;
                if (flyLeg >= flyLegCount)
                {
                    rb.linearVelocity = Vector2.zero;
                    SetState(ServerState.FlyRest, KidAnim.Idle); // 빗자루에서 내려 숨 고르기(때릴 기회)
                    return;
                }
                BeginLeg();
            }
        }

        private void BeginLeg()
        {
            flyDirection = nextFlyDirection;
            flyHitMask = 0;
            nextLegWarned = false;
            lastDustPosition = rb.position;
            if (state != ServerState.Flying)
            {
                SetState(ServerState.Flying, KidAnim.Ride);
            }
            else
            {
                stateTime = 0f;
            }
        }

        /// <summary>
        /// 261122 - 예섭 요청: 좌우 지그재그 대신 구간마다 두 사람 중 가까운 쪽을 똑바로 노린다(toward = 그 플레이어 방향).
        /// 화살표 예고가 먼저 떠야 해서 방향은 예고하는 순간 정해진다(그 뒤에 피하면 빗나감).
        /// </summary>
        private static Vector2 PickLegDirection(Vector2 toward)
        {
            return toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector2.down;
        }

        private void FlyContact()
        {
            float radius = stats.flyHitRadius + stats.bodyRadius;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count && i < 31; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || (flyHitMask & (1 << i)) != 0)
                {
                    continue;
                }
                Vector2 to = (Vector2)player.transform.position - rb.position;
                if (to.sqrMagnitude > radius * radius)
                {
                    continue;
                }
                flyHitMask |= 1 << i;
                player.TakeDamage(stats.flyContactDamage * DamageScale);
                KnockbackDistance(player, to.sqrMagnitude > 0.0001f ? to.normalized : flyDirection, stats.flyKnockbackTiles);
                HitRpc(player.transform.position);
            }
        }

        private void DropDust()
        {
            if ((rb.position - lastDustPosition).sqrMagnitude < DustSpacing * DustSpacing)
            {
                return;
            }
            lastDustPosition = rb.position;
            dust.Add(new DustPoint { Position = rb.position, ExpireTime = Time.time + stats.dustSeconds });
        }

        /// <summary>먼지 줄 - 지난 것은 앞에서부터 지우고(시간 순서라 앞이 가장 오래됨), 밟고 있는 사람은 잠깐 느려진다.</summary>
        private void UpdateDust()
        {
            int expired = 0;
            while (expired < dust.Count && dust[expired].ExpireTime <= Time.time)
            {
                expired++;
            }
            if (expired > 0)
            {
                dust.RemoveRange(0, expired);
            }
            if (dust.Count == 0)
            {
                return;
            }
            float radiusSqr = stats.dustRadius * stats.dustRadius;
            for (int p = 0; p < PlayerHealth.ActivePlayers.Count; p++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[p];
                if (player == null || player.IsDead || !player.TryGetComponent(out PlayerMovement movement))
                {
                    continue;
                }
                Vector2 position = player.transform.position;
                for (int i = 0; i < dust.Count; i++)
                {
                    if ((dust[i].Position - position).sqrMagnitude <= radiusSqr)
                    {
                        movement.ServerSlowFor(stats.dustSlow, 0.15f);
                        break;
                    }
                }
            }
        }

        // ---------- 빗자루 회오리 ----------

        /// <summary>바닥에 화살표 3개 1초 예고 → SPIN 첫 장(45)에 흙먼지 회오리 3개를 부채꼴로 → 6칸 직진 후 사라짐.</summary>
        private void StartTornado(Vector2 towardTarget)
        {
            rb.linearVelocity = Vector2.zero;
            tornadoBaseDirection = towardTarget;
            SetState(ServerState.TornadoWindup, KidAnim.Idle);
            int count = Mathf.Clamp(stats.tornadoCount, 1, MaxTornadoes);
            float width = stats.tornadoHitRadius * 2f;
            for (int i = 0; i < count; i++)
            {
                ArrowRpc(rb.position, TornadoDirection(i, count), width, stats.tornadoDistance, TornadoWarn);
            }
            WarnSoundRpc(rb.position);
        }

        private Vector2 TornadoDirection(int index, int count)
        {
            float offset = (index - (count - 1) * 0.5f) * stats.tornadoSpreadDegrees;
            return Rotate(tornadoBaseDirection, offset);
        }

        private void UpdateTornadoCast()
        {
            rb.linearVelocity = Vector2.zero;
            if (state == ServerState.TornadoWindup)
            {
                if (stateTime < TornadoWarn)
                {
                    return;
                }
                SetState(ServerState.Spinning, KidAnim.Spin, CastSpeed);
                int count = Mathf.Clamp(stats.tornadoCount, 1, MaxTornadoes);
                tornadoes.Clear();
                for (int i = 0; i < count; i++)
                {
                    tornadoes.Add(new Tornado { Origin = rb.position, Direction = TornadoDirection(i, count), Elapsed = 0f, HitMask = 0 });
                }
                TornadoRpc(rb.position, tornadoBaseDirection, count, stats.tornadoSpreadDegrees, stats.tornadoSpeed, stats.tornadoDistance);
                return;
            }
            if (stateTime >= SpinSeconds / CastSpeed)
            {
                tornadoCooldownLeft = stats.tornadoCooldown;
                SetState(ServerState.Chasing, KidAnim.Idle);
            }
        }

        private void UpdateTornadoes(float dt)
        {
            if (tornadoes.Count == 0)
            {
                return;
            }
            float life = stats.tornadoDistance / Mathf.Max(0.1f, stats.tornadoSpeed);
            float radiusSqr = stats.tornadoHitRadius * stats.tornadoHitRadius;
            for (int t = tornadoes.Count - 1; t >= 0; t--)
            {
                Tornado tornado = tornadoes[t];
                tornado.Elapsed += dt;
                if (tornado.Elapsed >= life)
                {
                    tornadoes.RemoveAt(t);
                    continue;
                }
                Vector2 position = tornado.Origin + tornado.Direction * (stats.tornadoSpeed * tornado.Elapsed);
                for (int i = 0; i < PlayerHealth.ActivePlayers.Count && i < 31; i++)
                {
                    PlayerHealth player = PlayerHealth.ActivePlayers[i];
                    if (player == null || player.IsDead || (tornado.HitMask & (1 << i)) != 0)
                    {
                        continue;
                    }
                    Vector2 to = (Vector2)player.transform.position - position;
                    if (to.sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }
                    tornado.HitMask |= 1 << i;
                    player.TakeDamage(stats.tornadoDamage * DamageScale);
                    KnockbackDistance(player, tornado.Direction, stats.tornadoKnockbackTiles);
                    HitRpc(player.transform.position);
                }
                tornadoes[t] = tornado;
            }
        }

        // ---------- 끝 ----------

        private void StartLeaving()
        {
            ReleaseHeldPlayer();
            ClearPlayerEffects();
            tornadoes.Clear();
            dust.Clear();
            rb.linearVelocity = Vector2.zero;
            SetState(ServerState.Leaving, KidAnim.Die); // DIE = 도망(메롱 → 연기로 사라짐)
            LaughRpc(rb.position);
        }

        private void FinishEncounter()
        {
            bool wasDefeated = defeated;
            ResetServerState();
            EoksoeBoss.RaiseEncounterEnded(BossEncounter.Mid, wasDefeated);
        }

        private void ResetServerState()
        {
            ReleaseHeldPlayer();
            ClearPlayerEffects();
            tornadoes.Clear();
            dust.Clear();
            if (IsSpawned)
            {
                visible.Value = false;
                health.Value = 0f;
            }
            rb.linearVelocity = Vector2.zero;
            state = ServerState.Absent;
            stateTime = 0f;
            defeated = false;
        }

        private void ClearPlayerEffects()
        {
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player != null && player.TryGetComponent(out PlayerMovement movement))
                {
                    movement.ServerClearBossSlow();
                }
            }
        }

        private void SetState(ServerState next, KidAnim anim, float speed = 1f)
        {
            state = next;
            stateTime = 0f;
            hitApplied = false;
            animSpeed.Value = speed;
            animState.Value = (byte)anim;
            animSerial.Value++;
        }

        private void SetAnim(KidAnim anim)
        {
            if (animState.Value == (byte)anim)
            {
                return;
            }
            animSpeed.Value = 1f;
            animState.Value = (byte)anim;
            animSerial.Value++;
        }

        private void Face(float x)
        {
            if (Mathf.Abs(x) > 0.05f)
            {
                faceRight.Value = x > 0f;
            }
        }

        private float BasicReach() => stats.basicRange + stats.bodyRadius;

        // 261122 - 시전 속도(예섭 요청: 빠르게) · 원한 피해 배율.
        private float CastSpeed => Mathf.Max(0.5f, stats.castSpeed);
        private float BasicWindup => stats.basicWindup / CastSpeed;
        private float GrabWindup => stats.grabWindupSeconds / CastSpeed;
        private float FlyWarn => stats.flyWarnSeconds / CastSpeed;
        private float TornadoWarn => stats.tornadoWarnSeconds / CastSpeed;
        private float DamageScale => BossStatsSO.Pick(stats.damageByStage, stage);
        private float GrabReach() => stats.grabRange + stats.bodyRadius;

        private static Vector2 AngleToDirection(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void DamagePlayersInSector(Vector2 origin, Vector2 direction, float radius, float arcDegrees, float damage, float knockbackTiles)
        {
            float half = arcDegrees * 0.5f;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead)
                {
                    continue;
                }
                Vector2 to = (Vector2)player.transform.position - origin;
                if (to.sqrMagnitude > radius * radius)
                {
                    continue;
                }
                if (arcDegrees < 359f && to.sqrMagnitude > 0.0001f && Vector2.Angle(direction, to) > half)
                {
                    continue;
                }
                player.TakeDamage(damage);
                KnockbackDistance(player, to.sqrMagnitude > 0.0001f ? to.normalized : direction, knockbackTiles);
            }
        }

        /// <summary>밀림 거리(칸) - PlayerMovement 넉백은 남은 비율의 제곱으로 줄어 이동 거리 = 속도 × 시간 / 3.</summary>
        private void KnockbackDistance(PlayerHealth player, Vector2 direction, float tiles)
        {
            if (tiles <= 0f || player.IsDead || player == heldPlayer || !player.TryGetComponent(out PlayerMovement movement))
            {
                return;
            }
            const float seconds = 0.2f;
            movement.ServerKnockback(direction * (tiles * 3f / seconds), seconds);
        }

        private PlayerHealth FindGrabTarget()
        {
            PlayerHealth best = null;
            float bestSqr = GrabReach() * GrabReach();
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead)
                {
                    continue;
                }
                float sqr = ((Vector2)player.transform.position - rb.position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = player;
                }
            }
            return best;
        }

        private bool IsValidHeld() => heldPlayer != null && heldPlayer.IsSpawned && !heldPlayer.IsDead;

        private void PlaceHeld(Vector2 position)
        {
            if (heldPlayer == null)
            {
                return;
            }
            if (heldPlayer.TryGetComponent(out Rigidbody2D playerBody))
            {
                playerBody.position = position;
                playerBody.linearVelocity = Vector2.zero;
            }
            else
            {
                heldPlayer.transform.position = position;
            }
        }

        private void ReleaseHeldPlayer()
        {
            if (heldPlayer != null && heldPlayer.TryGetComponent(out PlayerMovement movement))
            {
                movement.ServerSetHeld(false);
            }
            heldPlayer = null;
        }

        // =====================================================================
        // Rpc (연출)
        // =====================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void TelegraphRpc(Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds)
        {
            BossTelegraph.Show(telegraphMaterial, center, direction, radius, arcDegrees, seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ArrowRpc(Vector2 start, Vector2 direction, float width, float length, float seconds)
        {
            BossTelegraph.ShowArrow(telegraphMaterial, start, direction, width, length, seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AppearRpc(Vector2 position)
        {
            EnsureFxBounds();
            PlayGroundBurst(position, 1.2f, 10f, -30, BrownTint);
            PlayerCameraRig.Shake(0.12f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlashRpc()
        {
            flashTimer = 0.1f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingRpc(Vector2 origin, Vector2 direction, float radius, float arcDegrees)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidSwing, origin);
            EnsureFxBounds();
            bool flip = direction.x > 0f;
            SpriteVisibleBounds.FitSlash(slashFxBounds, origin, direction, radius, arcDegrees, flip,
                                         out Vector2 center, out float angle, out Vector2 slashScale);
            BossFx.Play(slashFxFrames, center, angle, slashScale, 1f / FrameSeconds, 30, Color.white, flip);
            PlayerCameraRig.Shake(0.06f, 0.12f);
            hitStopTimer = 0.05f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingSoundRpc(Vector2 origin)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidSwing, origin);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WarnSoundRpc(Vector2 origin)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossWarn, origin);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ThrowImpactRpc(Vector2 position)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidImpact, position);
            PlayGroundBurst(position, 0.9f, 12f, 29, BrownTint);
            PlayerCameraRig.Shake(0.15f, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlyStartRpc(Vector2 position)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidFly, position);
            clientDustStarted = false;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HitRpc(Vector2 position)
        {
            PlayGroundBurst(position, 0.5f, 16f, 29, BrownTint);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TornadoRpc(Vector2 origin, Vector2 baseDirection, int count, float spreadDegrees, float speed, float distance)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidSpin, origin);
            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * spreadDegrees;
                BossProjectileFx.Play(tornadoFrames, origin + visualBaseOffset, Rotate(baseDirection, offset), speed, distance, 1f / FrameSeconds, 0.7f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LaughRpc(Vector2 position)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.KidLaugh, position);
        }

        private void EnsureFxBounds()
        {
            if (fxBoundsReady)
            {
                return;
            }
            fxBoundsReady = true;
            slashFxBounds = SpriteVisibleBounds.Of(slashFxFrames);
            burstFxBounds = SpriteVisibleBounds.Of(burstFxFrames);
        }

        private void PlayGroundBurst(Vector2 center, float radius, float fps, int sortingOrder, Color color)
        {
            EnsureFxBounds();
            SpriteVisibleBounds.FitGroundBurst(burstFxBounds, center, radius, out Vector2 position, out float burstScale);
            BossFx.Play(burstFxFrames, position, 0f, burstScale, fps, sortingOrder, color, false);
        }

        // =====================================================================
        // 클라이언트 연출
        // =====================================================================

        private void HandleVisibleChanged(bool previous, bool current)
        {
            ApplyVisible(current);
            if (current && !previous)
            {
                CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.KidLaugh); // 등장 - SFX_BS_Laugh 높게
                CheonMyeong.Audio.GameAudio.PlayBgm(CheonMyeong.Audio.SoundKeys.BgmBoss);
            }
            else if (!current && previous && CheonMyeong.Waves.WaveManager.Instance != null)
            {
                CheonMyeong.Waves.WaveManager.Instance.RestoreWaveBgm();
            }
        }

        private void ApplyVisible(bool on)
        {
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(on);
            }
            if (shadowRenderer != null)
            {
                shadowRenderer.enabled = on;
            }
            if (bodyCollider != null)
            {
                bodyCollider.enabled = on;
            }
            fadeAlpha = 0f;
            playedAnimSerial = -1;
            hasLastVisualPosition = false;
            hitStopTimer = 0f;
            clientDustStarted = false;
            if (!on)
            {
                BossTelegraph.HideAll();
            }
        }

        private void Update()
        {
            UpdateHealthBar();
            if (!visible.Value || animator == null || bodyRenderer == null)
            {
                return;
            }
            if (animSerial.Value != playedAnimSerial)
            {
                playedAnimSerial = animSerial.Value;
                animator.Play(AnimStateHashes[animState.Value % AnimStateHashes.Length], 0, 0f);
                currentAnimSpeed = animSpeed.Value;
            }
            hitStopTimer -= Time.deltaTime;
            animator.speed = hitStopTimer > 0f ? 0f : currentAnimSpeed;
            bodyRenderer.flipX = faceRight.Value;
            UpdateHopAndTint();
            UpdateClientDust();
        }

        private void UpdateHopAndTint()
        {
            float dt = Time.deltaTime;
            KidAnim anim = (KidAnim)animState.Value;
            Vector2 currentPosition = transform.position;
            float moved = hasLastVisualPosition ? Vector2.Distance(currentPosition, lastVisualPosition) : 0f;
            lastVisualPosition = currentPosition;
            hasLastVisualPosition = true;

            float height = 0f;
            float stretch = 0f;
            if (anim == KidAnim.Walk && stats != null)
            {
                float stride = Mathf.Max(0.05f, stats.moveSpeed / Mathf.Max(0.05f, stats.hopsPerSecond));
                float previous = hopPhase;
                hopPhase += Mathf.Min(moved, stride * 0.5f) / stride;
                if (Mathf.Floor(hopPhase) > Mathf.Floor(previous))
                {
                    squashTimer = 0.08f;
                }
                float p = hopPhase - Mathf.Floor(hopPhase);
                height = Mathf.Sin(p * Mathf.PI) * stats.hopHeight;
                stretch = Mathf.Sin(p * Mathf.PI) * 0.06f;
            }
            else if (anim == KidAnim.Ride)
            {
                // 빗자루 타고 나는 동안: 떠서 흔들림(그림은 1장 - 코드 연출).
                hopPhase = 0f;
                height = 0.35f + 0.08f * Mathf.Sin(Time.time * 14f);
            }
            else
            {
                hopPhase = 0f;
            }

            float squash = 0f;
            if (squashTimer > 0f && stats != null)
            {
                squashTimer -= dt;
                squash = Mathf.Clamp01(squashTimer / 0.08f) * stats.landingSquash;
            }
            if (visualRoot != null)
            {
                visualRoot.localPosition = new Vector3(visualBaseOffset.x, visualBaseOffset.y + height, 0f);
                visualRoot.localScale = new Vector3(1f + squash - stretch * 0.5f, 1f - squash + stretch, 1f);
                float tilt = anim == KidAnim.Ride ? (faceRight.Value ? -1f : 1f) * 8f * Mathf.Sin(Time.time * 7f) : 0f;
                visualRoot.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }

            fadeAlpha = Mathf.MoveTowards(fadeAlpha, 1f, dt / 0.4f);
            Color color = flashTimer > 0f ? new Color(1f, 0.45f, 0.45f, fadeAlpha) : new Color(1f, 1f, 1f, fadeAlpha);
            flashTimer -= dt;
            bodyRenderer.color = color;
            if (shadowRenderer != null)
            {
                float shrink = 1f - 0.4f * Mathf.Clamp01(height / 0.5f);
                shadowRenderer.transform.localScale = new Vector3(0.75f * shrink, 0.28f * shrink, 1f);
                shadowRenderer.color = new Color(0f, 0f, 0f, 0.4f * fadeAlpha * (anim == KidAnim.Die ? 0.5f : 1f));
            }
        }

        /// <summary>빗자루 비행 먼지 줄(로컬 연출) - 서버 판정과 같은 간격 · 시간으로 갈색 먼지를 깐다.</summary>
        private void UpdateClientDust()
        {
            if ((KidAnim)animState.Value != KidAnim.Ride || stats == null)
            {
                clientDustStarted = false;
                return;
            }
            Vector2 position = transform.position;
            if (!clientDustStarted)
            {
                clientDustStarted = true;
                lastClientDustPosition = position;
                return;
            }
            if ((position - lastClientDustPosition).sqrMagnitude < DustSpacing * DustSpacing)
            {
                return;
            }
            lastClientDustPosition = position;
            BossDustFx.Play(position, stats.dustRadius, stats.dustSeconds);
        }

        private void UpdateHealthBar()
        {
            bool show = visible.Value;
            if (!show)
            {
                ReleaseHealthBar();
                return;
            }
            if (healthBar == null)
            {
                if (InGameHudScreen.Instance == null)
                {
                    return;
                }
                healthBar = BossHealthBarView.Create(InGameHudScreen.Instance.transform, barFrameSprite, BarHolePixels, BarColor);
            }
            healthBar.SetVisible(true);
            healthBar.SetBossBounds(bodyRenderer != null && bodyRenderer.enabled, bodyRenderer != null ? bodyRenderer.bounds : default);
            healthBar.Refresh(stats != null ? stats.displayName : "어린 억쇠", health.Value, maxHealth.Value, 1, false, 0f);
        }

        private void ReleaseHealthBar()
        {
            if (healthBar != null)
            {
                healthBar.Release();
                healthBar = null;
            }
        }
    }
}
