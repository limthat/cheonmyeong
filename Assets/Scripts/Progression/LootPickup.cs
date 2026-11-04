using System.Collections.Generic;
using CheonMyeong.Player;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 경험치/재화 드랍 아이템 하나(네트워크 버전) - EnemyPool/Enemy와 완전히 같은 풀링 패턴을
    /// 그대로 따른다(NGO 공식 Object Pooling 가이드, INetworkPrefabInstanceHandler로 스폰/디스폰에
    /// 끼워 넣기). 종류(골드/경험치)는 이 클래스를 상속한 GoldLootPickup/ExpLootPickup 두 개의
    /// 별도 프리팹으로 나눠서, 네트워크로 "지금 이게 골드인지 경험치인지" 따로 동기화할 필요가
    /// 없게 했다(위치만 NetworkTransform으로 복제하면 끝).
    ///
    /// 자동줍기(Magnet): 서버가 매 FixedUpdate마다 "가장 가까운 살아있는 플레이어"를 찾아서
    /// (PlayerHealth.FindNearestAlivePlayerHealth) 그 플레이어의 MagnetRadius(CharacterStatsSO.
    /// magnetRadius) 안에 들어오면 그 플레이어 쪽으로 끌려가고, collectDistance 안까지 가까워지면
    /// 즉시 수거되어 풀로 반환된다. 여러 플레이어가 동시에 자석 범위에 들어올 수 있는 경우 "가장
    /// 가까운 한 명"에게만 끌려간다 - 두 플레이어가 하나의 드랍을 나눠 갖는 규칙은 기획에 없어서
    /// (확인 전까지) 가장 단순하게 처리했다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class LootPickup : NetworkBehaviour
    {
        [Tooltip("자석 범위 안에서 플레이어 쪽으로 끌려가는 속도(유닛/초). 값은 미정 - 느낌 보고 조정.")]
        [SerializeField] private float pullSpeed = 8f;

        [Tooltip("이 거리 안으로 들어오면 그 자리에서 즉시 수거된다(유닛).")]
        [SerializeField] private float collectDistance = 0.25f;

        [Tooltip("261027 추가 - 플레이어가 \"직접 닿았다\"고 보는 거리(유닛, 플레이어 중심 ~ 아이템 중심).\n" +
                 "자석(MagnetRadius)이 0이어도 이 거리 안이면 줍는다 - 플레이어 콜라이더 반지름(0.4) +\n" +
                 "아이템 크기 절반 정도.")]
        [SerializeField] private float touchPickupRadius = 0.55f;

        [Tooltip("261104 - 일회용 신통력 '넋 거두기'로 맵의 영혼이 끌려올 때 속도(유닛/초).")]
        [SerializeField] private float harvestPullSpeed = 20f;

        // 261104 - 넋 거두기 대상(서버 전용). 있으면 자석 범위와 상관없이 이 플레이어에게 끌려간다.
        private PlayerHealth harvestTarget;

        /// <summary>
        /// 261027 추가 - 보상 양을 클라이언트에도 알려주는 값(영혼 크기별 스프라이트용). 풀링 재사용이라
        /// ServerInitialize에서 Spawn() 전에 쓰면 스폰 메시지에 실려 처음부터 맞는 값으로 복제된다
        /// (Enemy.isDying과 같은 패턴).
        /// </summary>
        private readonly NetworkVariable<int> visualAmount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private int amount;

        // 261027 추가 - 웨이브 종료(징) 때 바닥에 남은 드랍을 한꺼번에 흡수하기 위한 목록(서버 전용).
        // 기획서 5-2/5-10: "바닥의 영혼은 자동 흡수(공유), 남은 엽전은 두 사람이 반씩 나눠 받는다".
        private static readonly List<LootPickup> ActiveLoot = new List<LootPickup>();
        private static readonly List<LootPickup> AbsorbBuffer = new List<LootPickup>();
        private static readonly List<PlayerProgress> PlayerBuffer = new List<PlayerProgress>();
        private static int pendingSplitGold;

        public override void OnNetworkSpawn()
        {
            harvestTarget = null; // 풀링 재사용 - 이전 생애 값 제거
            if (IsServer)
            {
                ActiveLoot.Add(this);
            }

            // 261027 수정 - 보상 양은 스폰 "중"(여기)에 써야 한다. Spawn() 전에 쓰면 아직 NetworkBehaviour와
            // 연결 안 된 NetworkVariable에 쓰는 거라 "NetworkVariable is written to, but doesn't know its
            // NetworkBehaviour yet" 경고가 났다.
            if (IsServer)
            {
                visualAmount.Value = amount;
            }

            visualAmount.OnValueChanged += HandleVisualAmountChanged;
            ApplyVisual(visualAmount.Value);
        }

        public override void OnNetworkDespawn()
        {
            ActiveLoot.Remove(this);
            harvestTarget = null;
            // 풀링으로 재사용되는 컴포넌트라 해제 안 하면 재스폰마다 콜백이 쌓인다.
            visualAmount.OnValueChanged -= HandleVisualAmountChanged;
        }

        private void HandleVisualAmountChanged(int previous, int current)
        {
            ApplyVisual(current);
        }

        /// <summary>보상 양에 따라 모양을 바꾸고 싶은 종류(영혼)만 오버라이드한다.</summary>
        protected virtual void ApplyVisual(int rewardAmount)
        {
        }

        /// <summary>
        /// 서버 전용 - WaveManager가 웨이브를 끝낼 때 호출. 영혼은 파티 경험치로, 엽전은 합쳐서
        /// 접속해 있는 플레이어(쓰러진 사람 포함)에게 똑같이 나누고, 나머지는 앞사람부터 1씩 준다.
        /// 목록을 복사해서 도는 이유: Despawn이 OnNetworkDespawn에서 원본 목록을 바로 수정한다.
        /// </summary>
        public static void ServerAbsorbAllAtWaveEnd()
        {
            PlayerBuffer.Clear();
            for (int i = 0; i < PlayerHealth.ActivePlayers.Count; i++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[i];
                if (player != null && player.TryGetComponent(out PlayerProgress progress))
                {
                    PlayerBuffer.Add(progress);
                }
            }

            pendingSplitGold = 0;
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveLoot);
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                LootPickup loot = AbsorbBuffer[i];
                if (loot == null || !loot.IsSpawned)
                {
                    continue;
                }
                loot.AbsorbAtWaveEnd(loot.amount);
                loot.NetworkObject.Despawn();
            }
            AbsorbBuffer.Clear();

            if (pendingSplitGold > 0 && PlayerBuffer.Count > 0)
            {
                int share = pendingSplitGold / PlayerBuffer.Count;
                int remainder = pendingSplitGold % PlayerBuffer.Count;
                for (int i = 0; i < PlayerBuffer.Count; i++)
                {
                    PlayerBuffer[i].AddGold(share + (i < remainder ? 1 : 0));
                }
            }
            pendingSplitGold = 0;
            PlayerBuffer.Clear();
        }

        /// <summary>
        /// 261027 추가 - 서버 전용. 판 종료/새 판 시작 때 바닥 드랍을 보상 없이 전부 치운다(Enemy.ServerDespawnAll과
        /// 같은 이유 - 씬 언로드로는 스폰된 NetworkObject가 사라지지 않는다).
        /// </summary>
        public static void ServerDespawnAll()
        {
            AbsorbBuffer.Clear();
            AbsorbBuffer.AddRange(ActiveLoot);
            for (int i = 0; i < AbsorbBuffer.Count; i++)
            {
                LootPickup loot = AbsorbBuffer[i];
                if (loot != null && loot.IsSpawned)
                {
                    loot.NetworkObject.Despawn();
                }
            }
            AbsorbBuffer.Clear();
        }

        /// <summary>
        /// 261104 - 서버 전용. 일회용 신통력 '넋 거두기'(기획서: 맵의 영혼 전부 끌어오기).
        /// 지금 바닥에 있는 영혼(ExpLootPickup)을 전부 target에게 끌려가게 한다. 닿으면 평소처럼 파티 경험치로 들어간다.
        /// </summary>
        public static void ServerPullAllExpTo(PlayerHealth target)
        {
            if (target == null)
            {
                return;
            }
            for (int i = 0; i < ActiveLoot.Count; i++)
            {
                LootPickup loot = ActiveLoot[i];
                if (loot is ExpLootPickup && loot.IsSpawned)
                {
                    loot.harvestTarget = target;
                }
            }
        }

        /// <summary>엽전 드랍이 웨이브 종료 때 나눠 가질 몫을 쌓아두는 용도(GoldLootPickup).</summary>
        protected static void AddPendingSplitGold(int value)
        {
            pendingSplitGold += value;
        }

        /// <summary>웨이브 종료 흡수 시 이 드랍의 보상을 어떻게 처리할지(종류별).</summary>
        protected abstract void AbsorbAtWaveEnd(int rewardAmount);

        /// <summary>
        /// 서버가 풀에서 꺼낸 직후 호출한다(LootPool.GetAndSpawn 참고 - Enemy.ServerInitialize와
        /// 같은 패턴). 풀링으로 재사용되는 인스턴스라 이전 생애의 값이 남아있지 않도록 여기서
        /// 전부 다시 채운다.
        /// </summary>
        public void ServerInitialize(int rewardAmount)
        {
            amount = rewardAmount;
        }

        private void FixedUpdate()
        {
            // 이동/수거 판정은 서버만 - 클라이언트는 NetworkTransform이 복제해주는 위치를 그대로 본다.
            if (!IsServer)
            {
                return;
            }

            // 261104 - 넋 거두기로 끌려가는 중. 대상이 사라지거나 쓰러지면 평소대로 돌아간다.
            if (harvestTarget != null)
            {
                if (!harvestTarget.IsSpawned || harvestTarget.IsDead)
                {
                    harvestTarget = null;
                }
                else
                {
                    Vector2 toTarget = (Vector2)harvestTarget.transform.position - (Vector2)transform.position;
                    float step = harvestPullSpeed * Time.fixedDeltaTime;
                    if (toTarget.magnitude <= Mathf.Max(touchPickupRadius, collectDistance, step))
                    {
                        Collect(harvestTarget);
                        return;
                    }
                    transform.position += (Vector3)(toTarget.normalized * step);
                    return;
                }
            }

            PlayerHealth nearest = PlayerHealth.FindNearestAlivePlayerHealth(transform.position);
            if (nearest == null)
            {
                return; // 접속해 있는(또는 살아있는) 플레이어가 없음 - 가만히 있는다.
            }

            // 261027 수정 - 예섭: "처음엔 플레이어가 직접 가서 닿아야 습득". 닿았으면(touchPickupRadius)
            // 자석 범위와 상관없이 바로 줍고, 자석 범위(MagnetRadius, 기본 0 - 증강으로 늘어남) 안일 때만
            // 끌려간다. 예전엔 자석 범위 밖이면 닿아도 못 주웠고, 기본 자석 2.5가 너무 넓었다.
            float distance = Vector2.Distance(transform.position, nearest.transform.position);
            if (distance <= Mathf.Max(touchPickupRadius, collectDistance))
            {
                Collect(nearest);
                return;
            }

            if (distance > nearest.MagnetRadius)
            {
                return; // 자석 범위 밖 - 가만히 있는다.
            }

            Vector2 dir = ((Vector2)nearest.transform.position - (Vector2)transform.position).normalized;
            transform.position += (Vector3)(dir * pullSpeed * Time.fixedDeltaTime);
        }

        private void Collect(PlayerHealth player)
        {
            if (player.TryGetComponent(out PlayerProgress progress))
            {
                ApplyReward(progress, amount);
            }

            // destroy 기본값(true)으로 Despawn하면 LootPool이 등록해둔 핸들러가 실제 Destroy
            // 대신 가로채서 풀로 반환한다(Enemy.FinishDeath와 동일 패턴).
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }

        /// <summary>종류별 지급 방법(골드는 수거한 플레이어 개인에게, 경험치는 파티 공유로) -
        /// GoldLootPickup/ExpLootPickup이 구현한다.</summary>
        protected abstract void ApplyReward(PlayerProgress progress, int rewardAmount);
    }
}
