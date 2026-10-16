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

        private int amount;

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

            PlayerHealth nearest = PlayerHealth.FindNearestAlivePlayerHealth(transform.position);
            if (nearest == null)
            {
                return; // 접속해 있는(또는 살아있는) 플레이어가 없음 - 가만히 있는다.
            }

            float distance = Vector2.Distance(transform.position, nearest.transform.position);
            if (distance > nearest.MagnetRadius)
            {
                return; // 자석 범위 밖 - 가만히 있는다.
            }

            if (distance <= collectDistance)
            {
                Collect(nearest);
                return;
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
