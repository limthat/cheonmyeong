using UnityEngine;
using UnityEngine.Pool;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// UnityEngine.Pool.ObjectPool&lt;Enemy&gt; 기반 풀. 스폰/소멸이 잦은
    /// 적을 Instantiate/Destroy 대신 Get/Release로 재사용해 GC 압박을 줄인다.
    /// defaultCapacity/maxSize는 미정 - 프로토타입 부하 테스트 후 조정한다.
    /// </summary>
    public class EnemyPool : MonoBehaviour
    {
        /// <summary>
        /// CheonMyeongPrototypeSetup.EnemyLayerName과 반드시 같은 이름이어야 한다.
        /// 에디터 셋업 스크립트가 이 레이어를 생성하고 적 프리팹에 지정해둔다.
        /// </summary>
        private const string EnemyLayerName = "Enemy";

        [SerializeField] private Enemy enemyPrefab;
        [SerializeField] private int defaultCapacity = 32;
        [SerializeField] private int maxSize = 512; // "수백 마리" 부하 테스트 기준 - 모자라면 Inspector에서 올리면 됨

        private ObjectPool<Enemy> pool;

        private void Awake()
        {
            DisableEnemySelfCollision();

            pool = new ObjectPool<Enemy>(
                createFunc: CreateEnemy,
                actionOnGet: e => { },
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => Destroy(e.gameObject),
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
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

        public Enemy Get(Vector2 position, Transform chaseTarget)
        {
            Enemy enemy = pool.Get();
            enemy.Spawn(position, chaseTarget);
            return enemy;
        }

        private Enemy CreateEnemy()
        {
            Enemy enemy = Instantiate(enemyPrefab, transform);
            enemy.OnDespawn += HandleDespawn;
            return enemy;
        }

        private void HandleDespawn(Enemy enemy)
        {
            pool.Release(enemy);
        }
    }
}
