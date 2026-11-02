using System;
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
    public enum BossEncounter : byte
    {
        None = 0,
        Mid = 1,   // 5웨이브 예행연습(작은 모습, 1칸)
        Final = 2, // 최종전(2배 크기, 3칸)
    }

    /// <summary>억쇠 애니메이션 상태. 순서 = Eoksoe-Sheet 줄 순서 = 기획서 TAG 순서.</summary>
    public enum BossAnim : byte
    {
        Idle,
        Walk,
        Summon,
        Atk,
        Mental,
        MentalIdle,
        Grab,
        Hold,
        Slam,
        Die,
    }

    /// <summary>금 나와라 뚝딱으로 뿌린 엽전 하나(서버가 쓰고 전원이 읽는다).</summary>
    public struct BossCoinState : INetworkSerializable, IEquatable<BossCoinState>
    {
        public Vector2 Origin;
        public Vector2 Target;
        public float SpawnTime;
        public byte Type;   // 0 동 · 1 은 · 2 금
        public byte Active; // 1 = 아직 바닥에 있음

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref SpawnTime);
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref Active);
        }

        public bool Equals(BossCoinState other)
        {
            return Origin == other.Origin && Target == other.Target && Mathf.Approximately(SpawnTime, other.SpawnTime)
                   && Type == other.Type && Active == other.Active;
        }
    }

    /// <summary>
    /// 261101 추가 - 억쇠(1스테이지 최종 보스). 5웨이브 중간 보스와 최종전을 같은 오브젝트가 맡는다(Gameplay 씬에 미리 놓인
    /// NetworkObject, 평소엔 숨어 있다가 WaveManager가 ServerBeginEncounter로 부른다).
    ///
    /// 구조
    /// - 판단/이동/피해는 전부 서버(FixedUpdate의 상태 기계). 클라이언트는 NetworkVariable(애니 상태 · 방향 · 체력 · 차원 ·
    ///   엽전 목록)을 읽어 그리기만 한다. 위치는 NetworkTransform.
    /// - 스킬: 기본 공격(방망이) · 금 나와라 뚝딱 · 배지기 · 정신병(분신 + 차원). 원한 단계(PartyProgress.GrudgeStage)에 따라
    ///   수치가 바뀐다(BossStatsSO). 스킬은 시전 순간의 단계로 고정한다.
    /// - 연출: 바닥 붉은 예고(BossTelegraph, 차오르는 방식) · 통통 뛰는 이동(외다리 - 예섭 요청, 위아래 튐 + 착지 찌그러짐) ·
    ///   맞으면 붉게 번쩍임 · 화면 흔들림(배지기 내려치기, 등장) · 체력 바(피해 잔상).
    /// - 애니메이션은 Animator 전이 없이 상태 이름으로 바로 Play한다(서버가 정한 상태를 그대로 따라가기 쉬움).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EoksoeBoss : NetworkBehaviour, IDamageable
    {
        public static EoksoeBoss Instance { get; private set; }

        /// <summary>서버 전용 - 보스전이 끝났을 때(종류, 격퇴했는지). WaveManager가 결과를 처리한다.</summary>
        public static event Action<BossEncounter, bool> ServerEncounterEnded;

        private static readonly string[] AnimStateNames =
            { "IDLE", "WALK", "SUMMON", "ATK", "MENTAL", "MENTAL_IDLE", "GRAB", "HOLD", "SLAM", "DIE" };

        private static readonly int[] AnimStateHashes = BuildHashes();

        private const float FrameSeconds = 0.1f; // 기획서: 전부 100ms
        private const float SummonSeconds = 7 * FrameSeconds;
        private const float MentalCastSeconds = 7 * FrameSeconds;
        private const float GrabSeconds = 9 * FrameSeconds;
        private const float GrabCheckSeconds = 6 * FrameSeconds;  // 잡기 판정 프레임 45 (39~47 중 7번째)
        private const float SlamSeconds = 6 * FrameSeconds;
        private const float SlamHitSeconds = 3 * FrameSeconds;    // 피해 · 놓기 프레임 55 (52~57 중 4번째)
        private const float AtkHitFrameTime = 4 * FrameSeconds;   // 타격 판정 프레임 24 (20~25 중 5번째)
        private const float DieSeconds = 9 * FrameSeconds;
        private const float TieDistanceEpsilon = 0.05f;

        [SerializeField] private BossStatsSO stats;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private SpriteRenderer shadowRenderer;

        [Tooltip("엽전 그림(동 · 은 · 금) - coin_xp-Sheet 아랫줄.")]
        [SerializeField] private Sprite[] coinSprites;

        [Tooltip("바닥 예고 머티리얼(Sprites-Default).")]
        [SerializeField] private Material telegraphMaterial;

        [Tooltip("Visual(그림)의 기본 위치 - 발이 이 오브젝트 위치(바닥)에 오도록 빌더가 계산해 넣는다.")]
        [SerializeField] private Vector2 visualBaseOffset;

        // ---------- 복제 상태 ----------
        private readonly NetworkVariable<byte> encounter = new NetworkVariable<byte>();
        private readonly NetworkVariable<float> health = new NetworkVariable<float>();
        private readonly NetworkVariable<float> maxHealth = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<byte> bars = new NetworkVariable<byte>(1);
        private readonly NetworkVariable<byte> animState = new NetworkVariable<byte>();
        private readonly NetworkVariable<int> animSerial = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> faceRight = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> visible = new NetworkVariable<bool>();
        // 시간 안에 못 잡았을 때: 쓰러지는 대신 통통 뛰며 흐려져 물러난다(격퇴와 화면상 구분).
        private readonly NetworkVariable<bool> leaving = new NetworkVariable<bool>();

        private readonly NetworkVariable<bool> realmActive = new NetworkVariable<bool>();
        private readonly NetworkVariable<ulong> realmPlayerId = new NetworkVariable<ulong>();
        private readonly NetworkVariable<Vector2> realmCenter = new NetworkVariable<Vector2>();
        private readonly NetworkVariable<Vector2> clonePosition = new NetworkVariable<Vector2>();
        private readonly NetworkVariable<byte> cloneAnim = new NetworkVariable<byte>();
        private readonly NetworkVariable<int> cloneAnimSerial = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> cloneFaceRight = new NetworkVariable<bool>();
        private readonly NetworkVariable<float> cloneHealth = new NetworkVariable<float>();
        private readonly NetworkVariable<float> cloneMaxHealth = new NetworkVariable<float>(1f);

        private NetworkList<BossCoinState> coins;

        public BossEncounter Encounter => (BossEncounter)encounter.Value;
        public bool IsEngaged => encounter.Value != (byte)BossEncounter.None;
        public bool RealmActive => realmActive.Value;
        public BossStatsSO Stats => stats;

        // ---------- 서버 상태 ----------
        private enum ServerState
        {
            Absent,
            Appearing,
            Chasing,
            Attacking,
            Summoning,
            MentalCast,
            MentalHold,
            Grabbing,
            Holding,
            Slamming,
            Dying,
        }

        private enum CloneState
        {
            Chasing,
            Attacking,
        }

        private Rigidbody2D rb;
        private ServerState state = ServerState.Absent;
        private float stateTime;
        private float scale = 1f;
        private int stage;
        private bool hitApplied;
        private Vector2 actionDirection;
        private float basicCooldownLeft;
        private float goldCooldownLeft;
        private float grabCooldownLeft;
        private float mentalCooldownLeft;
        private bool defeated;

        private PlayerHealth mentalTarget;
        private PlayerHealth heldPlayer;
        private float holdAngle;
        private float holdTotalDegrees;
        private float holdTurned;
        private Vector2 throwFrom;
        private Vector2 throwTo;

        private float realmTimeLeft;
        private float cloneDamage;
        private CloneState cloneState;
        private float cloneStateTime;
        private float cloneCooldownLeft;
        private Vector2 cloneServerPosition;
        private Vector2 cloneAttackDirection;
        private bool cloneHitApplied;
        private GameObject cloneHitboxObject;

        // ---------- 클라이언트 연출 상태 ----------
        private int playedAnimSerial = -1;
        private float hopPhase;
        private float squashTimer;
        private float flashTimer;
        private float fadeAlpha;
        private BossHealthBarView healthBar;
        private GameObject cloneView;
        private SpriteRenderer cloneRenderer;
        private Animator cloneAnimator;
        private int playedCloneSerial = -1;
        private Vector2 cloneShownPosition;
        private LineRenderer realmRing;
        private SpriteRenderer realmDarkness;
        private readonly List<SpriteRenderer> realmBoosted = new List<SpriteRenderer>();
        private readonly List<int> realmBoostedOrders = new List<int>();
        private readonly List<SpriteRenderer> realmFaded = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> coinRenderers = new List<SpriteRenderer>();
        private Transform coinRoot;
        private bool realmVisualOn;

        private static Sprite softCircleSprite;

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
            coins = new NetworkList<BossCoinState>();
            if (shadowRenderer != null && shadowRenderer.sprite == null)
            {
                shadowRenderer.sprite = GetSoftCircleSprite(); // 빌더가 그림자 그림을 못 넣었을 때 대비
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            visible.OnValueChanged += HandleVisibleChanged;
            realmActive.OnValueChanged += HandleRealmChanged;
            ApplyVisible(visible.Value);
            if (IsServer)
            {
                ResetServerState();
            }
        }

        public override void OnNetworkDespawn()
        {
            visible.OnValueChanged -= HandleVisibleChanged;
            realmActive.OnValueChanged -= HandleRealmChanged;
            SetRealmVisual(false);
            if (IsServer)
            {
                ReleaseHeldPlayer();
                DestroyCloneHitbox();
                ClearAllBossSlows();
            }
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
            if (cloneView != null)
            {
                Destroy(cloneView);
            }
            if (realmRing != null)
            {
                Destroy(realmRing.gameObject);
            }
            if (realmDarkness != null)
            {
                Destroy(realmDarkness.gameObject);
            }
            if (healthBar != null)
            {
                Destroy(healthBar.gameObject);
            }
            base.OnDestroy();
        }

        // =====================================================================
        // 외부(서버) 진입점
        // =====================================================================

        /// <summary>서버 전용 - 보스전 시작. 맵 가운데(박석 광장 - 기획서 "보스 등장")에 나타난다.</summary>
        public void ServerBeginEncounter(BossEncounter kind)
        {
            if (!IsServer || stats == null || kind == BossEncounter.None)
            {
                return;
            }

            ResetServerState();
            encounter.Value = (byte)kind;
            scale = kind == BossEncounter.Final ? stats.finalScale : stats.midScale;
            transform.localScale = new Vector3(scale, scale, 1f);

            float total = kind == BossEncounter.Final ? stats.finalHealthPerBar * stats.finalBars : stats.midMaxHealth;
            maxHealth.Value = total;
            health.Value = total;
            bars.Value = (byte)(kind == BossEncounter.Final ? Mathf.Max(1, stats.finalBars) : 1);

            MapBounds bounds = MapBounds.Instance;
            Vector2 spawn = bounds != null && bounds.CameraArea.width > 0f ? bounds.CameraArea.center : Vector2.zero;
            rb.position = spawn;
            transform.position = spawn;
            if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
            {
                networkTransform.Teleport(spawn, Quaternion.identity, transform.localScale);
            }

            basicCooldownLeft = 0.5f;
            goldCooldownLeft = stats.goldFirstCooldown;
            grabCooldownLeft = stats.grabFirstCooldown;
            mentalCooldownLeft = stats.mentalFirstCooldown;
            visible.Value = true;
            SetState(ServerState.Appearing, BossAnim.Idle);
            ShakeRpc(0.25f * scale, 0.5f);
        }

        /// <summary>서버 전용 - 보스를 잡지 못한 채 시간이 끝났을 때(5웨이브 끝 / 최종전 시간 초과). 사라지며 결과를 알린다.</summary>
        public void ServerTimeUp()
        {
            if (!IsServer || !IsEngaged || state == ServerState.Dying || state == ServerState.Absent)
            {
                return;
            }
            defeated = false;
            StartDying();
        }

        /// <summary>서버 전용 - 판이 끝나거나 다시 시작할 때 결과 처리 없이 즉시 치운다.</summary>
        public void ServerForceHide()
        {
            if (!IsServer)
            {
                return;
            }
            ResetServerState();
        }

        public bool IsRealmPlayer(GameObject target)
        {
            return realmActive.Value && target != null && target.TryGetComponent(out NetworkObject networkObject)
                   && networkObject.IsSpawned && networkObject.NetworkObjectId == realmPlayerId.Value;
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
            if (state == ServerState.Absent || state == ServerState.Appearing || state == ServerState.Dying)
            {
                return;
            }

            health.Value = Mathf.Max(0f, health.Value - amount);
            FlashRpc();
            if (health.Value <= 0f)
            {
                defeated = true;
                StartDying();
            }
        }

        /// <summary>서버 전용 - BossCloneHitbox가 받은 피해.</summary>
        public void ServerDamageClone(float amount)
        {
            if (!IsServer || !realmActive.Value || amount <= 0f)
            {
                return;
            }
            cloneHealth.Value = Mathf.Max(0f, cloneHealth.Value - amount);
            if (cloneHealth.Value <= 0f)
            {
                EndRealm();
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
            TickCooldowns(dt);
            UpdateCoinPickups();
            if (realmActive.Value)
            {
                UpdateRealm(dt);
            }

            switch (state)
            {
                case ServerState.Appearing:
                    rb.linearVelocity = Vector2.zero;
                    if (stateTime >= stats.appearSeconds)
                    {
                        SetState(ServerState.Chasing, BossAnim.Idle);
                    }
                    break;
                case ServerState.Chasing:
                    UpdateChasing();
                    break;
                case ServerState.Attacking:
                    UpdateAttacking();
                    break;
                case ServerState.Summoning:
                    UpdateSummoning();
                    break;
                case ServerState.MentalCast:
                    UpdateMentalCast();
                    break;
                case ServerState.MentalHold:
                    rb.linearVelocity = Vector2.zero;
                    if (!realmActive.Value)
                    {
                        mentalCooldownLeft = BossStatsSO.Pick(stats.mentalCooldown, stage);
                        SetState(ServerState.Chasing, BossAnim.Idle);
                    }
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
                case ServerState.Dying:
                    rb.linearVelocity = Vector2.zero;
                    if (stateTime >= DieSeconds)
                    {
                        FinishEncounter();
                    }
                    break;
            }
        }

        private void TickCooldowns(float dt)
        {
            basicCooldownLeft -= dt;
            goldCooldownLeft -= dt;
            grabCooldownLeft -= dt;
            if (!realmActive.Value && state != ServerState.MentalCast && state != ServerState.MentalHold)
            {
                mentalCooldownLeft -= dt; // 지속시간이 끝난 뒤부터 돈다(예섭)
            }
        }

        private void UpdateChasing()
        {
            PlayerHealth target = PlayerHealth.FindNearestAlivePlayerHealth(rb.position);
            if (target == null)
            {
                rb.linearVelocity = Vector2.zero;
                SetAnim(BossAnim.Idle);
                return;
            }

            stage = CurrentStage();
            Vector2 toTarget = (Vector2)target.transform.position - rb.position;
            float distance = toTarget.magnitude;
            Face(toTarget.x);

            if (mentalCooldownLeft <= 0f && !realmActive.Value)
            {
                PlayerHealth mental = PickMentalTarget();
                if (mental != null)
                {
                    mentalTarget = mental;
                    SetState(ServerState.MentalCast, BossAnim.Mental);
                    return;
                }
            }
            if (goldCooldownLeft <= 0f)
            {
                SetState(ServerState.Summoning, BossAnim.Summon);
                return;
            }
            if (grabCooldownLeft <= 0f && distance <= GrabReach())
            {
                SetState(ServerState.Grabbing, BossAnim.Grab);
                TelegraphRpc(rb.position, Vector2.right, GrabReach(), 360f, GrabCheckSeconds);
                return;
            }
            if (basicCooldownLeft <= 0f && distance <= BasicReach())
            {
                actionDirection = distance > 0.0001f ? toTarget / distance : Vector2.left;
                SetState(ServerState.Attacking, BossAnim.Atk);
                TelegraphRpc(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, stats.basicWindup);
                return;
            }

            rb.linearVelocity = distance > 0.0001f ? toTarget / distance * stats.moveSpeed : Vector2.zero;
            SetAnim(BossAnim.Walk);
        }

        private void UpdateAttacking()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= stats.basicWindup)
            {
                hitApplied = true;
                float damage = BossStatsSO.Pick(stats.basicDamage, stage);
                DamagePlayersInSector(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, damage, false);
            }
            if (stateTime >= stats.basicWindup + stats.basicRecovery)
            {
                basicCooldownLeft = BossStatsSO.Pick(stats.basicCooldown, stage);
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        private void UpdateSummoning()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= SummonSeconds - FrameSeconds)
            {
                hitApplied = true; // 마지막 프레임(19)에 엽전 생성
                ScatterCoins(BossStatsSO.Pick(stats.goldCoinCount, stage));
            }
            if (stateTime >= SummonSeconds)
            {
                goldCooldownLeft = stats.goldCooldown;
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        private void UpdateMentalCast()
        {
            rb.linearVelocity = Vector2.zero;
            if (stateTime < MentalCastSeconds)
            {
                return;
            }

            if (mentalTarget == null || mentalTarget.IsDead)
            {
                mentalTarget = PickMentalTarget();
            }
            if (mentalTarget == null)
            {
                mentalCooldownLeft = 2f;
                SetState(ServerState.Chasing, BossAnim.Idle);
                return;
            }

            StartRealm(mentalTarget);
            mentalTarget = null;
            SetState(ServerState.MentalHold, BossAnim.MentalIdle);
        }

        private void UpdateGrabbing()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= GrabCheckSeconds)
            {
                hitApplied = true;
                PlayerHealth target = FindGrabTarget();
                if (target != null)
                {
                    StartHold(target);
                    return;
                }
            }
            if (stateTime >= GrabSeconds)
            {
                grabCooldownLeft = stats.grabCooldown * 0.5f; // 헛잡으면 절반만 기다린다(미정)
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        private void StartHold(PlayerHealth target)
        {
            heldPlayer = target;
            Vector2 offset = (Vector2)target.transform.position - rb.position;
            holdAngle = offset.sqrMagnitude > 0.0001f ? Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg : 0f;
            holdTotalDegrees = Mathf.Max(0f, BossStatsSO.Pick(stats.grabRotations, stage)) * 360f;
            holdTurned = 0f;
            if (target.TryGetComponent(out PlayerMovement movement))
            {
                movement.ServerSetHeld(true);
                float holdSeconds = BossStatsSO.Pick(stats.grabRotations, stage) * stats.grabSecondsPerTurn + SlamSeconds;
                movement.ServerSuppress(Mathf.Max(BossStatsSO.Pick(stats.grabSuppressSeconds, stage), holdSeconds + 0.3f));
            }
            SetState(ServerState.Holding, BossAnim.Hold);
        }

        private void UpdateHolding(float dt)
        {
            rb.linearVelocity = Vector2.zero;
            if (!IsValidHeld())
            {
                ReleaseHeldPlayer();
                grabCooldownLeft = stats.grabCooldown;
                SetState(ServerState.Chasing, BossAnim.Idle);
                return;
            }

            // 시계 방향 = 각도가 줄어드는 쪽. 잡힌 사람은 보스 배 앞(두 주먹 사이)을 따라 원을 그린다.
            float step = 360f / stats.grabSecondsPerTurn * dt;
            holdAngle -= step;
            holdTurned += step;
            Vector2 dir = AngleToDirection(holdAngle);
            PlaceHeld(rb.position + dir * stats.grabHoldRadius * scale);
            Face(dir.x);

            float remaining = holdTotalDegrees - holdTurned;
            if (remaining <= 0f)
            {
                throwFrom = heldPlayer.transform.position;
                Vector2 landing = rb.position + dir * stats.grabRange * stats.grabThrowMultiplier;
                MapBounds bounds = MapBounds.Instance;
                throwTo = bounds != null ? bounds.ClampInside(landing) : landing;
                TelegraphRpc(throwTo, Vector2.right, 1f * Mathf.Max(1f, scale * 0.6f), 360f, SlamHitSeconds);
                SetState(ServerState.Slamming, BossAnim.Slam);
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
                    float eased = t * t * (3f - 2f * t);
                    PlaceHeld(Vector2.Lerp(throwFrom, throwTo, eased));
                }
                if (stateTime >= SlamHitSeconds)
                {
                    hitApplied = true;
                    if (IsValidHeld())
                    {
                        PlaceHeld(throwTo);
                        heldPlayer.TakeDamage(BossStatsSO.Pick(stats.grabSlamDamage, stage));
                    }
                    ReleaseHeldPlayer();
                    ShakeRpc(0.35f, 0.35f);
                }
            }
            if (stateTime >= SlamSeconds)
            {
                grabCooldownLeft = stats.grabCooldown;
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        private void StartDying()
        {
            ReleaseHeldPlayer();
            if (realmActive.Value)
            {
                EndRealm();
            }
            coins.Clear();
            ClearAllBossSlows();
            leaving.Value = !defeated;
            SetState(ServerState.Dying, defeated ? BossAnim.Die : BossAnim.Walk);
        }

        private void FinishEncounter()
        {
            BossEncounter kind = (BossEncounter)encounter.Value;
            bool wasDefeated = defeated;
            ResetServerState();
            ServerEncounterEnded?.Invoke(kind, wasDefeated);
        }

        private void ResetServerState()
        {
            ReleaseHeldPlayer();
            DestroyCloneHitbox();
            ClearAllBossSlows();
            if (IsSpawned)
            {
                realmActive.Value = false;
                coins.Clear();
                visible.Value = false;
                encounter.Value = (byte)BossEncounter.None;
                health.Value = 0f;
                leaving.Value = false;
            }
            rb.linearVelocity = Vector2.zero;
            state = ServerState.Absent;
            stateTime = 0f;
            defeated = false;
            mentalTarget = null;
        }

        private void SetState(ServerState next, BossAnim anim)
        {
            state = next;
            stateTime = 0f;
            hitApplied = false;
            SetAnim(anim, true);
        }

        private void SetAnim(BossAnim anim, bool restart = false)
        {
            if (animState.Value == (byte)anim && !restart)
            {
                return;
            }
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

        private int CurrentStage()
        {
            return PartyProgress.Instance != null ? Mathf.Clamp(PartyProgress.Instance.GrudgeStage, 0, BossStatsSO.StageCount - 1) : 0;
        }

        private float BasicReach()
        {
            return stats.basicRange + stats.bodyRadius * scale;
        }

        private float GrabReach()
        {
            return stats.grabRange + stats.bodyRadius * scale;
        }

        private static Vector2 AngleToDirection(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private void DamagePlayersInSector(Vector2 origin, Vector2 direction, float radius, float arcDegrees, float damage, bool realmOnly)
        {
            float half = arcDegrees * 0.5f;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject) != realmOnly)
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
            }
        }

        private PlayerHealth FindGrabTarget()
        {
            PlayerHealth best = null;
            float bestSqr = GrabReach() * GrabReach();
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject))
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

        /// <summary>
        /// 정신병 대상(예섭 기획): 가장 가까운 캐릭터 → 거리가 같으면 현재 체력이 가장 낮은 캐릭터 → 그것도 같으면 무작위.
        /// 같은 거리 판정은 0.05칸 오차를 둔다.
        /// </summary>
        private readonly List<PlayerHealth> tieBuffer = new List<PlayerHealth>(4);

        private PlayerHealth PickMentalTarget()
        {
            float bestDistance = float.MaxValue;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsHeld(player))
                {
                    continue;
                }
                bestDistance = Mathf.Min(bestDistance, Vector2.Distance(player.transform.position, rb.position));
            }
            if (bestDistance == float.MaxValue)
            {
                return null;
            }

            tieBuffer.Clear();
            float lowestHealth = float.MaxValue;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsHeld(player))
                {
                    continue;
                }
                if (Vector2.Distance(player.transform.position, rb.position) > bestDistance + TieDistanceEpsilon)
                {
                    continue;
                }
                if (player.CurrentHealth < lowestHealth - 0.01f)
                {
                    lowestHealth = player.CurrentHealth;
                    tieBuffer.Clear();
                    tieBuffer.Add(player);
                }
                else if (Mathf.Abs(player.CurrentHealth - lowestHealth) <= 0.01f)
                {
                    tieBuffer.Add(player);
                }
            }
            return tieBuffer.Count > 0 ? tieBuffer[UnityEngine.Random.Range(0, tieBuffer.Count)] : null;
        }

        private bool IsHeld(PlayerHealth player)
        {
            return heldPlayer != null && heldPlayer == player;
        }

        private bool IsValidHeld()
        {
            return heldPlayer != null && heldPlayer.IsSpawned && !heldPlayer.IsDead;
        }

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
        // 금 나와라 뚝딱
        // =====================================================================

        /// <summary>
        /// 맵에 "고르게" 뿌린다 - 지터 격자(stratified sampling): 플레이 영역을 거의 정사각형 칸으로 나누고 무작위로 고른 칸마다
        /// 칸 안의 임의 위치에 하나씩. 순수 무작위보다 뭉침이 적고, 푸아송 디스크보다 싸다.
        /// </summary>
        private readonly List<int> cellBuffer = new List<int>(64);

        private void ScatterCoins(int count)
        {
            if (count <= 0)
            {
                return;
            }
            MapBounds bounds = MapBounds.Instance;
            Rect area = bounds != null ? bounds.PlayableArea : new Rect(rb.position - Vector2.one * 8f, Vector2.one * 16f);
            float aspect = area.height > 0.01f ? area.width / area.height : 1f;
            int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count / Mathf.Max(0.01f, aspect))));
            int cols = Mathf.Max(1, Mathf.CeilToInt((float)count / rows));
            float cellW = area.width / cols;
            float cellH = area.height / rows;

            cellBuffer.Clear();
            for (int i = 0; i < rows * cols; i++)
            {
                cellBuffer.Add(i);
            }
            for (int i = cellBuffer.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (cellBuffer[i], cellBuffer[j]) = (cellBuffer[j], cellBuffer[i]);
            }

            float now = NetworkManager.ServerTime.TimeAsFloat;
            Vector2 origin = rb.position + Vector2.up * 1.2f * scale;
            for (int i = 0; i < count && i < cellBuffer.Count; i++)
            {
                int cell = cellBuffer[i];
                int cx = cell % cols;
                int cy = cell / cols;
                var target = new Vector2(
                    area.xMin + (cx + UnityEngine.Random.Range(0.15f, 0.85f)) * cellW,
                    area.yMin + (cy + UnityEngine.Random.Range(0.15f, 0.85f)) * cellH);
                coins.Add(new BossCoinState
                {
                    Origin = origin,
                    Target = target,
                    SpawnTime = now,
                    Type = PickCoinType(),
                    Active = 1,
                });
            }
        }

        private byte PickCoinType()
        {
            int total = 0;
            for (int i = 0; i < 3; i++)
            {
                total += Mathf.Max(0, BossStatsSO.Pick(stats.goldTypeWeights, i));
            }
            int roll = UnityEngine.Random.Range(0, Mathf.Max(1, total));
            for (int i = 0; i < 3; i++)
            {
                roll -= Mathf.Max(0, BossStatsSO.Pick(stats.goldTypeWeights, i));
                if (roll < 0)
                {
                    return (byte)i;
                }
            }
            return 0;
        }

        private void UpdateCoinPickups()
        {
            if (coins.Count == 0)
            {
                return;
            }
            float now = NetworkManager.ServerTime.TimeAsFloat;
            float radiusSqr = stats.goldPickupRadius * stats.goldPickupRadius;
            for (int i = 0; i < coins.Count; i++)
            {
                BossCoinState coin = coins[i];
                if (coin.Active == 0 || now - coin.SpawnTime < stats.goldFlightSeconds)
                {
                    continue;
                }
                for (int p = 0; p < PlayerHealth.ActivePlayers.Count; p++)
                {
                    PlayerHealth player = PlayerHealth.ActivePlayers[p];
                    if (player == null || player.IsDead || IsRealmPlayer(player.gameObject))
                    {
                        continue;
                    }
                    if (((Vector2)player.transform.position - coin.Target).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }
                    CollectCoin(player, coin.Type);
                    coin.Active = 0;
                    coins[i] = coin;
                    break;
                }
            }
        }

        private void CollectCoin(PlayerHealth player, byte type)
        {
            int value = BossStatsSO.Pick(stats.goldCoinValue, type);
            if (value > 0 && player.TryGetComponent(out PlayerProgress progress))
            {
                progress.AddGold(value);
            }
            float damage = player.CurrentHealth * BossStatsSO.Pick(stats.goldCoinDamageRatio, type);
            if (damage > 0f)
            {
                player.TakeDamage(damage);
            }
            if (player.TryGetComponent(out PlayerMovement movement))
            {
                movement.ServerAddBossSlow(BossStatsSO.Pick(stats.goldCoinSlow, type), stats.goldSlowCap);
            }
        }

        private static void ClearAllBossSlows()
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

        // =====================================================================
        // 정신병 - 분신 + 차원
        // =====================================================================

        private void StartRealm(PlayerHealth target)
        {
            Vector2 center = target.transform.position;
            realmCenter.Value = center;
            realmPlayerId.Value = target.NetworkObjectId;
            realmTimeLeft = stats.mentalDuration;

            float maxClone = maxHealth.Value * BossStatsSO.Pick(stats.mentalCloneHealthRatio, stage);
            cloneMaxHealth.Value = Mathf.Max(1f, maxClone);
            cloneHealth.Value = cloneMaxHealth.Value;
            cloneDamage = BossStatsSO.Pick(stats.basicDamage, stage) * BossStatsSO.Pick(stats.mentalCloneDamageRatio, stage);

            Vector2 away = center - rb.position;
            Vector2 offsetDir = away.sqrMagnitude > 0.0001f ? -away.normalized : Vector2.left;
            cloneServerPosition = center + offsetDir * Mathf.Min(2.5f, stats.mentalRealmRadius * 0.6f);
            clonePosition.Value = cloneServerPosition;
            cloneState = CloneState.Chasing;
            cloneStateTime = 0f;
            cloneCooldownLeft = 1f;
            SetCloneAnim(BossAnim.Idle);

            CreateCloneHitbox();
            realmActive.Value = true;
        }

        private void UpdateRealm(float dt)
        {
            PlayerHealth target = FindRealmPlayer();
            realmTimeLeft -= dt;
            if (target == null || target.IsDead || realmTimeLeft <= 0f || cloneHealth.Value <= 0f)
            {
                EndRealm();
                return;
            }

            // 끌려간 사람은 차원 원 밖으로 못 나간다.
            Vector2 center = realmCenter.Value;
            float limit = stats.mentalRealmRadius - 0.3f;
            Vector2 offset = (Vector2)target.transform.position - center;
            if (offset.sqrMagnitude > limit * limit && target.TryGetComponent(out Rigidbody2D targetBody))
            {
                targetBody.position = center + offset.normalized * limit;
            }

            UpdateClone(target, dt);
        }

        private void UpdateClone(PlayerHealth target, float dt)
        {
            cloneStateTime += dt;
            cloneCooldownLeft -= dt;
            Vector2 toTarget = (Vector2)target.transform.position - cloneServerPosition;
            float distance = toTarget.magnitude;
            float reach = BasicReach();

            if (cloneState == CloneState.Chasing)
            {
                if (Mathf.Abs(toTarget.x) > 0.05f)
                {
                    cloneFaceRight.Value = toTarget.x > 0f;
                }
                if (cloneCooldownLeft <= 0f && distance <= reach)
                {
                    cloneAttackDirection = distance > 0.0001f ? toTarget / distance : Vector2.left;
                    cloneState = CloneState.Attacking;
                    cloneStateTime = 0f;
                    cloneHitApplied = false;
                    SetCloneAnim(BossAnim.Atk, true);
                    TelegraphRpc(cloneServerPosition, cloneAttackDirection, reach, stats.basicArcDegrees, stats.basicWindup);
                }
                else if (distance > reach * 0.6f)
                {
                    Vector2 next = cloneServerPosition + toTarget / Mathf.Max(0.0001f, distance) * stats.moveSpeed * dt;
                    Vector2 fromCenter = next - realmCenter.Value;
                    float limit = stats.mentalRealmRadius - 0.3f;
                    if (fromCenter.sqrMagnitude > limit * limit)
                    {
                        next = realmCenter.Value + fromCenter.normalized * limit;
                    }
                    cloneServerPosition = next;
                    SetCloneAnim(BossAnim.Walk);
                }
                else
                {
                    SetCloneAnim(BossAnim.Idle);
                }
            }
            else
            {
                if (!cloneHitApplied && cloneStateTime >= stats.basicWindup)
                {
                    cloneHitApplied = true;
                    DamagePlayersInSector(cloneServerPosition, cloneAttackDirection, reach, stats.basicArcDegrees, cloneDamage, true);
                }
                if (cloneStateTime >= stats.basicWindup + stats.basicRecovery)
                {
                    cloneState = CloneState.Chasing;
                    cloneCooldownLeft = BossStatsSO.Pick(stats.basicCooldown, stage);
                    SetCloneAnim(BossAnim.Idle);
                }
            }

            if ((clonePosition.Value - cloneServerPosition).sqrMagnitude > 0.0004f)
            {
                clonePosition.Value = cloneServerPosition;
            }
            if (cloneHitboxObject != null)
            {
                cloneHitboxObject.transform.position = cloneServerPosition;
            }
        }

        private void SetCloneAnim(BossAnim anim, bool restart = false)
        {
            if (cloneAnim.Value == (byte)anim && !restart)
            {
                return;
            }
            cloneAnim.Value = (byte)anim;
            cloneAnimSerial.Value++;
        }

        private void EndRealm()
        {
            if (!realmActive.Value)
            {
                return;
            }
            realmActive.Value = false;
            DestroyCloneHitbox();
        }

        private PlayerHealth FindRealmPlayer()
        {
            if (NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(realmPlayerId.Value, out NetworkObject networkObject))
            {
                return null;
            }
            return networkObject != null ? networkObject.GetComponent<PlayerHealth>() : null;
        }

        private void CreateCloneHitbox()
        {
            DestroyCloneHitbox();
            cloneHitboxObject = new GameObject("EoksoeCloneHitbox");
            cloneHitboxObject.layer = gameObject.layer; // Enemy 레이어 - 플레이어 공격 판정 대상
            cloneHitboxObject.transform.position = cloneServerPosition;
            var circle = cloneHitboxObject.AddComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = stats.bodyRadius * scale;
            circle.offset = new Vector2(0f, stats.bodyRadius * scale);
            var hitbox = cloneHitboxObject.AddComponent<BossCloneHitbox>();
            hitbox.Owner = this;
        }

        private void DestroyCloneHitbox()
        {
            if (cloneHitboxObject != null)
            {
                Destroy(cloneHitboxObject);
                cloneHitboxObject = null;
            }
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
        private void FlashRpc()
        {
            flashTimer = 0.1f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShakeRpc(float amplitude, float seconds)
        {
            PlayerCameraRig.Shake(amplitude, seconds);
        }

        // =====================================================================
        // 클라이언트 연출
        // =====================================================================

        private void HandleVisibleChanged(bool previous, bool current)
        {
            ApplyVisible(current);
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
            if (!on)
            {
                BossTelegraph.HideAll();
            }
        }

        private void Update()
        {
            UpdateHealthBar();
            UpdateCoinViews();
            UpdateCloneView();
            UpdateRealmFades();

            if (!visible.Value || animator == null || bodyRenderer == null)
            {
                return;
            }

            if (animSerial.Value != playedAnimSerial)
            {
                playedAnimSerial = animSerial.Value;
                BossAnim anim = (BossAnim)animState.Value;
                animator.Play(AnimStateHashes[(int)anim], 0, 0f);
                // 공격 예고 시간에 맞춰 ATK 앞부분을 늘린다(타격 프레임이 예고가 끝나는 순간에 오도록).
                animator.speed = anim == BossAnim.Atk && stats != null ? AtkHitFrameTime / Mathf.Max(0.05f, stats.basicWindup) : 1f;
            }

            bodyRenderer.flipX = faceRight.Value;
            UpdateHopAndTint();
        }

        /// <summary>
        /// 통통 뛰기: 걷는 동안 그림만 위아래로 |sin| 튀고(그림자는 바닥에 그대로), 착지하는 순간 짧게 납작해졌다 돌아온다
        /// (squash & stretch). 공중에서는 살짝 길어진다. 서버 위치와 무관한 순수 연출이라 각 화면에서 따로 계산한다.
        /// </summary>
        private void UpdateHopAndTint()
        {
            float dt = Time.deltaTime;
            bool walking = (BossAnim)animState.Value == BossAnim.Walk;
            float height = 0f;
            float stretch = 0f;

            if (walking && stats != null)
            {
                float previous = hopPhase;
                hopPhase += dt * stats.hopsPerSecond;
                if (Mathf.Floor(hopPhase) > Mathf.Floor(previous))
                {
                    squashTimer = 0.09f; // 착지
                }
                float p = hopPhase - Mathf.Floor(hopPhase);
                height = Mathf.Sin(p * Mathf.PI) * stats.hopHeight;
                stretch = Mathf.Sin(p * Mathf.PI) * 0.06f;
            }
            else
            {
                if (hopPhase - Mathf.Floor(hopPhase) > 0.01f)
                {
                    squashTimer = 0.09f;
                }
                hopPhase = 0f;
            }

            float squash = 0f;
            if (squashTimer > 0f && stats != null)
            {
                squashTimer -= dt;
                squash = Mathf.Clamp01(squashTimer / 0.09f) * stats.landingSquash;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = new Vector3(visualBaseOffset.x, visualBaseOffset.y + height, 0f);
                visualRoot.localScale = new Vector3(1f + squash - stretch * 0.5f, 1f - squash + stretch, 1f);
            }

            fadeAlpha = Mathf.MoveTowards(fadeAlpha, leaving.Value ? 0f : 1f, dt / (leaving.Value ? DieSeconds : 0.6f));
            Color color = flashTimer > 0f ? new Color(1f, 0.45f, 0.45f, fadeAlpha) : new Color(1f, 1f, 1f, fadeAlpha);
            flashTimer -= dt;
            if ((BossAnim)animState.Value == BossAnim.Die)
            {
                color.a = fadeAlpha;
            }
            bodyRenderer.color = color;

            if (shadowRenderer != null)
            {
                float shrink = stats != null && stats.hopHeight > 0f ? 1f - 0.35f * height / stats.hopHeight : 1f;
                shadowRenderer.transform.localScale = new Vector3(1.3f * shrink, 0.45f * shrink, 1f);
                Color shadowColor = encounter.Value == (byte)BossEncounter.Final
                    ? new Color(0.45f, 0.02f, 0.05f, 0.45f + 0.15f * Mathf.Sin(Time.time * 3f)) // 원한 오라
                    : new Color(0f, 0f, 0f, 0.4f);
                shadowColor.a *= fadeAlpha;
                shadowRenderer.color = shadowColor;
            }
        }

        private void UpdateHealthBar()
        {
            bool show = visible.Value && encounter.Value != (byte)BossEncounter.None;
            if (healthBar == null)
            {
                if (!show || InGameHudScreen.Instance == null)
                {
                    return;
                }
                healthBar = BossHealthBarView.Create(InGameHudScreen.Instance.transform);
            }
            healthBar.SetVisible(show);
            if (!show)
            {
                return;
            }
            string title = stats != null ? stats.displayName : "억쇠";
            title += encounter.Value == (byte)BossEncounter.Final ? " · 최종전" : " · 예행연습";
            float cloneRatio = cloneMaxHealth.Value > 0f ? cloneHealth.Value / cloneMaxHealth.Value : 0f;
            healthBar.Refresh(title, health.Value, maxHealth.Value, bars.Value, realmActive.Value, cloneRatio);
        }

        private void UpdateCoinViews()
        {
            int count = coins != null ? coins.Count : 0;
            if (count == 0 && coinRenderers.Count == 0)
            {
                return;
            }
            if (coinRoot == null)
            {
                coinRoot = new GameObject("EoksoeCoins").transform;
            }
            while (coinRenderers.Count < count)
            {
                var go = new GameObject("Coin");
                go.transform.SetParent(coinRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 2;
                coinRenderers.Add(sr);
            }

            float now = NetworkManager != null ? NetworkManager.ServerTime.TimeAsFloat : Time.time;
            float flight = stats != null ? stats.goldFlightSeconds : 0.6f;
            for (int i = 0; i < coinRenderers.Count; i++)
            {
                SpriteRenderer sr = coinRenderers[i];
                if (i >= count || coins[i].Active == 0)
                {
                    if (sr.enabled)
                    {
                        sr.enabled = false;
                    }
                    continue;
                }

                BossCoinState coin = coins[i];
                if (coinSprites != null && coin.Type < coinSprites.Length)
                {
                    sr.sprite = coinSprites[coin.Type];
                }
                sr.enabled = true;
                float t = Mathf.Clamp01((now - coin.SpawnTime) / flight);
                Vector2 pos = Vector2.Lerp(coin.Origin, coin.Target, t);
                float arc = 4f * t * (1f - t) * 2.5f; // 포물선으로 날아간다
                float bob = t >= 1f ? Mathf.Sin(Time.time * 4f + i) * 0.05f : 0f;
                sr.transform.position = new Vector3(pos.x, pos.y + arc + bob, 0f);
                sr.transform.localScale = Vector3.one * (t >= 1f ? 1f + 0.08f * Mathf.Sin(Time.time * 6f + i) : 0.8f);
            }
        }

        private void UpdateCloneView()
        {
            bool show = realmActive.Value;
            if (!show)
            {
                if (cloneView != null && cloneView.activeSelf)
                {
                    cloneView.SetActive(false);
                }
                return;
            }

            if (cloneView == null)
            {
                cloneView = new GameObject("EoksoeCloneView");
                var visual = new GameObject("Visual");
                visual.transform.SetParent(cloneView.transform, false);
                visual.transform.localPosition = visualBaseOffset;
                cloneRenderer = visual.AddComponent<SpriteRenderer>();
                cloneRenderer.sortingOrder = bodyRenderer != null ? bodyRenderer.sortingOrder : 5;
                cloneAnimator = visual.AddComponent<Animator>();
                if (animator != null)
                {
                    cloneAnimator.runtimeAnimatorController = animator.runtimeAnimatorController;
                }
            }
            if (!cloneView.activeSelf)
            {
                cloneView.SetActive(true);
                cloneShownPosition = clonePosition.Value;
                playedCloneSerial = -1;
            }

            cloneView.transform.localScale = transform.localScale;
            // 서버 위치(틱 단위로 들어옴)를 부드럽게 따라간다.
            cloneShownPosition = Vector2.Lerp(cloneShownPosition, clonePosition.Value, 1f - Mathf.Exp(-15f * Time.deltaTime));
            cloneView.transform.position = cloneShownPosition;
            if (cloneAnimSerial.Value != playedCloneSerial && cloneAnimator.runtimeAnimatorController != null)
            {
                playedCloneSerial = cloneAnimSerial.Value;
                BossAnim anim = (BossAnim)cloneAnim.Value;
                cloneAnimator.Play(AnimStateHashes[(int)anim], 0, 0f);
                cloneAnimator.speed = anim == BossAnim.Atk && stats != null ? AtkHitFrameTime / Mathf.Max(0.05f, stats.basicWindup) : 1f;
            }
            cloneRenderer.flipX = cloneFaceRight.Value;
            // 분신 표현: 보랏빛 그림자 몸 + 깜빡이는 투명도.
            cloneRenderer.color = new Color(0.55f, 0.3f, 0.85f, 0.78f + 0.12f * Mathf.Sin(Time.time * 8f));
        }

        private void HandleRealmChanged(bool previous, bool current)
        {
            SetRealmVisual(current);
        }

        /// <summary>
        /// 차원 연출. 끌려간 본인 화면: 바깥 세상을 어둡게 덮고(카메라 앞 반투명 막), 자기 자신과 분신만 막 위로 올려 그린다.
        /// 다른 사람 화면: 차원 안 두 대상을 반투명하게 보이고 경계 원만 그린다. 위치는 그대로라 카메라 처리가 필요 없다.
        /// </summary>
        private void SetRealmVisual(bool on)
        {
            RestoreRealmRenderers();
            realmVisualOn = on;
            if (realmRing != null)
            {
                realmRing.gameObject.SetActive(on);
            }
            if (realmDarkness != null)
            {
                realmDarkness.gameObject.SetActive(false);
            }
            if (!on)
            {
                return;
            }

            EnsureRealmRing();
            DrawRealmRing(realmCenter.Value, stats != null ? stats.mentalRealmRadius : 4.5f);

            NetworkObject localPlayer = NetworkManager != null && NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            bool localIsInside = localPlayer != null && localPlayer.NetworkObjectId == realmPlayerId.Value;
            if (localIsInside)
            {
                EnsureRealmDarkness();
                realmDarkness.gameObject.SetActive(true);
                BoostRenderers(localPlayer.gameObject);
            }
        }

        private void UpdateRealmFades()
        {
            if (!realmVisualOn)
            {
                return;
            }

            if (realmDarkness != null && realmDarkness.gameObject.activeSelf)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    realmDarkness.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
                    float h = cam.orthographicSize * 2.2f;
                    realmDarkness.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                }
                if (cloneRenderer != null)
                {
                    cloneRenderer.sortingOrder = 1001;
                }
                return;
            }

            // 바깥 사람 화면 - 차원 안 플레이어를 반투명하게(분신은 UpdateCloneView에서 이미 반투명 보라).
            realmFaded.Clear();
            if (NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(realmPlayerId.Value, out NetworkObject inside) && inside != null)
            {
                inside.GetComponentsInChildren(realmFaded);
                for (int i = 0; i < realmFaded.Count; i++)
                {
                    Color c = realmFaded[i].color;
                    c.a = 0.35f;
                    realmFaded[i].color = c;
                }
                if (cloneRenderer != null)
                {
                    Color c = cloneRenderer.color;
                    c.a *= 0.45f;
                    cloneRenderer.color = c;
                }
            }
        }

        private void BoostRenderers(GameObject target)
        {
            target.GetComponentsInChildren(realmBoosted);
            realmBoostedOrders.Clear();
            for (int i = 0; i < realmBoosted.Count; i++)
            {
                realmBoostedOrders.Add(realmBoosted[i].sortingOrder);
                realmBoosted[i].sortingOrder += 1000;
            }
        }

        private void RestoreRealmRenderers()
        {
            for (int i = 0; i < realmBoosted.Count; i++)
            {
                if (realmBoosted[i] != null && i < realmBoostedOrders.Count)
                {
                    realmBoosted[i].sortingOrder = realmBoostedOrders[i];
                }
            }
            realmBoosted.Clear();
            realmBoostedOrders.Clear();

            if (NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(realmPlayerId.Value, out NetworkObject inside) && inside != null)
            {
                inside.GetComponentsInChildren(realmFaded);
                for (int i = 0; i < realmFaded.Count; i++)
                {
                    Color c = realmFaded[i].color;
                    c.a = 1f;
                    realmFaded[i].color = c;
                }
            }
            realmFaded.Clear();
            if (cloneRenderer != null)
            {
                cloneRenderer.sortingOrder = bodyRenderer != null ? bodyRenderer.sortingOrder : 5;
            }
        }

        private void EnsureRealmRing()
        {
            if (realmRing != null)
            {
                return;
            }
            var go = new GameObject("EoksoeRealmRing");
            realmRing = go.AddComponent<LineRenderer>();
            realmRing.useWorldSpace = true;
            realmRing.loop = true;
            realmRing.widthMultiplier = 0.12f;
            realmRing.sharedMaterial = telegraphMaterial;
            realmRing.startColor = realmRing.endColor = new Color(0.65f, 0.35f, 1f, 0.9f);
            realmRing.sortingOrder = 1002;
        }

        private void DrawRealmRing(Vector2 center, float radius)
        {
            const int points = 64;
            realmRing.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                realmRing.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f));
            }
        }

        private void EnsureRealmDarkness()
        {
            if (realmDarkness != null)
            {
                return;
            }
            var go = new GameObject("EoksoeRealmDarkness");
            realmDarkness = go.AddComponent<SpriteRenderer>();
            realmDarkness.sprite = GetSoftCircleSprite();
            realmDarkness.drawMode = SpriteDrawMode.Simple;
            realmDarkness.color = new Color(0.08f, 0.02f, 0.14f, 0.82f);
            realmDarkness.sortingOrder = 1000;
            // 동그란 부드러운 원 대신 화면을 꽉 채우려고 단색 사각 스프라이트를 쓴다.
            realmDarkness.sprite = GetSolidSprite();
        }

        private static Sprite solidSprite;

        private static Sprite GetSolidSprite()
        {
            if (solidSprite == null)
            {
                solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            }
            return solidSprite;
        }

        /// <summary>그림자/오라용 부드러운 원(한 번만 만든다).</summary>
        public static Sprite GetSoftCircleSprite()
        {
            if (softCircleSprite != null)
            {
                return softCircleSprite;
            }
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            softCircleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return softCircleSprite;
        }
    }
}
