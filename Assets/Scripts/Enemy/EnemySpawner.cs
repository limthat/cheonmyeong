using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 임시 스폰 로직 - 웨이브 vs 지속 스폰이 아직 미정이라 우선
    /// "일정 주기마다 화면 밖 원형 경계에서 1마리씩 지속 스폰"으로 가장
    /// 단순하게 구현했다. 기획이 확정되면 이 클래스만 교체/확장하면 되도록
    /// 스폰 판단 로직을 Spawn() 밖으로 분리해뒀다.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private EnemyPool pool;
        [SerializeField] private Transform player;
        [SerializeField] private float spawnInterval = 1f;
        [SerializeField] private float spawnRadius = 10f;

        [Header("부하 테스트용 - 평소엔 1")]
        [Tooltip("한 번 스폰할 때 몇 마리씩 뿌릴지. 부하 테스트할 때만 Inspector에서 올렸다 내리면 됨.")]
        [SerializeField] private int enemiesPerTick = 1;

        [Tooltip("동시에 살아있을 수 있는 적의 최대 수. 이 값을 넘으면 스폰을 쉰다 - " +
                 "상한이 없으면 처치 속도보다 스폰 속도가 빠를 때 무한정 쌓인다. 값은 미정 - 밸런스에 맞춰 조정.")]
        [SerializeField] private int maxAliveEnemies = 100;

        private float timer;

        private void Update()
        {
            if (pool == null || player == null)
            {
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = spawnInterval;

                if (pool.ActiveCount >= maxAliveEnemies)
                {
                    return; // 상한 도달 - 누가 죽어서 자리가 빌 때까지 스폰 대기
                }

                for (int i = 0; i < enemiesPerTick; i++)
                {
                    if (pool.ActiveCount >= maxAliveEnemies)
                    {
                        break;
                    }
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
            Vector2 spawnPos = (Vector2)player.position + offset;
            pool.Get(spawnPos, player);
        }
    }
}
