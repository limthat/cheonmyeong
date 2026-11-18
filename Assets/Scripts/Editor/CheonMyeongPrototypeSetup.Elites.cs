using System.Collections.Generic;
using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Waves;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261119 추가 - 중형 원귀 3종(기획서 v1.1 8장 · [스탯] 「중형 원귀」 시트) 한 번에 만들기.
    /// 메뉴: CheonMyeong/Setup/Create Elite Enemies (아귀 · 물귀신 · 역귀)
    /// 1) Assets/Art/Monster/Normal 의 AGWI · MULGWISIN · YEOKGWI 시트를 64px로 자른다(태그마다 한 줄).
    /// 2) 스탯 에셋(Data/Enemies/Agwi · Mulgwisin · Yeokgwi) - 엑셀 값. 속도는 새 단위 ÷ 60.
    /// 3) 프리팹(손각시 프리팹을 복사 → Animator 빼고 EliteEnemy에 줄별 그림 연결) + 기본 네트워크 프리팹 목록 등록.
    /// 4) 씬마다 풀(EnemyPool_Agwi 등)을 만들어 EnemySpawner.pools에 더한다.
    /// 5) DefaultWaves: 6~7웨이브 5%, 8~10웨이브 10%를 몽달귀 몫에서 떼어 아귀 40 · 물귀신 30 · 역귀 30으로 나눈다.
    /// 다시 실행해도 안전(이미 있는 건 값만 다시 맞춘다).
    /// </summary>
    public static partial class CheonMyeongPrototypeSetup
    {
        private const string EliteArtFolder = MonsterArtFolder + "/Normal";

        private struct EliteSpec
        {
            public string Sheet;
            public string StatsPath;
            public string PrefabPath;
            public string PoolName;
            public string Id;
            public string Name;
            public EliteSkill Skill;
            public float Hp, Speed, Damage, ContactInterval;
            public int Soul;
            public float CoinRate;
            public int CoinAmount;
            public int ShareInElites; // 중형 안 비율(%)
            public string[] Rows;     // 시트 줄 이름(위에서부터)
        }

        private static readonly EliteSpec[] EliteSpecs =
        {
            new EliteSpec
            {
                Sheet = "AGWI-Sheet", StatsPath = EnemiesFolder + "/Agwi.asset", PrefabPath = PrefabsFolder + "/Enemy_Agwi.prefab",
                PoolName = "EnemyPool_Agwi", Id = "agwi", Name = "아귀", Skill = EliteSkill.Charge,
                Hp = 260f, Speed = 135f / 60f, Damage = 12f, ContactInterval = 1f, Soul = 5, CoinRate = 0.24f, CoinAmount = 1,
                ShareInElites = 40, Rows = new[] { "WALK", "ATK", "DIE" },
            },
            new EliteSpec
            {
                Sheet = "MULGWISIN-Sheet", StatsPath = EnemiesFolder + "/Mulgwisin.asset", PrefabPath = PrefabsFolder + "/Enemy_Mulgwisin.prefab",
                PoolName = "EnemyPool_Mulgwisin", Id = "mulgwisin", Name = "물귀신", Skill = EliteSkill.Submerge,
                Hp = 220f, Speed = 120f / 60f, Damage = 10f, ContactInterval = 1.5f, Soul = 5, CoinRate = 0.24f, CoinAmount = 1,
                ShareInElites = 30, Rows = new[] { "SWIM", "RISE", "WALK", "ATK", "DIE" },
            },
            new EliteSpec
            {
                Sheet = "YEOKGWI-Sheet", StatsPath = EnemiesFolder + "/Yeokgwi.asset", PrefabPath = PrefabsFolder + "/Enemy_Yeokgwi.prefab",
                PoolName = "EnemyPool_Yeokgwi", Id = "yeokgwi", Name = "역귀", Skill = EliteSkill.Throw,
                Hp = 180f, Speed = 110f / 60f, Damage = 8f, ContactInterval = 1f, Soul = 5, CoinRate = 0.24f, CoinAmount = 1,
                ShareInElites = 30, Rows = new[] { "WALK", "ATK", "DIE", "HORIBYEONG" },
            },
        };

        [MenuItem("CheonMyeong/Setup/Create Elite Enemies (아귀 · 물귀신 · 역귀)")]
        public static void CreateEliteEnemies()
        {
            if (AssetDatabase.LoadAssetAtPath<Enemy>(Ghost3PrefabPath) == null)
            {
                Debug.LogError($"[CheonMyeong] '{Ghost3PrefabPath}'가 없습니다 - 'Create Enemy Variants (Ghost 2-3)'를 먼저 실행하세요.");
                return;
            }

            var made = new List<(EliteSpec spec, EnemyStatsSO stats, Enemy prefab)>();
            foreach (EliteSpec spec in EliteSpecs)
            {
                string sheetPath = $"{EliteArtFolder}/{spec.Sheet}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath) == null)
                {
                    Debug.LogError($"[CheonMyeong] '{sheetPath}'가 없습니다 - jiwon22 브랜치(중형 몹 3종 애니메이션)를 머지했는지 확인하세요.");
                    return;
                }
                PrepareEliteSheet(sheetPath);
                EnemyStatsSO stats = EnsureGhostStats(spec.StatsPath);
                ApplyEliteStats(stats, spec);
                Enemy prefab = SetupElitePrefab(spec, stats, LoadSheetRows(sheetPath));
                made.Add((spec, stats, prefab));
            }
            AssetDatabase.SaveAssets();

            RegisterNetworkPrefabs(made);
            AddEliteWaves(made);

            int sceneCount = ForEachSceneContaining(SpawnerObjectName, (scene, spawnerGo) =>
            {
                var spawner = spawnerGo.GetComponent<EnemySpawner>();
                if (spawner == null)
                {
                    return;
                }
                var so = new SerializedObject(spawner);
                SerializedProperty poolsProp = so.FindProperty("pools");
                var pools = new List<Object>();
                for (int i = 0; i < poolsProp.arraySize; i++)
                {
                    Object existing = poolsProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (existing != null)
                    {
                        pools.Add(existing);
                    }
                }
                foreach (var m in made)
                {
                    EnemyPool pool = EnsureEnemyVariantPool(scene, m.spec.PoolName, m.prefab);
                    if (!pools.Contains(pool))
                    {
                        pools.Add(pool);
                    }
                }
                AssignPrivateObjectArrayField(spawner, "pools", pools.ToArray());
                EditorUtility.SetDirty(spawner);
            });

            Debug.Log($"[CheonMyeong] 중형 원귀 3종(아귀 · 물귀신 · 역귀) 스탯 · 프리팹 · 풀 · 웨이브 등장 비율 완료 (씬 {sceneCount}개). " +
                      "6~7웨이브 5% · 8~10웨이브 10%, 중형 안에서 40 : 30 : 30.");
        }

        /// <summary>원귀 시트와 같은 임포트 설정(PPU 100 · 점 필터 · 무압축)으로 맞추고 64px로 자른다.</summary>
        private static void PrepareEliteSheet(string sheetPath)
        {
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
                if (importer.spriteImportMode != SpriteImportMode.Multiple) { importer.spriteImportMode = SpriteImportMode.Multiple; changed = true; }
                if (!Mathf.Approximately(importer.spritePixelsPerUnit, 100f)) { importer.spritePixelsPerUnit = 100f; changed = true; }
                if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
            if (!IsGridSliced(sheetPath, MonsterFrameSize))
            {
                SliceToGrid(sheetPath, MonsterFrameSize);
            }
        }

        private static void ApplyEliteStats(EnemyStatsSO stats, EliteSpec spec)
        {
            stats.enemyId = spec.Id;
            stats.displayName = spec.Name;
            stats.maxHealth = spec.Hp;
            stats.moveSpeed = spec.Speed;
            stats.contactDamage = spec.Damage;
            stats.contactDamageInterval = spec.ContactInterval;
            stats.expReward = spec.Soul;
            stats.goldDropChance = spec.CoinRate;
            stats.goldReward = spec.CoinAmount;
            stats.grudgeWeight = 1f;
            stats.skill = spec.Skill;
            // 기획서 v1.1 확정값 + 밸런스 메모 제안값(26.11.19 예섭: 제안값 적용)
            stats.chargeDetectRange = 3f;
            stats.chargeDistance = 3f;
            stats.chargeWindup = 0.8f;
            stats.chargeDamage = 18f;
            stats.chargeKnockback = 1f;
            stats.chargeWallStun = 1f;
            stats.chargeCooldown = 30f;
            stats.chargeFirstCooldown = 0f;  // 제안: 등장 직후 첫 돌진 바로 가능
            stats.surfaceRange = 2.5f;
            stats.submergeRange = 4f;        // 제안: 들어가는 거리 4칸
            stats.maxAlive = spec.Skill == EliteSkill.Submerge ? 6 : 0; // 제안: 물귀신 동시 최대 6
            stats.throwRange = 6f;
            stats.throwCooldown = 5f;
            stats.throwWindup = 0.8f;
            stats.throwSpeed = 360f / 60f;   // 제안 360(새 단위)
            stats.poisonDamage = 1f;
            stats.poisonTicks = 5;
            EditorUtility.SetDirty(stats);
        }

        private static Enemy SetupElitePrefab(EliteSpec spec, EnemyStatsSO stats, List<Sprite[]> rows)
        {
            if (AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath) == null)
            {
                AssetDatabase.CopyAsset(Ghost3PrefabPath, spec.PrefabPath);
                AssetDatabase.ImportAsset(spec.PrefabPath);
            }

            Sprite[] Row(string name)
            {
                int index = System.Array.IndexOf(spec.Rows, name);
                return index >= 0 && index < rows.Count ? rows[index] : new Sprite[0];
            }

            Sprite[] die = Row("DIE");
            if (die.Length > 0)
            {
                stats.deathStaggerDuration = die.Length * 0.1f;
                EditorUtility.SetDirty(stats);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            contents.name = System.IO.Path.GetFileNameWithoutExtension(spec.PrefabPath);
            var animator = contents.GetComponent<Animator>();
            if (animator != null)
            {
                Object.DestroyImmediate(animator, true); // 그림은 EliteEnemy가 줄별로 직접 넘긴다
            }
            var sr = contents.GetComponent<SpriteRenderer>();
            Sprite[] first = spec.Skill == EliteSkill.Submerge ? Row("SWIM") : Row("WALK");
            if (sr != null && first.Length > 0)
            {
                sr.sprite = first[0];
                sr.color = Color.white;
            }
            var enemy = contents.GetComponent<Enemy>();
            AssignPrivateField(enemy, "stats", stats);
            AssignPrivateField(enemy, "spriteRenderer", sr);
            AssignPrivateField(enemy, "animator", null);

            var elite = contents.GetComponent<EliteEnemy>();
            if (elite == null)
            {
                elite = contents.AddComponent<EliteEnemy>();
            }
            AssignPrivateField(elite, "spriteRenderer", sr);
            AssignPrivateObjectArrayField(elite, "walkFrames", Row("WALK"));
            AssignPrivateObjectArrayField(elite, "attackFrames", Row("ATK"));
            AssignPrivateObjectArrayField(elite, "dieFrames", die);
            AssignPrivateObjectArrayField(elite, "swimFrames", Row("SWIM"));
            AssignPrivateObjectArrayField(elite, "riseFrames", Row("RISE"));
            Sprite[] jar = Row("HORIBYEONG");
            AssignPrivateField(elite, "jarSprite", jar.Length > 0 ? jar[0] : null);

            PrefabUtility.SaveAsPrefabAsset(contents, spec.PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            return AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath);
        }

        private static void RegisterNetworkPrefabs(List<(EliteSpec spec, EnemyStatsSO stats, Enemy prefab)> made)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (list == null)
            {
                Debug.LogWarning("[CheonMyeong] Assets/DefaultNetworkPrefabs.asset을 못 찾음 - NetworkManager의 Network Prefabs에 중형 원귀 프리팹 3개를 직접 넣어주세요.");
                return;
            }
            foreach (var m in made)
            {
                if (m.prefab != null && !list.Contains(m.prefab.gameObject))
                {
                    list.Add(new NetworkPrefab { Prefab = m.prefab.gameObject });
                }
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 기획서 v1.1 8장 "나오는 비율": 6~7웨이브 5%, 8~10웨이브 10%. 웨이브별 총 마릿수는 그대로, 몽달귀 몫에서 뺀다.
        /// 가중치가 정수라 기존 비율을 ×20 한 뒤 나눈다(예: 60/30/10 → 1100/600/200 + 아귀 40 · 물귀신 30 · 역귀 30).
        /// 이미 중형이 들어간 웨이브는 건드리지 않는다.
        /// </summary>
        private static void AddEliteWaves(List<(EliteSpec spec, EnemyStatsSO stats, Enemy prefab)> made)
        {
            var config = AssetDatabase.LoadAssetAtPath<WaveConfigSO>(DefaultWaveConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{DefaultWaveConfigPath}'가 없어 웨이브 등장 비율은 넣지 못함.");
                return;
            }
            EnemyStatsSO mongdal = AssetDatabase.LoadAssetAtPath<EnemyStatsSO>(EnemyStatsPathForMongdal());
            for (int w = 0; w < config.waves.Count; w++)
            {
                int waveNumber = w + 1;
                float ratio = waveNumber >= 8 ? 0.10f : waveNumber >= 6 ? 0.05f : 0f;
                WaveDefinition wave = config.waves[w];
                if (ratio <= 0f || wave == null || wave.spawns == null || wave.spawns.Count == 0)
                {
                    continue;
                }
                bool hasElite = false;
                foreach (WaveSpawnEntry e in wave.spawns)
                {
                    hasElite |= e != null && e.enemy != null && e.enemy.skill != EliteSkill.None;
                }
                if (hasElite)
                {
                    continue;
                }
                const int scale = 20;
                int total = 0;
                WaveSpawnEntry mongdalEntry = null;
                foreach (WaveSpawnEntry e in wave.spawns)
                {
                    if (e == null)
                    {
                        continue;
                    }
                    e.weight *= scale;
                    total += e.weight;
                    if (e.enemy == mongdal)
                    {
                        mongdalEntry = e;
                    }
                }
                int eliteTotal = Mathf.RoundToInt(total * ratio);
                if (mongdalEntry == null || mongdalEntry.weight <= eliteTotal)
                {
                    Debug.LogWarning($"[CheonMyeong] {waveNumber}웨이브에 몽달귀 항목이 없어 중형 비율을 넣지 못함.");
                    continue;
                }
                mongdalEntry.weight -= eliteTotal;
                foreach (var m in made)
                {
                    wave.spawns.Add(new WaveSpawnEntry { enemy = m.stats, weight = Mathf.RoundToInt(eliteTotal * m.spec.ShareInElites / 100f) });
                }
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private static string EnemyStatsPathForMongdal()
        {
            EnemyStatsSO basic = EnsureBasicEnemyStats();
            return AssetDatabase.GetAssetPath(basic);
        }
    }
}
