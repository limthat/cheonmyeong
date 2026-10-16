using CheonMyeong.Networking;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 드랍 아이템(골드 또는 경험치, 둘 중 하나) 전용 네트워크 풀. 공통 풀링 로직은
    /// NetworkPrefabPoolBase로 옮겼다(261014 정리 - EnemyPool.cs와 완전히 같던 중복 코드를
    /// 하나로 합침, 동작 변화 없음). 골드/경험치는 서로 다른 프리팹(GoldLootPickup/ExpLootPickup)
    /// 이라 풀도 LootSpawner가 종류별로 하나씩(goldPool/expPool) 따로 들고 있는다 - 한 풀에서
    /// 타입을 섞어 쓰면 Get()이 꺼내주는 인스턴스가 실제로 Gold인지 Exp인지 구분할 방법이
    /// 없어진다.
    ///
    /// defaultCapacity/maxSize는 미정 - 프로토타입 부하 테스트 후 조정한다(EnemyPool과 동일).
    /// </summary>
    public class LootPool : NetworkPrefabPoolBase
    {
        [SerializeField] private LootPickup lootPrefab;

        private void Awake()
        {
            InitializePool(lootPrefab != null ? lootPrefab.gameObject : null);
        }

        /// <summary>
        /// 서버 전용 - 풀에서 꺼내 위치/보상량을 초기화하고 바로 NetworkObject.Spawn()까지
        /// 호출해서 돌려준다. LootSpawner가 서버에서만 이 메서드를 호출해야 한다.
        /// </summary>
        public LootPickup GetAndSpawn(Vector2 position, int rewardAmount)
        {
            NetworkObject netObj = GetFromPool();
            netObj.transform.position = position;

            LootPickup loot = netObj.GetComponent<LootPickup>();
            loot.ServerInitialize(rewardAmount);

            netObj.Spawn();
            return loot;
        }
    }
}
