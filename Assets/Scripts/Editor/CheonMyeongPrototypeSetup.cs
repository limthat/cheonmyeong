using System.Collections.Generic;
using System.IO;
using CheonMyeong.Data;
using CheonMyeong.Enemies;
using CheonMyeong.Input;
using CheonMyeong.Networking;
using CheonMyeong.Player;
using CheonMyeong.Progression;
using CheonMyeong.UI.Title;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private const string PlayerPrefabPath = PrefabsFolder + "/Hayoung_Player.prefab";
        private const string GoldLootPrefabPath = PrefabsFolder + "/Loot_Gold.prefab";
        private const string ExpLootPrefabPath = PrefabsFolder + "/Loot_Exp.prefab";

        private const string PrototypeObjectName = "Hayoung_Prototype";
        private const string PoolObjectName = "EnemyPool";
        private const string SpawnerObjectName = "EnemySpawner";
        private const string ProgressionRootObjectName = "ProgressionSystems";
        private const string NetworkBootstrapObjectName = "NetworkBootstrap";

        /// <summary>
        /// 적 전용 레이어 이름. EnemyPool이 런타임에 이 레이어끼리의 충돌/트리거
        /// 판정 자체를 꺼서(Physics2D.IgnoreLayerCollision) 수백 마리가 겹쳤을 때
        /// 벌어지는 적-적 OnTriggerStay2D 폭증(조합 폭발)을 막는다. 이름은
        /// EnemyPool.EnemyLayerName과 반드시 같아야 한다.
        /// </summary>
        private const string EnemyLayerName = "Enemy";

        /// <summary>
        /// 타이틀 화면 부적 placeholder 관련 상수. 픽셀 크기는 지금은 임의로 정한 값 -
        /// 최종 아트가 이 크기(세로로 긴 부적 비율, 512x768)로 맞춰져서 교체될 거라는
        /// 전제이며, 바뀌면 이 두 값만 고치고 다시 실행하면 전부 재생성된다(단, 이미
        /// 존재하는 PNG는 작업물 보호를 위해 덮어쓰지 않으니 교체하려면 먼저 지워야 함).
        /// </summary>
        private const string TitlePlaceholderFolder = "Assets/Art/Title/Placeholder";
        private const string TitleTalismanParentName = "TitleTalismans";
        private const int TitlePieceWidth = 512;
        private const int TitlePieceHeight = 768;
        private const float TitlePixelsPerUnit = 100f;
        private const float TitleTalismanScale = 0.25f; // 261013 - "지금의 25% 크기로" 조정

        /// <summary>
        /// 하영 애니메이션 관련 상수. 프레임 구간은 Project 창 썸네일로 직접 대조해서
        /// 확정한 값(시트에 섞인 "IDLE/walk/ATK" 손글씨 라벨이 알파 기준으로 같이
        /// 슬라이스된 조각들 - Hayoung-Sheet_7/_17/_29 - 은 제외함). 30~39번은
        /// 2026-10-12 기준 용도 미확인이라 어떤 클립에도 안 넣었다.
        /// </summary>
        private const string HayoungSpriteSheetPath = "Assets/img/ch/Hayoung-Sheet.png";
        private const string HayoungSpriteBaseName = "Hayoung-Sheet";
        private const string HayoungAnimFolder = "Assets/Animations/Hayoung";
        private const string HayoungIdleClipPath = HayoungAnimFolder + "/Hayoung_Idle.anim";
        private const string HayoungWalkClipPath = HayoungAnimFolder + "/Hayoung_Walk.anim";
        private const string HayoungAttackClipPath = HayoungAnimFolder + "/Hayoung_Attack.anim";
        private const string HayoungAnimatorControllerPath = HayoungAnimFolder + "/Hayoung_Animator.controller";
        private const int IdleFrameStart = 0, IdleFrameEnd = 6;
        private const int WalkFrameStart = 8, WalkFrameEnd = 16;
        private const int AttackFrameStart = 18, AttackFrameEnd = 28;

        /// <summary>
        /// 이동속도 임시 너프. 이미 만들어진 스탯 에셋(Hayoung.asset, BasicEnemy.asset)의
        /// 값을 직접 덮어쓴다 - EnsureHayoungStats/EnsureBasicEnemyStats는 에셋이 이미
        /// 있으면 그대로 반환하기만 해서(최초 생성 시 기본값만 넣음) 이 메서드로 따로
        /// 수치를 조정한다. 정확한 값은 미정 - 밸런스 확정 전까지 체감상 너프된 임시
        /// 수치이며, 두 ScriptableObject 모두 이후 Inspector에서 바로 조정 가능하다.
        /// 적(1.2)보다 하영(3.2)이 확실히 빠르게 유지.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Tune Movement Speeds (Temp Nerf)")]
        public static void TuneMovementSpeeds()
        {
            var hayoungStats = AssetDatabase.LoadAssetAtPath<CharacterStatsSO>(HayoungStatsPath);
            var enemyStats = AssetDatabase.LoadAssetAtPath<EnemyStatsSO>(BasicEnemyStatsPath);

            if (hayoungStats == null || enemyStats == null)
            {
                Debug.LogError("[CheonMyeong] 스탯 에셋을 찾을 수 없습니다. " +
                                "먼저 'Create Hayoung Prototype Object'와 'Create Enemy Prototype'을 실행하세요.");
                return;
            }

            hayoungStats.moveSpeed = 3.2f;
            enemyStats.moveSpeed = 1.2f;

            EditorUtility.SetDirty(hayoungStats);
            EditorUtility.SetDirty(enemyStats);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CheonMyeong] 이동속도 너프 적용 완료 — 하영: {hayoungStats.moveSpeed}, " +
                      $"적: {enemyStats.moveSpeed} (값은 미정, 밸런스 확정 전 임시 수치). " +
                      "Play 중이던 세션이 있다면 재시작해야 반영됩니다(스탯은 스폰/초기화 시점에 읽으므로).");
        }

        /// <summary>
        /// 기획 지시 변경 이력: "사거리 롤백 + 사거리보다 10% 더 큰 원 안에 적이
        /// 들어오면 자동공격" → 이후 "그럼 사거리를 늘리는 셈이잖아. 몬스터 인지거리를
        /// 사거리랑 동일하게 바꾸고 사거리 롤백" - 실제 타격 판정이 attackRange가 아니라
        /// attackRange * attackAwarenessRangeMultiplier를 쓰도록 고치면서(PlayerAutoAttack.
        /// Attack 참고 - 공격 시작 판정과 타격 판정 반경을 통일해 "스윙은 나갔는데 안 맞는"
        /// 버그를 고침), 배수가 1보다 크면 attackRange 롤백이 무의미해지므로 1배(=
        /// attackRange와 완전히 동일)로 맞춘다.
        ///
        /// 기본값은 하드코딩해서 중복시키지 않고, CharacterStatsSO의 실제 필드
        /// 기본값(인스턴스를 하나 임시로 만들어서 읽음)을 그대로 가져와 사용한다 -
        /// 이러면 CharacterStatsSO.cs의 기본값이 나중에 바뀌어도 이 메서드가 자동으로
        /// 따라간다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Rollback Attack Range + Set Awareness Equal To Range")]
        public static void RollbackHayoungAttackRangeAndSetAwareness()
        {
            var hayoungStats = AssetDatabase.LoadAssetAtPath<CharacterStatsSO>(HayoungStatsPath);

            if (hayoungStats == null)
            {
                Debug.LogError("[CheonMyeong] 하영 스탯 에셋을 찾을 수 없습니다. " +
                                "먼저 'Create Hayoung Prototype Object'를 실행하세요.");
                return;
            }

            var defaults = ScriptableObject.CreateInstance<CharacterStatsSO>();
            float defaultAttackRange = defaults.attackRange;
            float defaultAwarenessMultiplier = defaults.attackAwarenessRangeMultiplier;
            Object.DestroyImmediate(defaults);

            float previousRange = hayoungStats.attackRange;
            float previousMultiplier = hayoungStats.attackAwarenessRangeMultiplier;

            hayoungStats.attackRange = defaultAttackRange;
            hayoungStats.attackAwarenessRangeMultiplier = defaultAwarenessMultiplier;

            EditorUtility.SetDirty(hayoungStats);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CheonMyeong] 하영 공격 사거리 롤백: {previousRange} → {hayoungStats.attackRange}. " +
                      $"몬스터 인지거리 배수: {previousMultiplier} → {hayoungStats.attackAwarenessRangeMultiplier} " +
                      "(= attackRange와 동일한 반경, 값은 미정 - 밸런스 확정 전 임시 수치). " +
                      "Play 중이던 세션이 있다면 재시작해야 반영됩니다(스탯은 스폰/초기화 시점에 읽으므로).");
        }

        /// <summary>
        /// 기획 지시: "공속도 지금의 70%로" - attacksPerSecond(초당 공격 횟수, 클수록
        /// 빠름)는 공식 스탯 시트와 같은 방향이라, 속도를 70%로 낮추려면 값 자체에
        /// 0.7을 곱하면 된다(이전 attackInterval 방식과 반대 방향이던 걸 필드 교체로
        /// 바로잡음 - 간격이 아니라 "횟수"라 작아질수록 느려짐). 역시 코드 상수가
        /// 아니라 CharacterStatsSO 에셋 값 자체를 수정.
        /// </summary>
        private const float HayoungAttackSpeedScale = 0.7f;

        [MenuItem("CheonMyeong/Setup/Tune Hayoung Attack Speed (Temp Nerf)")]
        public static void TuneHayoungAttackSpeed()
        {
            var hayoungStats = AssetDatabase.LoadAssetAtPath<CharacterStatsSO>(HayoungStatsPath);

            if (hayoungStats == null)
            {
                Debug.LogError("[CheonMyeong] 하영 스탯 에셋을 찾을 수 없습니다. " +
                                "먼저 'Create Hayoung Prototype Object'를 실행하세요.");
                return;
            }

            float previousValue = hayoungStats.attacksPerSecond;
            hayoungStats.attacksPerSecond = previousValue * HayoungAttackSpeedScale;

            EditorUtility.SetDirty(hayoungStats);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CheonMyeong] 하영 공속 {HayoungAttackSpeedScale * 100f}%로 축소: " +
                      $"attacksPerSecond {previousValue} → {hayoungStats.attacksPerSecond} " +
                      "(값은 미정, 밸런스 확정 전 임시 수치 - 공식 스탯 시트 기준값 위에 추가로 " +
                      "적용되는 임시 너프이니 중복 적용 여부를 꼭 확인하세요). Play 중이던 세션이 " +
                      "있다면 재시작해야 반영됩니다(스탯은 스폰/초기화 시점에 읽으므로).");
        }

        /// <summary>
        /// 기획 확정(2026-10-13): 공식 스탯 시트(스탯 천명_캐릭터_261012.xlsx, Sheet1)의
        /// 하영 기준 스탯 행 수치로 CharacterStatsSO(Hayoung.asset)를 맞춘다.
        /// 시트 수치는 maxHealth=150, moveSpeed=5, attackPower=20, attacksPerSecond=0.8,
        /// attackRange=1 - 이 메서드의 상수들이 시트 원본 값이다(다른 튜닝 메뉴처럼
        /// "느낌 보고 조정하는 임시값"이 아니라 기획서가 확정한 숫자이므로 여기 직접
        /// 적어둠). attackArcDegrees/attackAwarenessRangeMultiplier는 이 시트에 없는
        /// 프로토타입 전용 값이라 건드리지 않는다. 이미 생성된 Hayoung.asset(직렬화된
        /// 값)은 CharacterStatsSO.cs의 필드 기본값만 바꿔선 갱신되지 않으므로, 기존
        /// 에셋에도 반영하려면 이 메뉴를 다시 실행해야 한다 - 여러 번 실행해도 항상
        /// 같은 결과(멱등)이다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Migrate Hayoung Stats To Official Sheet (261012)")]
        public static void MigrateHayoungStatsToOfficialSheet()
        {
            var hayoungStats = AssetDatabase.LoadAssetAtPath<CharacterStatsSO>(HayoungStatsPath);

            if (hayoungStats == null)
            {
                Debug.LogError("[CheonMyeong] 하영 스탯 에셋을 찾을 수 없습니다. " +
                                "먼저 'Create Hayoung Prototype Object'를 실행하세요.");
                return;
            }

            const float officialMaxHealth = 150f;
            const float officialMoveSpeed = 5f;
            const float officialAttackPower = 20f;
            const float officialAttacksPerSecond = 0.8f;
            const float officialAttackRange = 1f;

            string before = $"maxHealth={hayoungStats.maxHealth}, moveSpeed={hayoungStats.moveSpeed}, " +
                             $"attackPower={hayoungStats.attackPower}, attacksPerSecond={hayoungStats.attacksPerSecond}, " +
                             $"attackRange={hayoungStats.attackRange}";

            hayoungStats.maxHealth = officialMaxHealth;
            hayoungStats.moveSpeed = officialMoveSpeed;
            hayoungStats.attackPower = officialAttackPower;
            hayoungStats.attacksPerSecond = officialAttacksPerSecond;
            hayoungStats.attackRange = officialAttackRange;

            EditorUtility.SetDirty(hayoungStats);
            AssetDatabase.SaveAssets();

            Debug.Log("[CheonMyeong] 하영 스탯을 공식 스탯 시트(261012) 값으로 마이그레이션 완료.\n" +
                      $"변경 전: {before}\n" +
                      $"변경 후: maxHealth={hayoungStats.maxHealth}, moveSpeed={hayoungStats.moveSpeed}, " +
                      $"attackPower={hayoungStats.attackPower}, attacksPerSecond={hayoungStats.attacksPerSecond}, " +
                      $"attackRange={hayoungStats.attackRange}. " +
                      "attackArcDegrees/attackAwarenessRangeMultiplier는 시트에 없는 프로토타입 전용 값이라 " +
                      "건드리지 않았습니다. Play 중이던 세션이 있다면 재시작해야 반영됩니다 " +
                      "(스탯은 스폰/초기화 시점에 읽으므로).");
        }

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

            // 버그 수정: enemyLayer(LayerMask)가 이전까지 여기서 전혀 할당되지
            // 않아서 기본값 0(아무 레이어도 선택 안 됨)으로 남아있었다 - 그래서
            // PlayerAutoAttack.Attack()의 OverlapCircle이 항상 0개를 맞혀서
            // 공격이 "나가긴 하는데"(타이머/로그는 돌아도) 아무것도 못 맞히고
            // 있었다. "Enemy" 레이어를 못 찾으면 에러로 알려주고 수동 연결을 안내한다.
            EnsureLayer(EnemyLayerName);
            int enemyLayerIndex = LayerMask.NameToLayer(EnemyLayerName);
            if (enemyLayerIndex == -1)
            {
                Debug.LogError($"[CheonMyeong] '{EnemyLayerName}' 레이어를 찾거나 만들지 못해서 " +
                                "PlayerAutoAttack의 Enemy Layer를 자동 연결하지 못했습니다. " +
                                "Inspector에서 직접 'Enemy' 레이어를 선택해주세요.");
            }
            else
            {
                AssignLayerMaskField(attack, "enemyLayer", 1 << enemyLayerIndex);
            }

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
            // 적은 이제 네트워크 프리팹이라 스폰 시점에 고정 타겟을 받지 않고
            // 서버에서 매 프레임 "가장 가까운 살아있는 플레이어"를 직접 찾는다
            // (PlayerHealth.FindNearestAlivePlayer) - 그래서 더 이상 씬에 하영
            // 오브젝트가 미리 있을 필요가 없다.
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

            EditorUtility.SetDirty(pool);
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(poolGo.scene);

            Selection.activeGameObject = spawnerGo;
            Debug.Log("[CheonMyeong] 적 프로토타입(풀+스포너) 생성/연결 완료. " +
                      "'Enemy' 레이어도 자동 생성됐고, Play 시작하면 EnemyPool이 적-적 레이어 " +
                      "충돌/트리거 판정을 꺼서 수백 마리가 겹쳐도 판정 폭증이 안 생기게 처리합니다. " +
                      "스폰은 서버(호스트)에서만 실행되고 접속해 있는 플레이어 중 무작위로 " +
                      "주변에 스폰합니다. 스폰 방식(웨이브/지속)과 타겟팅 전략은 미정이라 " +
                      "지금은 가장 단순한 지속 스폰 + 최근접 추적 임시 구현입니다.");
        }

        /// <summary>
        /// 경험치/재화 드랍 - 자동줍기 - 레벨업 루프(261013, 구현 우선순위 3번 - "다음 작업").
        /// 골드/경험치 드랍 각각 전용 풀(EnemyPool과 같은 네트워크 풀링 패턴) + 파티 공유
        /// 레벨/경험치(PartyProgress, in-scene 배치 NetworkObject)를 한 번에 만들고 연결한다.
        /// 재실행해도 안전(멱등) - 이미 있는 프리팹/오브젝트는 그대로 재사용한다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Create Progression Prototype (Loot + Party Progress)")]
        public static void CreateProgressionPrototype()
        {
            GoldLootPickup goldPrefab = EnsureLootPrefab<GoldLootPickup>(GoldLootPrefabPath, "Loot_Gold_Temp",
                new Color(0.95f, 0.75f, 0.15f)); // 엽전 - 금빛/주황(wireframe의 "주황 점" 참고)
            ExpLootPickup expPrefab = EnsureLootPrefab<ExpLootPickup>(ExpLootPrefabPath, "Loot_Exp_Temp",
                new Color(0.45f, 0.75f, 0.95f)); // 경험치 - 차가운 청록(원혼 느낌, 색은 미정이라 임시)

            GameObject rootGo = EnsureSceneObject(ProgressionRootObjectName);
            if (rootGo.GetComponent<NetworkObject>() == null) rootGo.AddComponent<NetworkObject>();

            var partyProgress = rootGo.GetComponent<PartyProgress>();
            if (partyProgress == null) partyProgress = rootGo.AddComponent<PartyProgress>();

            var lootSpawner = rootGo.GetComponent<LootSpawner>();
            if (lootSpawner == null) lootSpawner = rootGo.AddComponent<LootSpawner>();

            GameObject goldPoolGo = EnsureChildObject(rootGo.transform, "LootPool_Gold");
            var goldPool = goldPoolGo.GetComponent<LootPool>();
            if (goldPool == null) goldPool = goldPoolGo.AddComponent<LootPool>();
            AssignPrivateField(goldPool, "lootPrefab", goldPrefab);

            GameObject expPoolGo = EnsureChildObject(rootGo.transform, "LootPool_Exp");
            var expPool = expPoolGo.GetComponent<LootPool>();
            if (expPool == null) expPool = expPoolGo.AddComponent<LootPool>();
            AssignPrivateField(expPool, "lootPrefab", expPrefab);

            AssignPrivateField(lootSpawner, "goldPool", goldPool);
            AssignPrivateField(lootSpawner, "expPool", expPool);

            EditorUtility.SetDirty(partyProgress);
            EditorUtility.SetDirty(lootSpawner);
            EditorUtility.SetDirty(goldPool);
            EditorUtility.SetDirty(expPool);
            EditorSceneManager.MarkSceneDirty(rootGo.scene);

            EnsurePlayerProgressOnPlayerPrefab();

            Selection.activeGameObject = rootGo;
            Debug.Log("[CheonMyeong] 경험치/재화 드랍 프로토타입(골드·경험치 풀 + 파티 진행도) 생성/연결 완료. " +
                      "'CheonMyeong/Setup/Split Into Bootstrap + Gameplay Scenes'를 다시 실행하면 " +
                      $"'{ProgressionRootObjectName}'이 EnemyPool/EnemySpawner와 같이 Gameplay 씬으로 이동합니다. " +
                      "적을 처치하면 사망 위치에 골드/경험치 드랍이 생기고, 플레이어 Magnet 반경 " +
                      "(CharacterStatsSO.magnetRadius)에 들어오면 자동으로 끌려와 수거됩니다. " +
                      "경험치는 파티 공유 레벨(PartyProgress)에 쌓이고, 레벨업하면 UPGRADE 화면이 자동으로 열립니다 - " +
                      "단 증강 효과 자체는 여전히 미구현입니다(희귀도/밸런스가 미정이라 보류 중 - 카드를 골라도 " +
                      "아직 아무 효과가 없습니다). 로비 화면의 '누적' 엽전 표시는 세이브/로드 체계가 아직 없어서 " +
                      "이번 작업 범위에 포함하지 않았습니다(확인 필요).");
        }

        private static GameObject EnsureChildObject(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>
        /// 드랍 아이템 프리팹 하나를 만든다(없으면) - Enemy 프리팹(EnsureEnemyPrefab)과 같은
        /// 패턴: SpriteRenderer + 색만 지정한 placeholder 비주얼(스프라이트 자체는 미정),
        /// NetworkObject + NetworkTransform으로 네트워크 프리팹화.
        /// </summary>
        private static T EnsureLootPrefab<T>(string prefabPath, string tempName, Color color) where T : LootPickup
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(prefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(PrefabsFolder, "Assets");

            // 임시 비주얼 - 스프라이트 미정이라 색만 지정(EnsureEnemyPrefab과 같은 관례).
            var temp = new GameObject(tempName);

            var sr = temp.AddComponent<SpriteRenderer>();
            sr.color = color;

            temp.AddComponent<T>();

            if (temp.GetComponent<NetworkObject>() == null) temp.AddComponent<NetworkObject>();
            if (temp.GetComponent<NetworkTransform>() == null) temp.AddComponent<NetworkTransform>();

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            Object.DestroyImmediate(temp);

            return prefabAsset.GetComponent<T>();
        }

        /// <summary>
        /// 이미 네트워크 플레이어 프리팹으로 전환된 Hayoung_Player.prefab에 PlayerProgress를
        /// 추가 연결한다 - FixPlayerAttackEnemyLayer와 같은 이유로 씬이 아니라 프리팹 에셋을
        /// PrefabUtility로 직접 연다(이미 붙어있으면 건너뜀 - 멱등).
        /// </summary>
        private static void EnsurePlayerProgressOnPlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{PlayerPrefabPath}'를 찾을 수 없어 PlayerProgress 연결을 건너뜁니다 - " +
                                  "먼저 'CheonMyeong/Setup/Convert Hayoung To Network Player'를 실행하세요.");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (contents.GetComponent<PlayerProgress>() != null)
            {
                PrefabUtility.UnloadPrefabContents(contents);
                return; // 이미 붙어있음
            }

            contents.AddComponent<PlayerProgress>();
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Debug.Log($"[CheonMyeong] '{PlayerPrefabPath}'에 PlayerProgress(개인 엽전) 컴포넌트를 추가했습니다.");
        }

        /// <summary>
        /// 261013 추가 - 예섭 요청("대미지 들어가는 이펙트 더미 도형으로 그 범위 정확히
        /// 맞춰서 보여줘, 알파값 낮춰서 반투명하게"). AttackRangeDebugVisual.cs가 실제로
        /// 쓰는 두 값(stats/autoAttack)을 EnsurePlayerProgressOnPlayerPrefab()과 같은
        /// LoadPrefabContents 패턴으로 연결한다 - stats는 PlayerAutoAttack이 이미 들고
        /// 있는 것과 같은 CharacterStatsSO 에셋(EnsureHayoungStats())을 그대로 재사용해서
        /// 수치가 어긋날 일이 없게 한다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Add Attack Range Debug Visual (To Player Prefab)")]
        public static void AddAttackRangeDebugVisualToPlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{PlayerPrefabPath}'를 찾을 수 없어 공격 범위 디버그 연출 연결을 " +
                                  "건너뜁니다 - 먼저 'CheonMyeong/Setup/Convert Hayoung To Network Player'를 실행하세요.");
                return;
            }

            CharacterStatsSO stats = EnsureHayoungStats();
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

            var autoAttack = contents.GetComponent<PlayerAutoAttack>();
            if (autoAttack == null)
            {
                Debug.LogError($"[CheonMyeong] '{PlayerPrefabPath}'에 PlayerAutoAttack 컴포넌트가 없습니다 - " +
                                "공격 범위 디버그 연출을 연결할 수 없습니다.");
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            var debugVisual = contents.GetComponent<AttackRangeDebugVisual>();
            if (debugVisual == null)
            {
                debugVisual = contents.AddComponent<AttackRangeDebugVisual>();
            }

            AssignPrivateField(debugVisual, "stats", stats);
            AssignPrivateField(debugVisual, "autoAttack", autoAttack);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Debug.Log($"[CheonMyeong] '{PlayerPrefabPath}'에 AttackRangeDebugVisual을 추가/연결했습니다 - " +
                      "공격할 때마다 실제 판정 범위(부채꼴)가 반투명 도형으로 잠깐 보입니다.");
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

        /// <summary>
        /// 기획 확정(261013): "한쪽 사망 시 남은 시간 동안 다른 쪽 플레이어에게
        /// 카메라 고정되고 관전". PlayerHealth가 PlayerCameraRig.Instance를 찾아서
        /// 쓰므로, 이 메뉴로 Main Camera에 PlayerCameraRig를 미리 붙여둬야 한다
        /// (씬에 Main Camera가 아예 없으면 하나 만들고 태그까지 지정).
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Create Local Camera Rig (Spectate On Death)")]
        public static void CreateLocalCameraRig()
        {
            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                targetCamera = cameraObject.AddComponent<Camera>();
                targetCamera.orthographic = true;
                Debug.Log("[CheonMyeong] 씬에 Main Camera가 없어서 새로 생성했습니다.");
            }

            if (targetCamera.GetComponent<PlayerCameraRig>() == null)
            {
                targetCamera.gameObject.AddComponent<PlayerCameraRig>();
                Debug.Log("[CheonMyeong] PlayerCameraRig를 Main Camera에 추가했습니다 - " +
                          "PlayerHealth가 사망/부활 시 자동으로 이 카메라의 추적 대상을 전환합니다.");
            }
            else
            {
                Debug.Log("[CheonMyeong] Main Camera에 이미 PlayerCameraRig가 있습니다 - 건너뜀.");
            }

            EditorUtility.SetDirty(targetCamera.gameObject);
            EditorSceneManager.MarkSceneDirty(targetCamera.gameObject.scene);
            Selection.activeGameObject = targetCamera.gameObject;
        }

        /// <summary>
        /// 버그 수정 전용 메뉴 - 이미 'Convert Hayoung To Network Player'로 만들어진
        /// Hayoung_Player.prefab을 직접 열어서 PlayerAutoAttack.enemyLayer를 고친다.
        ///
        /// 원인: CreateHayoungPrototype()이 PlayerAutoAttack의 inputReader/stats는
        /// 연결했지만 enemyLayer(LayerMask)는 한 번도 연결한 적이 없어서 기본값 0
        /// (아무 레이어도 선택 안 됨)으로 남아있었다. 그 결과 Attack()의
        /// Physics2D.OverlapCircle(ContactFilter2D로 enemyLayer 사용)이 항상 0개를
        /// 맞혀서 공격 타이머/루프는 정상 작동해도 실제로는 아무 피해도 안 들어가고
        /// 있었다 - "공격이 안 나간다"고 보고된 증상의 원인.
        ///
        /// 하영은 이미 네트워크 플레이어 프리팹으로 전환돼서 씬에 더 이상
        /// '{PrototypeObjectName}' 오브젝트가 없기 때문에, CreateHayoungPrototype()을
        /// 다시 실행하면 씬에 엉뚱한 새 오브젝트가 생길 뿐 실제 스폰되는 프리팹은
        /// 고쳐지지 않는다 - 그래서 프리팹 에셋을 직접 여는 이 전용 메서드가 필요하다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Fix Player Attack Enemy Layer (Bugfix)")]
        public static void FixPlayerAttackEnemyLayer()
        {
            EnsureLayer(EnemyLayerName);
            int enemyLayerIndex = LayerMask.NameToLayer(EnemyLayerName);
            if (enemyLayerIndex == -1)
            {
                Debug.LogError($"[CheonMyeong] '{EnemyLayerName}' 레이어를 찾거나 만들지 못했습니다. " +
                                "Edit > Project Settings > Tags and Layers에서 직접 확인해주세요.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogError($"[CheonMyeong] '{PlayerPrefabPath}'를 찾을 수 없습니다. " +
                                "먼저 'CheonMyeong/Setup/Convert Hayoung To Network Player'를 실행하세요.");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            var attack = contents.GetComponent<PlayerAutoAttack>();
            if (attack == null)
            {
                Debug.LogError($"[CheonMyeong] '{PlayerPrefabPath}'에 PlayerAutoAttack 컴포넌트가 없습니다.");
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            AssignLayerMaskField(attack, "enemyLayer", 1 << enemyLayerIndex);
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            Debug.Log($"[CheonMyeong] '{PlayerPrefabPath}'의 PlayerAutoAttack.Enemy Layer를 'Enemy'로 " +
                      "연결했습니다. 지금까지 이 필드가 비어있어서(레이어 마스크 0) 공격 판정 자체가 " +
                      "항상 0개를 맞혀서 아무 피해도 안 들어가고 있었습니다. Play Mode를 재시작해서 " +
                      "다시 테스트해보세요(Console에 '하영 공격 적중' 로그가 뜨는지 확인).");
        }

        /// <summary>
        /// 하영 애니메이션 클립 3개(Idle/Walk/Attack)와 Animator Controller를 만들고,
        /// Hayoung_Player.prefab에 SpriteRenderer + Animator + PlayerAnimator까지
        /// 전부 연결한다. 하영은 이미 네트워크 플레이어 프리팹으로 전환돼서 씬이 아니라
        /// 프리팹 에셋이기 때문에(FixPlayerAttackEnemyLayer와 같은 이유) PrefabUtility로
        /// 직접 연다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Create Hayoung Animations + Animator")]
        public static void CreateHayoungAnimationsAndAnimator()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(HayoungSpriteSheetPath) == null)
            {
                Debug.LogError($"[CheonMyeong] '{HayoungSpriteSheetPath}'를 찾을 수 없습니다.");
                return;
            }

            EnsureFolderRecursive(HayoungAnimFolder);

            Dictionary<string, Sprite> sprites = LoadNamedSprites(HayoungSpriteSheetPath);

            Sprite[] idleFrames = GetSpriteRange(sprites, HayoungSpriteBaseName, IdleFrameStart, IdleFrameEnd);
            Sprite[] walkFrames = GetSpriteRange(sprites, HayoungSpriteBaseName, WalkFrameStart, WalkFrameEnd);
            Sprite[] attackFrames = GetSpriteRange(sprites, HayoungSpriteBaseName, AttackFrameStart, AttackFrameEnd);

            if (idleFrames.Length == 0 || walkFrames.Length == 0 || attackFrames.Length == 0)
            {
                Debug.LogError("[CheonMyeong] 애니메이션 프레임을 하나도 못 찾아서 중단합니다. 위 에러 로그를 확인하세요.");
                return;
            }

            AnimationClip idleClip = CreateSpriteAnimationClip(HayoungIdleClipPath, idleFrames, 8f, true);
            AnimationClip walkClip = CreateSpriteAnimationClip(HayoungWalkClipPath, walkFrames, 10f, true);
            AnimationClip attackClip = CreateSpriteAnimationClip(HayoungAttackClipPath, attackFrames, 12f, false);

            AnimatorController controller = CreateHayoungAnimatorController(idleClip, walkClip, attackClip);
            AssetDatabase.SaveAssets();

            ApplyAnimatorToPlayerPrefab(controller, attackClip);

            Debug.Log("[CheonMyeong] 하영 애니메이션 클립 3개(Idle/Walk/Attack) + Animator Controller 생성하고 " +
                      $"'{PlayerPrefabPath}'에 SpriteRenderer/Animator/PlayerAnimator까지 연결 완료. " +
                      "프레임 속도(Idle 8fps / Walk 10fps / Attack 12fps)는 값은 미정 - 느낌 보고 클립에서 " +
                      "직접 조정하면 됩니다. Attack 애니메이션은 이제 attackInterval에 맞춰 재생 속도가 " +
                      "자동으로 스케일됩니다(PlayerAnimator의 AttackSpeed 파라미터, 클립 길이는 고정) - " +
                      "길이 자체를 바꾸는 게 아니라 재생 속도만 바뀌는 방식입니다. " +
                      "방향 표현은 회전 대신 SpriteRenderer.flipX(좌우반전)로 바꿨습니다 - " +
                      "PlayerAutoAttack.cs 주석에 이유 적어뒀습니다.");
        }

        private static Dictionary<string, Sprite> LoadNamedSprites(string texturePath)
        {
            var map = new Dictionary<string, Sprite>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(texturePath))
            {
                if (asset is Sprite sprite)
                {
                    map[sprite.name] = sprite;
                }
            }
            return map;
        }

        private static Sprite[] GetSpriteRange(Dictionary<string, Sprite> sprites, string baseName, int startInclusive, int endInclusive)
        {
            var list = new List<Sprite>();
            for (int i = startInclusive; i <= endInclusive; i++)
            {
                string spriteName = $"{baseName}_{i}";
                if (sprites.TryGetValue(spriteName, out Sprite sprite))
                {
                    list.Add(sprite);
                }
                else
                {
                    Debug.LogError($"[CheonMyeong] 스프라이트 '{spriteName}'을(를) {HayoungSpriteSheetPath}에서 " +
                                    "찾지 못했습니다. 이름이 바뀌었거나 슬라이스가 다시 됐을 수 있습니다.");
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 같은 경로에 이미 AnimationClip이 있으면 그걸 계속 써서(프레임만 다시 세팅)
        /// 재실행해도 다른 에셋이 참조하는 clip 레퍼런스가 깨지지 않게 한다.
        /// </summary>
        private static AnimationClip CreateSpriteAnimationClip(string clipPath, Sprite[] frames, float frameRate, bool loop)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            bool isNew = clip == null;
            if (isNew)
            {
                clip = new AnimationClip();
            }
            clip.frameRate = frameRate;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty, // SpriteRenderer가 Animator와 같은 오브젝트(루트)에 있다는 전제
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe { time = i / frameRate, value = frames[i] };
            }
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (isNew)
            {
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            else
            {
                EditorUtility.SetDirty(clip);
            }

            return clip;
        }

        /// <summary>
        /// 컨트롤러가 이미 있으면(재실행) 상태별 모션만 최신 클립으로 다시 연결하고,
        /// 파라미터/트랜지션은 처음 만들 때만 세팅한다 - 재실행마다 트랜지션이
        /// 중복으로 쌓이는 걸 막기 위함.
        /// </summary>
        private static AnimatorController CreateHayoungAnimatorController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip attackClip)
        {
            bool isNew = AssetDatabase.LoadAssetAtPath<AnimatorController>(HayoungAnimatorControllerPath) == null;
            AnimatorController controller = isNew
                ? AnimatorController.CreateAnimatorControllerAtPath(HayoungAnimatorControllerPath)
                : AssetDatabase.LoadAssetAtPath<AnimatorController>(HayoungAnimatorControllerPath);

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

            AnimatorState idleState = FindState(rootStateMachine, "Idle") ?? rootStateMachine.AddState("Idle");
            AnimatorState walkState = FindState(rootStateMachine, "Walk") ?? rootStateMachine.AddState("Walk");
            AnimatorState attackState = FindState(rootStateMachine, "Attack") ?? rootStateMachine.AddState("Attack");

            idleState.motion = idleClip;
            walkState.motion = walkClip;
            attackState.motion = attackClip;

            // 기획 지시: 첫 애니메이션은 무조건 Idle로 시작(고정값) - Entry 노드는
            // Mecanim에서 defaultState로 자동 연결되므로 이 한 줄이 곧 "스폰 직후
            // 재생되는 상태"를 결정한다. PlayerAnimator.OnNetworkSpawn에서도
            // animator.Play("Idle")로 한 번 더 강제하는 이중 안전장치를 둬서, 나중에
            // 누군가 에디터에서 defaultState를 실수로 바꿔도 항상 Idle로 시작한다.
            rootStateMachine.defaultState = idleState;

            // 공속(attackInterval)이 바뀌어도 애니메이션 '모션 자체'는 그대로 두고 재생
            // 속도만 맞추기 위한 파라미터 - PlayerAnimator가 매 프레임
            // (공격 클립 길이 / attackInterval)을 계산해서 여기 넣어준다. 재실행 때도
            // 매번 다시 걸어줘야 한다(isNew 블록 밖) - speedParameter 자체는 상태(State)
            // 설정이라 트랜지션처럼 중복 누적되는 게 아니기 때문.
            attackState.speed = 1f;
            attackState.speedParameterActive = true;
            attackState.speedParameter = "AttackSpeed";

            // AttackSpeed는 이 기능이 나중에 추가된 파라미터라, 이전에 이미 만들어져 있던
            // 컨트롤러 에셋에는 없을 수 있다 - isNew 여부와 무관하게 없으면 추가한다
            // (있으면 중복 추가 안 함 - HasParameter로 먼저 확인).
            if (!HasParameter(controller, "AttackSpeed"))
            {
                controller.AddParameter("AttackSpeed", AnimatorControllerParameterType.Float);
            }

            if (isNew)
            {
                controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

                AnimatorStateTransition idleToWalk = idleState.AddTransition(walkState);
                idleToWalk.hasExitTime = false;
                idleToWalk.duration = 0f;
                idleToWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

                AnimatorStateTransition walkToIdle = walkState.AddTransition(idleState);
                walkToIdle.hasExitTime = false;
                walkToIdle.duration = 0f;
                walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

                AnimatorStateTransition anyToAttack = rootStateMachine.AddAnyStateTransition(attackState);
                anyToAttack.hasExitTime = false;
                anyToAttack.duration = 0f;
                // 261014 수정 - "공격이 모션보다 반박자 늦게 나온다" 제보 대응. 공격의 실제
                // 타이밍은 PlayerAutoAttack.attackTimer(스크립트 시계)가 유일한 권위 기준인데,
                // Animator의 Attack 상태는 그와 별개로 attackToIdle의 exitTime(아래, exitTime=1f)
                // 기준으로 자기 재생이 끝나야 Idle로 빠져나간다. canTransitionToSelf가 false면
                // Animator가 아직 Attack 상태 "안"에 있는 동안 들어온 새 Attack 트리거는
                // 무시되고, 그 상태가 끝난 뒤(다음 프레임 AnyState 재평가 시점)에야 처리된다 -
                // AttackSpeed로 재생 속도를 맞춰도 두 시계가 완벽히 같은 위상으로 맞물린다는
                // 보장은 없으므로(공속이 바뀌는 순간, 프레임레이트 흔들림 등), 트리거가 들어온
                // 시점과 애니메이션이 실제로 재시작되는 시점 사이에 지연(최대 한 상태분)이
                // 생길 수 있다. true로 바꾸면 스크립트가 트리거를 쏜 바로 그 프레임에
                // Attack 상태를 처음부터 다시 재생하므로, 유일한 타이밍 권위(attackTimer)와
                // 시각 연출이 항상 같은 프레임에 맞물린다.
                // 확인 못 함 - 이 변경이 실제로 체감 지연을 없애는지는 Unity 에디터에서 직접
                // 재생해봐야 확정된다(코드/문서 검토로 찾은 가장 유력한 원인이고, 이 변경
                // 자체가 다른 전환 로직에 영향을 주지 않는다는 것만 코드상 확인함). 아래 메뉴를
                // 다시 실행해서 Animator Controller를 재생성한 뒤 직접 테스트해 확인 바람.
                anyToAttack.canTransitionToSelf = true;
                anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

                // Attack 끝나면 일단 Idle로 복귀 - 계속 이동 중이었다면 Idle->Walk 조건이
                // 다음 프레임에 바로 다시 걸려서 자연스럽게 Walk로 넘어간다(미세한 한 프레임
                // 차이는 있지만 지금 단계에선 문제 없음 - 더 매끄럽게 하려면 나중에
                // "attack 직전 상태로 복귀" 방식으로 바꾸면 됨, 지금은 미정).
                AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
                attackToIdle.hasExitTime = true;
                attackToIdle.exitTime = 1f;
                attackToIdle.hasFixedDuration = true;
                attackToIdle.duration = 0f;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state.name == stateName)
                {
                    return child.state;
                }
            }
            return null;
        }

        private static bool HasParameter(AnimatorController controller, string parameterName)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == parameterName)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ApplyAnimatorToPlayerPrefab(AnimatorController controller, AnimationClip attackClip)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogError($"[CheonMyeong] '{PlayerPrefabPath}'를 찾을 수 없습니다. " +
                                "먼저 'CheonMyeong/Setup/Convert Hayoung To Network Player'를 실행하세요.");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

            var movement = contents.GetComponent<PlayerMovement>();
            var attack = contents.GetComponent<PlayerAutoAttack>();
            if (movement == null || attack == null)
            {
                Debug.LogError($"[CheonMyeong] '{PlayerPrefabPath}'에 PlayerMovement/PlayerAutoAttack이 없습니다.");
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            var spriteRenderer = contents.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) spriteRenderer = contents.AddComponent<SpriteRenderer>();

            var animator = contents.GetComponent<Animator>();
            if (animator == null) animator = contents.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // 명시적으로 Normal 고정 - "Animate Physics"였다면 트리거(SetTrigger)는
            // Update()에서 즉시 세팅돼도 실제 평가는 FixedUpdate 주기에 맞춰 한 템포
            // 늦게 일어나서, 프레임레이트와 Fixed Timestep 차이만큼 몇 프레임 정도
            // "항상 일정하게 반복되는" 지연이 생길 수 있다(스윙 이후 피격까지 미약한
            // 딜레이 증상 - 확인 못 했지만 가장 유력한 후보라 아예 배제해버린다).
            // 이 캐릭터는 물리 기반 애니메이션(래그돌 등)을 쓰지 않으므로 Normal이 맞다.
            animator.updateMode = AnimatorUpdateMode.Normal;

            var playerAnimator = contents.GetComponent<PlayerAnimator>();
            if (playerAnimator == null) playerAnimator = contents.AddComponent<PlayerAnimator>();

            AssignPrivateField(playerAnimator, "animator", animator);
            AssignPrivateField(playerAnimator, "spriteRenderer", spriteRenderer);
            AssignPrivateField(playerAnimator, "movement", movement);
            AssignPrivateField(playerAnimator, "attack", attack);
            AssignPrivateField(playerAnimator, "attackClip", attackClip);

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>
        /// 타이틀 화면 부적(Start/Settings/Exit) 3종을 생성한다. 각 부적은 "온전한 상태"
        /// 스프라이트 1장 + 대각선 절단 조각 4장(TopLeft-BottomRight 방향 2장,
        /// TopRight-BottomLeft 방향 2장) = 총 5장이고, 전부 {TitlePieceWidth}x{TitlePieceHeight}
        /// 픽셀의 단색 사각형 placeholder PNG로 Assets/Art/Title/Placeholder 하위에 실제
        /// 에셋 파일로 생성된다 - 나중에 같은 파일명·같은 픽셀 크기의 아트(절단면 모양만
        /// 남기고 나머지는 투명 처리한 PNG)로 그대로 덮어쓰면 위치/피벗을 다시 안 잡아도 됨.
        ///
        /// 네트워크 동기화는 필요 없음 - 타이틀은 접속 전 로컬 화면이라 TitleTalisman은
        /// NetworkBehaviour가 아니다. 드래그 판정은 TitleDragCutController 하나가 씬 전체를
        /// 보고 처리한다.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Create Title Talisman Prototype (Placeholder)")]
        public static void CreateTitleTalismanPrototype()
        {
            GameObject parentGo = EnsureSceneObject(TitleTalismanParentName);

            // 위치/회전 전부 임시(261013, 예섭 확인 - "세로 정렬, 부적은 약간 대각선으로
            // 붙어있을 것") - 문 자체(목재문 아트)가 아직 없어서 정확한 좌표는 모르니 세로로
            // 겹치지 않게 늘어놓고 각각 살짝 다른 각도로 기울여만 뒀다. 실제 문 아트 들어오면
            // 이 다섯 값(위치 3개+각도 3개)만 다시 맞추면 됨 - 아직 반복 조정 단계라 재실행할
            // 때마다 매번 이 값으로 덮어쓴다(수동으로 옮겨도 다음 재실행에 되돌아감에 주의).
            // 261013 추가(예섭 요청) - "시작/설정/종료" 세로쓰기 글자.
            CreateTalisman(parentGo.transform, "Start", new Color(0.80f, 0.25f, 0.20f), new Vector3(0f, 3f, 0f), -6f, "시작");
            CreateTalisman(parentGo.transform, "Settings", new Color(0.25f, 0.45f, 0.80f), new Vector3(0f, 0f, 0f), 8f, "설정");
            CreateTalisman(parentGo.transform, "Exit", new Color(0.35f, 0.35f, 0.35f), new Vector3(0f, -3f, 0f), -4f, "종료");

            GameObject controllerGo = EnsureSceneObject(nameof(TitleDragCutController));
            var controller = controllerGo.GetComponent<TitleDragCutController>();
            if (controller == null) controller = controllerGo.AddComponent<TitleDragCutController>();
            EditorUtility.SetDirty(controller);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(parentGo.scene);

            Selection.activeGameObject = parentGo;
            Debug.Log("[CheonMyeong] 타이틀 부적 3종(Start/Settings/Exit) placeholder 생성 완료 - " +
                      $"각각 온전한 상태 1장 + 대각선 절단 조각 4장, 전부 {TitlePieceWidth}x{TitlePieceHeight} " +
                      $"단색 사각형입니다({TitlePlaceholderFolder} 하위). 같은 파일명·같은 픽셀 크기로 최종 " +
                      "아트 PNG를 덮어쓰면 위치/피벗 재설정 없이 그대로 적용됩니다. " +
                      "대각선 좌/우 판정(TitleDragCutController.ClassifyDirection)은 임의로 한쪽을 " +
                      "정해뒀는데, 실제 드래그해보고 '왼쪽/오른쪽 대각선'이 반대로 느껴지면 그 메서드의 " +
                      "반환값 두 개만 서로 바꾸면 됩니다(확인 못 함 - 플레이 테스트 필요). " +
                      "Active Input Handling이 Input System(또는 Both)으로 설정돼 있어야 드래그가 인식됩니다.");
        }

        private static void CreateTalisman(Transform parent, string talismanName, Color baseColor,
                                            Vector3 localPosition, float localRotationZDegrees, string labelText)
        {
            Transform existing = parent.Find(talismanName + "_Talisman");
            GameObject root = existing != null ? existing.gameObject : new GameObject(talismanName + "_Talisman");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, 0f, localRotationZDegrees);
            // 스케일로 줄이는 방식 - PNG 자체(TitlePixelsPerUnit)는 그대로 둬서, 이미 만들어진
            // placeholder 텍스처를 다시 굽지 않고도 크기만 바로 반영된다. BoxCollider2D(hitArea)도
            // 로컬 스케일을 그대로 따라가므로 드래그 판정 영역도 같이 줄어든다.
            root.transform.localScale = Vector3.one * TitleTalismanScale;

            Sprite whole = CreateOrLoadPlaceholderSprite(talismanName, "Whole", baseColor);
            Sprite tlbrA = CreateOrLoadPlaceholderSprite(talismanName, "DiagTLBR_A", Color.Lerp(baseColor, Color.black, 0.15f));
            Sprite tlbrB = CreateOrLoadPlaceholderSprite(talismanName, "DiagTLBR_B", Color.Lerp(baseColor, Color.black, 0.30f));
            Sprite trblA = CreateOrLoadPlaceholderSprite(talismanName, "DiagTRBL_A", Color.Lerp(baseColor, Color.white, 0.15f));
            Sprite trblB = CreateOrLoadPlaceholderSprite(talismanName, "DiagTRBL_B", Color.Lerp(baseColor, Color.white, 0.30f));

            SpriteRenderer wholeRenderer = CreateWholeObject(root.transform, whole);

            var hitArea = root.GetComponent<BoxCollider2D>();
            if (hitArea == null) hitArea = root.AddComponent<BoxCollider2D>();
            hitArea.isTrigger = true;
            hitArea.size = whole.bounds.size;

            GameObject tlbrAGo = CreatePieceObject(root.transform, "DiagTLBR_A", tlbrA);
            GameObject tlbrBGo = CreatePieceObject(root.transform, "DiagTLBR_B", tlbrB);
            GameObject trblAGo = CreatePieceObject(root.transform, "DiagTRBL_A", trblA);
            GameObject trblBGo = CreatePieceObject(root.transform, "DiagTRBL_B", trblB);

            TextMesh label = CreateOrUpdateLabel(root.transform, labelText);

            var talisman = root.GetComponent<TitleTalisman>();
            if (talisman == null) talisman = root.AddComponent<TitleTalisman>();

            AssignPrivateField(talisman, "wholeRenderer", wholeRenderer);
            AssignPrivateField(talisman, "hitArea", hitArea);
            AssignPrivateField(talisman, "label", label);
            AssignPrivateField(talisman, "diagonalTLBR_PieceA", tlbrAGo);
            AssignPrivateField(talisman, "diagonalTLBR_PieceB", tlbrBGo);
            AssignPrivateField(talisman, "diagonalTRBL_PieceA", trblAGo);
            AssignPrivateField(talisman, "diagonalTRBL_PieceB", trblBGo);

            EditorUtility.SetDirty(talisman);
        }

        // 261013 재조정(예섭 피드백 - "문체를 궁서로, 크기를 줄여. 부적 대비 커 보이지 않게").
        // 처음 값(폰트 LegacyRuntime.ttf, characterSize 0.5f)이 너무 커 보인다는 피드백 반영 -
        // characterSize를 절반 이하로 줄임. 수치는 여전히 "느낌 보고 조정" 단계라 더 줄여야
        // 하면 바로 알려줄 것.
        private const int TitleLabelFontSize = 64;
        private const float TitleLabelCharacterSize = 0.2f; // 0.5f -> 0.2f (261013, 예섭 - "너무 커")
        private static readonly Color TitleLabelColor = Color.white;

        // Windows 기본 한글 폰트 중 "궁서"/"궁서체" 계열 - 영문 locale에서 내부 폰트 패밀리명이
        // "Gungsuh"/"GungsuhChe"로 잡히는 경우가 있어 둘 다 후보로 넣는다(사용자 PC가 Windows라
        // 보통 기본 설치돼 있음 - 확인 못 함, 실제로 안 뜨면 이 배열에 설치된 정확한 폰트명을
        // 추가해야 함).
        private static readonly string[] TitleLabelFontCandidates = { "궁서", "Gungsuh", "궁서체", "GungsuhChe" };

        /// <summary>
        /// 부적 위에 올라갈 세로쓰기 글자(261013, 예섭 요청 - "시작/설정/종료 세로쓰기로", 이후
        /// "문체 궁서로, 크기 줄여" 재요청).
        /// 레거시 UnityEngine.UI.Text는 Canvas(RectTransform) 전용이라 Canvas 없이 떠 있는
        /// 이 월드스페이스 SpriteRenderer 오브젝트에는 못 붙는다. 그래서 Canvas가 필요 없는
        /// 내장 TextMesh(3D 텍스트)를 쓴다. 폰트는 TextMeshPro가 아니라 Unity 레거시 Font
        /// 시스템의 Font.CreateDynamicFontFromOSFont로 사용자 PC(Windows)에 이미 설치된
        /// "궁서" 계열 OS 폰트를 동적으로 불러온다 - 별도 에셋 임포트/라이선스 파일 없이
        /// 시스템 폰트를 그대로 쓰는 표준적인 방법. 후보 폰트명이 전부 설치돼 있지 않으면
        /// Unity가 OS 기본 폰트로 조용히 대체한다 - 실제로 궁서체로 안 보이면
        /// TitleLabelFontCandidates에 정확한 설치된 폰트명을 추가해야 함(확인 못 함).
        ///
        /// TextMesh 자체엔 세로쓰기 모드가 없어서, 글자 하나당 줄 하나("\n"로 분리)로 만들어
        /// 위에서 아래로 쌓이게 하는 방식으로 흉내냈다 - "시작"/"설정"/"종료" 전부 2글자라
        /// 이 정도로 충분. 더 긴 문구나 가로쓰기와 섞어 써야 하면 이 방식으로는 부족하니
        /// 다시 검토할 것.
        /// </summary>
        private static TextMesh CreateOrUpdateLabel(Transform parent, string labelText)
        {
            Transform existing = parent.Find("Label");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var textMesh = go.GetComponent<TextMesh>();
            if (textMesh == null) textMesh = go.AddComponent<TextMesh>();

            Font font = Font.CreateDynamicFontFromOSFont(TitleLabelFontCandidates, TitleLabelFontSize);
            textMesh.font = font;
            textMesh.fontSize = TitleLabelFontSize;
            textMesh.characterSize = TitleLabelCharacterSize;
            textMesh.color = TitleLabelColor;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.text = string.Join("\n", labelText.ToCharArray());

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = font.material;
            meshRenderer.sortingOrder = 10; // Whole/조각 SpriteRenderer들보다 위에 그려지도록

            return textMesh;
        }

        private static SpriteRenderer CreateWholeObject(Transform parent, Sprite sprite)
        {
            Transform existing = parent.Find("Whole");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Whole");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.enabled = true;
            return sr;
        }

        /// <summary>
        /// 절단 조각 하나를 만든다. 비활성 상태로 시작해서(TitleTalisman.Cut이 필요할 때만
        /// 켠다) Rigidbody2D를 미리 붙여두되 평소엔 GameObject 자체가 꺼져 있어 물리 연산
        /// 비용이 들지 않는다. 콜라이더는 붙이지 않음 - 바닥/다른 오브젝트와 충돌할 필요
        /// 없는 순수 장식용 낙하 연출이라서(필요해지면 나중에 추가).
        /// </summary>
        private static GameObject CreatePieceObject(Transform parent, string childName, Sprite sprite)
        {
            Transform existing = parent.Find(childName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(childName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
            rb.freezeRotation = false;

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// 이미 같은 경로에 PNG가 있으면 그대로 불러온다(아트로 교체된 걸 재실행 한 번에
        /// 덮어써버리지 않기 위함). 없을 때만 {TitlePieceWidth}x{TitlePieceHeight} 단색
        /// 텍스처를 새로 만들어 PNG로 저장하고 Sprite로 임포트한다.
        /// </summary>
        private static Sprite CreateOrLoadPlaceholderSprite(string talismanName, string pieceName, Color color)
        {
            string folder = $"{TitlePlaceholderFolder}/{talismanName}";
            EnsureFolderRecursive(folder);
            string path = $"{folder}/{talismanName}_{pieceName}.png";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(TitlePieceWidth, TitlePieceHeight, TextureFormat.RGBA32, false);
            var pixels = new Color[TitlePieceWidth * TitlePieceHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            File.WriteAllBytes(path, png);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TitlePixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// EnsureFolder(path, parent)는 한 단계만 처리해서 "Assets/Art/Title/Placeholder/Start"처럼
        /// 여러 단계가 한 번에 없을 수 있는 경로엔 못 쓴다 - 그래서 상위부터 순서대로
        /// 하나씩 만들어 올라가는 버전을 따로 둔다.
        /// </summary>
        private static void EnsureFolderRecursive(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        /// <summary>
        /// 씬에 놓인 '{PrototypeObjectName}'을 네트워크 플레이어 프리팹으로 전환한다:
        /// 1) NetworkObject + NetworkTransform 부착 (NGO 2.13.3 기준 NetworkTransform은
        ///    클라이언트-서버 토폴로지에서 기본이 서버 권위라 별도 설정 불필요 - 공식 문서로 확인함)
        /// 2) 프리팹으로 저장 후 NetworkManager.NetworkConfig.PlayerPrefab에 연결
        ///    (PlayerPrefab은 공개 필드 - GitHub 소스로 확인함, 버전 바뀌어도 못 찾으면
        ///    AssignNestedField가 추측 없이 에러를 남긴다)
        /// 3) 씬에 남아있던 원본 오브젝트는 삭제 (프리팹이 NetworkManager가 자동 스폰할
        ///    플레이어 오브젝트 역할을 대신함)
        /// 4) 적이 아직 네트워크 동기화가 안 돼 있어서('{SpawnerObjectName}'은 각 클라이언트가
        ///    각자 로컬로 독자 실행 중) 참가 전에 로컬 적한테 맞아 죽는 문제를 막기 위해
        ///    스포너를 임시로 비활성화 - 적 네트워킹은 다음 단계에서 별도로 작업 예정.
        /// </summary>
        [MenuItem("CheonMyeong/Setup/Convert Hayoung To Network Player")]
        public static void ConvertHayoungToNetworkPlayer()
        {
            GameObject sceneInstance = GameObject.Find(PrototypeObjectName);
            if (sceneInstance == null)
            {
                Debug.LogError($"[CheonMyeong] '{PrototypeObjectName}'를 찾을 수 없습니다. " +
                                "먼저 'CheonMyeong/Setup/Create Hayoung Prototype Object'를 실행하세요.");
                return;
            }

            GameObject networkBootstrap = GameObject.Find(NetworkBootstrapObjectName);
            if (networkBootstrap == null)
            {
                Debug.LogError($"[CheonMyeong] '{NetworkBootstrapObjectName}'를 찾을 수 없습니다. " +
                                "먼저 'CheonMyeong/Setup/Create Network Bootstrap'을 실행하세요.");
                return;
            }

            var networkManager = networkBootstrap.GetComponent<NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogError($"[CheonMyeong] '{NetworkBootstrapObjectName}'에 NetworkManager 컴포넌트가 없습니다.");
                return;
            }

            if (sceneInstance.GetComponent<NetworkObject>() == null)
            {
                sceneInstance.AddComponent<NetworkObject>();
            }

            if (sceneInstance.GetComponent<NetworkTransform>() == null)
            {
                sceneInstance.AddComponent<NetworkTransform>();
            }

            EnsureFolder(PrefabsFolder, "Assets");

            var originalScene = sceneInstance.scene;

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(sceneInstance, PlayerPrefabPath);

            AssignNestedField(networkManager, "NetworkConfig", "PlayerPrefab", prefabAsset);
            EditorUtility.SetDirty(networkManager);

            Object.DestroyImmediate(sceneInstance);

            GameObject spawnerGo = GameObject.Find(SpawnerObjectName);
            if (spawnerGo != null)
            {
                spawnerGo.SetActive(false);
                EditorUtility.SetDirty(spawnerGo);
            }

            EditorSceneManager.MarkSceneDirty(originalScene);

            Debug.Log($"[CheonMyeong] 하영을 네트워크 플레이어 프리팹으로 변환 완료: {PlayerPrefabPath}. " +
                      "NetworkManager의 Player Prefab 필드에 자동 연결했습니다(위에 에러 로그 없는지 확인하세요). " +
                      "Inspector에서 NetworkManager의 'Create Player Prefab' 체크박스가 켜져 있는지는 " +
                      "직접 확인해주세요 - 필드명이 NGO 버전마다 달라서 못 찾으면 자동화하지 않고 확인 못 함으로 둡니다. " +
                      $"씬에 있던 '{SpawnerObjectName}'은 적이 아직 네트워크 동기화가 안 돼 있어서(각 클라이언트가 " +
                      "로컬로 독자 스폰 중) 참가 전에 죽는 문제를 막으려고 임시로 비활성화했습니다.");
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
            // 2026-10-13: 공식 스탯 시트(스탯 천명_캐릭터_261012.xlsx, Sheet1) 값으로
            // 최초 생성 시점부터 맞춰둠 - MigrateHayoungStatsToOfficialSheet()와 같은 수치.
            stats.maxHealth = 150f;
            stats.moveSpeed = 5f;
            stats.attackPower = 20f;
            stats.attacksPerSecond = 0.8f;
            stats.attackRange = 1f;

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

            // Enemy에 [RequireComponent(typeof(NetworkObject))]가 붙어있어서 자동으로도
            // 추가되지만, NetworkTransform은 별도로 붙여줘야 위치가 복제된다.
            var enemyComponent = temp.AddComponent<Enemy>();
            AssignPrivateField(enemyComponent, "stats", stats);
            AssignPrivateField(enemyComponent, "spriteRenderer", sr);

            if (temp.GetComponent<NetworkObject>() == null) temp.AddComponent<NetworkObject>();
            if (temp.GetComponent<NetworkTransform>() == null) temp.AddComponent<NetworkTransform>();

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, EnemyPrefabPath);
            Object.DestroyImmediate(temp);

            return prefabAsset.GetComponent<Enemy>();
        }

        /// <summary>
        /// 이전 버전으로 만들어진 Enemy_Basic 프리팹을 고쳐서 다시 저장한다:
        /// 1) 콜라이더 isTrigger = true (적끼리 뭉쳐서 멈추는 문제 수정)
        /// 2) 레이어를 "Enemy"로 변경 (EnemyPool이 끄는 적-적 충돌 판정 대상이 되려면 필요)
        /// 3) NetworkObject + NetworkTransform 추가 (적을 네트워크 프리팹으로 전환하기 전에
        ///    만들어진 프리팹에는 없을 수 있음 - EnemyPool의 INetworkPrefabInstanceHandler가
        ///    NetworkObject 없이는 동작하지 않는다)
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

            if (contents.GetComponent<NetworkObject>() == null)
            {
                contents.AddComponent<NetworkObject>();
                changed = true;
            }

            if (contents.GetComponent<NetworkTransform>() == null)
            {
                contents.AddComponent<NetworkTransform>();
                changed = true;
            }

            // 점멸 연출을 추가하기 전에 만들어진 프리팹에는 Enemy.spriteRenderer 필드가
            // 비어 있을 수 있다 - SpriteRenderer는 이미 있으니 참조만 새로 연결해준다.
            var enemyComponent = contents.GetComponent<Enemy>();
            var sr = contents.GetComponent<SpriteRenderer>();
            if (enemyComponent != null && sr != null)
            {
                var so = new SerializedObject(enemyComponent);
                var prop = so.FindProperty("spriteRenderer");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    AssignPrivateField(enemyComponent, "spriteRenderer", sr);
                    changed = true;
                }
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, EnemyPrefabPath);
                Debug.Log("[CheonMyeong] 기존 Enemy_Basic 프리팹을 Trigger + 'Enemy' 레이어 + " +
                          "NetworkObject/NetworkTransform + spriteRenderer 연결로 수정했습니다.");
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
            var existing = FindAcrossLoadedScenes(name);
            if (existing != null)
            {
                return existing;
            }
            return new GameObject(name);
        }

        /// <summary>
        /// GameObject.Find 대신 로드된 모든 씬을 비활성 오브젝트까지 포함해서 직접 순회한다(261013 수정).
        /// GameObject.Find는 "비활성화(SetActive(false))된 GameObject는 찾지 못한다"는 Unity의 공식
        /// 동작이라, 씬 분리 이후 EnemySpawner처럼 비활성 상태로 Gameplay 씬에 들어가 있는 오브젝트를
        /// 이 메뉴를 재실행할 때 못 찾고 "이미 있는데 또 새로 만드는" 중복 생성 버그가 있었다
        /// (GameplaySceneSplitSetup.cs의 MoveIfFound 버그와 원인이 같고 수정 방식도 같음).
        /// </summary>
        private static GameObject FindAcrossLoadedScenes(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject found = FindInHierarchyIncludingInactive(root.transform, name);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }

        private static GameObject FindInHierarchyIncludingInactive(Transform current, string name)
        {
            if (current.name == name)
            {
                return current.gameObject;
            }

            foreach (Transform child in current)
            {
                GameObject found = FindInHierarchyIncludingInactive(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void EnsureFolder(string path, string parent)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string folderName = path.Substring(parent.Length + 1);
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        /// <summary>
        /// LayerMask 필드 전용 - LayerMask는 UnityEngine.Object가 아니라
        /// objectReferenceValue로 못 넣는다. 직렬화된 실제 형태는 int 비트마스크라
        /// intValue로 넣는다(SerializedProperty.FindProperty("필드명.m_Bits")가 아니라
        /// LayerMask 필드 자체의 SerializedProperty가 바로 int형 - Unity가 LayerMask를
        /// 이렇게 직렬화하는 건 공식적으로 잘 알려진 동작이며 버전에 따라 바뀌지 않는다).
        /// </summary>
        private static void AssignLayerMaskField(Object target, string fieldName, int layerMaskValue)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[CheonMyeong] {target.GetType().Name}에서 필드 '{fieldName}'를 찾지 못했습니다. " +
                                "스크립트 필드명이 바뀌었다면 이 에디터 스크립트도 같이 고쳐야 합니다.");
                return;
            }
            prop.intValue = layerMaskValue;
            so.ApplyModifiedProperties();
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
