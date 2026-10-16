using CheonMyeong.Networking;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 네트워크 버전 풀. 공통 풀링 로직(ObjectPool&lt;NetworkObject&gt; +
    /// INetworkPrefabInstanceHandler 등록, NGO 2.13.3 공식 Object Pooling 가이드 패턴)은
    /// NetworkPrefabPoolBase로 옮겼다(261014 정리 - LootPool.cs와 완전히 같던 중복 코드를
    /// 하나로 합침, 동작 변화 없음). 이 클래스는 Enemy 전용 GetAndSpawn()만 갖는다.
    ///
    /// defaultCapacity/maxSize는 미정 - 프로토타입 부하 테스트 후 조정한다.
    /// </summary>
    public class EnemyPool : NetworkPrefabPoolBase
    {
        /// <summary>
        /// CheonMyeongPrototypeSetup.EnemyLayerName과 반드시 같은 이름이어야 한다.
        /// 에디터 셋업 스크립트가 이 레이어를 생성하고 적 프리팹에 지정해둔다.
        /// </summary>
        private const string EnemyLayerName = "Enemy";

        [SerializeField] private Enemy enemyPrefab;

        private void Awake()
        {
            DisableEnemySelfCollision();
            InitializePool(enemyPrefab != null ? enemyPrefab.gameObject : null);
        }

        /// <summary>
        /// 적끼리는 서로 물리/트리거 판정을 안 하게 끈다. 수백 마리가 같은
        /// 지점에 몰리면 적-적 쌍끼리 겹침 판정(OnTriggerStay2D)이 조합 폭발하듯
        /// 늘어나서(최악의 경우 n^2) 프레임이 급격히 떨어지는 걸 확인했다 -
        /// 이 설정으로 적-플레이어 판정은 그대로 두고 적-적 판정만 물리 엔진
        /// 단계에서 아예 제거한다(이벤트가 호출된 뒤 걸러내는 것보다 훨씬 싸다).
        /// </summary>
        private static void DisableEnemySelfCollision()
        {
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
            if (enemyLayer == -1)
            {
                Debug.LogError($"[CheonMyeong] '{EnemyLayerName}' 레이어가 없습니다. " +
                                "CheonMyeong/Setup/Create Enemy Prototype 메뉴를 먼저 실행하세요.");
                return;
            }

            Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, true);
        }

        /// <summary>
        /// 서버 전용 - 풀에서 꺼내 위치/체력을 초기화하고 바로 NetworkObject.Spawn()까지
        /// 호출해서 돌려준다. EnemySpawner가 서버에서만 이 메서드를 호출해야 한다.
        /// 추적 대상은 더 이상 스폰 시점에 고정하지 않는다 - Enemy가 매 FixedUpdate마다
        /// 서버에서 가장 가까운 살아있는 플레이어를 직접 찾는다(멀티플레이 대응).
        /// </summary>
        public Enemy GetAndSpawn(Vector2 position)
        {
            NetworkObject netObj = GetFromPool();
            netObj.transform.position = position;

            Enemy enemy = netObj.GetComponent<Enemy>();
            enemy.ServerInitialize();

            netObj.Spawn();
            return enemy;
        }
    }
}
