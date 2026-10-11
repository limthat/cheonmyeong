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
                SpawnOne();
                timer = spawnInterval;
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
