using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Input;
using CheonMyeong.Player;
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
                      "Play 누르면 1초마다 하영 주변 반경 10유닛 밖에서 적이 스폰되어 추적합니다. " +
                      "주의: 아직 플레이어 쪽에 IDamageable(체력)이 없어서 적 접촉 피해는 무시됩니다 - " +
                      "체력/UI 시스템은 별도 단계에서 다룹니다. " +
                      "스폰 방식(웨이브/지속)은 미정이라 지금은 지속 스폰 임시 구현입니다.");
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
            var existing = AssetDatabase.LoadAssetAtPath<Enemy>(EnemyPrefabPath);
            if (existing != null)
            {
                MigrateColliderToTrigger();
                return AssetDatabase.LoadAssetAtPath<Enemy>(EnemyPrefabPath);
            }

            EnsureFolder(PrefabsFolder, "Assets");

            // 임시 비주얼 - 스프라이트 미정이라 기본 스프라이트 + 빨간색으로만 표시.
            var temp = new GameObject("Enemy_Basic_Temp");
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
        /// 이전에 솔리드 콜라이더로 만들어진 Enemy_Basic 프리팹이 이미 있다면
        /// isTrigger = true로 고쳐서 다시 저장한다. (적끼리 뭉쳐서 멈추는 문제 수정)
        /// </summary>
        private static void MigrateColliderToTrigger()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            var collider = contents.GetComponent<CircleCollider2D>();
            if (collider != null && !collider.isTrigger)
            {
                collider.isTrigger = true;
                PrefabUtility.SaveAsPrefabAsset(contents, EnemyPrefabPath);
                Debug.Log("[CheonMyeong] 기존 Enemy_Basic 프리팹의 콜라이더를 Trigger로 수정했습니다.");
            }
            PrefabUtility.UnloadPrefabContents(contents);
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
    }
}
