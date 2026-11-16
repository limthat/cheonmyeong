using System;
using System.Collections.Generic;
using CheonMyeong.Data;
using UnityEngine;

namespace CheonMyeong.Waves
{
    /// <summary>
    /// 261027 추가 - 웨이브 구성 데이터("하드코딩 말고 유연하게, 한 웨이브당 시간도 수정 용이하게" -
    /// 예섭 요청). 웨이브 수/순서/길이/원귀 수/등장 비율/체력 증가/징 회복량을 전부 이 에셋 하나에서
    /// Inspector로 바꾼다 - WaveManager/EnemySpawner는 숫자를 하나도 들고 있지 않다.
    ///
    /// 웨이브 길이: defaultWaveDurationSeconds 한 값만 고치면 전 웨이브가 같이 바뀌고, 특정 웨이브만
    /// 바꾸고 싶으면 그 웨이브의 durationOverrideSeconds에 0보다 큰 값을 넣는다.
    ///
    /// 기본값의 출처는 전부 [기획서] 천명_261013 5-1/5-2/5-3/5-10(Create Wave System 메뉴가 그대로 채움).
    /// 맵마다 다른 구성이 필요해지면 이 에셋을 맵별로 만들어 각 Gameplay 씬의 WaveManager에 연결.
    /// 억쇠(261101): WaveDefinition.boss = Mid인 웨이브에 중간 보스가 나오고(기본 5웨이브), 마지막 웨이브가 끝나면
    /// finalBattleSeconds 동안 최종전을 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWaveConfig", menuName = "CheonMyeong/Wave Config")]
    public class WaveConfigSO : ScriptableObject
    {
        [Tooltip("게임 시작 후 첫 웨이브 전 준비 시간(초) - 261013 기획 지시 15초.")]
        [Min(0f)] public float preparationSeconds = 15f;

        [Tooltip("261110 - 웨이브가 끝나고 다음 웨이브(또는 최종전)가 시작되기 전 대기 시간(초). 이 동안 원귀가 안 나온다.")]
        [Min(0f)] public float intermissionSeconds = 15f;

        [Tooltip("모든 웨이브의 기본 길이(초). 이 값 하나로 전 웨이브 길이가 바뀐다 - 기획서 v0.9.3: 75초.")]
        [Min(1f)] public float defaultWaveDurationSeconds = 75f;

        [Tooltip("웨이브가 하나 지날 때마다 원귀 체력에 더해지는 비율(1웨이브 = 1배). 기획서 v0.9.3: +10%\n" +
                 "(10웨이브 1.9배 = 선형). 웨이브별 healthMultiplierOverride가 있으면 그게 우선.")]
        [Min(0f)] public float healthGrowthPerWave = 0.10f;

        [Tooltip("261101 추가 - 웨이브가 하나 지날 때마다 원귀 공격(접촉) 피해에 더해지는 비율. 기획서 v0.9.3: +7%\n" +
                 "(10웨이브 1.63배).")]
        [Min(0f)] public float damageGrowthPerWave = 0.07f;

        [Header("소환 패턴 - 261101 추가 (뱀서류처럼 사방 흘러나옴 + 주기적인 무리 소환)")]
        [Tooltip("웨이브 총 원귀 수 중 '상시로 흘러나오는' 비율. 나머지는 아래 패턴 소환으로 나온다.\n" +
                 "1이면 패턴 없이 전부 고르게 흘러나옴(예전 방식), 0이면 전부 패턴으로만 나옴.")]
        [Range(0f, 1f)] public float trickleShare = 0.4f;

        [Tooltip("패턴 소환 간격(초). 웨이브 시간 ÷ 이 값 = 그 웨이브의 패턴 소환 횟수.")]
        [Min(1f)] public float patternIntervalSeconds = 6f;

        [Tooltip("261110 - 웨이브 끝나기 이 시간(초) 전까지 그 웨이브 원귀를 전부 낸다(마지막 패턴 원귀를 잡을 시간). 웨이브가 짧으면 최소 절반 길이 동안은 나눠 낸다.")]
        [Min(0f)] public float spawnEndsBeforeWaveEnd = 30f;

        /// <summary>261110 - 원귀를 나눠 내는 시간(초) = 웨이브 길이 - spawnEndsBeforeWaveEnd (최소 웨이브의 절반).</summary>
        public float SpawnWindow(float waveDuration)
        {
            return Mathf.Max(waveDuration * 0.5f, waveDuration - spawnEndsBeforeWaveEnd);
        }

        [Tooltip("패턴 종류와 뽑힐 비중. 비어 있으면 패턴 몫도 상시로 흘러나온다.")]
        public List<SpawnPatternEntry> spawnPatterns = new List<SpawnPatternEntry>
        {
            new SpawnPatternEntry { pattern = SpawnPatternType.Cluster, weight = 5 },
            new SpawnPatternEntry { pattern = SpawnPatternType.Ring, weight = 2 },
            new SpawnPatternEntry { pattern = SpawnPatternType.Line, weight = 3 },
        };

        [Header("웨이브 종료(징) - 기획서 5-2/5-10")]
        [Tooltip("살아있는 플레이어가 회복하는 양(최대 체력 비율) - 기획서 10%.")]
        [Range(0f, 1f)] public float endOfWaveHealRatio = 0.1f;

        [Tooltip("쓰러진 플레이어가 부활할 때의 체력(최대 체력 비율) - 기획서 30%.")]
        [Range(0f, 1f)] public float reviveHealthRatio = 0.3f;

        [Header("판 종료 - 기획서 5-10")]
        [Tooltip("클리어하면 각자 받는 엽전 - 기획서 +200(실패하면 없음, 주운 엽전만 받음).")]
        [Min(0)] public int clearRewardGold = 200;

        [Header("억쇠 최종전 - 기획서 9장 (261101)")]
        [Tooltip("마지막 웨이브가 끝난 뒤 최종전 제한 시간(초) - 기획서 3분. 0이면 최종전 없이 바로 클리어.")]
        [Min(0f)] public float finalBattleSeconds = 180f;

        [Tooltip("웨이브 목록 - 위에서부터 순서대로 진행한다. 개수 = 전체 웨이브 수.")]
        public List<WaveDefinition> waves = new List<WaveDefinition>();

        public int WaveCount => waves != null ? waves.Count : 0;

        /// <summary>1부터 시작하는 웨이브 번호로 정의를 가져온다. 범위 밖이면 null.</summary>
        public WaveDefinition GetWave(int waveNumber)
        {
            int index = waveNumber - 1;
            if (waves == null || index < 0 || index >= waves.Count)
            {
                return null;
            }
            return waves[index];
        }

        public float GetDuration(WaveDefinition wave)
        {
            if (wave != null && wave.durationOverrideSeconds > 0f)
            {
                return wave.durationOverrideSeconds;
            }
            return defaultWaveDurationSeconds;
        }

        public float GetDamageMultiplier(int waveNumber)
        {
            return 1f + damageGrowthPerWave * Mathf.Max(0, waveNumber - 1);
        }

        public float GetHealthMultiplier(int waveNumber)
        {
            WaveDefinition wave = GetWave(waveNumber);
            if (wave != null && wave.healthMultiplierOverride > 0f)
            {
                return wave.healthMultiplierOverride;
            }
            return 1f + healthGrowthPerWave * Mathf.Max(0, waveNumber - 1);
        }
    }

    [Serializable]
    public class WaveDefinition
    {
        [Tooltip("Inspector 구분용 이름(예: \"1웨이브\"). 게임 로직에는 쓰지 않는다.")]
        public string label;

        [Tooltip("0 이하 = WaveConfigSO.defaultWaveDurationSeconds 사용.")]
        public float durationOverrideSeconds;

        [Tooltip("이 웨이브 동안 나오는 원귀 총 수 - 웨이브 시간 동안 고르게 나눠서 스폰한다.\n" +
                 "기획서 v0.9.3: 125/175/225/275/150/350/410/475/540/625.")]
        [Min(0)] public int enemyCount = 100;

        [Tooltip("동시에 살아있을 수 있는 최대 원귀 수 - 기획서 4: 약 300. 이걸 넘으면 자리가 날 때까지\n" +
                 "스폰을 미뤘다가 이어서 낸다(총 수는 그대로).")]
        [Min(1)] public int maxAliveEnemies = 300;

        [Tooltip("0 이하 = healthGrowthPerWave 공식 사용. 이 웨이브만 체력 배율을 직접 정하고 싶을 때.")]
        public float healthMultiplierOverride;

        [Tooltip("261101 - 이 웨이브에 나오는 보스. Mid = 억쇠 중간 보스(기획서: 5웨이브, 웨이브 시간 안에 쫓아내기).\n" +
                 "Final은 여기 넣지 않는다(마지막 웨이브 뒤 최종전으로 자동 진행).")]
        public Boss.BossEncounter boss;

        [Tooltip("등장 원귀와 비율(가중치). 기획서 5-3: 몽달귀 60 / 달걀귀신 30 / 손각시 10.\n" +
                 "비어있으면 EnemySpawner에 등록된 풀 전체에서 균등하게 뽑는다.")]
        public List<WaveSpawnEntry> spawns = new List<WaveSpawnEntry>();
    }

    public enum SpawnPatternType
    {
        [Tooltip("한 방향 화면 밖에서 한 덩어리로 몰려온다(같은 종류).")]
        Cluster,
        [Tooltip("플레이어를 화면 밖에서 원형으로 둘러싸고 조여온다.")]
        Ring,
        [Tooltip("한 방향 화면 밖에서 옆으로 늘어선 줄(벽)로 밀려온다.")]
        Line,
    }

    [Serializable]
    public class SpawnPatternEntry
    {
        public SpawnPatternType pattern;
        [Min(0)] public int weight = 1;
    }

    [Serializable]
    public class WaveSpawnEntry
    {
        [Tooltip("등장시킬 원귀의 스탯 에셋 - EnemySpawner가 이 스탯을 쓰는 프리팹의 풀을 찾아 스폰한다.")]
        public EnemyStatsSO enemy;

        [Min(0)] public int weight = 1;
    }
}
