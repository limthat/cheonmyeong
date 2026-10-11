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
        [SerializeField] private Enemy enemyPrefab;
        [SerializeField] private int defaultCapacity = 32;
        [SerializeField] private int maxSize = 256;

        private ObjectPool<Enemy> pool;

        private void Awake()
        {
            pool = new ObjectPool<Enemy>(
                createFunc: CreateEnemy,
                actionOnGet: e => { },
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => Destroy(e.gameObject),
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
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
