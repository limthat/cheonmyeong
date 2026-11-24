using System.Collections.Generic;
using CheonMyeong.Combat;
using CheonMyeong.Data;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 기본 적 - 네트워크 버전. 이동 계산/접촉 피해 판정/체력·사망 처리는 전부
    /// 서버만 수행한다(IsServer 가드) - 클라이언트도 리지드바디/콜라이더를
    /// 똑같이 갖고 있어서 로컬에서도 트리거 이벤트가 그대로 발생하기 때문에,
    /// 가드가 없으면 전원이 각자 피해를 적용해서 중복 피해가 생긴다. 서버가
    /// 계산한 위치는 같은 오브젝트의 NetworkTransform이 전 클라이언트로 복제한다.
    ///
    /// 타겟팅: 플레이어가 네트워크 프리팹으로 바뀌면서 씬에 미리 박아둔 단일
    /// Transform을 고정 타겟으로 쫓던 예전 방식이 더 이상 유효하지 않다(멀티플레이에선
    /// 플레이어가 여럿이고, 접속/사망 여부도 바뀐다) - 그래서 매 FixedUpdate마다
    /// PlayerHealth.FindNearestAlivePlayer로 "가장 가까운 살아있는 플레이어"를
    /// 서버에서 다시 찾는다. AI 타겟팅 전략(어그로/우선순위 등)은 미정이라 일단
    /// 가장 단순한 최근접 추적으로 구현.
    ///
    /// 분리(Separation): 적끼리 뭉치지 않고 퍼져서 쫓아오도록 추적 방향에 "주변
    /// 적들로부터 밀려나는 힘"을 더한다. Physics2D.OverlapCircle 같은 물리 쿼리는
    /// 쓰지 않았다 - EnemyPool이 Physics2D.IgnoreLayerCollision으로 적-적 레이어
    /// 충돌을 꺼뒀는데, 이게 OverlapCircle 같은 수동 쿼리에도 영향을 주는지 공식
    /// 문서로 확인이 안 돼서(확인 못 함) 불확실한 동작에 기대지 않기로 했다.
    /// 대신 ActiveEnemies 정적 목록을 직접 순회하는 단순 O(n) 방식을 쓴다 - 적
    /// 수백 마리 기준이면 매 FixedUpdate마다 수만 번의 벡터 연산 정도라 물리
    /// 엔진 트리거 이벤트 폭증(이전에 겪은 문제)과는 비용 성격이 다르고 충분히
    /// 싸다. 적 상한이 크게 늘어나면(수천 마리 이상) 공간 분할(그리드/쿼드트리)
    /// 최적화가 필요할 수 있음 - 지금은 미정.
    ///
    /// 풀 반환은 더 이상 자체 이벤트(OnDespawn)로 하지 않는다 - NetworkObject.Despawn()을
    /// 호출하면 EnemyPool이 등록해둔 INetworkPrefabInstanceHandler가 실제 Destroy
    /// 대신 그 처리를 가로채 풀로 반환한다(서버 자신도 포함해서 서버/클라이언트
    /// 양쪽에서 각자 호출됨 - NGO 2.13.3 소스(NetworkSpawnManager.OnDespawnObject)로 확인함).
    ///
    /// 콜라이더는 Trigger로 둔다 - 적끼리, 적과 플레이어가 서로 물리적으로 밀어내면
    /// 수백 마리가 뭉쳤을 때 서로 막혀서 멈추거나, 물리 연산 비용이 급격히 늘어난다.
    ///
    /// 피격 점멸/사망 경직: isDying은 NetworkVariable&lt;bool&gt;(Everyone 읽기/Server 쓰기)로
    /// 유지 - 풀링으로 재사용되는 오브젝트의 "지금 경직 중인지" 같은 지속 상태라서, 나중에
    /// 들어오는 클라이언트도 현재 값을 그대로 복제받아야 한다. 경직 중에는 FixedUpdate의
    /// 이동/추적과 OnTriggerStay2D의 접촉 피해 판정을 서버에서 멈춰서, 죽어가는 적이
    /// 계속 쫓아오거나 때리는 것처럼 보이지 않게 한다. 실제 풀 반환(소멸)은
    /// deathStaggerDuration이 다 지난 뒤에만 일어난다.
    ///
    /// 점멸(hitFlash)은 반대로 NetworkVariable이 아니라 Rpc다(261013 수정) - 원래
    /// NetworkVariable&lt;int&gt; 카운터 + OnValueChanged였는데, 공식 문서 확인(NGO 2.x,
    /// 2026-10-13) 결과 NetworkVariable 변경 전파는 매 프레임이 아니라 네트워크 틱 단위로
    /// 묶여서 나가기 때문에 "맞은 순간과 점멸 시작 사이에 한 박자 지연"이 생기는 버그가
    /// 있었다. 점멸은 상태가 아니라 "지금 막 맞았다"는 일회성 신호라 late-join 동기화가
    /// 필요 없어서, 호스트 자신에게는 같은 스택에서 즉시 실행되는 Rpc(ClientsAndHost)로
    /// 바꿨다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(NetworkObject))]
    public class Enemy : NetworkBehaviour, IDamageable
    {
        /// <summary>스폰돼 있는 모든 Enemy. 분리(Separation) 계산용 - 서버에서만 읽는다.</summary>
        private static readonly List<Enemy> ActiveEnemies = new List<Enemy>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveEnemies.Clear();
            AliveByStats.Clear();
            gridBuiltAt = -1f;
        }

        // 261114 최적화 - 맞았을 때 점멸 신호. 예전엔 타격마다 RPC 한 통이었는데(광역 · 관통 · 연속 공격이면 한 틱에 수십 통),
        // 이제 숫자만 올린다. NetworkVariable은 틱마다 마지막 값만 보내서 같은 틱의 여러 타격이 한 번으로 합쳐진다.
        private readonly NetworkVariable<byte> hitFlashSerial = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField] private EnemyStatsSO stats;
        [SerializeField] private SpriteRenderer spriteRenderer;

        /// <summary>261027 추가 - PartyProgress의 원한(怨恨) 집계가 grudgeWeight를 읽어가는 용도.</summary>
        public EnemyStatsSO Stats => stats;

        /// <summary>261027 추가 - 사망 경직 중인지(이미 쓰러짐). 처치 수 집계에서 "이번 타격으로 쓰러졌는지" 판단용.</summary>
        public bool IsDying => isDying.Value;

        [Header("분리(Separation) - 뭉치지 않고 퍼져서 쫓아오게. 값은 미정, 밸런스에 맞춰 조정")]
        [Tooltip("이 반경 안의 다른 적들로부터 밀려난다.")]
        [SerializeField] private float separationRadius = 1.1f;
        [Tooltip("추적 방향 대비 분리(밀어내기) 힘의 비중. 높을수록 더 퍼져서 쫓아오지만 추적이 둔해진다.")]
        [SerializeField] private float separationWeight = 2f;
        [Tooltip("261123 - 바로 앞을 막은 원귀가 있으면 옆으로 비켜 가는 힘(분리 힘에 더해짐). 줄지어 오는 것을 흩는다.")]
        [SerializeField] private float sidestepWeight = 1f;

        // 261101 추가 - "화면의 원귀 대부분이 덜덜 떤다"(예섭, 1인 호스트) 수정용. 원인은 조향 계산이었다:
        // (1) 추적+분리 합을 항상 길이 1로 정규화해서, 두 힘이 거의 상쇄되는 무리 안에서도 매 물리 스텝 최고 속도로
        //     방향이 뒤집혔다(분리가 1/거리²라 겹친 이웃이 좌우만 바뀌어도 값이 크게 튐).
        // (2) 원귀 콜라이더는 트리거라 플레이어 중심까지 파고드는데, 거리와 상관없이 최고 속도로 쫓아서 중심을 지나칠
        //     때마다 방향이 반대로 바뀌었다.
        // 고친 방식: 합을 정규화하지 않고 길이 1까지만 자른다(상쇄되면 느려짐), 분리는 거리에 비례해 부드럽게 줄어드는
        // 선형 가중, 목표 근처에서 감속(arrive), 속도는 가속도 한도 안에서만 바뀐다(한 스텝에 방향이 뒤집히지 않음).
        [Header("조향(Steering) - 261101 떨림 수정")]
        [Tooltip("플레이어 중심에서 이 거리 안이면 추적을 멈춘다. 원귀/플레이어 콜라이더 반지름 합(0.4+0.4)보다 작아야 접촉 피해가 유지된다.")]
        [SerializeField] private float arriveStopDistance = 0.3f;
        [Tooltip("이 거리부터 감속을 시작해 arriveStopDistance에서 0이 된다.")]
        [SerializeField] private float arriveSlowDistance = 0.7f;
        [Tooltip("속도가 바뀔 수 있는 최대 가속도(유닛/초²). 클수록 반응이 빠르고, 작을수록 부드럽다.")]
        [SerializeField] private float steeringAcceleration = 20f;

        private const float PitBodyRadius = 0.25f;
        private const float FlowDirectChaseDistance = 1.5f;

        // 261123 - 지원 피드백 "원귀가 고르게 퍼지지 않고 일자로 접근":
        // ① 플레이어까지 막힌 게 없으면 길찾기(거리장) 대신 곧장 쫓는다(확인은 원귀마다 0.25초에 한 번, 서로 엇갈리게).
        // ② 곧장 쫓을 때 원귀마다 정해진 각도(±35도)만큼 비스듬히 다가와, 멀리서는 옆으로 벌어지고 가까울수록 정면으로 모인다(둘러싸기).
        private const float LineOfSightInterval = 0.25f;
        private const float FlankStartDistance = 3f;
        private const float FlankFullDistance = 9f;
        private const float MaxFlankDegrees = 35f;
        private float nextLineOfSightCheck;
        private bool lineOfSightClear;
        private float flankDegrees;

        private Rigidbody2D rb;
        private float currentHealth;
        private float contactTimer;
        private float deathTimer;

        [Tooltip("261027 추가 - 이동 루프 + 소멸(Die 트리거) 애니메이터. Create Enemy Variants 메뉴가 연결한다.\n" +
                 "비어 있으면 애니메이션 없이 동작(점멸/경직만).")]
        [SerializeField] private Animator animator;

        private static readonly int DieTriggerHash = Animator.StringToHash("Die");
        private static readonly int MoveStateHash = Animator.StringToHash("Move");

        // 웨이브 종료 흡수(ServerAbsorbAllIntoGrudge)에서 목록을 복사해 돌기 위한 재사용 버퍼(할당 방지).
        private static readonly List<Enemy> AbsorbBuffer = new List<Enemy>();

        // 클라이언트 로컬 점멸 진행 상태 - 네트워크로 동기화할 필요 없음(순수 연출이라
        // 클라이언트마다 몇 프레임 어긋나도 문제 없음).
        private int blinksRemaining;
        private float blinkTimer;

        /// <summary>true면 사망 경직 중 - 이동/추적/접촉 피해가 멈춘다.</summary>
        private readonly NetworkVariable<bool> isDying = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private bool dyingSubscribed;

        // 261119 - 중형 원귀 스킬(없으면 일반 원귀).
        private EliteEnemy elite;

        // 261119 - 종류별로 지금 스폰돼 있는 수(서버). 동시 최대 수(EnemyStatsSO.maxAlive, 물귀신 6)에 쓴다.
        private static readonly Dictionary<EnemyStatsSO, int> AliveByStats = new Dictionary<EnemyStatsSO, int>();
        private bool countedAlive;

        /// <summary>261119 - 서버 전용. 이 종류가 지금 몇 마리 스폰돼 있는지.</summary>
        public static int ServerAliveCount(EnemyStatsSO statsKey) =>
            statsKey != null && AliveByStats.TryGetValue(statsKey, out int n) ? n : 0;

        /// <summary>261119 - 물귀신이 웅덩이에 잠겨 있으면 원거리 공격 · 자동 조준 대상이 아니다.</summary>
        public static bool IsHiddenFromRanged(Collider2D col) =>
            col != null && col.TryGetComponent(out EliteEnemy e) && e.HiddenFromRanged;

        private void Awake()
        {
            TryGetComponent(out elite);
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            // 261013 추가 - PlayerMovement.cs와 같은 이유(물리 틱 vs 렌더 프레임 불일치로
            // 생기는 계단식 움직임/떨림)로 적도 똑같이 Interpolate로 맞춘다 - 플레이어
            // 카메라가 적을 따라다니진 않지만, 화면에 보이는 움직임 자체가 매끄러워야
            // 일관된 느낌을 준다(실제 이동 계산은 그대로 서버 FixedUpdate에서 함, 영향 없음).
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            ApplyVisualScale();
        }

        /// <summary>261122 - 종류별 크기(EnemyStatsSO.visualScale). 프리팹마다 스탯이 고정이라 한 번만, 모든 화면에서 같은 값.</summary>
        private void ApplyVisualScale()
        {
            float scale = stats != null ? stats.visualScale : 1f;
            if (!Mathf.Approximately(scale, 1f))
            {
                transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        public override void OnNetworkSpawn()
        {
            ActiveEnemies.Add(this);
            if (IsServer && stats != null)
            {
                AliveByStats[stats] = ServerAliveCount(stats) + 1;
                countedAlive = true;
            }
            gridBuiltAt = -1f; // 목록이 바뀌었으니 다음 고정 프레임에 격자를 다시 만든다

            isDying.OnValueChanged += HandleDyingChanged;
            hitFlashSerial.OnValueChanged += HandleHitFlashChanged;
            dyingSubscribed = true;

            // 풀에서 재사용될 때 이전 생애의 소멸 애니메이션이 남지 않도록 이동 루프로 되돌린다.
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.ResetTrigger(DieTriggerHash);
                animator.Play(MoveStateHash, 0, 0f);
            }
        }

        public override void OnNetworkDespawn()
        {
            ActiveEnemies.Remove(this);
            if (countedAlive && stats != null)
            {
                AliveByStats[stats] = Mathf.Max(0, ServerAliveCount(stats) - 1);
            }
            countedAlive = false;
            gridBuiltAt = -1f;
            gridIndex = -1;

            // 풀링 때문에 이 컴포넌트는 파괴되지 않고 재사용된다 - 구독 해제를 안 하면
            // 디스폰/재스폰을 반복할 때마다 콜백이 계속 누적되는 전형적인 메모리 누수가 된다.
            if (dyingSubscribed)
            {
                isDying.OnValueChanged -= HandleDyingChanged;
                hitFlashSerial.OnValueChanged -= HandleHitFlashChanged;
                dyingSubscribed = false;
            }
        }

        /// <summary>
        /// 서버가 풀에서 꺼낸 직후 호출해 상태를 초기화한다(EnemyPool.GetAndSpawn 참고).
        /// 서버 전용 - 클라이언트는 NetworkTransform으로 결과 위치만 복제받는다.
        ///
        /// isDying을 여기서 false로 되돌리는 게 중요하다 - 이 컴포넌트는 풀링으로
        /// 재사용되기 때문에, 이전 생애의 사망 경직 상태(true)가 NetworkVariable
        /// 필드에 그대로 남아있을 수 있다(Despawn은 값을 초기화해주지 않음). 이
        /// 호출은 EnemyPool.GetAndSpawn에서 NetworkObject.Spawn()보다 먼저 일어나서
        /// 클라이언트에게는 처음부터 false로 복제된다.
        /// </summary>
        public void ServerInitialize()
        {
            // 261027 수정 - 기획서 5-10 "원귀 체력은 웨이브마다 +8%". 배율은 WaveConfigSO에서 온다.
            float healthMultiplier = WaveManager.Instance != null ? WaveManager.Instance.CurrentHealthMultiplier : 1f;
            currentHealth = (stats != null ? stats.maxHealth : 1f) * healthMultiplier;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
            contactTimer = 0f;
            deathTimer = 0f;
            isDying.Value = false;
            // 261114 - 풀에서 다시 나올 때 이전 상태 효과를 지운다.
            serverKnockbackLeft = 0f;
            serverStunUntil = 0f;
            serverSlowUntil = 0f;
            serverSlowRatio = 0f;
            serverTauntTarget = null;
            serverTauntUntil = 0f;
            flankDegrees = Random.Range(-MaxFlankDegrees, MaxFlankDegrees);
            lineOfSightClear = false;
            nextLineOfSightCheck = Time.time + Random.Range(0f, LineOfSightInterval); // 한 프레임에 몰리지 않게
            ServerClearBrand();
            ServerVulnerableSource = null;
            ServerVulnerableBonus = 0f;
            ServerVulnerableUntil = 0f;
            ServerBindCount = 0;
            ServerExposedUntil = 0f;
            ServerJointUntil = 0f;
            ServerLastHitter = null;
            ServerLastHitTime = -999f;
            ServerSummonedByBoss = false;
            if (elite != null)
            {
                elite.ServerReset();
            }
        }

        /// <summary>
        /// 261121 - 억쇠 '원귀 부르기'로 나온 원귀(서버). 영혼 · 엽전을 떨어뜨리지 않고, 보스전이 끝나면 같이 사라진다(기획서 v1.1).
        /// 풀에서 다시 나올 때 ServerInitialize가 false로 되돌린다.
        /// </summary>
        public bool ServerSummonedByBoss { get; set; }

        /// <summary>261121 - 서버 전용. 보스가 불러낸 원귀를 결과 처리(원한 · 드랍) 없이 전부 치운다.</summary>
        public static void ServerDespawnSummoned()
        {
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveEnemies);
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                Enemy enemy = AbsorbBuffer[i];
                if (enemy != null && enemy.IsServer && enemy.IsSpawned && enemy.ServerSummonedByBoss)
                {
                    enemy.NetworkObject.Despawn();
                }
            }
            AbsorbBuffer.Clear();
        }

        /// <summary>
        /// 261121 - 서버 전용. 땅 꺼짐 구덩이(area)에 있던 원귀는 같이 떨어져 사라진다(기획서 v1.2) - 처치가 아니라 드랍 · 원한 없음.
        /// 돌려주는 값 = 사라진 수.
        /// </summary>
        public static int ServerRemoveInArea(Rect area)
        {
            int removed = 0;
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveEnemies);
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                Enemy enemy = AbsorbBuffer[i];
                if (enemy == null || !enemy.IsServer || !enemy.IsSpawned || enemy.rb == null || !area.Contains(enemy.rb.position))
                {
                    continue;
                }
                enemy.NetworkObject.Despawn();
                removed++;
            }
            AbsorbBuffer.Clear();
            return removed;
        }

        /// <summary>
        /// 261027 추가 - 서버 전용. 웨이브가 끝날 때(징) 살아남은 원귀를 전부 원한으로 흡수한다
        /// (기획서 5-2/5-3: "남은 원귀는 원한으로 흡수", "남은 원귀 1마리 = 원한 1"). 흡수된 원귀는
        /// 드랍 없이 바로 풀로 돌아간다. 이미 죽어가는(경직 중) 원귀는 처치된 것이라 원한에서 뺀다.
        /// 돌려주는 값 = 이번에 쌓일 원한 양(grudgeWeight 합).
        /// </summary>
        /// <summary>
        /// 261027 추가 - 서버 전용. 판이 끝나거나 새 판이 시작될 때 남은 원귀를 원한 집계 없이 전부 치운다.
        /// NetworkObject는 기본적으로 씬이 언로드돼도 파괴되지 않고 활성 씬으로 옮겨져 살아남기 때문에
        /// (DestroyWithScene 기본값 false), 이걸 안 하면 "다시 하기" 때 이전 판의 원귀가 그대로 남아서
        /// 새 판 시작과 동시에 플레이어를 때려 바로 게임오버가 됐다.
        /// </summary>
        public static void ServerDespawnAll()
        {
            ServerAbsorbAllIntoGrudge();
        }

        /// <summary>261110 - 서버 전용. 지금 살아있는(쓰러지는 중 아닌) 원귀 위치를 buffer에 담는다 - 웨이브 끝 원한 흡수 연출용.</summary>
        public static void ServerCollectAlivePositions(List<Vector2> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < ActiveEnemies.Count; i++)
            {
                Enemy enemy = ActiveEnemies[i];
                if (enemy != null && enemy.IsServer && enemy.IsSpawned && !enemy.isDying.Value)
                {
                    buffer.Add(enemy.transform.position);
                }
            }
        }

        public static int ServerAbsorbAllIntoGrudge()
        {
            float total = 0f;
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveEnemies);
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                Enemy enemy = AbsorbBuffer[i];
                if (enemy == null || !enemy.IsServer || !enemy.IsSpawned)
                {
                    continue;
                }
                if (!enemy.isDying.Value && enemy.stats != null)
                {
                    total += enemy.stats.grudgeWeight;
                }
                enemy.NetworkObject.Despawn();
            }
            AbsorbBuffer.Clear();
            return Mathf.RoundToInt(total);
        }

        /// <summary>
        /// 261104 - 서버 전용. 일회용 신통력 '부정 씻기'(기획서: 주변 원귀 정화, 처치로 계산).
        /// center에서 radius 안의 살아있는 원귀를 즉시 쓰러뜨린다(영혼 · 엽전도 평소처럼 떨어짐).
        /// reference와 다른 차원(억쇠 '정신병')에 있는 원귀는 건드리지 않는다. 억쇠는 Enemy가 아니라 대상이 아니다.
        /// 돌려주는 값 = 쓰러뜨린 수(처치 수 집계용).
        /// </summary>
        public static int ServerPurifyAround(Vector2 center, float radius, GameObject reference)
        {
            if (radius <= 0f)
            {
                return 0;
            }
            float sqrRadius = radius * radius;
            int kills = 0;
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveEnemies); // Die 중 목록이 바뀌어도 안전하게 복사본으로 순회
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                Enemy enemy = AbsorbBuffer[i];
                if (enemy == null || !enemy.IsServer || !enemy.IsSpawned || enemy.isDying.Value || enemy.stats == null)
                {
                    continue;
                }
                if (((Vector2)enemy.transform.position - center).sqrMagnitude > sqrRadius)
                {
                    continue;
                }
                if (reference != null && !Boss.DeathRealm.CanInteract(reference, enemy.gameObject))
                {
                    continue;
                }
                enemy.currentHealth = 0f;
                enemy.Die();
                kills++;
            }
            AbsorbBuffer.Clear();
            return kills;
        }

        private void Update()
        {
            // 점멸 진행은 서버/클라이언트 구분 없이 전부 로컬로 돈다 - 순수 시각 효과.
            if (blinksRemaining <= 0 || spriteRenderer == null)
            {
                return;
            }

            blinkTimer -= Time.deltaTime;
            if (blinkTimer > 0f)
            {
                return;
            }

            blinkTimer = stats != null ? stats.hitFlashBlinkInterval : 0.08f;
            spriteRenderer.enabled = !spriteRenderer.enabled;
            blinksRemaining--;

            if (blinksRemaining <= 0)
            {
                spriteRenderer.enabled = true; // 항상 켜진 채로 마무리
            }
        }

        private void FixedUpdate()
        {
            // 이동 "계산"은 서버만 - 클라이언트는 복제받은 위치를 그대로 보여준다.
            if (!IsServer || stats == null)
            {
                return;
            }

            // 261014 추가 - "증강 선택중 게임 및 캐릭터 정지 없음" 수정. 플레이어뿐 아니라
            // 적도 완전히 멈춰야 "게임 전체 정지"가 된다 - 이동/추적/분리는 물론, 사망 경직
            // 타이머(deathTimer)까지 포함해서 멈춘다(그냥 return만 하면 직전 속도값이 남아
            // 계속 미끄러지므로 속도도 명시적으로 0으로 고정).
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            if (isDying.Value)
            {
                // 사망 경직 중 - 추적/이동을 멈추고 경직 시간이 다 지나면 실제로 소멸시킨다.
                deathTimer -= Time.fixedDeltaTime;
                if (deathTimer <= 0f)
                {
                    FinishDeath();
                }
                return;
            }

            // 261114 - 넉백 · 기절 · 둔화 · 도발(플레이어 공격 효과).
            if (serverKnockbackLeft > 0f)
            {
                serverKnockbackLeft -= Time.fixedDeltaTime;
                float k = Mathf.Clamp01(serverKnockbackLeft / KnockbackSeconds);
                rb.linearVelocity = serverKnockbackVelocity * k;
                return;
            }
            if (Time.time < serverStunUntil)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            Transform nearestPlayer = Time.time < serverTauntUntil && serverTauntTarget != null && serverTauntTarget.gameObject.activeInHierarchy
                ? serverTauntTarget
                : PlayerHealth.FindNearestAlivePlayer(rb.position);
            // 261119 - 중형 원귀 스킬(돌진 · 던지기 중엔 스킬이 이동을 정한다).
            if (elite != null && elite.ServerFixedStep(nearestPlayer, Time.fixedDeltaTime))
            {
                return;
            }
            if (nearestPlayer == null)
            {
                // 접속해 있는(또는 살아있는) 플레이어가 없음 - 제자리 대기.
                return;
            }

            Vector2 toTarget = (Vector2)nearestPlayer.position - rb.position;
            float distance = toTarget.magnitude;
            Vector2 chase = Vector2.zero;
            if (distance > arriveStopDistance)
            {
                float slowRange = Mathf.Max(0.0001f, arriveSlowDistance - arriveStopDistance);
                Vector2 chaseDirection = toTarget / distance;
                // 261121 - 160 맵: 멀면 길찾기(집 · 물 · 울타리를 돌아서), 가까우면 바로 쫓는다.
                // 261123 - 막힌 게 없으면 멀어도 곧장 + 원귀마다 다른 각도로 비스듬히(둘러싸기).
                Maps.MapFlowField field = Maps.MapFlowField.Instance;
                bool direct = field == null || distance <= FlowDirectChaseDistance;
                if (!direct)
                {
                    if (Time.time >= nextLineOfSightCheck)
                    {
                        nextLineOfSightCheck = Time.time + LineOfSightInterval;
                        lineOfSightClear = field.HasClearLine(rb.position, nearestPlayer.position);
                    }
                    direct = lineOfSightClear;
                }
                if (!direct && field.TryGetDirection(rb.position, out Vector2 flowDirection))
                {
                    chaseDirection = flowDirection;
                }
                else if (distance > FlankStartDistance)
                {
                    float flank = flankDegrees * Mathf.Clamp01((distance - FlankStartDistance) / (FlankFullDistance - FlankStartDistance));
                    chaseDirection = Quaternion.Euler(0f, 0f, flank) * chaseDirection;
                }
                chase = chaseDirection * Mathf.Clamp01((distance - arriveStopDistance) / slowRange);
            }

            float slow = Time.time < serverSlowUntil ? serverSlowRatio : 0f;
            Vector2 chaseDir = chase.sqrMagnitude > 0.0001f ? chase.normalized : Vector2.zero;
            Vector2 desired = Vector2.ClampMagnitude(chase + ComputeSeparation(chaseDir) * separationWeight, 1f) * (stats.moveSpeed * (1f - slow));
            Vector2 velocity = Vector2.MoveTowards(rb.linearVelocity, desired, steeringAcceleration * Time.fixedDeltaTime);
            // 261121 - 땅 꺼짐 구덩이는 원귀도 못 지나간다(원귀는 Map 레이어와 충돌을 꺼 둬서 속도로 막는다).
            Maps.MapFlowField obstacleField = Maps.MapFlowField.Instance;
            if (obstacleField != null)
            {
                velocity = obstacleField.Steer(rb.position, velocity, Time.fixedDeltaTime); // 막힌 칸(물 · 물체 · 구덩이)에 안 들어감
            }
            else if (Boss.BossPitField.Count > 0)
            {
                velocity = Boss.BossPitField.Steer(rb.position, velocity, PitBodyRadius, Time.fixedDeltaTime);
            }
            rb.linearVelocity = velocity;
        }

        /// <summary>
        /// 서버 전용 - separationRadius 안에 있는 다른 적들로부터 밀려나는 방향을
        /// 계산한다(가까울수록 더 세게 밀림 - 261101부터 선형 가중: 반경 끝 0 ~ 완전히 겹침 1).
        /// 주변에 아무도 없으면 Vector2.zero.
        /// </summary>
        private const float KnockbackSeconds = 0.18f;
        private Vector2 serverKnockbackVelocity;
        private float serverKnockbackLeft;
        private float serverStunUntil;
        private float serverSlowUntil;
        private float serverSlowRatio;
        private Transform serverTauntTarget;
        private float serverTauntUntil;

        // 261114 - 플레이어 캐릭터 효과가 원귀에 남기는 표식(서버 전용). 출처(GameObject)로 "동료가 때리면"을 가른다.
        public GameObject ServerBrandSource { get; private set; }   // 주사 낙인(한주원)
        public float ServerBrandUntil { get; private set; }
        public GameObject ServerVulnerableSource { get; private set; } // "맞은 원귀, 동료 피해 +n%"
        public float ServerVulnerableBonus { get; private set; }
        public float ServerVulnerableUntil { get; private set; }
        public int ServerBindCount { get; set; }                     // 결박부 5단계(3번 속박 → 기절)
        // 261118 - 공통 · 협동 신통력 표식(서버 전용).
        public float ServerExposedUntil { get; set; }                // 매의 눈 5단계 「급살」: 누가 때려도 +10%
        public float ServerJointUntil { get; set; }                  // 짝패 5단계 「합공」: 받는 피해 +20%
        public GameObject ServerLastHitter { get; set; }             // 합공 판정용(마지막으로 때린 플레이어)
        public float ServerLastHitTime { get; set; } = -999f;
        public bool IsServerKnockedBack => serverKnockbackLeft > 0f;

        public void ServerBrand(GameObject source, float seconds)
        {
            if (IsServer && source != null)
            {
                ServerBrandSource = source;
                ServerBrandUntil = Time.time + seconds;
            }
        }

        public void ServerClearBrand()
        {
            ServerBrandSource = null;
            ServerBrandUntil = 0f;
        }

        public bool IsBrandedBy(GameObject source) => ServerBrandSource == source && Time.time < ServerBrandUntil;

        public bool HasBrandFromOther(GameObject attacker) =>
            ServerBrandSource != null && ServerBrandSource != attacker && Time.time < ServerBrandUntil;

        /// <summary>source가 아닌 사람(동료)이 때리면 피해 +bonus. 더 센 쪽이 남는다.</summary>
        public void ServerMarkVulnerable(GameObject source, float bonus, float seconds)
        {
            if (!IsServer || source == null)
            {
                return;
            }
            if (Time.time >= ServerVulnerableUntil || bonus >= ServerVulnerableBonus || ServerVulnerableSource == source)
            {
                ServerVulnerableSource = source;
                ServerVulnerableBonus = Mathf.Max(bonus, Time.time < ServerVulnerableUntil && ServerVulnerableSource == source ? ServerVulnerableBonus : 0f);
                ServerVulnerableUntil = Time.time + seconds;
            }
        }

        public float VulnerableBonusFor(GameObject attacker) =>
            ServerVulnerableSource != null && ServerVulnerableSource != attacker && Time.time < ServerVulnerableUntil ? ServerVulnerableBonus : 0f;

        /// <summary>261114 - 서버 전용. direction 쪽으로 distance(칸)만큼 밀쳐낸다(0.18초 동안 감속하며).</summary>
        public void ServerKnockback(Vector2 direction, float distance)
        {
            if (!IsServer || isDying.Value || distance <= 0f || direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            // 감속(선형) 이동 거리 = 초기 속도 × 시간 / 2.
            serverKnockbackVelocity = direction.normalized * (distance * 2f / KnockbackSeconds);
            serverKnockbackLeft = KnockbackSeconds;
        }

        /// <summary>261114 - 서버 전용. seconds 동안 못 움직인다(기절).</summary>
        public void ServerStun(float seconds)
        {
            if (IsServer && seconds > 0f)
            {
                serverStunUntil = Mathf.Max(serverStunUntil, Time.time + seconds);
            }
        }

        /// <summary>261114 - 서버 전용. seconds 동안 이동 속도 ratio(0~1)만큼 감소. 더 센 쪽이 남는다.</summary>
        public void ServerSlow(float ratio, float seconds)
        {
            if (!IsServer || seconds <= 0f)
            {
                return;
            }
            if (Time.time >= serverSlowUntil || ratio >= serverSlowRatio)
            {
                serverSlowRatio = Mathf.Clamp01(ratio);
            }
            serverSlowUntil = Mathf.Max(serverSlowUntil, Time.time + seconds);
        }

        /// <summary>261114 - 서버 전용. seconds 동안 target을 먼저 노린다(신내림 "나를 먼저 노림").</summary>
        public void ServerTaunt(Transform target, float seconds)
        {
            if (IsServer && target != null && seconds > 0f)
            {
                serverTauntTarget = target;
                serverTauntUntil = Time.time + seconds;
            }
        }

        // 261114 최적화 - 분리 계산을 "모든 원귀 × 모든 원귀"(300마리면 고정 프레임마다 9만 쌍, 쌍마다 rb.position 네이티브 호출)에서
        // 격자(칸 크기 = 분리 반경) 이웃 9칸만 보는 방식으로 바꿨다. 위치는 고정 프레임마다 한 번만 배열에 모으고(첫 원귀가 만든다),
        // 버킷은 계수 정렬로 채워서 할당이 없다. 결과(밀어내는 방향)는 예전과 같다.
        private const int GridBucketCount = 1024; // 2의 거듭제곱(마스크로 나머지)
        private static readonly int[] GridBucketStart = new int[GridBucketCount + 1];
        private static readonly int[] GridBucketFill = new int[GridBucketCount];
        private static Vector2[] gridPositions = new Vector2[256];
        private static int[] gridSorted = new int[256];
        private static float gridBuiltAt = -1f;
        private static float gridCellSize = 1f;
        private int gridIndex = -1;

        private static int GridBucket(int cx, int cy)
        {
            unchecked
            {
                return (cx * 73856093 ^ cy * 19349663) & (GridBucketCount - 1);
            }
        }

        private void EnsureSeparationGrid()
        {
            if (gridBuiltAt == Time.fixedTime && gridIndex >= 0)
            {
                return;
            }
            gridBuiltAt = Time.fixedTime;
            gridCellSize = Mathf.Max(0.1f, separationRadius);
            int n = ActiveEnemies.Count;
            if (gridPositions.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                gridPositions = new Vector2[size];
                gridSorted = new int[size];
            }
            System.Array.Clear(GridBucketStart, 0, GridBucketStart.Length);
            float inv = 1f / gridCellSize;
            for (int i = 0; i < n; i++)
            {
                Enemy e = ActiveEnemies[i];
                Vector2 pos = e.rb.position;
                gridPositions[i] = pos;
                e.gridIndex = i;
                GridBucketStart[GridBucket(Mathf.FloorToInt(pos.x * inv), Mathf.FloorToInt(pos.y * inv)) + 1]++;
            }
            for (int b = 0; b < GridBucketCount; b++)
            {
                GridBucketStart[b + 1] += GridBucketStart[b];
                GridBucketFill[b] = GridBucketStart[b];
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 pos = gridPositions[i];
                int bucket = GridBucket(Mathf.FloorToInt(pos.x * inv), Mathf.FloorToInt(pos.y * inv));
                gridSorted[GridBucketFill[bucket]++] = i;
            }
        }

        /// <remarks>
        /// 261123 - 지원 피드백 "원귀가 일자로 쭉 온다": 앞뒤로 줄 선 원귀끼리는 미는 힘이 진행 방향과 나란해서 서로 상쇄될 뿐
        /// 옆으로는 안 퍼졌다. 그래서 진행 방향(chaseDir) 바로 앞에 있는 이웃은 옆으로 비켜 가는 힘(sidestep)을 더한다 -
        /// 어느 쪽으로 비킬지는 그 이웃이 내 왼쪽/오른쪽 어디에 있는지, 거의 일직선이면 원귀마다 정해진 둘러싸기 방향으로.
        /// 반경 1.1(콜라이더 0.4 + 0.4보다 넓게) · 비중 2로 간격도 더 벌린다.
        /// </remarks>
        private Vector2 ComputeSeparation(Vector2 chaseDir)
        {
            bool hasChase = chaseDir.sqrMagnitude > 0.0001f;
            Vector2 perp = new Vector2(-chaseDir.y, chaseDir.x);
            float flankSign = flankDegrees >= 0f ? 1f : -1f;
            EnsureSeparationGrid();
            Vector2 self = gridPositions[gridIndex];
            Vector2 push = Vector2.zero;
            int neighborCount = 0;
            float radiusSqr = separationRadius * separationRadius;
            float inv = 1f / gridCellSize;
            int cx = Mathf.FloorToInt(self.x * inv);
            int cy = Mathf.FloorToInt(self.y * inv);
            int visited0 = -1, visited1 = -1, visited2 = -1, visited3 = -1, visited4 = -1, visited5 = -1, visited6 = -1, visited7 = -1;
            int visitedCount = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int bucket = GridBucket(cx + dx, cy + dy);
                    // 서로 다른 칸이 같은 버킷으로 해시되면 두 번 세지 않게 건너뛴다.
                    if (bucket == visited0 || bucket == visited1 || bucket == visited2 || bucket == visited3
                        || bucket == visited4 || bucket == visited5 || bucket == visited6 || bucket == visited7)
                    {
                        continue;
                    }
                    switch (visitedCount++)
                    {
                        case 0: visited0 = bucket; break;
                        case 1: visited1 = bucket; break;
                        case 2: visited2 = bucket; break;
                        case 3: visited3 = bucket; break;
                        case 4: visited4 = bucket; break;
                        case 5: visited5 = bucket; break;
                        case 6: visited6 = bucket; break;
                        case 7: visited7 = bucket; break;
                    }
                    for (int k = GridBucketStart[bucket]; k < GridBucketStart[bucket + 1]; k++)
                    {
                        int other = gridSorted[k];
                        if (other == gridIndex)
                        {
                            continue;
                        }
                        Vector2 diff = self - gridPositions[other];
                        float sqrDist = diff.sqrMagnitude;
                        if (sqrDist >= radiusSqr || sqrDist < 0.0001f)
                        {
                            continue;
                        }
                        float dist = Mathf.Sqrt(sqrDist);
                        float strength = 1f - dist / separationRadius;
                        push += diff / dist * strength;
                        // diff = 나 - 이웃 → Dot(diff, chaseDir) < 0 이면 이웃이 내 앞(가는 길)에 있다.
                        if (hasChase && Vector2.Dot(diff, chaseDir) < 0f)
                        {
                            float side = Vector2.Dot(diff, perp);
                            float sign = Mathf.Abs(side) > 0.05f ? Mathf.Sign(side) : flankSign;
                            push += perp * (sign * strength * sidestepWeight);
                        }
                        neighborCount++;
                    }
                }
            }

            return neighborCount > 0 ? push / neighborCount : Vector2.zero;
        }

        public void TakeDamage(float amount)
        {
            // 체력 적용/사망 판정은 서버만 - 클라이언트가 직접 적 체력을 깎을 수 있게
            // 두면 치팅이나 중복 피해 위험이 있다.
            // isDying이면 이미 사망 경직 중 - 추가 피해를 무시해서 중복 사망 처리(Die
            // 재호출, deathTimer 리셋 등)를 막는다.
            if (!IsServer || stats == null || isDying.Value)
            {
                return;
            }

            currentHealth -= amount;

            // 맞았다는 사실 자체는 점멸로 - 죽는 타격이어도 일단 한 번은 번쩍이게 둔다.
            unchecked
            {
                hitFlashSerial.Value++;
            }

            if (currentHealth <= 0f)
            {
                Die();
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // 접촉 피해 판정도 서버만 - 서버/클라이언트 양쪽 다 콜라이더가 있어서
            // 가드 없이 두면 전원이 각자 피해를 적용하게 된다.
            // 사망 경직 중에는 접촉 피해도 멈춘다(기획 지시: 경직 중엔 아무 행동도 안 함).
            // 261014 추가 - 일시정지 중에도 접촉 피해를 멈춘다(증강 선택 화면 보는 동안
            // 캐릭터가 가만히 있어도 적과 겹쳐 있으면 계속 맞는 것 방지).
            if (!IsServer || stats == null || isDying.Value
                || (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused))
            {
                return;
            }

            contactTimer -= Time.fixedDeltaTime;
            if (contactTimer > 0f)
            {
                return;
            }

            // 다른 적은 제외 - IDamageable을 구현하고 있어서 그냥 두면 적끼리도 서로 때린다.
            // 261101 - 억쇠 본체/분신(같은 Enemy 레이어, IDamageable)도 맞으면 안 되므로 아예 플레이어만 대상으로 좁혔다.
            // 억쇠 '정신병' 차원에 끌려간 플레이어도 밖의 원귀와는 상호작용하지 않는다.
            if (!other.TryGetComponent(out PlayerHealth damageable)
                || !Boss.DeathRealm.CanInteract(gameObject, other.gameObject))
            {
                return;
            }
            if (elite != null && !elite.CanContact)
            {
                return; // 261119 - 물귀신은 웅덩이에서 나와 있을 때만 공격
            }

            float damageMultiplier = WaveManager.Instance != null ? WaveManager.Instance.CurrentDamageMultiplier : 1f;
            damageable.TakeDamage(stats.contactDamage * damageMultiplier);
            contactTimer = stats.contactDamageInterval;
            if (elite != null)
            {
                elite.ServerOnContactHit();
            }
        }

        /// <summary>
        /// 체력이 0 이하가 된 순간 호출 - 바로 소멸시키지 않고 경직 상태로 전환만
        /// 한다. 실제 풀 반환은 FixedUpdate에서 deathTimer가 다 지난 뒤 FinishDeath()가 한다.
        /// </summary>
        private void Die()
        {
            rb.linearVelocity = Vector2.zero;
            deathTimer = stats.deathStaggerDuration;
            isDying.Value = true;

            SpawnDrops();
        }

        /// <summary>
        /// 사망 순간 엽전/경험치 드랍 생성(261013 추가) - Die()가 이미 TakeDamage의
        /// IsServer 가드를 거친 컨텍스트에서만 호출되므로 여기서 다시 체크하지 않는다.
        /// LootSpawner가 아직 씬에 없으면(에디터 셋업을 안 했거나 Gameplay 씬 이동 전이면)
        /// 조용히 건너뛴다 - 드랍 생성 실패가 적 사망 처리 자체를 막으면 안 되기 때문.
        /// </summary>
        private void SpawnDrops()
        {
            if (LootSpawner.Instance == null || ServerSummonedByBoss)
            {
                return; // 261121 - 억쇠가 불러낸 원귀는 영혼 · 엽전 없음
            }

            // 261027 수정 - 기획서 5-10: 영혼은 100%, 엽전은 원귀별 확률(goldDropChance)로.
            if (Random.value < stats.goldDropChance)
            {
                LootSpawner.Instance.SpawnGold(rb.position, stats.goldReward);
            }
            LootSpawner.Instance.SpawnExp(rb.position, stats.expReward);
        }

        /// <summary>경직 시간이 다 지난 뒤 실제로 네트워크 디스폰시켜 풀에 반환한다.</summary>
        private void FinishDeath()
        {
            // destroy 기본값(true)으로 Despawn하면, EnemyPool이 등록해둔 핸들러가
            // 실제 GameObject.Destroy 대신 가로채서 풀로 반환한다.
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }

        /// <summary>hitFlashSerial이 바뀌면(서버는 값을 쓰는 즉시, 참가자는 다음 네트워크 틱에) 점멸 시작.</summary>
        private void HandleHitFlashChanged(byte previous, byte current)
        {
            HandleHitFlashTriggered();
        }

        /// <summary>점멸 시작 - stats.hitFlashBlinkCount회만큼 켜짐/꺼짐을 토글한다
        /// (1회 = 토글 2번: 꺼짐→켜짐). 진행 자체는 Update()가 담당.</summary>
        private void HandleHitFlashTriggered()
        {
            if (stats == null || spriteRenderer == null)
            {
                return;
            }

            blinksRemaining = Mathf.Max(0, stats.hitFlashBlinkCount) * 2;
            blinkTimer = stats.hitFlashBlinkInterval;
            spriteRenderer.enabled = false;
        }

        /// <summary>
        /// isDying이 false로 돌아왔다는 건(풀에서 재사용돼 ServerInitialize가 리셋한 것)
        /// 이전 생애의 점멸이 진행 중이었을 수 있다는 뜻 - 찌꺼기를 지우고 스프라이트를
        /// 확실히 켜둔다. true로 바뀐 경우(경직 진입)는 이동 정지만으로 충분해 별도
        /// 처리 없음 - 틴트 등 추가 연출이 필요해지면(미정) 여기에 추가하면 된다.
        /// </summary>
        private void HandleDyingChanged(bool previousValue, bool currentValue)
        {
            // 261027 수정 - 경직 진입(true) 때는 소멸 애니메이션(기획서 8: "공통 사라짐 이펙트")을
            // 틀고, 진행 중이던 점멸은 끊어서 스프라이트를 켠 채로 보이게 한다.
            blinksRemaining = 0;
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = true;
            }
            // 261116 - 퇴마 소리(모든 화면, 위치 소리 - 폭주 방지는 SoundCue MaxInstances · MinInterval).
            if (currentValue && !previousValue)
            {
                // 261121 - 중형 원귀는 퇴마 소리를 낮게(사운드 기획서 v1.2).
                CheonMyeong.Audio.GameAudio.PlayAt(elite != null ? CheonMyeong.Audio.SoundKeys.EliteExorcise : CheonMyeong.Audio.SoundKeys.EnemyDie,
                    transform.position);
            }

            if (currentValue && animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(DieTriggerHash);
            }
        }
    }
}
