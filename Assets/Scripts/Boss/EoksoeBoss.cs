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
        Quake, // 261121 - 땅 꺼짐(기획서 v1.2, 시트 맨 아래 줄 · 125ms)
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

        /// <summary>261121 - 어린 억쇠(5웨이브)도 같은 결과 이벤트로 알린다(WaveManager는 이 이벤트 하나만 듣는다).</summary>
        internal static void RaiseEncounterEnded(BossEncounter kind, bool defeated)
        {
            ServerEncounterEnded?.Invoke(kind, defeated);
        }

        private static readonly string[] AnimStateNames =
            { "IDLE", "WALK", "SUMMON", "ATK", "MENTAL", "MENTAL_IDLE", "GRAB", "HOLD", "SLAM", "DIE", "QUAKE" };

        private static readonly int[] AnimStateHashes = BuildHashes();

        private const float FrameSeconds = 0.1f; // 기획서: 전부 100ms
        private const float SummonSeconds = 7 * FrameSeconds;
        private const float MentalCastSeconds = 7 * FrameSeconds;
        private const float GrabSeconds = 9 * FrameSeconds;
        private const float GrabCheckSeconds = 6 * FrameSeconds;  // 잡기 판정 프레임 44 (38~46 중 7번째) - 기획서 v1.2
        private const float SlamSeconds = 6 * FrameSeconds;
        private const float SlamHitSeconds = 3 * FrameSeconds;    // 피해 · 놓기 프레임 54 (51~56 중 4번째)
        private const float AtkHitFrameTime = 3 * FrameSeconds;   // 타격 판정 프레임 23 (20~24 중 4번째, ATK 5장) - 기획서 v1.2
        private const float DieSeconds = 9 * FrameSeconds;
        // 261121 - 땅 꺼짐 QUAKE 66~74 (9장 · 125ms): 71(6번째)에서 예고 시간만큼 멈춤 → 72 내리침 → 73 피해 · 구덩이 · SLAM_FX → 74 → IDLE.
        private const float QuakeFrameSeconds = 0.125f;
        private const float QuakeFreezeClipTime = 5.5f * QuakeFrameSeconds; // 71번 장 한가운데에서 멈춘다
        private const float QuakeHitClipTime = 7f * QuakeFrameSeconds;      // 73번 장이 시작되는 순간
        private const float QuakeClipLength = 9f * QuakeFrameSeconds;
        private const float TieDistanceEpsilon = 0.05f;
        private const float MentalRecoverSeconds = 0.6f; // 차원을 연 뒤 짚은 자세로 멈춰 있는 시간 - 그 뒤 바깥 사람을 쫓는다

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

        [Tooltip("261102 - Eoksoe_FX-Sheet 윗줄(초승달 베기, 왼쪽을 향해 그려짐).")]
        [SerializeField] private Sprite[] slashFxFrames;
        [Tooltip("261102 - Eoksoe_FX-Sheet 둘째 줄(땅 터짐 SLAM_FX).")]
        [SerializeField] private Sprite[] burstFxFrames;
        [Tooltip("261121 - Eoksoe_FX-Sheet 셋째 줄 QUAKE_PIT(땅 꺼짐 구덩이 그림 1장).")]
        [SerializeField] private Sprite pitSprite;
        [Tooltip("261123 - 정신병 차원 원 밖(끌려간 본인 화면) 장식용 맵 물체 그림(작은 나무 · 대나무 · 바위 등). 앞쪽일수록 자주 뽑힌다.")]
        [SerializeField] private Sprite[] realmDecorSprites;

        [Tooltip("Visual(그림)의 기본 위치 - 발이 이 오브젝트 위치(바닥)에 오도록 빌더가 계산해 넣는다.")]
        [SerializeField] private Vector2 visualBaseOffset;

        // ---------- 복제 상태 ----------
        private readonly NetworkVariable<byte> encounter = new NetworkVariable<byte>();
        private readonly NetworkVariable<float> health = new NetworkVariable<float>();
        private readonly NetworkVariable<float> maxHealth = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<byte> bars = new NetworkVariable<byte>(1);
        private readonly NetworkVariable<byte> animState = new NetworkVariable<byte>();
        private readonly NetworkVariable<int> animSerial = new NetworkVariable<int>();
        // 261121 - 재생 속도(전조 시간에 맞춰 늘린 모션). 같은 MENTAL이라도 정신병(늘림)과 원귀 부르기(원래 속도)가 달라 서버가 정해 보낸다.
        private readonly NetworkVariable<float> animSpeed = new NetworkVariable<float>(1f);
        // 261121 - 땅 꺼짐 예고 시간(QUAKE 71번 장에서 멈추는 시간).
        private readonly NetworkVariable<float> quakeHold = new NetworkVariable<float>();
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
        // 261121 - 땅 꺼짐 구덩이 가운데 좌표(오래된 것부터). BossPitField가 모든 화면에서 같은 구덩이를 만든다.
        private NetworkList<Vector2> pitCenters;

        public BossEncounter Encounter => (BossEncounter)encounter.Value;
        public bool IsEngaged => encounter.Value != (byte)BossEncounter.None;
        public bool RealmActive => realmActive.Value;
        public BossStatsSO Stats => stats;

        // ---------- 서버 상태 ----------
        private enum ImpactKind : byte
        {
            Swing,
            CloneSwing,
            Slam,
            Land,
            Appear,
            Quake,
        }

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
            Leaping,
            Dying,
            Calling,   // 261121 - 원귀 부르기: MENTAL(땅 짚기)
            CallHold,  //            MENTAL_IDLE 1.2초(보스 멈춤)
            Quaking,   // 261121 - 땅 꺼짐
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
        private float leapCooldownLeft;
        private float summonCooldownLeft;
        private float quakeCooldownLeft;
        private int quakeChainLeft;
        private Vector2 quakeCenter;
        private float quakeHitTime;
        private float quakeEndTime;
        private float hopClock;
        private Vector2 leapFrom;
        private Vector2 leapTo;
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
        private float animPlayStartTime;
        private float hopPhase;
        private float squashTimer;
        private float leapVisualStart = -1f;
        private Vector2 lastVisualPosition;
        private bool hasLastVisualPosition;
        private float hitStopTimer;
        private float currentAnimSpeed = 1f;
        private float hitPunchTimer;
        private float warnTimeLeft;
        private float warnDuration = 1f;
        private Transform warnIcon;
        private ulong markTargetId;
        private float markTimeLeft;
        private float markDuration = 1f;
        private LineRenderer markRing;
        private float leapVisualWindup;
        private float leapVisualAir;
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
        private readonly List<int> realmBoostedLayers = new List<int>();
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
            pitCenters = new NetworkList<Vector2>();
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
            realmPlayerId.OnValueChanged += HandleRealmTargetChanged;
            realmCenter.OnValueChanged += HandleRealmCenterChanged;
            animState.OnValueChanged += HandleAnimSound;
            pitCenters.OnListChanged += HandlePitsChanged;
            realmVisualDirty = realmActive.Value; // 늦게 들어온 클라이언트도 열린 차원을 그린다
            EditorSyncShaders(true);
            RebuildPits();
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
            realmPlayerId.OnValueChanged -= HandleRealmTargetChanged;
            realmCenter.OnValueChanged -= HandleRealmCenterChanged;
            animState.OnValueChanged -= HandleAnimSound;
            pitCenters.OnListChanged -= HandlePitsChanged;
            realmVisualDirty = false;
            EditorSyncShaders(false);
            BossPitField.Clear();
            SetRealmVisual(false);
            CheonMyeong.Audio.GameAudio.Stop(realmAmbience);
            realmAmbience = default;
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
            EditorSyncShaders(false); // 플레이를 끝내며 바로 파괴될 때도 에디터 설정을 되돌린다
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
            if (realmRingFx != null)
            {
                Destroy(realmRingFx.gameObject);
            }
            if (realmRingMaterial != null)
            {
                Destroy(realmRingMaterial); // 런타임에 만든 머티리얼 사본
            }
            DestroyRealmDecor();
            if (realmDarkness != null)
            {
                Destroy(realmDarkness.gameObject);
            }
            if (playerFlame != null)
            {
                Destroy(playerFlame.gameObject);
            }
            if (cloneFlame != null)
            {
                Destroy(cloneFlame.gameObject);
            }
            // 261122 - 차원 막 텍스처는 실행 중에 만든 것이라 직접 해제(메모리 누수 방지).
            if (realmMaskSprite != null)
            {
                Destroy(realmMaskSprite);
                realmMaskSprite = null;
            }
            if (realmMaskTexture != null)
            {
                Destroy(realmMaskTexture);
                realmMaskTexture = null;
            }
            if (healthBar != null)
            {
                // 261117 - 씬의 아트 틀(BossBarFrame)을 쓰는 경우 틀째 지우면 다음 보스(최종전)가 틀 없는 임시 바로 나왔다 - 붙인 것만 걷는다.
                healthBar.Release();
                healthBar = null;
            }
            if (markRing != null)
            {
                Destroy(markRing.gameObject);
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
            stage = CurrentStage();
            total *= BossStatsSO.Pick(stats.healthByStage, stage); // 261122 - 원한이 짙을수록 단단하게(시작 순간 단계로 고정)
            maxHealth.Value = total;
            health.Value = total;
            bars.Value = (byte)(kind == BossEncounter.Final ? Mathf.Max(1, stats.finalBars) : 1);

            MapBounds bounds = MapBounds.Instance;
            Vector2 spawn = bounds != null ? bounds.BossSpawnPoint : Vector2.zero; // 261121 - 박석 마당(보스 터)
            rb.position = spawn;
            transform.position = spawn;
            if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
            {
                networkTransform.Teleport(spawn, Quaternion.identity, transform.localScale);
            }

            basicCooldownLeft = 0.5f;
            goldCooldownLeft = stats.goldFirstCooldown;
            leapCooldownLeft = 1f;
            grabCooldownLeft = stats.grabFirstCooldown;
            mentalCooldownLeft = stats.mentalFirstCooldown;
            summonCooldownLeft = stats.summonFirstCooldown;
            quakeCooldownLeft = stats.quakeFirstCooldown;
            visible.Value = true;
            SetState(ServerState.Appearing, BossAnim.Idle);
            ImpactRpc(spawn, Vector2.down, (byte)ImpactKind.Appear, 2.5f * scale, 360f);
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
                    // 261122 - 예섭 요청: 차원이 열려 있는 동안 진짜 억쇠는 바깥 사람과 1대1로 계속 싸운다(짚은 자세는 잠깐만).
                    rb.linearVelocity = Vector2.zero;
                    if (!realmActive.Value || stateTime >= MentalRecoverSeconds)
                    {
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
                case ServerState.Leaping:
                    UpdateLeaping();
                    break;
                case ServerState.Calling:
                case ServerState.CallHold:
                    UpdateCalling();
                    break;
                case ServerState.Quaking:
                    UpdateQuaking();
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
            leapCooldownLeft -= dt;
            if (IsFinal)
            {
                summonCooldownLeft -= dt;
                quakeCooldownLeft -= dt;
            }
            if (!realmActive.Value && state != ServerState.MentalCast && state != ServerState.MentalHold)
            {
                mentalCooldownLeft -= dt; // 지속시간이 끝난 뒤부터 돈다(예섭)
            }
        }

        private void UpdateChasing()
        {
            PlayerHealth target = FindNearestOutsidePlayer(); // 261122 - 차원에 끌려간 사람은 쫓지 않는다(바깥에서 1대1)
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
                    WarnRpc(stats.mentalWindupSeconds);
                    MarkTargetRpc(mental.NetworkObjectId, stats.mentalWindupSeconds);
                    return;
                }
            }
            // 261121 - 최종전 추가 스킬(기획서 v1.1): 땅 꺼짐 · 원귀 부르기. 부르기 직후에 땅 꺼짐이 이어지기 쉽게 땅 꺼짐을 먼저 본다.
            if (IsFinal && quakeCooldownLeft <= 0f && TryStartQuake(BossStatsSO.Pick(stats.quakeChain, stage)))
            {
                return;
            }
            if (IsFinal && summonCooldownLeft <= 0f)
            {
                StartCalling();
                return;
            }
            if (goldCooldownLeft <= 0f)
            {
                SetState(ServerState.Summoning, BossAnim.Summon);
                return;
            }
            if (grabCooldownLeft <= 0f && distance <= GrabReach())
            {
                SetState(ServerState.Grabbing, BossAnim.Grab);
                TelegraphRpc(rb.position, Vector2.right, GrabReach(), 360f, stats.grabWindupSeconds);
                WarnRpc(stats.grabWindupSeconds);
                return;
            }
            if (basicCooldownLeft <= 0f && distance <= BasicReach())
            {
                actionDirection = distance > 0.0001f ? toTarget / distance : Vector2.left;
                SetState(ServerState.Attacking, BossAnim.Atk);
                TelegraphRpc(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, stats.basicWindup);
                return;
            }

            if (leapCooldownLeft <= 0f && distance >= stats.leapTriggerDistance && TryStartLeap(toTarget, distance))
            {
                return;
            }

            // 무게감: 공중에 뜬 동안만 앞으로 가고(가속 → 감속), 땅을 딛는 동안은 멈춘다. 한 주기 평균 속도 = 이동 속도.
            if ((BossAnim)animState.Value != BossAnim.Walk)
            {
                hopClock = 0f;
            }
            hopClock += Time.fixedDeltaTime * HopRate();
            float cycle = hopClock - Mathf.Floor(hopClock);
            float air = Mathf.Clamp(stats.hopAirFraction, 0.3f, 1f);
            float speedFactor = cycle < air ? Mathf.PI * 0.5f / air * Mathf.Sin(Mathf.PI * cycle / air) : 0f;
            Vector2 moveDirection = distance > 0.0001f ? toTarget / distance : Vector2.zero;
            MapFlowField field = MapFlowField.Instance;
            if (field != null && distance > 3f && field.TryGetDirection(rb.position, out Vector2 flowDirection))
            {
                moveDirection = flowDirection; // 261121 - 160 맵: 마당 밖으로 쫓아갈 때 집 · 물을 돌아서
            }
            Vector2 velocity = moveDirection * (MoveSpeed() * speedFactor);
            if (BossPitField.Count > 0)
            {
                velocity = BossPitField.Steer(rb.position, velocity, PitBodyRadius(), Time.fixedDeltaTime); // 구덩이는 보스도 못 지나간다
            }
            if (field != null)
            {
                velocity = field.Steer(rb.position, velocity, Time.fixedDeltaTime);
            }
            rb.linearVelocity = velocity;
            SetAnim(BossAnim.Walk);
        }

        private PlayerHealth FindNearestOutsidePlayer()
        {
            PlayerHealth best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject))
                {
                    continue;
                }
                float sqr = ((Vector2)player.transform.position - rb.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = player;
                }
            }
            return best;
        }

        private bool IsFinal => encounter.Value == (byte)BossEncounter.Final;
        // 261122 - 원한 단계별 이동 속도(서버는 지금 단계, 화면 연출은 단계를 몰라 1배로 근사).
        private float MoveSpeed() => stats.moveSpeed * (IsFinal ? stats.finalMoveSpeedMultiplier : 1f) * BossStatsSO.Pick(stats.moveSpeedByStage, IsServer ? stage : 0);
        private float HopRate() => stats.hopsPerSecond * (IsFinal ? stats.finalHopRateMultiplier : 1f);
        private float DamageScale() => IsFinal ? stats.finalDamageMultiplier : 1f;
        private float ImpactScale() => IsFinal ? stats.finalImpactMultiplier : 1f;
        // 클라이언트에선 서버 전용 scale 필드가 없으므로 NetworkTransform으로 동기화된 실제 크기를 쓴다.
        private float VisualSize() => Mathf.Max(1f, transform.localScale.x);

        /// <summary>
        /// 도약(예섭: 멀어지면 이동 칸을 늘려 한 번에 좁히기). 웅크림(예고) → 포물선 점프 → 착지 흔들림.
        /// 착지 지점은 대상 앞(기본 공격 사거리 안쪽)이고 최대 거리 · 맵 경계로 자른다. 예고 원이 착지 지점을 미리 보여준다.
        /// </summary>
        private bool TryStartLeap(Vector2 toTarget, float distance)
        {
            Vector2 direction = toTarget / distance;
            float travel = Mathf.Min(stats.leapMaxDistance, distance - BasicReach() * 0.6f);
            if (travel < 1f)
            {
                return false;
            }

            leapFrom = rb.position;
            leapTo = leapFrom + direction * travel;
            MapBounds bounds = MapBounds.Instance;
            if (bounds != null)
            {
                leapTo = bounds.ClampInside(leapTo);
            }
            leapTo = BossPitField.PushOut(leapTo, PitBodyRadius()); // 261121 - 구덩이 위로는 착지하지 않는다

            rb.linearVelocity = Vector2.zero;
            Face(direction.x);
            SetState(ServerState.Leaping, BossAnim.Idle);
            float totalSeconds = stats.leapWindupSeconds + stats.leapAirSeconds;
            TelegraphRpc(leapTo, direction, stats.landingPushRadius * scale, 360f, totalSeconds); // = 착지 충격파 범위
            LeapRpc(stats.leapWindupSeconds, stats.leapAirSeconds);
            return true;
        }

        private void UpdateLeaping()
        {
            float airTime = stateTime - stats.leapWindupSeconds;
            if (airTime < 0f)
            {
                rb.linearVelocity = Vector2.zero; // 웅크림
                return;
            }
            if (airTime < stats.leapAirSeconds)
            {
                // 다음 물리 스텝에 있어야 할 위치로 가는 속도 - 스텝 간격과 무관하게 정확히 착지 지점에서 끝난다.
                SetAnim(BossAnim.Walk);
                float next = Mathf.Clamp01((airTime + Time.fixedDeltaTime) / stats.leapAirSeconds);
                Vector2 nextPosition = Vector2.Lerp(leapFrom, leapTo, next);
                rb.linearVelocity = (nextPosition - rb.position) / Time.fixedDeltaTime;
                return;
            }

            rb.linearVelocity = Vector2.zero;
            if (!hitApplied)
            {
                hitApplied = true;
                SetAnim(BossAnim.Idle);
                ImpactRpc(rb.position, Vector2.down, (byte)ImpactKind.Land, stats.landingPushRadius * scale, 360f);
                PushPlayersAround(rb.position, stats.landingPushRadius * scale, false);
            }
            if (airTime >= stats.leapAirSeconds + stats.leapRecoverySeconds)
            {
                leapCooldownLeft = stats.leapCooldown;
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        private void UpdateAttacking()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= stats.basicWindup)
            {
                hitApplied = true;
                float damage = BossStatsSO.Pick(stats.basicDamage, stage) * DamageScale();
                DamagePlayersInSector(rb.position, actionDirection, BasicReach(), stats.basicArcDegrees, damage, false);
                ImpactRpc(rb.position, actionDirection, (byte)ImpactKind.Swing, BasicReach(), stats.basicArcDegrees);
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
            if (stateTime < stats.mentalWindupSeconds)
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
            if (!hitApplied && stateTime >= stats.grabWindupSeconds)
            {
                hitApplied = true;
                PlayerHealth target = FindGrabTarget();
                if (target != null)
                {
                    StartHold(target);
                    return;
                }
            }
            if (stateTime >= GrabSeconds * (stats.grabWindupSeconds / GrabCheckSeconds))
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
                throwTo = ResolveLanding(rb.position, throwTo); // 261122 - 대숲 · 울타리 · 물체 · 구덩이 너머로 던지지 않게
                TelegraphRpc(throwTo, Vector2.right, SlamRadius(), 360f, SlamHitSeconds);
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
                        heldPlayer.TakeDamage(BossStatsSO.Pick(stats.grabSlamDamage, stage) * DamageScale());
                    }
                    ReleaseHeldPlayer();
                    ImpactRpc(throwTo, Vector2.down, (byte)ImpactKind.Slam, SlamRadius(), 360f);
                }
            }
            if (stateTime >= SlamSeconds)
            {
                grabCooldownLeft = stats.grabCooldown;
                SetState(ServerState.Chasing, BossAnim.Idle);
            }
        }

        // =====================================================================
        // 261121 원귀 부르기 (최종전 · 기획서 v1.1 · v1.2)
        // =====================================================================

        /// <summary>MENTAL로 땅을 짚고(원래 속도 7장) → 마지막 장에 보스 둘레 6칸 원 위로 원귀가 솟는다 → MENTAL_IDLE 1.2초 멈춤.</summary>
        private void StartCalling()
        {
            rb.linearVelocity = Vector2.zero;
            SetState(ServerState.Calling, BossAnim.Mental, 1f);
            int count = BossStatsSO.Pick(stats.summonCount, stage);
            float angleOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            callAngleOffset = angleOffset;
            callCount = count;
            CallRpc(rb.position, stats.summonRingRadius, Mathf.Min(count, MaxCallPillars), angleOffset, MentalCastSeconds, stats.summonHoldSeconds);
        }

        private const int MaxCallPillars = 24; // 넋 기둥은 이 수까지만 그린다(80마리여도 원 위에 고르게 24개)
        private float callAngleOffset;
        private int callCount;

        private void UpdateCalling()
        {
            rb.linearVelocity = Vector2.zero;
            if (state == ServerState.Calling)
            {
                if (stateTime < MentalCastSeconds)
                {
                    return;
                }
                CheonMyeong.Enemies.EnemySpawner spawner = CheonMyeong.Enemies.EnemySpawner.Instance;
                if (spawner != null)
                {
                    spawner.ServerQueueBossSummon(rb.position, stats.summonRingRadius, callCount,
                        BossStatsSO.Pick(stats.summonEliteRatio, stage), callAngleOffset);
                }
                SetState(ServerState.CallHold, BossAnim.MentalIdle);
                return;
            }
            if (stateTime < stats.summonHoldSeconds)
            {
                return;
            }
            summonCooldownLeft = BossStatsSO.Pick(stats.summonCooldown, stage);
            // 노림(기획서): 땅 꺼짐은 원귀 부르기 직후에 자주 쓴다 - 곧 돌아올 땅 꺼짐이면 바로 이어서.
            if (quakeCooldownLeft <= stats.quakeComboWindow)
            {
                quakeCooldownLeft = 0f;
            }
            SetState(ServerState.Chasing, BossAnim.Idle);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CallRpc(Vector2 center, float radius, int pillars, float angleOffset, float riseSeconds, float holdSeconds)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossSummon, center); // 부르는 소리 - 기획서에 따로 없어 "boss.summon" 칸 사용
            float height = 1.6f * VisualSize();
            for (int i = 0; i < pillars; i++)
            {
                float a = angleOffset + Mathf.PI * 2f * i / Mathf.Max(1, pillars);
                BossPillarFx.Play(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, riseSeconds, holdSeconds, height);
            }
        }

        private void DespawnSummoned()
        {
            if (!IsServer)
            {
                return;
            }
            if (CheonMyeong.Enemies.EnemySpawner.Instance != null)
            {
                CheonMyeong.Enemies.EnemySpawner.Instance.ServerClearBossSummons();
            }
            CheonMyeong.Enemies.Enemy.ServerDespawnSummoned();
        }

        // =====================================================================
        // 261121 땅 꺼짐 (최종전 · 기획서 v1.2)
        // =====================================================================

        /// <summary>
        /// 대상 = 두 사람 중 보스에게 더 가까운 쪽(정신병 차원 안은 제외). 발밑에 붉은 원(4칸) + 가운데 진한 원(1.5칸) 예고.
        /// QUAKE 66~71을 재생하다 71에서 예고 시간만큼 멈추고, 72 내리침 → 73에 피해 · 구덩이.
        /// </summary>
        private bool TryStartQuake(int chain)
        {
            PlayerHealth target = FindQuakeTarget();
            if (target == null)
            {
                return false;
            }
            quakeChainLeft = Mathf.Max(1, chain) - 1;
            BeginQuake(target);
            return true;
        }

        private void BeginQuake(PlayerHealth target)
        {
            rb.linearVelocity = Vector2.zero;
            Vector2 center = target.transform.position;
            // 구덩이를 0.5칸 격자에 맞춘다(타일 한 장 = 0.5칸이라 QUAKE_PIT이 바닥과 어긋나 보이지 않게).
            quakeCenter = new Vector2(Mathf.Round(center.x * 2f) * 0.5f, Mathf.Round(center.y * 2f) * 0.5f);
            Face(quakeCenter.x - rb.position.x);
            float hold = BossStatsSO.Pick(stats.quakeTelegraphSeconds, stage);
            quakeHold.Value = hold;
            quakeHitTime = QuakeHitClipTime + hold;
            quakeEndTime = QuakeClipLength + hold;
            SetState(ServerState.Quaking, BossAnim.Quake, 1f);
            TelegraphRpc(quakeCenter, Vector2.right, stats.quakeOuterRadius, 360f, quakeHitTime);
            StrongTelegraphRpc(quakeCenter, stats.quakeInnerRadius, quakeHitTime);
            WarnRpc(quakeHitTime);
        }

        private void UpdateQuaking()
        {
            rb.linearVelocity = Vector2.zero;
            if (!hitApplied && stateTime >= quakeHitTime)
            {
                hitApplied = true;
                ApplyQuakeHit();
            }
            if (stateTime < quakeEndTime)
            {
                return;
            }
            if (quakeChainLeft > 0)
            {
                PlayerHealth next = FindQuakeTarget();
                if (next != null)
                {
                    quakeChainLeft--;
                    BeginQuake(next);
                    return;
                }
            }
            quakeChainLeft = 0;
            quakeCooldownLeft = BossStatsSO.Pick(stats.quakeCooldown, stage);
            SetState(ServerState.Chasing, BossAnim.Idle); // 뽑는 동작 없이 바로 IDLE(기획서 v1.2)
        }

        private PlayerHealth FindQuakeTarget()
        {
            PlayerHealth best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject) || IsHeld(player))
                {
                    continue;
                }
                float sqr = ((Vector2)player.transform.position - rb.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = player;
                }
            }
            return best;
        }

        /// <summary>가운데 = 즉사(호신부 못 막음 · 무적만 막음), 바깥 = 피해 + 2칸 밀림, 가운데에 3×3칸 구덩이.</summary>
        private void ApplyQuakeHit()
        {
            float inner = stats.quakeInnerRadius;
            float outer = stats.quakeOuterRadius;
            float damage = BossStatsSO.Pick(stats.quakeDamage, stage);
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject))
                {
                    continue;
                }
                Vector2 to = (Vector2)player.transform.position - quakeCenter;
                float sqr = to.sqrMagnitude;
                if (sqr <= inner * inner)
                {
                    player.ServerInstantKill();
                    continue;
                }
                if (sqr <= outer * outer)
                {
                    player.TakeDamage(damage);
                    KnockbackDistance(player, sqr > 0.0001f ? to.normalized : Vector2.up, stats.quakeKnockbackTiles);
                }
            }
            ImpactRpc(quakeCenter, Vector2.down, (byte)ImpactKind.Quake, inner, 360f);
            TryAddPit(quakeCenter);
        }

        /// <summary>구덩이 규칙(기획서 v1.2): 금지 구역이면 안 만든다. 최대 6개(넘으면 오래된 것부터 메움). 안의 원귀는 사라지고, 갇힌 사람은 빈 칸으로.</summary>
        private void TryAddPit(Vector2 center)
        {
            float size = stats.pitSize;
            MapBounds bounds = MapBounds.Instance;
            if (bounds != null && !bounds.CanPlacePit(center, size))
            {
                return;
            }
            while (pitCenters.Count >= Mathf.Max(1, stats.pitMax))
            {
                pitCenters.RemoveAt(0);
            }
            pitCenters.Add(center);
            RebuildPits(); // 서버는 바로 반영(목록 이벤트를 기다리지 않고 판정에 쓴다)

            var area = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            CheonMyeong.Enemies.Enemy.ServerRemoveInArea(area);

            // 구덩이 위에 서 있던 사람(밀려나지 않은 즉사 생존 - 무적 등)과 구덩이 사이에 갇힌 사람을 가장 가까운 빈 칸으로.
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || !player.TryGetComponent(out PlayerMovement movement))
                {
                    continue;
                }
                Vector2 position = player.transform.position;
                Vector2 outside = BossPitField.PushOut(position, PlayerPitRadius);
                if (BossPitField.TryFindEscape(outside, PlayerPitRadius, out Vector2 escape))
                {
                    outside = escape;
                }
                if ((outside - position).sqrMagnitude > 0.0001f)
                {
                    movement.ServerTeleport(outside);
                }
            }
            // 보스도 구덩이 안이면 밖으로.
            Vector2 bossOut = BossPitField.PushOut(rb.position, PitBodyRadius());
            if ((bossOut - rb.position).sqrMagnitude > 0.0001f)
            {
                rb.position = bossOut;
            }
        }

        private const float PlayerPitRadius = 0.4f;

        private float PitBodyRadius() => Mathf.Max(0.3f, stats.bodyRadius * scale * 0.6f);

        /// <summary>밀림 거리(칸)로 밀기 - PlayerMovement 넉백은 남은 비율의 제곱으로 줄어 이동 거리 = 속도 × 시간 / 3.</summary>
        private static void KnockbackDistance(PlayerHealth player, Vector2 direction, float tiles)
        {
            if (tiles <= 0f || player.IsDead || !player.TryGetComponent(out PlayerMovement movement))
            {
                return;
            }
            const float seconds = 0.25f;
            movement.ServerKnockback(direction * (tiles * 3f / seconds), seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrongTelegraphRpc(Vector2 center, float radius, float seconds)
        {
            BossTelegraph.Show(telegraphMaterial, center, Vector2.right, radius, 360f, seconds, 2.2f);
        }

        private void HandlePitsChanged(NetworkListEvent<Vector2> change)
        {
            RebuildPits();
        }

        private readonly List<Vector2> pitBuffer = new List<Vector2>(8);

        private void RebuildPits()
        {
            pitBuffer.Clear();
            if (pitCenters != null)
            {
                for (int i = 0; i < pitCenters.Count; i++)
                {
                    pitBuffer.Add(pitCenters[i]);
                }
            }
            BossPitField.Rebuild(pitBuffer, stats != null ? stats.pitSize : 3f, pitSprite);
        }

        /// <summary>
        /// QUAKE 재생(클라이언트): 71번 장 한가운데까지 재생 → 예고 시간 동안 멈춤 → 나머지. 시간을 직접 정해 주므로(Animator 속도 0)
        /// 서버가 피해를 주는 순간(73번 장 시작)과 그림이 맞는다.
        /// </summary>
        private void DriveQuakeAnimation()
        {
            float t = Time.time - animPlayStartTime;
            float hold = Mathf.Max(0f, quakeHold.Value);
            float clip = t < QuakeFreezeClipTime ? t : (t < QuakeFreezeClipTime + hold ? QuakeFreezeClipTime : t - hold);
            animator.speed = 0f;
            animator.Play(AnimStateHashes[(int)BossAnim.Quake], 0, Mathf.Clamp(clip / QuakeClipLength, 0f, 0.999f));
        }

        private void StartDying()
        {
            ReleaseHeldPlayer();
            DespawnSummoned(); // 261121 - 불려 나온 원귀는 보스와 함께 사라진다
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
                DespawnSummoned();
                pitCenters.Clear(); // 구덩이는 보스전 끝까지만
                quakeHold.Value = 0f;
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
            quakeChainLeft = 0;
        }

        private void SetState(ServerState next, BossAnim anim)
        {
            SetState(next, anim, AnimSpeedFor(anim));
        }

        private void SetState(ServerState next, BossAnim anim, float speed)
        {
            state = next;
            stateTime = 0f;
            hitApplied = false;
            animSpeed.Value = speed;
            SetAnim(anim, true);
        }

        private void SetAnim(BossAnim anim, bool restart = false)
        {
            if (animState.Value == (byte)anim && !restart)
            {
                return;
            }
            if (!restart)
            {
                animSpeed.Value = AnimSpeedFor(anim); // 상태 안에서 바뀌는 모션(걷기 ↔ 대기 등)은 기본 속도
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

        private float SlamRadius() => Mathf.Max(1f, scale * 0.6f);

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
                Knockback(player, to.sqrMagnitude > 0.0001f ? to.normalized : direction, 1f);
            }
        }

        /// <summary>도약 착지 충격파 - 반경 안 플레이어를 바깥으로 밀어낸다(피해 없음).</summary>
        private void PushPlayersAround(Vector2 center, float radius, bool realmOnly)
        {
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.IsDead || IsRealmPlayer(player.gameObject) != realmOnly)
                {
                    continue;
                }
                Vector2 to = (Vector2)player.transform.position - center;
                if (to.sqrMagnitude > radius * radius)
                {
                    continue;
                }
                Knockback(player, to.sqrMagnitude > 0.0001f ? to.normalized : Vector2.up, 0.8f);
            }
        }

        private void Knockback(PlayerHealth player, Vector2 direction, float strength)
        {
            if (player.IsDead || player == heldPlayer || !player.TryGetComponent(out PlayerMovement movement))
            {
                return;
            }
            float impact = Mathf.Sqrt(ImpactScale()); // 최종전은 더 멀리(연출 배율의 제곱근 - 너무 날아가지 않게)
            movement.ServerKnockback(direction * stats.knockbackSpeed * impact * strength, stats.knockbackSeconds);
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
                // 261122 - 도는 중에 풀려나면(동료가 때림 등) 벽 · 물체 안에 낀 채로 남지 않게 가장 가까운 빈 자리로.
                MapFlowField field = MapFlowField.Instance;
                if (field != null)
                {
                    PlaceHeld(field.ResolveStuck(heldPlayer.transform.position, HeldBodyRadius));
                }
                movement.ServerSetHeld(false);
            }
            heldPlayer = null;
        }

        private const float HeldBodyRadius = 0.3f;

        /// <summary>261122 - 던질 자리를 걸을 수 있는 곳으로(MapFlowField가 없는 옛 맵이면 그대로).</summary>
        private static Vector2 ResolveLanding(Vector2 from, Vector2 to)
        {
            MapFlowField field = MapFlowField.Instance;
            return field != null ? field.ResolveLanding(from, to, HeldBodyRadius) : to;
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
            cloneDamage = BossStatsSO.Pick(stats.basicDamage, stage) * BossStatsSO.Pick(stats.mentalCloneDamageRatio, stage) * DamageScale();

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
                    RealmTelegraphRpc(cloneServerPosition, cloneAttackDirection, reach, stats.basicArcDegrees, stats.basicWindup);
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
                    ImpactRpc(cloneServerPosition, cloneAttackDirection, (byte)ImpactKind.CloneSwing, reach, stats.basicArcDegrees);
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
            mentalCooldownLeft = BossStatsSO.Pick(stats.mentalCooldown, stage); // 쿨타임은 지속시간이 끝난 뒤부터(예섭)
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

        /// <summary>261122 - 분신 예고. 끌려간 본인 화면에선 막 위(RealmTop)에 그려 흐리지 않는다.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void RealmTelegraphRpc(Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds)
        {
            int layer = realmLocalInside && TryGetRealmLayers() ? realmTopLayer : DefaultSortingLayer();
            BossTelegraph.Show(telegraphMaterial, center, direction, radius, arcDegrees, seconds, 1f, layer);
        }

        /// <summary>전조 시간에 맞춰 늘린 모션의 재생 속도(판정 프레임이 전조가 끝나는 순간에 오도록).</summary>
        private float AnimSpeedFor(BossAnim anim)
        {
            if (stats == null)
            {
                return 1f;
            }
            switch (anim)
            {
                case BossAnim.Atk:
                    return AtkHitFrameTime / Mathf.Max(0.05f, stats.basicWindup);
                case BossAnim.Grab:
                    return GrabCheckSeconds / Mathf.Max(0.05f, stats.grabWindupSeconds);
                case BossAnim.Mental:
                    return MentalCastSeconds / Mathf.Max(0.05f, stats.mentalWindupSeconds);
                default:
                    return 1f;
            }
        }

        /// <summary>큰 기술 전조 - 보스 머리 위 '!'(튀어나왔다가 끝날 즈음 깜빡임).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void WarnRpc(float seconds)
        {
            CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossWarn, transform.position);
            warnTimeLeft = seconds;
            warnDuration = Mathf.Max(0.05f, seconds);
        }

        /// <summary>정신병 대상 전조 - 그 플레이어 발밑에 따라다니는 표식.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void MarkTargetRpc(ulong targetObjectId, float seconds)
        {
            markTargetId = targetObjectId;
            markTimeLeft = seconds;
            markDuration = Mathf.Max(0.05f, seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapRpc(float windupSeconds, float airSeconds)
        {
            leapVisualStart = Time.time;
            leapVisualWindup = windupSeconds;
            leapVisualAir = airSeconds;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlashRpc()
        {
            flashTimer = 0.1f;
            hitPunchTimer = 0.08f; // 맞을 때 몸이 움찔(짧은 찌그러짐)
        }

        /// <summary>
        /// 타격 연출(로컬): 이펙트 + 화면 흔들림 + 히트스톱(보스 모션 멈칫). 최종전은 ImpactScale만큼 전부 크게.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void ImpactRpc(Vector2 origin, Vector2 direction, byte kind, float radius, float arcDegrees)
        {
            if (stats == null)
            {
                return;
            }
            EnsureFxBounds();
            float impact = ImpactScale();
            // 261117 사운드 기획서 5장: 휘두르기는 SFX_BS_Whoosh, 내리꽂기 · 착지 · 등장은 SFX_BS_Impact.
            bool swing = kind == (byte)ImpactKind.Swing || kind == (byte)ImpactKind.CloneSwing;
            if (kind == (byte)ImpactKind.Quake)
            {
                // 261121 사운드 기획서 v1.2: 내리침 SFX_BS_Impact 크게 + SFX_Drum 1타 → 구덩이 SFX_Grudge 낮게.
                CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossQuakeImpact, origin);
                CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.BossQuakeDrum);
                CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossQuakeGrudge, origin);
            }
            else
            {
                CheonMyeong.Audio.GameAudio.PlayAt(swing ? CheonMyeong.Audio.SoundKeys.BossSwing : CheonMyeong.Audio.SoundKeys.BossSlam, origin);
            }
            switch ((ImpactKind)kind)
            {
                case ImpactKind.Swing:
                case ImpactKind.CloneSwing:
                {
                    // 실제 판정 부채꼴(원점 · 방향 · 반경 · 각도) 그대로: 초승달 앞 끝 = 사거리, 세로 폭 = 부채꼴 현.
                    bool clone = kind == (byte)ImpactKind.CloneSwing;
                    Color tint = clone ? new Color(0.75f, 0.5f, 1f, 1f) : Color.white;
                    bool flip = direction.x > 0f;
                    SpriteVisibleBounds.FitSlash(slashFxBounds, origin, direction, radius, arcDegrees, flip,
                                                 out Vector2 center, out float angle, out Vector2 slashScale);
                    BossFx.Play(slashFxFrames, center, angle, slashScale, 20f, 30, tint, flip);
                    PlayGroundBurst(origin + direction * radius * 0.7f, radius * 0.35f, 16f, 29, tint);
                    PlayerCameraRig.Shake(stats.impactShake * impact * (clone ? 0.6f : 1f), 0.16f + 0.05f * impact);
                    if (!clone)
                    {
                        hitStopTimer = stats.hitStopSeconds * impact;
                    }
                    break;
                }
                case ImpactKind.Slam:
                    PlayGroundBurst(origin, radius, 12f, 29, Color.white);
                    PlayerCameraRig.Shake(0.3f * impact, 0.3f + 0.1f * impact);
                    hitStopTimer = stats.hitStopSeconds * 1.5f * impact;
                    break;
                case ImpactKind.Land:
                    PlayGroundBurst(origin, radius, 14f, -30, Color.white);
                    PlayerCameraRig.Shake(0.16f * impact, 0.22f + 0.08f * impact);
                    break;
                case ImpactKind.Appear:
                    PlayGroundBurst(origin, radius, 10f, -30, Color.white);
                    PlayerCameraRig.Shake(0.25f * impact, 0.5f + 0.2f * impact);
                    break;
                case ImpactKind.Quake:
                    // 기획서 v1.2: QUAKE 73번 장에 억쇠 SLAM_FX(흙 터짐)를 2배 크기로 겹쳐 재생(전용 이펙트 없음).
                    PlayGroundBurst(origin, radius * 2f, 10f, 29, Color.white);
                    PlayerCameraRig.Shake(0.45f * impact, 0.45f + 0.1f * impact);
                    hitStopTimer = stats.hitStopSeconds * 1.5f * impact;
                    break;
            }
        }

        /// <summary>261117 - 잡기 동작이 시작될 때 전원에서 잡는 소리(SFX_BS_Grab).</summary>
        private void HandleAnimSound(byte previous, byte current)
        {
            if (current == (byte)BossAnim.Grab && previous != (byte)BossAnim.Grab)
            {
                CheonMyeong.Audio.GameAudio.PlayAt(CheonMyeong.Audio.SoundKeys.BossGrab, transform.position);
            }
        }

        private Rect slashFxBounds;
        private Rect burstFxBounds;
        private bool fxBoundsReady;

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

        /// <summary>땅 터짐을 반경 radius 원에 맞춰(가로 폭 = 지름) 그린다.</summary>
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
            // 261116 - 등장: 웃음 + 북 연타 + 보스전 곡. 사라지면(중간 보스 도망) 웨이브 곡으로.
            if (current && !previous)
            {
                CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.BossAppear);
                CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.BossDrum);
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
            leapVisualStart = -1f;
            hasLastVisualPosition = false;
            hitStopTimer = 0f;
            warnTimeLeft = 0f;
            markTimeLeft = 0f;
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
            if (realmVisualDirty)
            {
                realmVisualDirty = false;
                SetRealmVisual(realmActive.Value);
            }
            UpdateRealmFades();
            UpdateWarnIcon();
            UpdateTargetMark();

            if (!visible.Value || animator == null || bodyRenderer == null)
            {
                return;
            }

            if (animSerial.Value != playedAnimSerial)
            {
                playedAnimSerial = animSerial.Value;
                BossAnim anim = (BossAnim)animState.Value;
                animator.Play(AnimStateHashes[(int)anim], 0, 0f);
                // 공격 예고 시간에 맞춰 ATK 앞부분을 늘린다(타격 프레임이 예고가 끝나는 순간에 오도록) - 서버가 정한 속도.
                currentAnimSpeed = animSpeed.Value;
                animPlayStartTime = Time.time;
            }
            // 히트스톱: 타격 순간 보스 모션만 잠깐 멈춘다(게임 시간은 그대로 - 2인 온라인이라 전체 정지는 안 씀).
            hitStopTimer -= Time.deltaTime;
            if ((BossAnim)animState.Value == BossAnim.Quake)
            {
                DriveQuakeAnimation();
            }
            else
            {
                animator.speed = hitStopTimer > 0f ? 0f : currentAnimSpeed;
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
            float crouch = 0f;

            float leapElapsed = leapVisualStart >= 0f ? Time.time - leapVisualStart : -1f;
            if (leapElapsed >= 0f && stats != null && leapElapsed < leapVisualWindup + leapVisualAir
                && (BossAnim)animState.Value != BossAnim.Die)
            {
                // 도약: 웅크렸다가(anticipation) 높게 포물선, 공중에선 세로로 늘어남.
                hopPhase = 0f;
                if (leapElapsed < leapVisualWindup)
                {
                    crouch = Mathf.Clamp01(leapElapsed / Mathf.Max(0.01f, leapVisualWindup)) * 0.22f;
                }
                else
                {
                    float p = Mathf.Clamp01((leapElapsed - leapVisualWindup) / Mathf.Max(0.01f, leapVisualAir));
                    float arc = Mathf.Sin(p * Mathf.PI);
                    height = arc * stats.leapHeight;
                    stretch = arc * 0.14f;
                }
                walking = false;
            }
            else if (leapVisualStart >= 0f)
            {
                leapVisualStart = -1f;
                squashTimer = 0.14f; // 큰 착지
                hopPhase = 0f;
            }

            // 실제로 움직인 거리로 뛰는 높이를 정한다 - 서버가 "공중에서만 전진"하므로 그림의 점프와 실제 이동이 정확히 맞는다.
            Vector2 currentPosition = transform.position;
            float moved = hasLastVisualPosition ? Vector2.Distance(currentPosition, lastVisualPosition) : 0f;
            lastVisualPosition = currentPosition;
            hasLastVisualPosition = true;

            if (walking && stats != null)
            {
                float stride = Mathf.Max(0.05f, MoveSpeed() / Mathf.Max(0.05f, HopRate()));
                float previous = hopPhase;
                hopPhase += Mathf.Min(moved, stride * 0.5f) / stride;
                float fraction = hopPhase - Mathf.Floor(hopPhase);
                bool landed = Mathf.Floor(hopPhase) > Mathf.Floor(previous);
                if (!landed && moved < 0.0005f && fraction > 0.8f)
                {
                    hopPhase = Mathf.Ceil(hopPhase); // 멈춘 순간 = 착지(위치 보간 오차로 1.0에 못 닿아도)
                    landed = true;
                }
                if (landed)
                {
                    OnHopLanded();
                }
                float p = hopPhase - Mathf.Floor(hopPhase);
                float heightScale = IsFinal ? 1.25f : 1f;
                height = Mathf.Sin(p * Mathf.PI) * stats.hopHeight * heightScale;
                stretch = Mathf.Sin(p * Mathf.PI) * 0.07f;
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
                squash = Mathf.Clamp01(squashTimer / 0.09f) * stats.landingSquash * (IsFinal ? 1.35f : 1f);
            }
            squash = Mathf.Max(squash, crouch);
            if (hitPunchTimer > 0f)
            {
                hitPunchTimer -= dt;
                squash = Mathf.Max(squash, 0.06f);
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
            if (hideBossInRealm)
            {
                color.a = 0f; // 261122 - 차원 안(끌려간 본인 화면)에서는 진짜 억쇠가 원 안에 들어와도 안 보인다
            }
            bodyRenderer.color = color;

            if (shadowRenderer != null)
            {
                float maxHeight = stats != null ? Mathf.Max(0.01f, stats.hopHeight, stats.leapHeight) : 1f;
                float shrink = 1f - 0.45f * Mathf.Clamp01(height / maxHeight);
                shadowRenderer.transform.localScale = new Vector3(1.3f * shrink, 0.45f * shrink, 1f);
                Color shadowColor = encounter.Value == (byte)BossEncounter.Final
                    ? new Color(0.45f, 0.02f, 0.05f, 0.45f + 0.15f * Mathf.Sin(Time.time * 3f)) // 원한 오라
                    : new Color(0f, 0f, 0f, 0.4f);
                shadowColor.a *= hideBossInRealm ? 0f : fadeAlpha;
                shadowRenderer.color = shadowColor;
            }
        }

        /// <summary>한 걸음 착지 - 최종전은 쿵 소리 대신 화면이 울리고 먼지가 크게 인다(카메라에서 멀수록 약하게).</summary>
        private void OnHopLanded()
        {
            squashTimer = IsFinal ? 0.14f : 0.1f;
            float size = VisualSize();
            Vector2 feet = transform.position;
            PlayGroundBurst(feet, (IsFinal ? 0.9f : 0.45f) * size, 14f, -30, new Color(1f, 1f, 1f, IsFinal ? 0.8f : 0.5f));
            if (IsFinal && stats.finalStepShake > 0f && PlayerCameraRig.Instance != null)
            {
                float distance = Vector2.Distance(PlayerCameraRig.Instance.transform.position, feet);
                float falloff = Mathf.Clamp01(1f - distance / 14f);
                PlayerCameraRig.Shake(stats.finalStepShake * falloff, 0.12f);
            }
        }

        private void UpdateHealthBar()
        {
            bool show = visible.Value && encounter.Value != (byte)BossEncounter.None;
            if (!show)
            {
                // 261121 - 같은 씬 틀(BossBarFrame)을 어린 억쇠와 나눠 쓰므로, 숨을 때는 붙인 것을 걷어 틀을 돌려준다.
                if (healthBar != null)
                {
                    healthBar.Release();
                    healthBar = null;
                }
                return;
            }
            if (healthBar == null)
            {
                if (InGameHudScreen.Instance == null)
                {
                    return;
                }
                healthBar = BossHealthBarView.Create(InGameHudScreen.Instance.transform);
            }
            healthBar.SetVisible(true);
            string title = stats != null ? stats.displayName : "억쇠";
            title += encounter.Value == (byte)BossEncounter.Final ? " · 최종전" : " · 예행연습";
            float cloneRatio = cloneMaxHealth.Value > 0f ? cloneHealth.Value / cloneMaxHealth.Value : 0f;
            healthBar.SetBossBounds(TryGetBodyBounds(out Bounds body), body);
            healthBar.Refresh(title, health.Value, maxHealth.Value, bars.Value, realmActive.Value, cloneRatio);
        }

        // 261119 - 체력 바가 보스를 가리는지 볼 때 쓰는 "실제로 그려진 부분" 경계. 스프라이트(Tight 메시)마다 한 번만 계산해 둔다.
        private static readonly Dictionary<Sprite, Rect> VisibleRectCache = new Dictionary<Sprite, Rect>();
        private static readonly Sprite[] OneSprite = new Sprite[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisibleRectCache()
        {
            VisibleRectCache.Clear();
            OneSprite[0] = null;
        }

        /// <summary>261119 - 보스 몸(지금 프레임의 그려진 부분)의 월드 경계. 안 보이면 false.</summary>
        private bool TryGetBodyBounds(out Bounds bounds)
        {
            bounds = default;
            if (bodyRenderer == null || !bodyRenderer.enabled || bodyRenderer.sprite == null)
            {
                return false;
            }
            Sprite sprite = bodyRenderer.sprite;
            if (!VisibleRectCache.TryGetValue(sprite, out Rect local))
            {
                OneSprite[0] = sprite;
                local = SpriteVisibleBounds.Of(OneSprite);
                OneSprite[0] = null;
                VisibleRectCache[sprite] = local;
            }
            if (bodyRenderer.flipX)
            {
                local = Rect.MinMaxRect(-local.xMax, local.yMin, -local.xMin, local.yMax);
            }
            Transform t = bodyRenderer.transform;
            Vector3 a = t.TransformPoint(new Vector3(local.xMin, local.yMin, 0f));
            Vector3 b = t.TransformPoint(new Vector3(local.xMax, local.yMax, 0f));
            bounds = new Bounds((a + b) * 0.5f, Vector3.zero);
            bounds.Encapsulate(a);
            bounds.Encapsulate(b);
            return true;
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

        private CheonMyeong.Audio.SoundHandle realmAmbience;

        // 261123 - 지원 버그(멀티): 클라이언트에서 끌려간 본인 화면에 차원이 안 그려지고 자기 몸이 영혼 불로 보였다.
        // NGO는 NetworkVariable 변경을 선언 순서대로 읽으며 그 자리에서 OnValueChanged를 부른다 - realmActive가 realmPlayerId ·
        // realmCenter보다 먼저 선언돼 있어 콜백 시점엔 대상 ID가 아직 옛 값(0)이라 "나는 바깥"으로 판단했다(호스트는 서버가 값을
        // 순서대로 넣어 문제없음). 그래서 세 값 중 무엇이 바뀌든 표시만 '다시 그림' 표시해 두고, 같은 메시지의 값이 다 들어온 뒤
        // Update에서 한 번만 그린다.
        private bool realmVisualDirty;

        // 261124 - 예섭 버그 "끌려가면 잠깐 하늘색 화면": 에디터의 비동기 셰이더 컴파일은 처음 쓰는 셰이더 변형을 컴파일하는 동안
        // 하늘색(시안) 임시 셰이더로 그린다. 차원 막(RealmMask)은 화면을 다 덮어서 그게 하늘색 화면으로 보였다.
        // 보스가 있는 동안만 에디터에서 동기 컴파일로 바꿔(첫 사용 때 잠깐 멈출 뿐 하늘색은 안 나옴) 끝나면 원래 설정으로 돌린다.
        // 빌드는 셰이더가 미리 컴파일돼 있어 이 문제가 없다.
#if UNITY_EDITOR
        private static bool editorAsyncOverridden;
        private static bool editorAsyncPrevious;
#endif

        private static void EditorSyncShaders(bool on)
        {
#if UNITY_EDITOR
            if (on && !editorAsyncOverridden)
            {
                editorAsyncPrevious = UnityEditor.ShaderUtil.allowAsyncCompilation;
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
                editorAsyncOverridden = true;
            }
            else if (!on && editorAsyncOverridden)
            {
                UnityEditor.ShaderUtil.allowAsyncCompilation = editorAsyncPrevious;
                editorAsyncOverridden = false;
            }
#endif
        }

        private void HandleRealmTargetChanged(ulong previous, ulong current)
        {
            if (realmActive.Value)
            {
                realmVisualDirty = true;
            }
        }

        private void HandleRealmCenterChanged(Vector2 previous, Vector2 current)
        {
            if (realmActive.Value)
            {
                realmVisualDirty = true;
            }
        }

        private void HandleRealmChanged(bool previous, bool current)
        {
            realmVisualDirty = true;
            // 261116 - 정신병 차원: 들어가고 나올 때 휘감기는 소리, 안에 있는 동안 먹먹한 환경음(반복).
            if (current != previous)
            {
                CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.BossWarp);
            }
            CheonMyeong.Audio.GameAudio.Stop(realmAmbience);
            realmAmbience = current ? CheonMyeong.Audio.GameAudio.Play(CheonMyeong.Audio.SoundKeys.BossDimension) : default;
        }

        /// <summary>
        /// 차원 연출(261122 - 예섭 요청: 모데카이저 궁극기처럼).
        /// - 끌려간 본인 화면: 차원 원 밖은 완전히 가린다(가운데만 뚫린 불투명 막). 원 안에 들어와 있는 다른 플레이어 · 진짜 억쇠 ·
        ///   원귀도 안 보이게 한다 - 차원 안에는 나와 분신뿐. 내 캐릭터와 분신은 막 위로 올려 그린다.
        /// - 바깥 사람 화면: 차원 안 두 대상을 반투명하게 보이고 경계 원만 그린다(바깥에서는 진짜 억쇠와 1대1).
        /// 위치는 그대로라 카메라 처리가 필요 없다.
        /// </summary>
        private void SetRealmVisual(bool on)
        {
            RestoreRealmRenderers();
            realmVisualOn = on;
            realmLocalInside = false;
            if (realmRing != null)
            {
                realmRing.gameObject.SetActive(false);
            }
            if (realmRingFx != null)
            {
                realmRingFx.gameObject.SetActive(false);
            }
            if (realmDarkness != null)
            {
                realmDarkness.gameObject.SetActive(false);
            }
            if (!on)
            {
                return;
            }

            float radius = stats != null ? stats.mentalRealmRadius : 4.5f;
            NetworkObject localPlayer = NetworkManager != null && NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            realmLocalInside = localPlayer != null && localPlayer.NetworkObjectId == realmPlayerId.Value;
            bool shader = TryGetRealmLayers();
            if (shader)
            {
                // 261123 - 도트 경계: 본인 화면은 막 셰이더가 경계까지 그리고, 바깥 사람 화면은 같은 셰이더의 '경계만' 사본(Default 레이어).
                if (!realmLocalInside)
                {
                    EnsureRealmRingFx();
                    PlaceRealmSprite(realmRingFx.transform, radius);
                    realmRingFx.gameObject.SetActive(true);
                }
            }
            else
            {
                EnsureRealmRing();
                DrawRealmRing(realmCenter.Value, radius);
                realmRing.gameObject.SetActive(true);
            }
            if (realmLocalInside)
            {
                EnsureRealmDarkness();
                PlaceRealmDarkness(radius);
                realmDarkness.gameObject.SetActive(true);
                if (shader)
                {
                    PlaceRealmDecor(realmCenter.Value, radius);
                }
                BoostRenderers(localPlayer.gameObject);
            }
        }

        private void UpdateRealmFades()
        {
            if (!realmVisualOn)
            {
                return;
            }

            if (realmLocalInside)
            {
                if (realmDarkness != null)
                {
                    PlaceRealmDarkness(stats != null ? stats.mentalRealmRadius : 4.5f);
                }
                UpdateRealmFlowerGlow();
                if (cloneRenderer != null)
                {
                    if (TryGetRealmLayers())
                    {
                        cloneRenderer.sortingLayerID = realmTopLayer; // 순서는 몸과 같게 - 내 캐릭터와 Y정렬
                    }
                    else
                    {
                        cloneRenderer.sortingOrder = 1001;
                    }
                }
                HideOutsidersInRealm();
                return;
            }

            // 바깥 사람 화면 - 261122 2차(모데카이저 궁극기처럼): 차원 안 플레이어 · 분신은 본 모습을 숨기고 영혼 불로만 보인다.
            // 영혼 불 셰이더가 없으면 예전처럼 반투명(분신은 UpdateCloneView에서 이미 반투명 보라).
            Material flame = GetSoulFlameMaterial();
            Transform insideTransform = null;
            realmFaded.Clear();
            if (NetworkManager != null && NetworkManager.SpawnManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(realmPlayerId.Value, out NetworkObject inside) && inside != null)
            {
                insideTransform = inside.transform;
                inside.GetComponentsInChildren(realmFaded);
                for (int i = 0; i < realmFaded.Count; i++)
                {
                    Color c = realmFaded[i].color;
                    c.a = flame != null ? 0f : 0.35f;
                    realmFaded[i].color = c;
                }
            }
            if (cloneRenderer != null)
            {
                Color c = cloneRenderer.color;
                c.a = flame != null ? 0f : c.a * 0.45f;
                cloneRenderer.color = c;
            }
            if (flame != null)
            {
                Transform cloneTransform = cloneView != null && cloneView.activeSelf ? cloneView.transform : null;
                PlaceSoulFlame(ref playerFlame, flame, insideTransform, PlayerFlameColor, SoulFlameSize, PlayerFlameBaseY, "RealmPlayerFlame");
                PlaceSoulFlame(ref cloneFlame, flame, cloneTransform, CloneFlameColor, SoulFlameSize, CloneFlameBaseY, "RealmCloneFlame");
            }
        }

        // ---------- 261123 - 차원 원 밖 장식(끌려간 본인 화면): 나무 · 바위 · 형형색색 꽃 ----------
        // 261123b 예섭: 5배 많게, 크기는 캐릭터(170~180cm ≈ 0.94유닛)에 맞춰 나무는 캐릭터보다 크게, 돌도 크게, 꽃은 여러 색.
        // 막(RealmFx 0) → 장식(RealmFx 5, Y정렬) → 회상 안개(RealmFx 20, 원 밖만) 순으로 그려 장식까지 같이 흐릿해진다.
        // 차원 안 내 캐릭터 · 분신은 RealmTop이라 늘 그 위. 조명 없는 Sprites-Default라 2D 라이트와 무관.
        private const int RealmDecorCount = 35;
        private const int RealmFlowerCount = 55;
        private const float RealmDecorInner = 0.8f;
        private const float RealmDecorOuter = 11f;
        private const int RealmDecorSortingOrder = 5;
        private const int RealmFogSortingOrder = 20;
        private const float MetersToUnits = 0.94f / 1.75f; // 캐릭터 그림(약 0.94유닛) = 175cm 기준
        private static readonly Color RealmDecorTint = new Color(0.6f, 0.64f, 0.8f, 1f); // 261123d - 안개 낀 저녁 숲: 푸르스름하게 어둡게(꽃은 그대로 밝게)
        private readonly List<SpriteRenderer> realmDecor = new List<SpriteRenderer>(RealmDecorCount);
        private readonly List<SpriteRenderer> realmFlowers = new List<SpriteRenderer>(RealmFlowerCount);
        private readonly List<SpriteRenderer> realmFlowerGlows = new List<SpriteRenderer>(RealmFlowerCount);
        private readonly List<float> realmFlowerPhases = new List<float>(RealmFlowerCount);
        private readonly List<Color> realmFlowerGlowColors = new List<Color>(RealmFlowerCount);
        private readonly List<Vector2> realmDecorSpots = new List<Vector2>(RealmDecorCount + RealmFlowerCount);
        private readonly List<float> realmDecorGaps = new List<float>(RealmDecorCount + RealmFlowerCount);
        private Texture2D realmFlowerTexture;
        private Sprite[] realmFlowerSprites;
        private Texture2D realmGlowTexture;
        private Sprite realmGlowSprite;
        private SpriteRenderer realmFog;
        private Material realmFogMaterial;
        private static readonly int FogOnlyId = Shader.PropertyToID("_FogOnly");

        /// <summary>
        /// 장식 그림별 높이(미터, 최소 ~ 최대). realmDecorSprites 순서와 맞춘다 - 이름으로 고르면 그림 순서가 바뀌어도 안전하다.
        /// 작은 나무 5~7m, 대나무 6~8m, 큰 나무 9~11m, 바위 1.2~2m, 풀포기 0.5~0.7m.
        /// </summary>
        private static Vector2 DecorHeightMeters(Sprite sprite)
        {
            string n = sprite != null ? sprite.name : string.Empty;
            if (n.Contains("bamboo")) return new Vector2(6f, 8f);
            if (n == "Obj_tree") return new Vector2(9f, 11f);
            if (n.Contains("tree")) return new Vector2(5f, 7f);
            if (n.Contains("rock")) return new Vector2(1.2f, 2f);
            if (n.Contains("tuft")) return new Vector2(0.5f, 0.7f);
            return new Vector2(2f, 3f);
        }

        private void PlaceRealmDecor(Vector2 center, float radius)
        {
            EnsureRealmDecorSprites();
            EnsureRealmFog();
            PlaceRealmSprite(realmFog.transform, radius);
            realmFog.gameObject.SetActive(true);

            realmDecorSpots.Clear();
            realmDecorGaps.Clear();
            int sortingLayer = realmFxLayer;
            int decorKinds = realmDecorSprites != null ? realmDecorSprites.Length : 0;
            int placed = 0;
            for (int i = 0; i < RealmDecorCount && decorKinds > 0; i++)
            {
                // 앞쪽(작은 나무 · 대나무)이 더 자주 나오게: 0번부터 가중치가 줄어든다.
                int kind = Mathf.Min(decorKinds - 1, Mathf.FloorToInt(UnityEngine.Random.value * UnityEngine.Random.value * decorKinds));
                Sprite sprite = realmDecorSprites[kind];
                if (sprite == null || sprite.bounds.size.y <= 0.001f)
                {
                    continue;
                }
                Vector2 meters = DecorHeightMeters(sprite);
                float height = Mathf.Lerp(meters.x, meters.y, UnityEngine.Random.value) * MetersToUnits;
                float scale = height / sprite.bounds.size.y;
                float width = sprite.bounds.size.x * scale;
                // 원 아래쪽은 위로 솟은 그림이 씨름판을 덮지 않게 높이만큼 더 띄운다. 간격 = 폭의 70%.
                if (!TryPickRealmSpot(center, radius, Mathf.Max(0.6f, width * 0.7f), height, out Vector2 spot))
                {
                    continue;
                }
                SpriteRenderer sr = GetPooled(realmDecor, placed++, "RealmDecor");
                sr.sprite = sprite;
                sr.flipX = UnityEngine.Random.value < 0.5f;
                sr.color = RealmDecorTint;
                sr.sortingLayerID = sortingLayer;
                sr.sortingOrder = RealmDecorSortingOrder;
                sr.transform.position = new Vector3(spot.x, spot.y, 0f);
                sr.transform.localScale = new Vector3(scale, scale, 1f);
                sr.gameObject.SetActive(true);
            }

            realmFlowerPhases.Clear();
            realmFlowerGlowColors.Clear();
            int flowers = 0;
            for (int i = 0; i < RealmFlowerCount; i++)
            {
                if (!TryPickRealmSpot(center, radius, 0.35f, 0f, out Vector2 spot))
                {
                    continue;
                }
                int variant = UnityEngine.Random.Range(0, FlowerPalettes.Length);
                SpriteRenderer glow = GetPooled(realmFlowerGlows, flowers, "RealmFlowerGlow");
                glow.sprite = realmGlowSprite;
                glow.sortingLayerID = sortingLayer;
                glow.sortingOrder = RealmDecorSortingOrder - 1;
                glow.transform.position = new Vector3(spot.x, spot.y + 0.13f, 0f); // 꽃송이 가운데(바닥에서 약 8px)
                glow.gameObject.SetActive(true);
                SpriteRenderer flower = GetPooled(realmFlowers, flowers, "RealmFlower");
                flower.sprite = realmFlowerSprites[variant];
                flower.flipX = UnityEngine.Random.value < 0.5f;
                flower.color = Color.white;
                flower.sortingLayerID = sortingLayer;
                flower.sortingOrder = RealmDecorSortingOrder;
                flower.transform.position = new Vector3(spot.x, spot.y, 0f);
                flower.gameObject.SetActive(true);
                realmFlowerPhases.Add(UnityEngine.Random.value * Mathf.PI * 2f);
                Color32 light = FlowerPalettes[variant][2];
                realmFlowerGlowColors.Add(new Color(light.r / 255f, light.g / 255f, light.b / 255f, 1f));
                flowers++;
            }
            UpdateRealmFlowerGlow();
        }

        /// <summary>
        /// 경계 바깥 띠에서 이미 고른 자리들과 (서로의 간격 중 큰 값) 이상 떨어진 곳(최대 16번 시도).
        /// belowPad = 원 아래쪽일수록 더 띄울 거리(키 큰 그림이 원 안을 덮지 않게).
        /// </summary>
        private bool TryPickRealmSpot(Vector2 center, float radius, float minGap, float belowPad, out Vector2 spot)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float distance = radius + Mathf.Lerp(RealmDecorInner, RealmDecorOuter, Mathf.Sqrt(UnityEngine.Random.value))
                                 + belowPad * Mathf.Max(0f, -Mathf.Sin(angle));
                spot = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                bool clear = true;
                for (int i = 0; i < realmDecorSpots.Count && clear; i++)
                {
                    float gap = Mathf.Max(minGap, realmDecorGaps[i]);
                    clear = (realmDecorSpots[i] - spot).sqrMagnitude >= gap * gap;
                }
                if (clear)
                {
                    realmDecorSpots.Add(spot);
                    realmDecorGaps.Add(minGap);
                    return true;
                }
            }
            spot = default;
            return false;
        }

        private SpriteRenderer GetPooled(List<SpriteRenderer> pool, int index, string name)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject(name);
                go.SetActive(false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = telegraphMaterial; // Sprites-Default(조명 없음)
                pool.Add(sr);
            }
            return pool[index];
        }

        /// <summary>회상 안개: RealmMask 사본(_FogOnly = 1)이 원 밖(짚 둘레 바깥)만 옅은 회색으로 덮는다 - 장식 위.</summary>
        private void EnsureRealmFog()
        {
            if (realmFog != null)
            {
                return;
            }
            realmFogMaterial = new Material(realmMaskMaterial) { name = "RealmFogOnly" };
            realmFogMaterial.SetFloat(FogOnlyId, 1f);
            var go = new GameObject("EoksoeRealmFog");
            realmFog = go.AddComponent<SpriteRenderer>();
            realmFog.sprite = GetRealmMaskSprite();
            realmFog.sharedMaterial = realmFogMaterial;
            realmFog.sortingLayerID = realmFxLayer;
            realmFog.sortingOrder = RealmFogSortingOrder;
        }

        /// <summary>꽃 빛 무리: 초당 8칸으로 끊어 천천히 숨 쉬듯(도트 애니메이션처럼). 꽃마다 색 · 박자가 다르다.</summary>
        private void UpdateRealmFlowerGlow()
        {
            float t = Mathf.Floor(Time.time * 8f) / 8f;
            for (int i = 0; i < realmFlowerGlows.Count && i < realmFlowerPhases.Count; i++)
            {
                SpriteRenderer glow = realmFlowerGlows[i];
                if (glow == null || !glow.gameObject.activeSelf)
                {
                    continue;
                }
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.8f + realmFlowerPhases[i]);
                Color c = realmFlowerGlowColors[i];
                c.a = 0.18f + 0.22f * pulse;
                glow.color = c;
                float scale = 0.85f + 0.12f * pulse;
                glow.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void HideRealmDecor()
        {
            HideAll(realmDecor);
            HideAll(realmFlowers);
            HideAll(realmFlowerGlows);
            if (realmFog != null)
            {
                realmFog.gameObject.SetActive(false);
            }
        }

        private static void HideAll(List<SpriteRenderer> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].gameObject.activeSelf)
                {
                    list[i].gameObject.SetActive(false);
                }
            }
        }

        private void DestroyRealmDecor()
        {
            DestroyAll(realmDecor);
            DestroyAll(realmFlowers);
            DestroyAll(realmFlowerGlows);
            if (realmFog != null) Destroy(realmFog.gameObject);
            if (realmFogMaterial != null) Destroy(realmFogMaterial);
            if (realmFlowerSprites != null)
            {
                for (int i = 0; i < realmFlowerSprites.Length; i++)
                {
                    if (realmFlowerSprites[i] != null) Destroy(realmFlowerSprites[i]);
                }
            }
            if (realmFlowerTexture != null) Destroy(realmFlowerTexture);
            if (realmGlowSprite != null) Destroy(realmGlowSprite);
            if (realmGlowTexture != null) Destroy(realmGlowTexture);
        }

        private static void DestroyAll(List<SpriteRenderer> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    Destroy(list[i].gameObject);
                }
            }
            list.Clear();
        }

        // 꽃 팔레트 7가지: [0] 외곽선, [1] 꽃잎, [2] 밝은 꽃잎(빛 무리 색), [3] 꽃술.
        private static readonly Color32[][] FlowerPalettes =
        {
            new[] { new Color32(92, 22, 32, 255), new Color32(222, 62, 72, 255), new Color32(255, 146, 146, 255), new Color32(255, 222, 120, 255) },   // 빨강
            new[] { new Color32(112, 82, 22, 255), new Color32(242, 202, 52, 255), new Color32(255, 242, 152, 255), new Color32(200, 112, 40, 255) },  // 노랑
            new[] { new Color32(112, 42, 82, 255), new Color32(242, 122, 182, 255), new Color32(255, 192, 226, 255), new Color32(255, 242, 202, 255) }, // 분홍
            new[] { new Color32(62, 32, 102, 255), new Color32(152, 92, 222, 255), new Color32(206, 172, 255, 255), new Color32(255, 236, 150, 255) },  // 보라
            new[] { new Color32(92, 92, 112, 255), new Color32(226, 229, 240, 255), new Color32(255, 255, 255, 255), new Color32(250, 210, 80, 255) },  // 하양
            new[] { new Color32(112, 52, 22, 255), new Color32(246, 142, 52, 255), new Color32(255, 202, 132, 255), new Color32(255, 242, 182, 255) },  // 주황
            new[] { new Color32(28, 46, 92, 255), new Color32(70, 150, 235, 255), new Color32(150, 215, 255, 255), new Color32(235, 252, 255, 255) },   // 파랑
        };

        /// <summary>
        /// 꽃 7색(각 11×12px, 한 텍스처에 가로로) · 빛 무리(21×21px)를 한 번만 그린다(PPU 64 = 맵 물체와 같은 픽셀 크기, Point 필터).
        /// 꽃: 꽃잎 4장 + 밝은 테 + 꽃술 + 짧은 줄기 · 잎. 빛 무리: Bayer 디더링 원(흰색, 꽃마다 색을 입힘).
        /// </summary>
        private void EnsureRealmDecorSprites()
        {
            if (realmFlowerSprites != null)
            {
                return;
            }
            // . 투명, d 외곽선, b 꽃잎, l 밝은 꽃잎, w 꽃술, g 줄기, G 잎
            string[] rows =
            {
                "....ddd....",
                "...dlbld...",
                ".ddlbbbldd.",
                "dlbbllwbbld",
                "dbbllwwlbbd",
                ".ddbwwwbdd.",
                "..dlbbbld..",
                "...dbbbd...",
                "....dgd....",
                "..GG.g.....",
                "...GGg.GG..",
                ".....gGG...",
            };
            int w = rows[0].Length;
            int h = rows.Length;
            int variants = FlowerPalettes.Length;
            realmFlowerTexture = new Texture2D(w * variants, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RealmFlowers",
            };
            var px = new Color32[w * variants * h];
            var stem = new Color32(40, 92, 60, 255);
            var leaf = new Color32(56, 120, 72, 255);
            for (int v = 0; v < variants; v++)
            {
                Color32[] pal = FlowerPalettes[v];
                for (int y = 0; y < h; y++)
                {
                    string row = rows[h - 1 - y]; // 텍스처는 아래 줄부터
                    for (int x = 0; x < w; x++)
                    {
                        px[y * w * variants + v * w + x] = row[x] switch
                        {
                            'd' => pal[0],
                            'b' => pal[1],
                            'l' => pal[2],
                            'w' => pal[3],
                            'g' => stem,
                            'G' => leaf,
                            _ => new Color32(0, 0, 0, 0),
                        };
                    }
                }
            }
            realmFlowerTexture.SetPixels32(px);
            realmFlowerTexture.Apply(false, true);
            realmFlowerSprites = new Sprite[variants];
            for (int v = 0; v < variants; v++)
            {
                realmFlowerSprites[v] = Sprite.Create(realmFlowerTexture, new Rect(v * w, 0, w, h), new Vector2(0.5f, 0f), 64f);
            }

            const int g = 21;
            int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            realmGlowTexture = new Texture2D(g, g, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RealmFlowerGlow",
            };
            var gp = new Color32[g * g];
            float half = (g - 1) * 0.5f;
            for (int y = 0; y < g; y++)
            {
                for (int x = 0; x < g; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half; // 0 가운데 ~ 1 테두리
                    float density = Mathf.Clamp01(1f - d) * 1.3f;
                    float threshold = (bayer[(x & 3) + (y & 3) * 4] + 0.5f) / 16f;
                    gp[y * g + x] = density > threshold ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            realmGlowTexture.SetPixels32(gp);
            realmGlowTexture.Apply(false, true);
            realmGlowSprite = Sprite.Create(realmGlowTexture, new Rect(0, 0, g, g), new Vector2(0.5f, 0.5f), 64f);
        }

        // ---------- 261122 2차 - 바깥 화면 영혼 불 ----------
        private const string SoulFlameMaterialPath = "SoulFlame"; // Assets/Resources/SoulFlame.mat
        private const int SoulFlameSortingOrder = 50;
        // 261124 - 예섭: 아군 = 하늘색, 억쇠(분신) = 보라색. 셰이더가 흰빛 쪽으로 연하게 그라데이션한다.
        private static readonly Color PlayerFlameColor = new Color(0.40f, 0.78f, 1.00f, 1f);  // 하늘색 넋(아군)
        private static readonly Color CloneFlameColor = new Color(0.68f, 0.42f, 1.00f, 1f);   // 보라 넋(억쇠 분신)
        // 261123 - 예섭: 영혼 불은 캐릭터 키(약 1유닛)의 절반, 플레이어 · 분신(보스) 같은 크기에 색만 다르게.
        private static readonly Vector2 SoulFlameSize = new Vector2(0.34f, 0.5f);
        // 불꽃 밑동 높이(대상 피벗 기준). 플레이어 피벗은 얼굴 높이라 몸통 가운데(-0.3)에 오게 -0.55, 분신은 피벗이 발치라 거의 그대로.
        private const float PlayerFlameBaseY = -0.55f;
        private const float CloneFlameBaseY = -0.05f;
        private static bool soulFlameChecked;
        private static Material soulFlameMaterial;
        private static Sprite soulFlameSprite;
        private SpriteRenderer playerFlame;
        private SpriteRenderer cloneFlame;

        private static Material GetSoulFlameMaterial()
        {
            if (!soulFlameChecked)
            {
                soulFlameChecked = true;
                soulFlameMaterial = Resources.Load<Material>(SoulFlameMaterialPath);
                if (soulFlameMaterial != null && (soulFlameMaterial.shader == null || !soulFlameMaterial.shader.isSupported))
                {
                    soulFlameMaterial = null;
                }
                if (soulFlameMaterial != null && soulFlameSprite == null)
                {
                    // 1유닛 정사각, 피벗 아래 가운데(발밑에서 불이 솟게). 텍스처는 내장 흰색이라 따로 해제할 것 없음.
                    soulFlameSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0f), 4f);
                    soulFlameSprite.name = "SoulFlameQuad";
                }
            }
            return soulFlameMaterial;
        }

        private static void PlaceSoulFlame(ref SpriteRenderer flame, Material material, Transform target, Color color, Vector2 size, float baseY, string name)
        {
            if (target == null)
            {
                if (flame != null && flame.gameObject.activeSelf)
                {
                    flame.gameObject.SetActive(false);
                }
                return;
            }
            if (flame == null)
            {
                var go = new GameObject(name);
                flame = go.AddComponent<SpriteRenderer>();
                flame.sprite = soulFlameSprite;
                flame.sharedMaterial = material;
                flame.sortingOrder = SoulFlameSortingOrder;
            }
            if (!flame.gameObject.activeSelf)
            {
                flame.gameObject.SetActive(true);
            }
            Vector3 p = target.position;
            flame.transform.position = new Vector3(p.x, p.y + baseY, 0f);
            flame.transform.localScale = new Vector3(size.x, size.y, 1f);
            flame.color = color;
        }

        private void HideSoulFlames()
        {
            if (playerFlame != null)
            {
                playerFlame.gameObject.SetActive(false);
            }
            if (cloneFlame != null)
            {
                cloneFlame.gameObject.SetActive(false);
            }
        }

        // ---------- 261122 차원 안(끌려간 본인 화면)에서 다른 것 숨기기 ----------

        private bool realmLocalInside;
        private bool hideBossInRealm;
        private readonly List<SpriteRenderer> realmHiddenOthers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> realmHiddenEnemies = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> realmEnemyScratch = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> realmRendererBuffer = new List<SpriteRenderer>(8);
        private readonly Collider2D[] realmOverlap = new Collider2D[256];
        private ContactFilter2D realmEnemyFilter;
        private bool realmFilterReady;

        /// <summary>
        /// 원 안에 있는 다른 플레이어 · 원귀를 투명하게, 진짜 억쇠도 원 안이면 투명하게(UpdateHopAndTint가 hideBossInRealm을 본다).
        /// 원귀는 원을 드나드니 매 프레임 다시 고르고, 원 밖으로 나간 것은 되돌린다(Enemy 클래스와 겹치지 않게 색 알파만 쓴다).
        /// </summary>
        private void HideOutsidersInRealm()
        {
            Vector2 center = realmCenter.Value;
            float radius = stats != null ? stats.mentalRealmRadius : 4.5f;
            float radiusSqr = radius * radius;

            hideBossInRealm = ((Vector2)transform.position - center).sqrMagnitude <= (radius + 1.5f * VisualSize()) * (radius + 1.5f * VisualSize());

            ulong insideId = realmPlayerId.Value;
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player == null || player.NetworkObjectId == insideId)
                {
                    continue;
                }
                bool inCircle = ((Vector2)player.transform.position - center).sqrMagnitude <= radiusSqr;
                player.GetComponentsInChildren(realmRendererBuffer);
                for (int r = 0; r < realmRendererBuffer.Count; r++)
                {
                    SpriteRenderer sr = realmRendererBuffer[r];
                    SetAlpha(sr, inCircle ? 0f : 1f);
                    if (inCircle && !realmHiddenOthers.Contains(sr))
                    {
                        realmHiddenOthers.Add(sr);
                    }
                }
            }

            if (!realmFilterReady)
            {
                realmFilterReady = true;
                realmEnemyFilter = new ContactFilter2D { useTriggers = true };
                realmEnemyFilter.SetLayerMask(1 << gameObject.layer); // 억쇠와 같은 Enemy 레이어
            }
            realmEnemyScratch.Clear();
            int count = Physics2D.OverlapCircle(center, radius, realmEnemyFilter, realmOverlap);
            for (int i = 0; i < count; i++)
            {
                Collider2D col = realmOverlap[i];
                if (col == null || col.GetComponentInParent<EoksoeBoss>() != null)
                {
                    continue;
                }
                if (col.TryGetComponent(out SpriteRenderer sr))
                {
                    realmEnemyScratch.Add(sr);
                    SetAlpha(sr, 0f);
                }
            }
            for (int i = realmHiddenEnemies.Count - 1; i >= 0; i--)
            {
                SpriteRenderer sr = realmHiddenEnemies[i];
                if (sr == null || !realmEnemyScratch.Contains(sr))
                {
                    SetAlpha(sr, 1f);
                    realmHiddenEnemies.RemoveAt(i);
                }
            }
            for (int i = 0; i < realmEnemyScratch.Count; i++)
            {
                if (!realmHiddenEnemies.Contains(realmEnemyScratch[i]))
                {
                    realmHiddenEnemies.Add(realmEnemyScratch[i]);
                }
            }
        }

        private static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            if (sr == null)
            {
                return;
            }
            Color c = sr.color;
            if (!Mathf.Approximately(c.a, alpha))
            {
                c.a = alpha;
                sr.color = c;
            }
        }

        private void RestoreHiddenOutsiders()
        {
            for (int i = 0; i < realmHiddenOthers.Count; i++)
            {
                SetAlpha(realmHiddenOthers[i], 1f);
            }
            realmHiddenOthers.Clear();
            for (int i = 0; i < realmHiddenEnemies.Count; i++)
            {
                SetAlpha(realmHiddenEnemies[i], 1f);
            }
            realmHiddenEnemies.Clear();
            hideBossInRealm = false;
        }

        /// <summary>뚫린 막을 차원 가운데에 - 구멍 반지름 = 차원 반지름(텍스처 구멍 RealmHolePixels 칸 = 반지름).</summary>
        private void PlaceRealmDarkness(float radius)
        {
            PlaceRealmSprite(realmDarkness.transform, radius);
        }

        private void PlaceRealmSprite(Transform target, float radius)
        {
            Vector2 c = realmCenter.Value;
            target.position = new Vector3(c.x, c.y, 0f);
            float scale = radius / RealmHolePixels;
            target.localScale = new Vector3(scale, scale, 1f);
        }

        // 261123 - 바깥 사람 화면의 도트 경계 원(RealmMask 사본, _RingOnly = 1). 화면 텍스처를 안 읽어서 Default 레이어에 그린다.
        private SpriteRenderer realmRingFx;
        private Material realmRingMaterial;
        private static readonly int RingOnlyId = Shader.PropertyToID("_RingOnly");

        private void EnsureRealmRingFx()
        {
            if (realmRingFx != null)
            {
                return;
            }
            realmRingMaterial = new Material(realmMaskMaterial) { name = "RealmRingOnly" };
            realmRingMaterial.SetFloat(RingOnlyId, 1f);
            var go = new GameObject("EoksoeRealmRingFx");
            realmRingFx = go.AddComponent<SpriteRenderer>();
            realmRingFx.sprite = GetRealmMaskSprite();
            realmRingFx.sharedMaterial = realmRingMaterial;
            realmRingFx.sortingOrder = 1002;
        }

        /// <summary>
        /// 내 캐릭터를 막 위로. RealmTop 정렬 레이어가 있으면 레이어만 옮긴다(순서 유지 → 분신과 Y정렬, 흐림 텍스처에도 안 찍힘).
        /// 레이어가 없으면(설정 전 프로젝트) 예전처럼 순서 +1000.
        /// </summary>
        private void BoostRenderers(GameObject target)
        {
            target.GetComponentsInChildren(realmBoosted);
            realmBoostedOrders.Clear();
            realmBoostedLayers.Clear();
            bool layers = TryGetRealmLayers();
            for (int i = 0; i < realmBoosted.Count; i++)
            {
                realmBoostedOrders.Add(realmBoosted[i].sortingOrder);
                realmBoostedLayers.Add(realmBoosted[i].sortingLayerID);
                if (layers)
                {
                    realmBoosted[i].sortingLayerID = realmTopLayer;
                }
                else
                {
                    realmBoosted[i].sortingOrder += 1000;
                }
            }
        }

        private void RestoreRealmRenderers()
        {
            RestoreHiddenOutsiders();
            HideSoulFlames();
            HideRealmDecor();
            for (int i = 0; i < realmBoosted.Count; i++)
            {
                if (realmBoosted[i] != null && i < realmBoostedOrders.Count)
                {
                    realmBoosted[i].sortingOrder = realmBoostedOrders[i];
                    realmBoosted[i].sortingLayerID = realmBoostedLayers[i];
                }
            }
            realmBoosted.Clear();
            realmBoostedOrders.Clear();
            realmBoostedLayers.Clear();

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
                cloneRenderer.sortingLayerID = DefaultSortingLayer();
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

        // 261122 - 가운데만 뚫린 막: 512px 텍스처, 구멍 반지름 32px(= 차원 반지름) → 막 전체 폭은 차원 반지름의 16배라 화면을 다 덮는다.
        private const int RealmMaskSize = 512;
        private const float RealmHolePixels = 32f;
        private static Texture2D realmMaskTexture;
        private static Sprite realmMaskSprite;

        private void EnsureRealmDarkness()
        {
            if (realmDarkness != null)
            {
                return;
            }
            var go = new GameObject("EoksoeRealmDarkness");
            realmDarkness = go.AddComponent<SpriteRenderer>();
            realmDarkness.sprite = GetRealmMaskSprite();
            realmDarkness.drawMode = SpriteDrawMode.Simple;
            realmDarkness.color = Color.white;
            if (TryGetRealmLayers())
            {
                // 261122 - 셰이더 막: 원 안 = Default까지 그린 화면을 약하게 흐림, 원 밖 = 검푸른 노이즈(RealmMask.shader).
                realmDarkness.sharedMaterial = realmMaskMaterial;
                realmDarkness.sortingLayerID = realmFxLayer;
                realmDarkness.sortingOrder = 0;
            }
            else
            {
                // 대체: 텍스처 색 그대로(밖 = 불투명 검푸른색, 안 = 옅은 보라) - 정렬 레이어/셰이더가 없을 때.
                realmDarkness.sortingOrder = 1000;
            }
        }

        // ---------- 261122 차원 막 셰이더 · 정렬 레이어 ----------
        // RealmFx: 막(Default 다음 - 261123c 씨름터: 원 안 모래 · 원 밖 반투명 풀밭을 직접 그려 화면 텍스처는 안 읽음).
        // RealmTop: 차원 안 내 캐릭터 · 분신 · 분신 예고 · 경계 원(막 위, 흐림 없음).
        private const string RealmFxLayerName = "RealmFx";
        private const string RealmTopLayerName = "RealmTop";
        private const string RealmMaskMaterialPath = "RealmMask"; // Assets/Resources/RealmMask.mat
        private static bool realmLayersChecked;
        private static int realmFxLayer;
        private static int realmTopLayer;
        private static Material realmMaskMaterial;

        private static bool TryGetRealmLayers()
        {
            if (!realmLayersChecked)
            {
                realmLayersChecked = true;
                int fx = SortingLayer.NameToID(RealmFxLayerName);
                int top = SortingLayer.NameToID(RealmTopLayerName);
                if (fx != 0 && top != 0 && SortingLayer.IsValid(fx) && SortingLayer.IsValid(top))
                {
                    realmFxLayer = fx;
                    realmTopLayer = top;
                    realmMaskMaterial = Resources.Load<Material>(RealmMaskMaterialPath);
                    if (realmMaskMaterial != null && (realmMaskMaterial.shader == null || !realmMaskMaterial.shader.isSupported))
                    {
                        realmMaskMaterial = null;
                    }
                }
                if (realmMaskMaterial == null)
                {
                    Debug.LogWarning("[EoksoeBoss] 정렬 레이어 RealmFx/RealmTop 또는 Resources/RealmMask.mat이 없어 차원 막을 단색으로 그립니다.");
                }
            }
            return realmMaskMaterial != null;
        }

        private static int DefaultSortingLayer() => 0; // Default 정렬 레이어 uniqueID

        private static Sprite GetRealmMaskSprite()
        {
            if (realmMaskSprite != null)
            {
                return realmMaskSprite;
            }
            const int size = RealmMaskSize;
            realmMaskTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "EoksoeRealmMask",
            };
            var outside = new Color32(4, 8, 22, 255);   // 원 밖 - 아예 안 보임(검푸른색, 셰이더가 없을 때만 쓰임)
            var inside = new Color32(90, 40, 140, 50);  // 원 안 - 다른 차원 느낌으로 아주 옅게
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            const float feather = 3f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float t = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - RealmHolePixels) / feather + 0.5f);
                    pixels[y * size + x] = Color32.Lerp(inside, outside, t);
                }
            }
            realmMaskTexture.SetPixels32(pixels);
            realmMaskTexture.Apply(false, true);
            realmMaskSprite = Sprite.Create(realmMaskTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1f);
            return realmMaskSprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRealmMask()
        {
            realmMaskSprite = null;
            realmMaskTexture = null;
            realmLayersChecked = false;
            realmMaskMaterial = null;
            soulFlameChecked = false;
            soulFlameMaterial = null;
            soulFlameSprite = null;
        }

        private static Sprite solidSprite;

        // ---------- 전조 표시(클라이언트) ----------

        private void UpdateWarnIcon()
        {
            bool show = warnTimeLeft > 0f && visible.Value;
            if (!show)
            {
                if (warnIcon != null && warnIcon.gameObject.activeSelf)
                {
                    warnIcon.gameObject.SetActive(false);
                }
                warnTimeLeft = Mathf.Max(0f, warnTimeLeft - Time.deltaTime);
                return;
            }
            if (warnIcon == null)
            {
                warnIcon = CreateWarnIcon();
            }
            warnTimeLeft -= Time.deltaTime;
            float elapsed = warnDuration - warnTimeLeft;
            // 튀어나오기(0.15초 오버슈트) → 유지 → 마지막 30%는 빠르게 깜빡임(곧 온다).
            float pop = elapsed < 0.15f ? Mathf.Lerp(0f, 1.35f, elapsed / 0.15f) : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01((elapsed - 0.15f) / 0.1f));
            bool blinkOff = warnTimeLeft < warnDuration * 0.3f && Mathf.Repeat(Time.time * 12f, 1f) < 0.35f;
            warnIcon.gameObject.SetActive(!blinkOff);
            warnIcon.localScale = new Vector3(pop, pop, 1f);
            float headHeight = visualBaseOffset.y + 1.45f;
            warnIcon.localPosition = new Vector3(0f, headHeight, 0f);
        }

        private Transform CreateWarnIcon()
        {
            Font font = CheonMyeong.UI.GameFontSet.Get(CheonMyeong.UI.FontRole.Title);
            var root = new GameObject("BossWarnIcon").transform;
            root.SetParent(transform, false);
            CreateWarnText(root, font, new Color(0.1f, 0f, 0f, 1f), new Vector3(0.04f, -0.04f, 0f), 1100);
            CreateWarnText(root, font, new Color(1f, 0.15f, 0.1f, 1f), Vector3.zero, 1101);
            return root;
        }

        private static void CreateWarnText(Transform parent, Font font, Color color, Vector3 offset, int sortingOrder)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.text = "!";
            text.fontSize = 64;
            text.characterSize = 0.1f;
            text.fontStyle = FontStyle.Normal;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = font.material;
            meshRenderer.sortingOrder = sortingOrder;
        }

        private void UpdateTargetMark()
        {
            Transform target = null;
            if (markTimeLeft > 0f && visible.Value && NetworkManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(markTargetId, out NetworkObject networkObject) && networkObject != null)
            {
                target = networkObject.transform;
            }
            markTimeLeft = Mathf.Max(0f, markTimeLeft - Time.deltaTime);

            if (target == null)
            {
                if (markRing != null && markRing.enabled)
                {
                    markRing.enabled = false;
                }
                return;
            }
            if (markRing == null)
            {
                markRing = CreateMarkRing();
            }
            markRing.enabled = true;

            // 줄어드는 고리: 크게 시작해 차원이 열리는 순간 발밑에 딱 붙는다(언제 끌려가는지 보이게).
            float progress = 1f - markTimeLeft / markDuration;
            float radius = Mathf.Lerp(1.6f, 0.55f, progress * progress);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
            Color color = Color.Lerp(new Color(0.65f, 0.2f, 0.95f, 0.8f), new Color(1f, 0.15f, 0.2f, 1f), progress);
            color.a *= 0.65f + 0.35f * pulse;
            markRing.startColor = color;
            markRing.endColor = color;
            markRing.widthMultiplier = 0.09f + 0.04f * pulse;
            Vector3 center = target.position;
            int count = markRing.positionCount;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                markRing.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius * 0.6f, 0f));
            }
        }

        private LineRenderer CreateMarkRing()
        {
            var go = new GameObject("BossTargetMark");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 40;
            line.sharedMaterial = telegraphMaterial;
            line.sortingOrder = -45;
            line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            return line;
        }

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
