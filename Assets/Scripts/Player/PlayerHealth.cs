using System.Collections.Generic;
using CheonMyeong.Combat;
using CheonMyeong.Data;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 하영의 체력 - 네트워크 버전. 체력/사망 여부는 서버만 쓰고 전원이 읽는다
    /// (기본 권한이 그대로 서버 전용 쓰기라 따로 지정 안 해도 되지만, 명시적으로
    /// 적어서 의도를 분명히 한다) - 클라이언트가 자기 체력을 직접 깎거나 무적으로
    /// 만들 수 없게 막는 용도.
    ///
    /// 사망 시 처리 - 기획 확정(261013, wireframe-reference 문서 확인):
    /// "다음 웨이브에 자동 부활" + "사망 중엔 살아있는 동료 쪽으로 카메라 고정,
    /// 남은 시간 동안 관전". 단, 웨이브 타이머 시스템 자체가 아직 코드에 없어서
    /// (EnemySpawner는 지속 스폰만 함) "다음 웨이브 시작" 트리거 대신 우선
    /// temporaryRespawnDelaySeconds 고정 시간으로 임시 구현한다 - 웨이브 시스템이
    /// 들어오면 이 타이머를 웨이브 전환 이벤트로 교체해야 한다(아래 Revive() 참고).
    ///
    /// ActivePlayers는 적(Enemy)이 서버에서 "가장 가까운 살아있는 플레이어"를
    /// 찾기 위한 용도로 추가했다(네트워크 플레이어 전환 전까지는 적이 씬에 미리
    /// 박아둔 단일 플레이어 Transform을 그냥 쫓아갔는데, 멀티플레이에서는 플레이어가
    /// 여럿이라 더 이상 유효하지 않다). 모든 프로세스(서버+클라이언트)에서 각자
    /// OnNetworkSpawn/Despawn될 때마다 갱신되지만, 실제로 읽는 쪽(Enemy)은 서버에서만
    /// 쓰므로 클라이언트 쪽 목록은 그냥 쓰이지 않고 남아있는 것뿐이라 문제 없다.
    /// 사망 중인 플레이어의 카메라 관전 대상을 고를 때도 이 목록을 그대로 재사용한다
    /// (GetRandomAlivePlayerTransform 호출 시점엔 이미 자신의 isDead가 true로 반영된
    /// 뒤라서 자기 자신은 자동으로 후보에서 제외된다).
    /// </summary>
    public class PlayerHealth : NetworkBehaviour, IDamageable
    {
        /// <summary>현재 스폰돼 있는 모든 PlayerHealth. Enemy의 타겟 탐색용.</summary>
        public static readonly List<PlayerHealth> ActivePlayers = new List<PlayerHealth>();

        [SerializeField] private CharacterStatsSO stats;

        [Header("부활 (임시 구현 - 웨이브 시스템 붙으면 교체 예정)")]
        [Tooltip("사망 후 자동 부활까지 걸리는 시간(초). 기획은 \"다음 웨이브 시작 시 부활\"이지만\n" +
                 "웨이브 타이머 시스템이 아직 없어서 우선 고정 시간으로 임시 대체했다 - 웨이브\n" +
                 "시스템이 들어오면 이 필드 대신 웨이브 전환 이벤트로 Revive()를 호출하도록 바꿔야 한다.")]
        [SerializeField] private float temporaryRespawnDelaySeconds = 10f;

        private float respawnTimer;

        // 261104 - 신통력(기획서 v0.9.4 07장): 최대 체력 · 방어력 · 체력 재생.
        private PlayerProgress progress;
        private float regenBuffer;
        private float regenFlushTimer;
        private const float RegenFlushInterval = 0.5f; // 매 프레임 NetworkVariable을 쓰지 않게 모아서 쓴다.

        private readonly NetworkVariable<float> currentHealth = new NetworkVariable<float>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> isDead = new NetworkVariable<bool>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float CurrentHealth => currentHealth.Value;
        public float MaxHealth => stats != null
            ? stats.maxHealth * (1f + (progress != null ? progress.Modifiers.MaxHealthBonus : 0f))
            : 1f;

        /// <summary>261104 - 이 캐릭터의 기준 스탯(신통력 카드 후보 판정에 쓴다).</summary>
        public CharacterStatsSO Stats => stats;

        private void Awake()
        {
            TryGetComponent(out progress);
        }

        /// <summary>
        /// 261104 - 서버 전용. 신통력으로 최대 체력이 바뀐 뒤 호출 - 늘어난 만큼 현재 체력도 채우고, 줄었으면 최대치로 자른다.
        /// </summary>
        public void ServerOnMaxHealthChanged(float previousMax)
        {
            if (!IsServer || !IsSpawned || isDead.Value)
            {
                return;
            }
            float max = MaxHealth;
            float delta = max - previousMax;
            float next = delta > 0f ? currentHealth.Value + delta : currentHealth.Value;
            next = Mathf.Min(max, next);
            if (!Mathf.Approximately(next, currentHealth.Value))
            {
                currentHealth.Value = next;
            }
        }
        public bool IsDead => isDead.Value;

        /// <summary>드랍 아이템(골드/경험치)이 자동으로 끌려오기 시작하는 반경(유닛) -
        /// LootPickup.cs가 서버에서 이 값으로 자석 판정을 한다(261013 추가).</summary>
        public float MagnetRadius => stats != null ? stats.magnetRadius : 0f;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                currentHealth.Value = MaxHealth;
            }

            isDead.OnValueChanged += HandleDeadChanged;
            ActivePlayers.Add(this);

            // 늦게 접속해서 이미 죽어있는 상태로 동기화받은 경우도 처리.
            if (isDead.Value)
            {
                ApplyDeadState();
            }
            else if (IsOwner && PlayerCameraRig.Instance != null)
            {
                // 스폰 직후(살아있는 상태) 기본값 - 내 카메라는 내 캐릭터를 따라간다.
                PlayerCameraRig.Instance.SetFollowTarget(transform);
            }
        }

        public override void OnNetworkDespawn()
        {
            isDead.OnValueChanged -= HandleDeadChanged;
            ActivePlayers.Remove(this);
        }

        private void Update()
        {
            // 부활 타이머는 서버만 - 죽은 동안에만 카운트다운하고, 0 이하가 되면 즉시 부활시킨다.
            // 261027 수정 - 웨이브 시스템(WaveManager)이 있으면 기획대로 "웨이브가 끝날 때(징) 부활"
            // (WaveManager → ServerReviveIfDead)만 쓰고 이 고정 시간 타이머는 쓰지 않는다.
            // WaveManager가 없는 씬(테스트용 등)에서만 예전 임시 동작으로 대체.
            if (!IsServer)
            {
                return;
            }
            if (!isDead.Value)
            {
                ServerTickRegen();
                return;
            }
            if (WaveManager.Instance != null)
            {
                return;
            }

            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                Revive();
            }
        }

        /// <summary>261104 - 신통력 '체력 재생'(초당). 일시정지 중엔 멈춘다.</summary>
        private void ServerTickRegen()
        {
            float perSecond = progress != null ? progress.Modifiers.RegenPerSecond : 0f;
            if (perSecond <= 0f || (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused))
            {
                return;
            }
            regenBuffer += perSecond * Time.deltaTime;
            regenFlushTimer += Time.deltaTime;
            if (regenFlushTimer < RegenFlushInterval)
            {
                return;
            }
            regenFlushTimer = 0f;
            float max = MaxHealth;
            if (currentHealth.Value < max)
            {
                currentHealth.Value = Mathf.Min(max, currentHealth.Value + regenBuffer);
            }
            regenBuffer = 0f;
        }

        /// <summary>
        /// 서버 전용 - 체력을 가득 채우고 사망 상태를 해제한다. isDead.Value가
        /// false로 바뀌면 OnValueChanged를 통해 전원(서버+모든 클라이언트)에서
        /// HandleDeadChanged → ApplyAliveState가 자동으로 실행된다.
        /// </summary>
        private void Revive()
        {
            currentHealth.Value = MaxHealth;
            isDead.Value = false;
        }

        /// <summary>
        /// 261027 수정 - 서버 전용. WaveManager가 웨이브를 끝낼 때(징) 호출한다. 기획서 5-10:
        /// "웨이브 끝 부활 시 체력 30%" - 비율은 WaveConfigSO.reviveHealthRatio에서 온다.
        /// </summary>
        public void ServerReviveIfDead(float healthRatio)
        {
            if (!IsServer || !isDead.Value)
            {
                return;
            }
            currentHealth.Value = Mathf.Max(1f, MaxHealth * Mathf.Clamp01(healthRatio));
            isDead.Value = false;
        }

        /// <summary>
        /// 261027 추가 - 서버 전용. 새 판(다시 하기 포함)이 시작될 때 WaveManager가 호출 - 플레이어
        /// 오브젝트는 Gameplay 씬과 별개로 살아남기 때문에, 이걸 안 하면 "봉인 실패" 뒤 다시 하기를
        /// 눌렀을 때 쓰러진 상태 그대로 시작해서 바로 다시 게임오버가 된다.
        /// </summary>
        public void ServerResetForNewRun()
        {
            if (!IsServer)
            {
                return;
            }
            respawnTimer = 0f;
            currentHealth.Value = MaxHealth;
            isDead.Value = false;
        }

        /// <summary>
        /// 261027 추가 - 서버 전용. 징이 울릴 때 살아있는 플레이어 체력 회복(기획서 5-10: 최대 체력의 10%).
        /// </summary>
        public void ServerHealByRatio(float ratio)
        {
            if (!IsServer || isDead.Value || ratio <= 0f)
            {
                return;
            }
            currentHealth.Value = Mathf.Min(MaxHealth, currentHealth.Value + MaxHealth * ratio);
        }

        /// <summary>
        /// 261027 추가 - 서버 전용. 접속한 플레이어가 1명 이상이고 전원 쓰러졌으면 true
        /// (기획서 5-2: "둘 다 쓰러지면 끝" - WaveManager가 게임오버 판정에 쓴다).
        /// </summary>
        public static bool AreAllPlayersDown()
        {
            bool anyPlayer = false;
            for (int i = 0; i < ActivePlayers.Count; i++)
            {
                PlayerHealth player = ActivePlayers[i];
                if (player == null)
                {
                    continue;
                }
                anyPlayer = true;
                if (!player.IsDead)
                {
                    return false;
                }
            }
            return anyPlayer;
        }

        /// <summary>
        /// 서버 전용 - 주어진 위치에서 가장 가까운 "살아있는" 플레이어의 Transform을 찾는다.
        /// 접속한 플레이어가 없거나 전원 사망 상태면 null.
        /// </summary>
        public static Transform FindNearestAlivePlayer(Vector2 fromPosition)
        {
            PlayerHealth nearest = FindNearestAlivePlayerHealth(fromPosition);
            return nearest != null ? nearest.transform : null;
        }

        /// <summary>
        /// 위와 같은 탐색이지만 Transform이 아니라 PlayerHealth 컴포넌트 자체를 돌려준다
        /// (261013 추가) - LootPickup.cs가 자석 판정에 쓸 MagnetRadius까지 같은 호출로
        /// 얻어가려고 추가했다. FindNearestAlivePlayer는 이 메서드에 위임해서 탐색 로직이
        /// 두 군데로 중복되지 않게 했다.
        /// </summary>
        public static PlayerHealth FindNearestAlivePlayerHealth(Vector2 fromPosition)
        {
            PlayerHealth nearest = null;
            float nearestSqrDist = float.MaxValue;

            foreach (PlayerHealth player in ActivePlayers)
            {
                // 261101 - 억쇠 '정신병' 차원에 끌려간 플레이어는 밖의 원귀 · 드랍 · 보스 본체가 노리지 않는다.
                if (player == null || player.IsDead || Boss.DeathRealm.IsInRealm(player.gameObject))
                {
                    continue;
                }

                float sqrDist = ((Vector2)player.transform.position - fromPosition).sqrMagnitude;
                if (sqrDist < nearestSqrDist)
                {
                    nearestSqrDist = sqrDist;
                    nearest = player;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 261014 추가 - 최적화: GetRandomAlivePlayerTransform()이 EnemySpawner의 매 스폰
        /// 주기마다 호출되는데, 그때마다 새 List를 할당하면 그만큼 GC 압박이 쌓인다(적/스폰
        /// 빈도가 늘어날수록 더 커짐). 정적 재사용 스크래치 리스트로 바꿔서 반환값/동작은
        /// 완전히 동일하게 두고 할당만 없앴다 - 이 메서드는 항상 메인 스레드(서버)에서만
        /// 호출되므로 정적 공유 버퍼를 써도 동시 접근 문제가 없다.
        /// </summary>
        private static readonly List<Transform> AliveScratchBuffer = new List<Transform>();

        /// <summary>
        /// 서버 전용 - 살아있는 플레이어 중 무작위로 하나의 Transform을 반환한다.
        /// EnemySpawner가 스폰 위치 기준점을 고를 때 쓴다(여러 명일 때 누구 주변에
        /// 스폰할지는 미정이라 우선 무작위로 처리).
        /// </summary>
        public static Transform GetRandomAlivePlayerTransform()
        {
            AliveScratchBuffer.Clear();
            foreach (PlayerHealth player in ActivePlayers)
            {
                // 261101 - 차원에 끌려간 플레이어는 되도록 제외(스폰 기준점이 차원 안으로 쏠리지 않게).
                if (player != null && !player.IsDead && !Boss.DeathRealm.IsInRealm(player.gameObject))
                {
                    AliveScratchBuffer.Add(player.transform);
                }
            }
            if (AliveScratchBuffer.Count == 0)
            {
                // 살아있는 사람이 차원 안에만 있으면 그 사람이라도 돌려준다(카메라 관전 전환 등).
                foreach (PlayerHealth player in ActivePlayers)
                {
                    if (player != null && !player.IsDead)
                    {
                        AliveScratchBuffer.Add(player.transform);
                    }
                }
            }

            if (AliveScratchBuffer.Count == 0)
            {
                return null;
            }

            return AliveScratchBuffer[Random.Range(0, AliveScratchBuffer.Count)];
        }

        public void TakeDamage(float amount)
        {
            // 피해 적용은 서버만 - 적 쪽도 서버에서만 공격 판정을 하게 될 예정이라
            // 보통 서버에서만 호출되지만, 혹시 몰라 한 번 더 막는다.
            if (!IsServer || isDead.Value || stats == null)
            {
                return;
            }

            // 261104 - 신통력 '방어력': 받는 피해에서 고정 수치만큼 뺀다(최소 1).
            float armor = progress != null ? progress.Modifiers.Armor : 0f;
            if (armor > 0f && amount > 0f)
            {
                amount = Mathf.Max(Mathf.Min(1f, amount), amount - armor);
            }

            currentHealth.Value -= amount;

            if (currentHealth.Value <= 0f)
            {
                currentHealth.Value = 0f;
                isDead.Value = true;
                respawnTimer = temporaryRespawnDelaySeconds;
            }
        }

        private void HandleDeadChanged(bool previous, bool current)
        {
            if (current)
            {
                ApplyDeadState();
            }
            else
            {
                ApplyAliveState();
            }
        }

        private void ApplyDeadState()
        {
            // 전원(서버 + 모든 클라이언트)이 각자 자기 쪽 로컬 컴포넌트를 멈춘다.
            if (TryGetComponent(out PlayerMovement movement))
            {
                movement.enabled = false;
            }
            if (TryGetComponent(out PlayerAutoAttack attack))
            {
                attack.enabled = false;
            }

            if (IsServer && TryGetComponent(out Rigidbody2D rb))
            {
                rb.linearVelocity = Vector2.zero;
            }

            // 기획 확정(261013): 사망 중엔 살아있는 동료 쪽으로 카메라 고정 + 관전.
            // 카메라는 순수 로컬 연출이라 "내가 죽었을 때 내 화면"에서만 의미가
            // 있으므로 IsOwner(죽은 캐릭터를 조종하던 바로 그 클라이언트)에서만 처리한다.
            if (IsOwner && PlayerCameraRig.Instance != null)
            {
                Transform teammate = GetRandomAlivePlayerTransform();
                // teammate가 null이면(전원 사망 등 아직 기획에 없는 예외 상황) 카메라는
                // 마지막 위치에 그대로 둔다 - SetFollowTarget 내부에서 null을 무시한다.
                PlayerCameraRig.Instance.SetFollowTarget(teammate);
            }

            Debug.Log("[CheonMyeong] 하영 사망 - 다음 웨이브(임시로는 고정 시간) 부활 전까지 " +
                      "정지 + 동료 관전(기획 확정, 261013).");
        }

        private void ApplyAliveState()
        {
            // 전원(서버 + 모든 클라이언트)이 각자 자기 쪽 로컬 컴포넌트를 다시 켠다.
            if (TryGetComponent(out PlayerMovement movement))
            {
                movement.enabled = true;
            }
            if (TryGetComponent(out PlayerAutoAttack attack))
            {
                attack.enabled = true;
            }

            // 부활 - 관전하던 카메라를 내 캐릭터로 되돌린다(IsOwner만, 위 ApplyDeadState와 동일 이유).
            if (IsOwner && PlayerCameraRig.Instance != null)
            {
                PlayerCameraRig.Instance.SetFollowTarget(transform);
            }

            Debug.Log("[CheonMyeong] 하영 부활.");
        }
    }
}
