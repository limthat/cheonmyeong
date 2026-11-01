using System.Collections.Generic;
using CheonMyeong.Data;
using CheonMyeong.Maps;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEngine;

namespace CheonMyeong.Enemies
{
    /// <summary>
    /// 몹 스폰 - 서버 전용(클라이언트가 각자 스폰하면 서로 다른 적이 중복으로 생긴다).
    /// 스폰 기준점은 PlayerHealth.GetRandomAlivePlayerTransform()으로 그때그때 고른다.
    ///
    /// 261027 수정 - 웨이브 시스템(WaveManager) 연동. 씬에 WaveManager가 있으면:
    /// - 웨이브 진행 중에만 스폰한다(준비 시간/완료 후엔 스폰 없음 - 기존 "시작 15초 유예"는
    ///   WaveConfigSO.preparationSeconds로 옮겨짐).
    /// - 웨이브별 원귀 총 수/동시 최대 수/등장 비율을 그 웨이브의 WaveDefinition에서 읽는다.
    /// WaveManager가 없는 씬(테스트 등)에서는 아래 Fallback 값으로 예전처럼 지속 스폰한다.
    ///
    /// pools는 "이 씬에서 스폰 가능한 몹 풀 목록"(레지스트리)이다. 웨이브 구성은 몹을
    /// EnemyStatsSO로 지정하고, 여기서 그 스탯을 쓰는 풀을 찾아 스폰한다(EnemyPool.PrefabStats).
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Tooltip("이 씬에서 스폰 가능한 적 풀 목록 - 풀마다 서로 다른 몹(EnemyStatsSO/프리팹)을 들고 있다.\n" +
                 "CheonMyeong/Setup/Create Enemy Variants 메뉴가 Ghost1/2/3 풀을 채워준다.")]
        [SerializeField] private EnemyPool[] pools;

        [Tooltip("플레이어에서 이 거리만큼 떨어진 곳에 소환한다(기획서: 화면 밖). 실제로는 카메라 화면 대각선 반 + spawnScreenMargin과\n" +
                 "이 값 중 큰 쪽을 쓴다 - 카메라 크기를 바꿔도 화면 안에서 튀어나오지 않게.")]
        [SerializeField] private float spawnRadius = 11f;

        [Tooltip("카메라 화면 가장자리 밖으로 얼마나 더 떨어뜨려 소환할지(유닛).")]
        [SerializeField] private float spawnScreenMargin = 1f;

        [Header("소환 패턴 모양 (261101)")]
        [Tooltip("Cluster(무리) 한 덩어리의 반지름.")]
        [SerializeField] private float clusterSpread = 1.5f;
        [Tooltip("Line(줄) 패턴에서 원귀 사이 간격.")]
        [SerializeField] private float lineSpacing = 0.8f;

        [Header("WaveManager가 없을 때만 쓰는 대체값 (테스트용)")]
        [Tooltip("pools와 같은 순서의 가중치. 길이가 안 맞으면 균등 확률.")]
        [SerializeField] private int[] poolWeights;
        [SerializeField] private float spawnInterval = 1f;
        [SerializeField] private float initialSpawnDelaySeconds = 15f;
        [Tooltip("한 번 스폰할 때 몇 마리씩 뿌릴지.")]
        [SerializeField] private int enemiesPerTick = 1;
        [SerializeField] private int maxAliveEnemies = 100;

        private float timer;
        private float serverRunningElapsed;

        private const int MaxSpawnPerFrame = 10;
        private int trackedWave;
        private int trickleSpawnedThisWave;
        private int patternSpawnedThisWave;
        private int patternEventsFired;

        // 261101 - 패턴은 한 번에 수십 마리를 정하므로 바로 다 스폰하지 않고 대기열에 넣어 한 프레임 MaxSpawnPerFrame씩,
        // 동시 상한(maxAliveEnemies) 안에서 꺼낸다. 위치는 패턴을 정한 순간 기준(그 사이 플레이어가 움직여도 모양 유지).
        private struct PendingSpawn
        {
            public EnemyPool Pool;
            public Vector2 Position;
        }

        private readonly Queue<PendingSpawn> pendingSpawns = new Queue<PendingSpawn>();
        private readonly List<int> patternWeightBuffer = new List<int>();

        // 웨이브 구성의 EnemyStatsSO → 풀 매핑 캐시. 풀 목록은 런타임에 안 바뀌므로 처음 한 번만 만든다.
        private Dictionary<EnemyStatsSO, EnemyPool> poolByStats;

        private void Update()
        {
            if (!IsServerRunning() || !HasAnyPool())
            {
                serverRunningElapsed = 0f;
                return;
            }

            if (PartyProgress.Instance != null && PartyProgress.Instance.IsPaused)
            {
                return;
            }

            WaveManager waveManager = WaveManager.Instance;
            if (waveManager != null)
            {
                UpdateWithWave(waveManager);
            }
            else
            {
                UpdateFallback();
            }
        }

        /// <summary>
        /// 웨이브 진행도(0~1)에 맞춰 "지금까지 나왔어야 할 수"를 계산하고 모자란 만큼 스폰한다 -
        /// 기획서 5-3의 웨이브별 원귀 수(enemyCount)가 웨이브 시간 동안 고르게 나뉜다. 동시 최대 수
        /// (maxAliveEnemies)에 막히면 밀린 만큼 자리가 날 때 이어서 낸다. 한 프레임 폭주 방지로
        /// 프레임당 최대 MaxSpawnPerFrame마리.
        /// </summary>
        private void UpdateWithWave(WaveManager waveManager)
        {
            WaveDefinition wave = waveManager.CurrentDefinition;
            if (wave == null)
            {
                trackedWave = 0;
                pendingSpawns.Clear();
                return;
            }

            if (trackedWave != waveManager.CurrentWave)
            {
                trackedWave = waveManager.CurrentWave;
                trickleSpawnedThisWave = 0;
                patternSpawnedThisWave = 0;
                patternEventsFired = 0;
                pendingSpawns.Clear();
            }

            WaveConfigSO config = waveManager.Config;
            bool usePatterns = config != null && HasUsablePattern(config);
            float trickleShare = usePatterns ? config.trickleShare : 1f;
            int trickleTotal = Mathf.RoundToInt(wave.enemyCount * trickleShare);
            int patternTotal = wave.enemyCount - trickleTotal;

            // 상시 소환 - 웨이브 시간에 맞춰 고르게.
            int trickleTarget = Mathf.Min(trickleTotal, Mathf.CeilToInt(trickleTotal * waveManager.ServerWaveProgress));
            int spawnedThisFrame = 0;
            while (trickleSpawnedThisWave < trickleTarget && spawnedThisFrame < MaxSpawnPerFrame)
            {
                if (TotalActiveCount() >= wave.maxAliveEnemies || !SpawnOne(PickPoolForWave(wave)))
                {
                    break;
                }
                trickleSpawnedThisWave++;
                spawnedThisFrame++;
            }

            // 패턴 소환 - patternIntervalSeconds마다 한 번, 남은 몫을 남은 횟수로 나눠서.
            if (usePatterns && patternTotal > 0)
            {
                float duration = waveManager.ServerWaveDuration;
                int eventCount = Mathf.Max(1, Mathf.FloorToInt(duration / config.patternIntervalSeconds));
                int due = Mathf.Min(eventCount, Mathf.FloorToInt(waveManager.ServerWaveElapsed / config.patternIntervalSeconds));
                while (patternEventsFired < due)
                {
                    patternEventsFired++;
                    int targetSoFar = Mathf.RoundToInt((float)patternTotal * patternEventsFired / eventCount);
                    int amount = targetSoFar - patternSpawnedThisWave;
                    if (amount > 0)
                    {
                        EnqueuePattern(config, wave, amount);
                        patternSpawnedThisWave += amount;
                    }
                }
            }

            while (pendingSpawns.Count > 0 && spawnedThisFrame < MaxSpawnPerFrame)
            {
                if (TotalActiveCount() >= wave.maxAliveEnemies)
                {
                    break;
                }
                PendingSpawn next = pendingSpawns.Dequeue();
                if (next.Pool != null)
                {
                    next.Pool.GetAndSpawn(next.Position);
                    spawnedThisFrame++;
                }
            }
        }

        private static bool HasUsablePattern(WaveConfigSO config)
        {
            if (config.spawnPatterns == null || config.trickleShare >= 1f)
            {
                return false;
            }
            for (int i = 0; i < config.spawnPatterns.Count; i++)
            {
                if (config.spawnPatterns[i] != null && config.spawnPatterns[i].weight > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void EnqueuePattern(WaveConfigSO config, WaveDefinition wave, int amount)
        {
            Transform target = PlayerHealth.GetRandomAlivePlayerTransform();
            if (target == null)
            {
                return;
            }

            Vector2 center = target.position;
            float radius = EffectiveSpawnRadius();
            SpawnPatternType pattern = PickPattern(config);
            float angle = PickOpenAngle(center, radius);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            MapBounds bounds = MapBounds.Instance;

            switch (pattern)
            {
                case SpawnPatternType.Ring:
                    for (int i = 0; i < amount; i++)
                    {
                        float a = angle + Mathf.PI * 2f * i / amount;
                        Vector2 pos = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                        if (bounds != null && !bounds.Contains(pos))
                        {
                            pos = PickSpawnPosition(center);
                        }
                        Enqueue(PickPoolForWave(wave), pos);
                    }
                    break;

                case SpawnPatternType.Line:
                {
                    Vector2 lineCenter = center + dir * radius;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    float half = (amount - 1) * lineSpacing * 0.5f;
                    for (int i = 0; i < amount; i++)
                    {
                        Enqueue(PickPoolForWave(wave), ClampToMap(lineCenter + side * (i * lineSpacing - half)));
                    }
                    break;
                }

                default:
                {
                    Vector2 clusterCenter = center + dir * (radius + clusterSpread);
                    EnemyPool pool = PickPoolForWave(wave);
                    for (int i = 0; i < amount; i++)
                    {
                        Enqueue(pool, ClampToMap(clusterCenter + Random.insideUnitCircle * clusterSpread));
                    }
                    break;
                }
            }
        }

        private void Enqueue(EnemyPool pool, Vector2 position)
        {
            if (pool != null)
            {
                pendingSpawns.Enqueue(new PendingSpawn { Pool = pool, Position = position });
            }
        }

        private SpawnPatternType PickPattern(WaveConfigSO config)
        {
            patternWeightBuffer.Clear();
            int total = 0;
            for (int i = 0; i < config.spawnPatterns.Count; i++)
            {
                SpawnPatternEntry entry = config.spawnPatterns[i];
                int weight = entry != null ? Mathf.Max(0, entry.weight) : 0;
                patternWeightBuffer.Add(weight);
                total += weight;
            }

            int roll = Random.Range(0, Mathf.Max(1, total));
            for (int i = 0; i < patternWeightBuffer.Count; i++)
            {
                roll -= patternWeightBuffer[i];
                if (roll < 0)
                {
                    return config.spawnPatterns[i].pattern;
                }
            }
            return SpawnPatternType.Cluster;
        }

        /// <summary>맵 안쪽으로 열려 있는 방향을 고른다(벽 쪽으로 소환하지 않게). 못 찾으면 맵 중앙 방향.</summary>
        private float PickOpenAngle(Vector2 center, float radius)
        {
            MapBounds bounds = MapBounds.Instance;
            for (int i = 0; i < SpawnPositionTries; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                if (bounds == null || bounds.Contains(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius))
                {
                    return angle;
                }
            }
            Vector2 toMiddle = bounds.PlayableArea.center - center;
            return Mathf.Atan2(toMiddle.y, toMiddle.x);
        }

        private static Vector2 ClampToMap(Vector2 position)
        {
            MapBounds bounds = MapBounds.Instance;
            return bounds != null ? bounds.ClampInside(position) : position;
        }

        /// <summary>카메라 화면 밖이 되도록 소환 거리를 보정한다(orthographic 기준).</summary>
        private float EffectiveSpawnRadius()
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                return spawnRadius;
            }
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            return Mathf.Max(spawnRadius, Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) + spawnScreenMargin);
        }

        private void UpdateFallback()
        {
            serverRunningElapsed += Time.deltaTime;
            if (serverRunningElapsed < initialSpawnDelaySeconds)
            {
                return;
            }

            timer -= Time.deltaTime;
            if (timer > 0f)
            {
                return;
            }
            timer = spawnInterval;

            for (int i = 0; i < enemiesPerTick; i++)
            {
                if (TotalActiveCount() >= maxAliveEnemies)
                {
                    break;
                }
                SpawnOne(PickPoolByIndexWeights());
            }
        }

        private static bool IsServerRunning()
        {
            return NetworkManager.Singleton != null
                   && NetworkManager.Singleton.IsListening
                   && NetworkManager.Singleton.IsServer;
        }

        private bool HasAnyPool()
        {
            if (pools == null)
            {
                return false;
            }
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null)
                {
                    return true;
                }
            }
            return false;
        }

        private int TotalActiveCount()
        {
            int total = 0;
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null)
                {
                    total += pools[i].ActiveCount;
                }
            }
            return total;
        }

        private EnemyPool FindPool(EnemyStatsSO stats)
        {
            if (poolByStats == null)
            {
                poolByStats = new Dictionary<EnemyStatsSO, EnemyPool>();
                for (int i = 0; i < pools.Length; i++)
                {
                    EnemyPool pool = pools[i];
                    if (pool != null && pool.PrefabStats != null && !poolByStats.ContainsKey(pool.PrefabStats))
                    {
                        poolByStats.Add(pool.PrefabStats, pool);
                    }
                }
            }

            if (stats == null)
            {
                return null;
            }
            poolByStats.TryGetValue(stats, out EnemyPool found);
            return found;
        }

        /// <summary>
        /// 웨이브 구성(spawns)의 가중치대로 고른다. 풀이 없는 항목(아직 변종 메뉴를 안 돌렸거나
        /// 이 씬에 없는 몹)은 건너뛰고, 고를 게 하나도 없으면 등록된 풀 전체에서 균등 선택.
        /// 매 스폰마다 리스트를 새로 만들지 않도록 두 번 순회(합계 → 추첨)로 처리.
        /// </summary>
        private EnemyPool PickPoolForWave(WaveDefinition wave)
        {
            List<WaveSpawnEntry> spawns = wave.spawns;
            int totalWeight = 0;
            if (spawns != null)
            {
                for (int i = 0; i < spawns.Count; i++)
                {
                    if (spawns[i] != null && spawns[i].weight > 0 && FindPool(spawns[i].enemy) != null)
                    {
                        totalWeight += spawns[i].weight;
                    }
                }
            }

            if (totalWeight <= 0)
            {
                return PickAnyPool();
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < spawns.Count; i++)
            {
                WaveSpawnEntry entry = spawns[i];
                if (entry == null || entry.weight <= 0)
                {
                    continue;
                }
                EnemyPool pool = FindPool(entry.enemy);
                if (pool == null)
                {
                    continue;
                }
                roll -= entry.weight;
                if (roll < 0)
                {
                    return pool;
                }
            }
            return PickAnyPool();
        }

        private EnemyPool PickPoolByIndexWeights()
        {
            bool weightsValid = poolWeights != null && poolWeights.Length == pools.Length;
            if (!weightsValid)
            {
                return PickAnyPool();
            }

            int totalWeight = 0;
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null)
                {
                    totalWeight += Mathf.Max(0, poolWeights[i]);
                }
            }
            if (totalWeight <= 0)
            {
                return PickAnyPool();
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] == null)
                {
                    continue;
                }
                roll -= Mathf.Max(0, poolWeights[i]);
                if (roll < 0)
                {
                    return pools[i];
                }
            }
            return PickAnyPool();
        }

        private EnemyPool PickAnyPool()
        {
            int validCount = 0;
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null)
                {
                    validCount++;
                }
            }
            if (validCount == 0)
            {
                return null;
            }

            int pick = Random.Range(0, validCount);
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] == null)
                {
                    continue;
                }
                if (pick-- == 0)
                {
                    return pools[i];
                }
            }
            return null;
        }

        private bool SpawnOne(EnemyPool pool)
        {
            if (pool == null)
            {
                return false;
            }

            Transform spawnCenter = PlayerHealth.GetRandomAlivePlayerTransform();
            if (spawnCenter == null)
            {
                return false;
            }

            pool.GetAndSpawn(PickSpawnPosition(spawnCenter.position));
            return true;
        }

        private const int SpawnPositionTries = 8;

        /// <summary>
        /// 261027 추가 - 기획서 5-2 "화면 밖, 맵 경계 안쪽에서 등장". 플레이어 주변 반경 spawnRadius 원 위에서
        /// 맵 안쪽(MapBounds)에 떨어지는 지점을 몇 번 무작위로 찾고, 끝내 못 찾으면(플레이어가 담 바로 옆 등)
        /// 맵 안쪽으로 끌어당긴 위치를 쓴다. 맵이 없는 씬에서는 예전처럼 원 위 아무 데나.
        /// </summary>
        private Vector2 PickSpawnPosition(Vector2 center)
        {
            MapBounds bounds = MapBounds.Instance;
            Vector2 candidate = center;
            for (int i = 0; i < SpawnPositionTries; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * EffectiveSpawnRadius();
                if (bounds == null || bounds.Contains(candidate))
                {
                    return candidate;
                }
            }
            return bounds.ClampInside(candidate);
        }
    }
}
