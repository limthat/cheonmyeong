using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Input;
using CheonMyeong.Networking;
using CheonMyeong.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 에디터 전용 셋업 스크립트. 하영/적 프로토타입에 필요한 에셋 생성 +
    /// 씬 오브젝트 구성 + 컴포넌트 참조 연결을 메뉴 클릭 한 번으로 처리한다.
    /// 수동으로 Inspector에서 드래그&드롭 하는 반복 작업을 줄이기 위한 용도이며,
    /// 플레이어 빌드에는 포함되지 않는다(Editor 폴더 하위).
    /// </summary>
    public static class CheonMyeongPrototypeSetup
    {
        private const string DataFolder = "Assets/Data";
        private const string CharactersFolder = DataFolder + "/Characters";
        private const string EnemiesFolder = DataFolder + "/Enemies";
        private const string PrefabsFolder = "Assets/Prefabs";

        private const string HayoungStatsPath = CharactersFolder + "/Hayoung.asset";
        private const string BasicEnemyStatsPath = EnemiesFolder + "/BasicEnemy.asset";
        private const string EnemyPrefabPath = PrefabsFolder + "/Enemy_Basic.prefab";

        private const string PrototypeObjectName = "Hayoung_Prototype";
        private const string PoolObjectName = "EnemyPool";
        private const string SpawnerObjectName = "EnemySpawner";
        private const string NetworkBootstrapObjectName = "NetworkBootstrap";

        /// <summary>
        /// 적 전용 레이어 이름. EnemyPool이 런타임에 이 레이어끼리의 충돌/트리거
        /// 판정 자체를 꺼서(Physics2D.IgnoreLayerCollision) 수백 마리가 겹쳤을 때
        /// 벌어지는 적-적 OnTriggerStay2D 폭증(조합 폭발)을 막는다. 이름은
        /// EnemyPool.EnemyLayerName과 반드시 같아야 한다.
        /// </summary>
        private const string EnemyLayerName = "Enemy";

        [MenuItem("CheonMyeong/Setup/Create Hayoung Prototype Object")]
        public static void CreateHayoungPrototype()
        {
            CharacterStatsSO stats = EnsureHayoungStats();
            GameObject go = EnsurePrototypeObject();

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            // 적의 트리거 콜라이더가 하영과의 접촉을 감지하려면 하영 쪽에도
            // 콜라이더가 하나는 있어야 한다. 모양/크기는 미정 - 임시로 원.
            var playerCollider = go.GetComponent<CircleCollider2D>();
            if (playerCollider == null) playerCollider = go.AddComponent<CircleCollider2D>();
            playerCollider.radius = 0.4f;

            var inputReader = go.GetComponent<PlayerInputReader>();
            if (inputReader == null) inputReader = go.AddComponent<PlayerInputReader>();

            var movement = go.GetComponent<PlayerMovement>();
            if (movement == null) movement = go.AddComponent<PlayerMovement>();

            var attack = go.GetComponent<PlayerAutoAttack>();
            if (attack == null) attack = go.AddComponent<PlayerAutoAttack>();

            var health = go.GetComponent<PlayerHealth>();
            if (health == null) health = go.AddComponent<PlayerHealth>();

            AssignPrivateField(movement, "inputReader", inputReader);
            AssignPrivateField(movement, "stats", stats);
            AssignPrivateField(attack, "inputReader", inputReader);
            AssignPrivateField(attack, "stats", stats);
            AssignPrivateField(health, "stats", stats);

            EditorUtility.SetDirty(movement);
            EditorUtility.SetDirty(attack);
            EditorUtility.SetDirty(health);
            EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            Debug.Log($"[CheonMyeong] '{PrototypeObjectName}' 생성/연결 완료 — 하영 스탯: {HayoungStatsPath}. " +
                      "Console에 에러 없는지 확인 후 Play 눌러서 WASD 이동, 마우스 방향 자동 공격(허공 판정) 테스트하세요. " +
                      "이제 체력(PlayerHealth)도 붙어있어서 적한테 닿으면 피 깎입니다(0 되면 이동/공격 멈춤 - 사망 연출은 미정).");
        }

        [MenuItem("CheonMyeong/Setup/Create Enemy Prototype (Pool + Spawner)")]
        public static void CreateEnemyPrototype()
        {
            GameObject player = GameObject.Find(PrototypeObjectName);
            if (player == null)
            {
                Debug.LogError($"[CheonMyeong] '{PrototypeObjectName}'를 찾을 수 없습니다. " +
                                "먼저 'CheonMyeong/Setup/Create Hayoung Prototype Object'를 실행하세요.");
                return;
            }

            EnemyStatsSO enemyStats = EnsureBasicEnemyStats();
            Enemy enemyPrefab = EnsureEnemyPrefab(enemyStats);

            GameObject poolGo = EnsureSceneObject(PoolObjectName);
            var pool = poolGo.GetComponent<EnemyPool>();
            if (pool == null) pool = poolGo.AddComponent<EnemyPool>();
            AssignPrivateField(pool, "enemyPrefab", enemyPrefab);

            GameObject spawnerGo = EnsureSceneObject(SpawnerObjectName);
            var spawner = spawnerGo.GetComponent<EnemySpawner>();
            if (spawner == null) spawner = spawnerGo.AddComponent<EnemySpawner>();
            AssignPrivateField(spawner, "pool", pool);
            AssignPrivateField(spawner, "player", player.transform);

            EditorUtility.SetDirty(pool);
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(poolGo.scene);

            Selection.activeGameObject = spawnerGo;
            Debug.Log("[CheonMyeong] 적 프로토타입(풀+스포너) 생성/연결 완료. " +
                      "'Enemy' 레이어도 자동 생성됐고, Play 시작하면 EnemyPool이 적-적 레이어 " +
                      "충돌/트리거 판정을 꺼서 수백 마리가 겹쳐도 판정 폭증이 안 생기게 처리합니다. " +
                      "스폰 방식(웨이브/지속)은 미정이라 지금은 지속 스폰 임시 구현입니다.");
        }

        [MenuItem("CheonMyeong/Setup/Create Network Bootstrap")]
        public static void CreateNetworkBootstrap()
        {
            GameObject go = EnsureSceneObject(NetworkBootstrapObjectName);

            var networkManager = go.GetComponent<NetworkManager>();
            if (networkManager == null) networkManager = go.AddComponent<NetworkManager>();

            var transport = go.GetComponent<UnityTransport>();
            if (transport == null) transport = go.AddComponent<UnityTransport>();

            // NGO 버전에 따라 필드명이 다를 수 있어서 못 찾으면 에러 로그로
            // 알려주고 수동 연결을 안내한다 (임의 추측 금지 원칙).
            AssignNestedField(networkManager, "NetworkConfig", "NetworkTransport", transport);

            var services = go.GetComponent<GameServicesBootstrap>();
            if (services == null) services = go.AddComponent<GameServicesBootstrap>();

            var sessionManager = go.GetComponent<SessionManager>();
            if (sessionManager == null) sessionManager = go.AddComponent<SessionManager>();

            EditorUtility.SetDirty(networkManager);
            EditorUtility.SetDirty(sessionManager);
            EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            Debug.Log($"[CheonMyeong] '{NetworkBootstrapObjectName}' 생성/연결 완료. " +
                      "Inspector에서 NetworkManager의 Network Transport 칸에 UnityTransport가 " +
                      "실제로 연결됐는지 꼭 확인하세요(에디터 스크립트로 못 찾았으면 위에 에러 로그가 떴을 겁니다). " +
                      "Play 누르면 화면 좌상단에 임시 '호스트 시작'/'코드로 참가' 버튼이 뜹니다 - " +
                      "Unity Cloud 프로젝트 연결 + 대시보드에서 Relay/Lobby 서비스 활성화가 먼저 돼 있어야 동작합니다.");
        }

        private static CharacterStatsSO EnsureHayoungStats()
        {
            EnsureFolder(DataFolder, "Assets");
            EnsureFolder(CharactersFolder, DataFolder);

            var stats = AssetDatabase.LoadAssetAtPath<CharacterStatsSO>(HayoungStatsPath);
            if (stats != null)
            {
                return stats;
            }

            stats = ScriptableObject.CreateInstance<CharacterStatsSO>();
            stats.characterId = "hayoung";
            stats.displayName = "하영";
            stats.maxHealth = 100f;
            stats.moveSpeed = 5f;
            stats.attackPower = 10f;
            stats.attackInterval = 1f;
            stats.attackRange = 2f;

            AssetDatabase.CreateAsset(stats, HayoungStatsPath);
            AssetDatabase.SaveAssets();
            return stats;
        }

        private static EnemyStatsSO EnsureBasicEnemyStats()
        {
            EnsureFolder(DataFolder, "Assets");
            EnsureFolder(EnemiesFolder, DataFolder);

            var stats = AssetDatabase.LoadAssetAtPath<EnemyStatsSO>(BasicEnemyStatsPath);
            if (stats != null)
            {
                return stats;
            }

            stats = ScriptableObject.CreateInstance<EnemyStatsSO>();
            stats.enemyId = "basic";
            stats.displayName = "기본 적(이름 미정)";
            stats.maxHealth = 20f;
            stats.moveSpeed = 2f;
            stats.contactDamage = 5f;
            stats.contactDamageInterval = 1f;

            AssetDatabase.CreateAsset(stats, BasicEnemyStatsPath);
            AssetDatabase.SaveAssets();
            return stats;
        }

        private static Enemy EnsureEnemyPrefab(EnemyStatsSO stats)
        {
            EnsureLayer(EnemyLayerName);
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);

            var existing = AssetDatabase.LoadAssetAtPath<Enemy>(EnemyPrefabPath);
            if (existing != null)
            {
                MigrateEnemyPrefab(enemyLayer);
                return AssetDatabase.LoadAssetAtPath<Enemy>(EnemyPrefabPath);
            }

            EnsureFolder(PrefabsFolder, "Assets");

            // 임시 비주얼 - 스프라이트 미정이라 기본 스프라이트 + 빨간색으로만 표시.
            var temp = new GameObject("Enemy_Basic_Temp");
            temp.layer = enemyLayer;

            var sr = temp.AddComponent<SpriteRenderer>();
            sr.color = Color.red;

            var collider = temp.AddComponent<CircleCollider2D>();
            collider.radius = 0.4f;
            // 트리거로 둔다 - 적끼리/적-플레이어가 물리적으로 밀어내면 수백 마리가
            // 뭉쳤을 때 서로 막혀서 멈추거나 물리 비용이 급격히 는다. 피해는
            // OnTriggerStay2D에서 처리한다.
            collider.isTrigger = true;

            var rb = temp.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var enemyComponent = temp.AddComponent<Enemy>();
            AssignPrivateField(enemyComponent, "stats", stats);

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, EnemyPrefabPath);
            Object.DestroyImmediate(temp);

            return prefabAsset.GetComponent<Enemy>();
        }

        /// <summary>
        /// 이전 버전으로 만들어진 Enemy_Basic 프리팹을 고쳐서 다시 저장한다:
        /// 1) 콜라이더 isTrigger = true (적끼리 뭉쳐서 멈추는 문제 수정)
        /// 2) 레이어를 "Enemy"로 변경 (EnemyPool이 끄는 적-적 충돌 판정 대상이 되려면 필요)
        /// </summary>
        private static void MigrateEnemyPrefab(int enemyLayer)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            bool changed = false;

            var collider = contents.GetComponent<CircleCollider2D>();
            if (collider != null && !collider.isTrigger)
            {
                collider.isTrigger = true;
                changed = true;
            }

            if (contents.layer != enemyLayer)
            {
                contents.layer = enemyLayer;
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, EnemyPrefabPath);
                Debug.Log("[CheonMyeong] 기존 Enemy_Basic 프리팹을 Trigger + 'Enemy' 레이어로 수정했습니다.");
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>
        /// 프로젝트에 해당 이름의 레이어가 없으면 비어있는 User Layer 슬롯(8~31)에
        /// 추가한다. Edit/Project Settings/Tags and Layers를 수동으로 누르는 대신
        /// 코드로 처리해서 git diff(ProjectSettings/TagManager.asset)로 추적되게 한다.
        /// </summary>
        private static void EnsureLayer(string layerName)
        {
            if (LayerMask.NameToLayer(layerName) != -1)
            {
                return;
            }

            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets.Length == 0)
            {
                Debug.LogError("[CheonMyeong] ProjectSettings/TagManager.asset을 찾지 못했습니다.");
                return;
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            var layersProp = tagManager.FindProperty("layers");

            for (int i = 8; i < layersProp.arraySize; i++) // 0~7은 유니티 내장 레이어라 건드리지 않음
            {
                SerializedProperty layerSlot = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layerSlot.stringValue))
                {
                    layerSlot.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[CheonMyeong] '{layerName}' 레이어를 슬롯 {i}에 생성했습니다.");
                    return;
                }
            }

            Debug.LogError($"[CheonMyeong] 빈 User Layer 슬롯이 없어서 '{layerName}' 레이어를 만들지 못했습니다. " +
                            "Edit > Project Settings > Tags and Layers에서 직접 확인해주세요.");
        }

        private static GameObject EnsurePrototypeObject()
        {
            return EnsureSceneObject(PrototypeObjectName);
        }

        private static GameObject EnsureSceneObject(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                return existing;
            }
            return new GameObject(name);
        }

        private static void EnsureFolder(string path, string parent)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string folderName = path.Substring(parent.Length + 1);
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static void AssignPrivateField(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[CheonMyeong] {target.GetType().Name}에서 필드 '{fieldName}'를 찾지 못했습니다. " +
                                "스크립트 필드명이 바뀌었다면 이 에디터 스크립트도 같이 고쳐야 합니다.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// "NetworkConfig.NetworkTransport"처럼 중첩된(직렬화된) 필드에 값을
        /// 할당한다. NGO 버전마다 내부 구조가 다를 수 있어서 못 찾으면 추측하지
        /// 않고 에러를 남긴 뒤 수동 연결을 안내한다.
        /// </summary>
        private static void AssignNestedField(Object target, string topField, string nestedField, Object value)
        {
            var so = new SerializedObject(target);
            var topProp = so.FindProperty(topField);
            if (topProp == null)
            {
                Debug.LogError($"[CheonMyeong] {target.GetType().Name}에서 필드 '{topField}'를 찾지 못했습니다. " +
                                "설치된 Netcode for GameObjects 버전이 달라서일 수 있습니다 - " +
                                "Inspector에서 Network Transport를 직접 연결해주세요.");
                return;
            }

            var nestedProp = topProp.FindPropertyRelative(nestedField);
            if (nestedProp == null)
            {
                Debug.LogError($"[CheonMyeong] {target.GetType().Name}.{topField}에서 필드 '{nestedField}'를 찾지 못했습니다. " +
                                "Inspector에서 Network Transport를 직접 연결해주세요.");
                return;
            }

            nestedProp.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
