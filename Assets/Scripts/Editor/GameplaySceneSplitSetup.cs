using System.IO;
using System.Linq;
using CheonMyeong.Data;
using CheonMyeong.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 씬 분리 작업(261013, 예섭 지시 - "지금 바로 진행" + "처음부터 맵별 교체 구조로").
    ///
    /// 지금까지는 SampleScene.unity 하나에 타이틀/UI 12화면/NetworkManager/EnemyPool/
    /// EnemySpawner가 전부 들어있었다 - 로비에서 구경만 해도 인게임 전용 오브젝트가 전부
    /// 메모리에 올라가 있고, 맵이 늘어나면(CHOICE_MAP에 "추후 공개" 3자리가 이미 있음)
    /// 전부 한 씬에 쌓여서 무한정 커지는 구조라 분리한다.
    ///
    /// 분리 후 구조:
    /// - Bootstrap 씬(SampleScene.unity, 항상 열려있는 단일 씬) - NetworkManager/SessionManager/
    ///   GameplaySceneLoader, EventSystem+UICanvas(12화면 전부), 타이틀 전부 그대로 유지.
    /// - Gameplay 씬(맵마다 하나, 지금은 "Gameplay_DalbamPyejangteo") - EnemyPool/EnemySpawner
    ///   (+ 앞으로 추가될 맵 지형/스폰 포인트). 대기실 START에서 호스트가 NetworkSceneManager로
    ///   Additive 로드 - NGO가 접속해 있는 모든 클라이언트에 자동으로 동기화해준다.
    ///
    /// 맵이 늘어날 때 할 일은 이 두 가지뿐: (1) 새 Gameplay 씬 만들어서 Build Settings에 등록,
    /// (2) MapCatalog.asset(Assets/Data/Maps)에 행 추가. 이 에디터 스크립트/ChoiceMapScreen
    /// 코드는 다시 안 건드려도 된다.
    ///
    /// 재실행해도 안전(멱등) - 이미 옮겨진 오브젝트/이미 있는 씬·자산은 건드리지 않는다.
    /// </summary>
    public static class GameplaySceneSplitSetup
    {
        private const string BootstrapSceneName = "SampleScene";
        private const string FirstGameplaySceneName = "Gameplay_DalbamPyejangteo";
        private const string GameplayScenesFolder = "Assets/Scenes";
        private const string MapDataFolder = "Assets/Data/Maps";
        private const string EnemyPoolObjectName = "EnemyPool";
        private const string EnemySpawnerObjectName = "EnemySpawner";
        private const string ProgressionRootObjectName = "ProgressionSystems"; // 261013 추가 - 골드/경험치 풀 + 파티 진행도

        [MenuItem("CheonMyeong/Setup/Split Into Bootstrap + Gameplay Scenes")]
        public static void SplitScenes()
        {
            Scene bootstrapScene = SceneManager.GetActiveScene();
            if (bootstrapScene.name != BootstrapSceneName)
            {
                Debug.LogWarning($"[CheonMyeong] 지금 활성 씬이 '{bootstrapScene.name}'입니다 - " +
                                  $"보통은 '{BootstrapSceneName}'을 열어둔 상태에서 실행합니다. " +
                                  "그래도 나머지 작업은 계속 진행합니다.");
            }

            // 261013 버그 수정 - "이미 로드돼 있어서 다시 로드 안 함"(GameplaySceneLoader.cs) 경고 때문에
            // Play 눌러도 WAIT_ROOM→START가 영영 안 넘어가던 문제의 원인을 여기서 찾았다: 이 메뉴를
            // 처음 실행할 때(Gameplay 씬 파일이 아직 없을 때)만 아래 createdNewGameplayScene 분기로
            // CloseScene을 했었는데, Gameplay 씬 파일이 이미 있는 상태로 이 메뉴를 "재실행"하면
            // EnsureGameplayScene이 OpenScene(Additive)으로 또 열기만 하고 아무도 안 닫아서 에디터에
            // 영구히 Additive로 로드된 채 남았다 - 그 상태로 Play를 누르면 Gameplay 씬이 "이미 로드됨"
            // 상태로 Play 모드에 그대로 들어가버려서, 런타임에 NetworkSceneManager.LoadScene이 호출돼도
            // SceneManager.GetSceneByName(...).isLoaded가 true라 아예 로드를 시작도 안 하고 막혔던 것.
            // 처음부터 "원래 열려 있던 상태였는지"만 기준으로 닫아야 재실행 때도 안전하다.
            string gameplayScenePath = $"{GameplayScenesFolder}/{FirstGameplaySceneName}.unity";
            bool gameplaySceneWasAlreadyOpen = SceneManager.GetSceneByPath(gameplayScenePath).IsValid()
                                               && SceneManager.GetSceneByPath(gameplayScenePath).isLoaded;

            Scene gameplayScene = EnsureGameplayScene(FirstGameplaySceneName, out _);
            string gameplaySceneNameForLog = gameplayScene.name; // 아래서 CloseScene 하면 .name이 빈 문자열이 돼버려서 미리 캐싱

            MoveIfFound(EnemyPoolObjectName, bootstrapScene, gameplayScene);
            MoveIfFound(EnemySpawnerObjectName, bootstrapScene, gameplayScene);
            // ProgressionSystems(골드/경험치 풀 + PartyProgress)는 선택 사항 - "Create Progression
            // Prototype" 메뉴를 아직 안 돌렸으면 없을 수 있으므로 못 찾아도 경고만 남기고 계속 진행.
            MoveIfFound(ProgressionRootObjectName, bootstrapScene, gameplayScene);

            RegisterSceneInBuildSettings(bootstrapScene.path);
            RegisterSceneInBuildSettings(gameplayScene.path);

            EnableNetworkSceneManagement();
            EnsureGameplaySceneLoaderComponent();

            MapCatalogSO catalog = EnsureMapCatalog(gameplayScene.name);

            EditorSceneManager.MarkSceneDirty(bootstrapScene);
            EditorSceneManager.MarkSceneDirty(gameplayScene);
            EditorSceneManager.SaveScene(gameplayScene);
            EditorSceneManager.SaveScene(bootstrapScene);

            if (!gameplaySceneWasAlreadyOpen)
            {
                // 평소 에디터 작업 흐름은 Bootstrap 씬 하나만 열어두는 것이므로, 이 메뉴 실행 전에
                // 원래 안 열려 있었다면(새로 만들었든, 파일은 있었는데 이번에 OpenScene으로
                // 열었든) 저장 후 다시 닫아둔다 - 실제 플레이 때는 런타임에 Additive로만 열린다.
                // (createdNewGameplayScene만 체크하면 "파일은 이미 있고 메뉴를 재실행한" 경우를
                // 놓쳐서 Gameplay 씬이 에디터에 계속 열린 채로 남는 버그가 있었다 - 위 주석 참고.)
                EditorSceneManager.CloseScene(gameplayScene, true);
                EditorSceneManager.SetActiveScene(bootstrapScene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CheonMyeong] 씬 분리 완료 - Bootstrap({bootstrapScene.name}) + " +
                      $"Gameplay({gameplaySceneNameForLog}). MapCatalog: {AssetDatabase.GetAssetPath(catalog)} - " +
                      "'CheonMyeong/Setup/Build Full UI Screens (Skeleton)'을 다시 실행해서 " +
                      "CHOICE_MAP 화면이 이 맵 목록을 읽어가게 갱신해주세요.");
        }

        private static Scene EnsureGameplayScene(string sceneName, out bool createdNew)
        {
            string path = $"{GameplayScenesFolder}/{sceneName}.unity";
            EnsureFolder(GameplayScenesFolder);

            if (File.Exists(path))
            {
                createdNew = false;
                Scene existing = SceneManager.GetSceneByPath(path);
                if (existing.IsValid() && existing.isLoaded)
                {
                    return existing;
                }
                return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            createdNew = true;
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(newScene, path);
            return newScene;
        }

        /// <summary>
        /// objectName을 sourceScene에서 찾아 targetScene으로 옮긴다. GameObject.Find가 아니라
        /// 씬의 루트 오브젝트들을 직접 순회하는 이유(261013 버그 수정) - GameObject.Find는 "비활성화된
        /// (SetActive(false)) GameObject는 찾지 못한다"는 Unity의 오래된 공식 동작이라, EnemySpawner가
        /// (CheonMyeongPrototypeSetup.cs에서 다른 설정 루틴이 SetActive(false)를 호출해둔 상태라서)
        /// 조용히 못 찾고 넘어갔었다 - 그 결과 EnemyPool만 Gameplay 씬으로 옮겨지고 EnemySpawner는
        /// SampleScene에 남아 "Cross scene references are not supported" 경고가 났었다.
        /// 이 방식은 비활성 오브젝트도 그대로 찾아낸다(Transform 계층은 활성 상태와 무관하게 순회 가능).
        /// </summary>
        private static void MoveIfFound(string objectName, Scene sourceScene, Scene targetScene)
        {
            if (FindInScene(targetScene, objectName) != null)
            {
                return; // 이미 옮겨져 있음 - 재실행 시 중복 처리 방지
            }

            GameObject go = FindInScene(sourceScene, objectName);
            if (go == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{objectName}' 오브젝트를 '{sourceScene.name}' 씬에서 찾지 못해 " +
                                  "Gameplay 씬으로 옮기지 못했습니다 - CheonMyeong/Setup의 적 프로토타입 생성 메뉴를 " +
                                  "먼저 실행했는지 확인하세요.");
                return;
            }

            SceneManager.MoveGameObjectToScene(go, targetScene);
            EditorUtility.SetDirty(go);
        }

        /// <summary>scene의 루트 오브젝트부터 자식까지 이름으로 전부 순회(비활성 포함) - GameObject.Find와
        /// 달리 SetActive(false)된 오브젝트도 찾아낸다. 씬 하나로 범위를 한정하므로 다른 씬에 동명 오브젝트가
        /// 있어도 엉뚱한 걸 집어오지 않는다(GameObject.Find는 로드된 모든 씬을 뒤진다).</summary>
        private static GameObject FindInScene(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject found = FindInHierarchy(root.transform, objectName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static GameObject FindInHierarchy(Transform current, string objectName)
        {
            if (current.name == objectName)
            {
                return current.gameObject;
            }

            foreach (Transform child in current)
            {
                GameObject found = FindInHierarchy(child, objectName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FirstOrDefault(s => s.path == scenePath);
            if (existing != null)
            {
                existing.enabled = true;
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// NetworkManager Inspector의 "Enable Scene Management"를 켠다 - 이게 꺼져 있으면
        /// GameplaySceneLoader의 LoadScene/UnloadScene이 전부 동작하지 않는다(공식 문서 확인,
        /// 261013). NGO 버전이 달라 필드를 못 찾으면 임의로 추측하지 않고 수동 설정을 안내한다.
        /// </summary>
        private static void EnableNetworkSceneManagement()
        {
            NetworkManager networkManager = Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogError("[CheonMyeong] NetworkManager를 찾지 못했습니다 - " +
                                "먼저 'CheonMyeong/Setup/Create Network Bootstrap'을 실행해주세요.");
                return;
            }

            var so = new SerializedObject(networkManager);
            var networkConfigProp = so.FindProperty("NetworkConfig");
            var enableSceneManagementProp = networkConfigProp?.FindPropertyRelative("EnableSceneManagement");
            if (enableSceneManagementProp == null)
            {
                Debug.LogError("[CheonMyeong] NetworkManager.NetworkConfig에서 'EnableSceneManagement' 필드를 " +
                                "찾지 못했습니다(NGO 버전이 달라졌을 수 있음 - 확인 못 함) - Inspector에서 " +
                                "NetworkManager의 'Enable Scene Management' 체크박스를 직접 켜주세요.");
                return;
            }

            enableSceneManagementProp.boolValue = true;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(networkManager);
        }

        private static void EnsureGameplaySceneLoaderComponent()
        {
            SessionManager sessionManager = Object.FindFirstObjectByType<SessionManager>();
            if (sessionManager == null)
            {
                Debug.LogError("[CheonMyeong] SessionManager를 찾지 못했습니다 - " +
                                "먼저 'CheonMyeong/Setup/Create Network Bootstrap'을 실행해주세요.");
                return;
            }

            GameObject bootstrapGo = sessionManager.gameObject;
            if (bootstrapGo.GetComponent<GameplaySceneLoader>() == null)
            {
                bootstrapGo.AddComponent<GameplaySceneLoader>();
            }
            EditorUtility.SetDirty(bootstrapGo);
        }

        private static MapCatalogSO EnsureMapCatalog(string firstGameplaySceneName)
        {
            EnsureFolder(MapDataFolder);
            string path = UICanvasBuilder.MapCatalogAssetPath;

            MapCatalogSO catalog = AssetDatabase.LoadAssetAtPath<MapCatalogSO>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MapCatalogSO>();
                catalog.maps = new[]
                {
                    new MapCatalogSO.MapEntry
                    {
                        mapId = "dalbam_pyejangteo",
                        displayName = "달밤의 폐장터",
                        gameplaySceneName = firstGameplaySceneName,
                        bossCaption = "보스·억쇠 | 10웨이브",
                        isUnlocked = true
                    },
                    new MapCatalogSO.MapEntry { mapId = "map_locked_1", displayName = "추후 공개", gameplaySceneName = "", bossCaption = "", isUnlocked = false },
                    new MapCatalogSO.MapEntry { mapId = "map_locked_2", displayName = "추후 공개", gameplaySceneName = "", bossCaption = "", isUnlocked = false },
                    new MapCatalogSO.MapEntry { mapId = "map_locked_3", displayName = "추후 공개", gameplaySceneName = "", bossCaption = "", isUnlocked = false },
                };
                AssetDatabase.CreateAsset(catalog, path);
            }
            else if (catalog.maps != null && catalog.maps.Length > 0 && string.IsNullOrEmpty(catalog.maps[0].gameplaySceneName))
            {
                // 이미 있던 자산인데 첫 맵의 씬 이름이 비어있으면(수동으로 만들어둔 경우 등) 채워준다.
                catalog.maps[0].gameplaySceneName = firstGameplaySceneName;
                EditorUtility.SetDirty(catalog);
            }

            return catalog;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string folderName = Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
