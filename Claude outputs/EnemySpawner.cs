using CheonMyeong.Player;
using CheonMyeong.Progression;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 임시 스폰 로직 - 웨이브 vs 지속 스폰이 아직 미정이라 우선
    /// "일정 주기마다 플레이어 주변 원형 경계에서 지속 스폰"으로 가장 단순하게
    /// 구현했다. 기획이 확정되면 이 클래스만 교체/확장하면 되도록 스폰 판단
    /// 로직을 SpawnOne() 밖으로 분리해뒀다.
    ///
    /// 네트워크 버전: 스폰 판단/실행은 서버만 한다(클라이언트가 각자 스폰하면
    /// 서로 다른 적이 중복으로 생긴다 - 이전에 "참가하기도 전에 죽는다"고 보고된
    /// 문제의 원인이 이거였다). 더 이상 고정된 단일 플레이어 Transform을 참조하지
    /// 않고, PlayerHealth.GetRandomAlivePlayerTransform()으로 그때그때 접속해 있는
    /// 플레이어 중 하나를 기준점으로 고른다(누구 주변에 스폰할지는 미정이라 우선
    /// 무작위 처리 - 밸런스 확정되면 교체).
    ///
    /// 시작 유예(261013 기획 지시): 게임(네트워크 세션) 시작 후 15초 동안은
    /// 몬스터가 전혀 생기지 않는다 - initialSpawnDelaySeconds 참고.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private EnemyPool pool;
        [SerializeField] private float spawnInterval = 1f;
        [SerializeField] private float spawnRadius = 10f;

        [Header("시작 유예")]
        [Tooltip("서버(호스트)가 실제로 리스닝을 시작한 시점부터 이 시간(초)이 지나기 전까지는\n" +
                 "전혀 스폰하지 않는다 - 기획 지시(261013): \"게임 시작 후 15초 카운트 후에 몬스터 생성\".\n" +
                 "씬 로드 시점(Awake/Start)이 아니라 '서버가 실제로 떠 있는 시간'을 기준으로 재는 이유:\n" +
                 "이 오브젝트는 네트워크가 시작되기 전부터 씬에 존재할 수 있어서, 단순히 컴포넌트가\n" +
                 "깨어난 시점부터 재면 대기실/로딩에 걸린 시간만큼 유예가 미리 깎여버린다.")]
        [SerializeField] private float initialSpawnDelaySeconds = 15f;

        [Header("부하 테스트용 - 평소엔 1")]
        [Tooltip("한 번 스폰할 때 몇 마리씩 뿌릴지. 부하 테스트할 때만 Inspector에서 올렸다 내리면 됨.")]
        [SerializeField] private int enemiesPerTick = 1;

        [Tooltip("동시에 살아있을 수 있는 적의 최대 수. 이 값을 넘으면 스폰을 쉰다 - " +
                 "상한이 없으면 처치 속도보다 스폰 속도가 빠를 때 무한정 쌓인다. 값은 미정 - 밸런스에 맞춰 조정.")]
        [SerializeField] private int maxAliveEnemies = 100;

        private float timer;
        private float serverRunningElapsed;

        private void Update()
        {
            // 스폰 판단/실행은 서버(호스트 포함)만 - 클라이언트는 아무것도 하지 않는다.
            if (!IsServerRunning() || pool == null)
            {
                // 서버가 아직 안 떴거나(대기실 단계) 멈췄으면 유예 타이머도 같이 리셋한다 -
                // "서버가 실제로 리스닝 중인 시간"만 누적해야 하기 때문.
                serverRunningElapsed = 0f;
                return;
            }

            // 261014 추가 - 일시정지 중엔 시작 유예/스폰 주기 타이머도 함께 멈춘다 - 안 멈추면
            // 증강 선택 화면이 떠 있는 동안에도 몬스터가 계속 쌓이듯 스폰된다.
            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                return;
            }

            serverRunningElapsed += Time.deltaTime;
            if (serverRunningElapsed < initialSpawnDelaySeconds)
            {
                return; // 시작 유예 시간 동안은 스폰 타이머 자체를 돌리지 않는다.
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

        private static bool IsServerRunning()
        {
            return NetworkManager.Singleton != null
                   && NetworkManager.Singleton.IsListening
                   && NetworkManager.Singleton.IsServer;
        }

        private void SpawnOne()
        {
            Transform spawnCenter = PlayerHealth.GetRandomAlivePlayerTransform();
            if (spawnCenter == null)
            {
                return; // 접속해 있는(또는 살아있는) 플레이어가 없음 - 스폰할 필요 없음
            }

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
            Vector2 spawnPos = (Vector2)spawnCenter.position + offset;
            pool.GetAndSpawn(spawnPos);
        }
    }
}
