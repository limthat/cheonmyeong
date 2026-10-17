using System.IO;
using CheonMyeong.Data;
using CheonMyeong.Networking;
using CheonMyeong.UI;
using CheonMyeong.UI.Screens;
using CheonMyeong.UI.Title;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 기획 확정(261013) - "UI 전반 구현"(1순위) 작업용 에디터 셋업 스크립트.
    /// wireframe-reference_261013.md의 12개 화면(타이틀 제외) 전부를 레이아웃
    /// 뼈대 수준으로 한 번에 생성한다. CheonMyeongPrototypeSetup.cs와 같은 관례
    /// (SerializedObject 기반 AssignPrivateField로 참조만 연결, 클릭 로직은
    /// 각 화면 컴포넌트 자신의 Awake에서 처리)를 그대로 따른다.
    ///
    /// 이 프로젝트는 New Input System만 쓰므로(PlayerInputReader, TitleDragCutController
    /// 등 전부 Mouse.current/Keyboard.current 직접 폴링 방식) EventSystem은 레거시
    /// StandaloneInputModule이 아니라 InputSystemUIInputModule을 써야 버튼 클릭이
    /// 실제로 들어온다 - com.unity.inputsystem 1.19.0 확인됨(manifest.json).
    ///
    /// TextMeshPro는 Essential Resources가 프로젝트에 import돼 있지 않아서(확인됨,
    /// Assets/ 밑에 "TextMesh Pro" 폴더 없음) 전부 레거시 UnityEngine.UI.Text +
    /// Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")로 만든다 -
    /// 별도 임포트 없이 즉시 렌더링됨을 보장하기 위함.
    ///
    /// 재실행하면 각 화면 GameObject를 통째로 지우고 새로 만든다(멱등) - 수치/문구를
    /// 손으로 고쳐뒀다면 재실행 전에 백업해둘 것.
    ///
    /// 스킬트리 노드 밸런스 / 원한 게이지 수치 / 증강 희귀도 체계는 팀 확정 전이라
    /// (wireframe-reference_261013.md 섹션 3) 해당 화면은 레이아웃 자리만 잡아두고
    /// 실제 수치·로직은 만들지 않는다 - 임의로 정하지 않는다는 프로젝트 규칙을 따름.
    /// </summary>
    public static class UICanvasBuilder
    {
        private const string CanvasObjectName = "UICanvas";
        private const string ScreensRootObjectName = "Screens";
        private const string SessionManagerObjectName = "SessionManager";
        private const string TitleTalismansObjectName = "TitleTalismans";
        private const string TitleBridgeObjectName = "TitleToLobbyBridge";

        private static readonly Color PanelColorLobby = new Color(0.08f, 0.10f, 0.16f, 1f);
        private static readonly Color PanelColorCharSkillTree = new Color(0.10f, 0.08f, 0.14f, 1f);
        private static readonly Color PanelColorStoryStorage = new Color(0.12f, 0.10f, 0.08f, 1f);
        private static readonly Color PanelColorChoiceMap = new Color(0.08f, 0.12f, 0.14f, 1f);
        private static readonly Color PanelColorWaitRoom = new Color(0.10f, 0.12f, 0.10f, 1f);
        private static readonly Color PanelColorLoading = new Color(0.05f, 0.05f, 0.07f, 1f);
        private static readonly Color PanelColorMapStory = new Color(0f, 0f, 0f, 1f);
        private static readonly Color PanelColorInGame = new Color(0f, 0f, 0f, 0f); // HUD는 투명 - 뒤에 게임 화면이 보여야 함
        private static readonly Color PanelColorUpgrade = new Color(0.02f, 0.02f, 0.03f, 0.85f);
        private static readonly Color PanelColorReward = new Color(0.10f, 0.09f, 0.06f, 1f);
        private static readonly Color PanelColorSettings = new Color(0.09f, 0.09f, 0.11f, 1f);
        private static readonly Color PanelColorEscMenu = new Color(0f, 0f, 0f, 0.6f); // 반투명 오버레이

        [MenuItem("CheonMyeong/Setup/Build Full UI Screens (Skeleton)")]
        public static void BuildFullUi()
        {
            EnsureEventSystem();
            Canvas canvas = EnsureCanvas();
            Transform screensRoot = EnsureScreensRoot(canvas.transform);
            EnsureFlowManager(canvas.gameObject, screensRoot);
            SessionManager sessionManager = EnsureSessionManager();

            BuildLobbyScreen(screensRoot);
            BuildCharSkillTreeScreen(screensRoot);
            BuildStoryStorageScreen(screensRoot);
            BuildChoiceMapScreen(screensRoot, sessionManager);
            BuildWaitRoomScreen(screensRoot, sessionManager);
            BuildLoadingScreen(screensRoot);
            BuildMapStoryScreen(screensRoot);
            BuildInGameHudScreen(screensRoot);
            BuildUpgradeScreen(screensRoot);
            BuildRewardScreen(screensRoot);
            BuildSettingsScreen(screensRoot);
            BuildEscMenuScreen(screensRoot);

            BuildTitleBridge();

            EditorUtility.SetDirty(canvas.gameObject);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;

            Debug.Log("[CheonMyeong] UI 화면 12개(뼈대, 타이틀 제외) 생성/갱신 완료 - " +
                      "스킬트리 노드 밸런스/원한 게이지/증강 희귀도는 팀 확정 전까지 레이아웃만 있음.");
        }

        // ----- 공통 인프라 (EventSystem / Canvas / Screens / UIFlowManager / SessionManager) -----

        private static void EnsureEventSystem()
        {
            EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
            GameObject eventSystemGo = existing != null ? existing.gameObject : new GameObject("EventSystem", typeof(EventSystem));

            StandaloneInputModule legacyModule = eventSystemGo.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                // 이 프로젝트는 New Input System만 쓴다 - 레거시 모듈이 남아있으면
                // InputSystemUIInputModule과 충돌 경고가 나거나 클릭이 안 먹을 수 있다.
                Object.DestroyImmediate(legacyModule);
            }

            if (eventSystemGo.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystemGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static Canvas EnsureCanvas()
        {
            GameObject canvasGo = GameObject.Find(CanvasObjectName);
            if (canvasGo == null)
            {
                canvasGo = new GameObject(CanvasObjectName);
            }

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasGo.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasGo.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
            {
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            return canvas;
        }

        private static Transform EnsureScreensRoot(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find(ScreensRootObjectName);
            if (existing != null)
            {
                return existing;
            }

            RectTransform rt = NewRect(canvasTransform, ScreensRootObjectName, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return rt;
        }

        private static void EnsureFlowManager(GameObject canvasGo, Transform screensRoot)
        {
            UIFlowManager flowManager = canvasGo.GetComponent<UIFlowManager>();
            if (flowManager == null)
            {
                flowManager = canvasGo.AddComponent<UIFlowManager>();
            }
            AssignPrivateField(flowManager, "screensRoot", screensRoot);
        }

        private static SessionManager EnsureSessionManager()
        {
            SessionManager existing = Object.FindFirstObjectByType<SessionManager>();
            if (existing != null)
            {
                return existing;
            }
            GameObject go = new GameObject(SessionManagerObjectName);
            return go.AddComponent<SessionManager>();
        }

        private static void BuildTitleBridge()
        {
            GameObject talismanParent = GameObject.Find(TitleTalismansObjectName);
            TitleTalisman start = FindTalisman(talismanParent, "Start");
            TitleTalisman settings = FindTalisman(talismanParent, "Settings");
            TitleTalisman exit = FindTalisman(talismanParent, "Exit");

            GameObject bridgeGo = GameObject.Find(TitleBridgeObjectName);
            if (bridgeGo == null)
            {
                bridgeGo = new GameObject(TitleBridgeObjectName);
            }

            TitleToLobbyBridge bridge = bridgeGo.GetComponent<TitleToLobbyBridge>();
            if (bridge == null)
            {
                bridge = bridgeGo.AddComponent<TitleToLobbyBridge>();
            }

            AssignPrivateField(bridge, "startTalisman", start);
            AssignPrivateField(bridge, "settingsTalisman", settings);
            AssignPrivateField(bridge, "exitTalisman", exit);
            AssignPrivateField(bridge, "titleRoot", talismanParent);

            if (start == null || settings == null || exit == null)
            {
                Debug.LogWarning("[CheonMyeong] 타이틀 부적(Start/Settings/Exit)을 찾지 못했습니다 - " +
                                 "먼저 'CheonMyeong/Setup/Create Title Talisman Prototype (Placeholder)'를 실행해주세요.");
            }
        }

        private static TitleTalisman FindTalisman(GameObject parent, string prefix)
        {
            if (parent == null)
            {
                return null;
            }
            Transform child = parent.transform.Find(prefix + "_Talisman");
            return child != null ? child.GetComponent<TitleTalisman>() : null;
        }

        // ----- 화면별 빌드 -----

        private static void BuildLobbyScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.Lobby, PanelColorLobby);

            Text playerInfoText = CreateLabel(panel, "PlayerInfoText", "Lv.-- · 하영",
                18, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0.4f, 1f), new Vector2(24f, -70f), new Vector2(-16f, -24f));
            Text goldText = CreateLabel(panel, "GoldText", "엽전 --",
                18, TextAnchor.MiddleRight, new Vector2(0.6f, 1f), new Vector2(1f, 1f), new Vector2(16f, -70f), new Vector2(-120f, -24f));
            Button settingsButton = CreateButton(panel, "SettingsButton", "설정",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-116f, -70f), new Vector2(-16f, -24f));

            CreateLabel(panel, "CenterLabel", "로비 마당 (캐릭터 placeholder)",
                20, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.35f), new Vector2(0.7f, 0.75f), Vector2.zero, Vector2.zero);

            RectTransform skillTreePanel = NewRect(panel, "SkillTreePanel", new Vector2(0f, 0.2f), new Vector2(0.22f, 0.8f), new Vector2(24f, 0f), new Vector2(0f, 0f));
            AddImage(skillTreePanel, new Color(0.14f, 0.12f, 0.2f, 0.9f));
            CreateLabel(skillTreePanel, "Title", "스킬트리", 20, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(0f, -40f), new Vector2(0f, 0f));
            Button skillTreeButton = CreateButton(skillTreePanel, "OpenButton", "열기", new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.25f), Vector2.zero, Vector2.zero);

            RectTransform storyPanel = NewRect(panel, "StoryStoragePanel", new Vector2(0.78f, 0.2f), new Vector2(1f, 0.8f), new Vector2(0f, 0f), new Vector2(-24f, 0f));
            AddImage(storyPanel, new Color(0.2f, 0.16f, 0.1f, 0.9f));
            CreateLabel(storyPanel, "Title", "스토리 보관함", 20, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(0f, -40f), new Vector2(0f, 0f));
            Button storyStorageButton = CreateButton(storyPanel, "OpenButton", "열기", new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.25f), Vector2.zero, Vector2.zero);

            Button sortieButton = CreateButton(panel, "SortieButton", "출격",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-140f, 24f), new Vector2(140f, 90f));

            LobbyScreen screen = panel.gameObject.AddComponent<LobbyScreen>();
            AssignPrivateField(screen, "playerInfoText", playerInfoText);
            AssignPrivateField(screen, "goldText", goldText);
            AssignPrivateField(screen, "sortieButton", sortieButton);
            AssignPrivateField(screen, "skillTreeButton", skillTreeButton);
            AssignPrivateField(screen, "storyStorageButton", storyStorageButton);
            AssignPrivateField(screen, "settingsButton", settingsButton);

            panel.gameObject.SetActive(false);
        }

        private static readonly string[] RosterLabels =
        {
            "하영", "한주원 (잠김)", "박규혁 (잠김)", "이아라 (잠김)",
            "최환조 (잠김)", "신지현 (잠김)", "임훈 (잠김)", "심여레 (잠김)"
        };

        private static void BuildCharSkillTreeScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.CharSkillTree, PanelColorCharSkillTree);

            Button backButton = CreateButton(panel, "BackButton", "← 뒤로", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(140f, -24f));

            RectTransform rosterContainer = CreateVerticalList(panel, "RosterContainer", new Vector2(0f, 0f), new Vector2(0.26f, 0.88f), new Vector2(16f, 0f), new Vector2(0f, 0f), RosterLabels, 44f);

            Text previewNameText = CreateLabel(panel, "PreviewNameText", "하영 · 사인검",
                26, TextAnchor.UpperCenter, new Vector2(0.3f, 0.65f), new Vector2(0.72f, 0.88f), Vector2.zero, Vector2.zero);
            Text previewDescText = CreateLabel(panel, "PreviewDescText", "근접·다수 | 패시브: 동료를 지키는 결계",
                18, TextAnchor.UpperCenter, new Vector2(0.3f, 0.52f), new Vector2(0.72f, 0.64f), Vector2.zero, Vector2.zero);
            CreateLabel(panel, "PreviewPlaceholder", "캐릭터 대기 모션 (placeholder)",
                18, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.2f), new Vector2(0.72f, 0.5f), Vector2.zero, Vector2.zero);

            RectTransform skillTreePanel = NewRect(panel, "SkillTreeContainer", new Vector2(0.74f, 0f), new Vector2(1f, 0.88f), new Vector2(0f, 0f), new Vector2(-16f, 0f));
            AddImage(skillTreePanel, new Color(0.08f, 0.07f, 0.12f, 0.9f));
            CreateLabel(skillTreePanel, "Caption", "스킬트리 노드 그래프\n(구조 미정 - 팀 확정 후 구현,\n레이아웃 자리만 확보)",
                16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            CharSkillTreeScreen screen = panel.gameObject.AddComponent<CharSkillTreeScreen>();
            AssignPrivateField(screen, "backButton", backButton);
            AssignPrivateField(screen, "rosterContainer", rosterContainer);
            AssignPrivateField(screen, "previewNameText", previewNameText);
            AssignPrivateField(screen, "previewDescText", previewDescText);

            panel.gameObject.SetActive(false);
        }

        private static readonly string[] StorySlotLabels =
        {
            "제1화 · 억쇠", "봉인하면 열림", "봉인하면 열림", "봉인하면 열림", "봉인하면 열림", "봉인하면 열림"
        };

        private static void BuildStoryStorageScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.StoryStorage, PanelColorStoryStorage);

            Button backButton = CreateButton(panel, "BackButton", "← 뒤로", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(140f, -24f));

            RectTransform slotsContainer = CreateGridList(panel, "SlotsContainer", new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.85f), Vector2.zero, Vector2.zero, StorySlotLabels, 3);

            StoryStorageScreen screen = panel.gameObject.AddComponent<StoryStorageScreen>();
            AssignPrivateField(screen, "backButton", backButton);
            AssignPrivateField(screen, "slotsContainer", slotsContainer);

            panel.gameObject.SetActive(false);
        }

        /// <summary>GameplaySceneSplitSetup.cs가 만드는 맵 목록 자산 경로 - 두 에디터 스크립트가 공유.</summary>
        public const string MapCatalogAssetPath = "Assets/Data/Maps/MapCatalog.asset";

        private static void BuildChoiceMapScreen(Transform screensRoot, SessionManager sessionManager)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.ChoiceMap, PanelColorChoiceMap);

            Button backButton = CreateButton(panel, "BackButton", "← 뒤로", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(140f, -24f));

            // 261014 2차 수정 - "선택된 맵과 캐릭터는 별도로 표기. 미정일 경우 미정으로 표기"
            // (예섭 요청) - 화면 상단, 뒤로 버튼과 같은 가로줄에 두 줄짜리 요약 표시. 뒤로
            // 버튼(x 16~140px)과 안 겹치게 x 160px부터 시작(anchor 비율 대신 픽셀 오프셋을
            // 쓴 이유 - 해상도가 달라져도 버튼 폭 자체는 고정 픽셀이라 겹침 없이 안전).
            Text selectedMapText = CreateLabel(panel, "SelectedMapText", "선택된 맵: 미정", 16, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(160f, -70f), new Vector2(0f, -24f));
            Text selectedCharacterText = CreateLabel(panel, "SelectedCharacterText", "선택된 캐릭터: 미정", 16, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), new Vector2(-16f, -24f));

            // 261014 수정 - "캐릭터 선택과 맵선택을 같은 화면에서"(예섭 요청) 반영. 기존
            // 좌(맵 0.05~0.55)/우(친구 패널 0.6~0.95) 2분할을 좌(맵)/가운데(캐릭터)/우(친구
            // 패널) 3분할로 재배치. 각 폭을 줄였을 뿐 레이아웃 구성 방식 자체는 그대로.
            MapCatalogSO mapCatalog = AssetDatabase.LoadAssetAtPath<MapCatalogSO>(MapCatalogAssetPath);
            string[] mapTileLabels = BuildMapTileLabels(mapCatalog);
            RectTransform mapTilesContainer = CreateGridList(panel, "MapTilesContainer", new Vector2(0.03f, 0.15f), new Vector2(0.40f, 0.85f), Vector2.zero, Vector2.zero, mapTileLabels, 2);

            CharacterCatalogSO characterCatalog = EnsureCharacterCatalog();
            string[] characterTileLabels = BuildCharacterTileLabels(characterCatalog);
            RectTransform characterTilesContainer = CreateVerticalList(panel, "CharacterTilesContainer", new Vector2(0.42f, 0.15f), new Vector2(0.60f, 0.85f), Vector2.zero, Vector2.zero, characterTileLabels, 40f);

            RectTransform friendPanel = NewRect(panel, "FriendPanel", new Vector2(0.62f, 0.15f), new Vector2(0.97f, 0.85f), Vector2.zero, Vector2.zero);
            AddImage(friendPanel, new Color(0.08f, 0.1f, 0.12f, 0.9f));
            CreateLabel(friendPanel, "Title", "친구와 함께", 20, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(0f, -40f), new Vector2(0f, 0f));
            Button createRoomButton = CreateButton(friendPanel, "CreateRoomButton", "방 만들기 (방장)", new Vector2(0.1f, 0.6f), new Vector2(0.9f, 0.78f), Vector2.zero, Vector2.zero);
            InputField joinCodeInputField = CreateInputField(friendPanel, "JoinCodeInputField", new Vector2(0.1f, 0.4f), new Vector2(0.65f, 0.55f), Vector2.zero, Vector2.zero, "초대 코드 입력");
            Button joinButton = CreateButton(friendPanel, "JoinButton", "참가", new Vector2(0.68f, 0.4f), new Vector2(0.9f, 0.55f), Vector2.zero, Vector2.zero);
            Text statusText = CreateLabel(friendPanel, "StatusText", "맵을 선택해주세요 / 둘이 같은 캐릭터도 선택 가능",
                14, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.3f), Vector2.zero, Vector2.zero);

            ChoiceMapScreen screen = panel.gameObject.AddComponent<ChoiceMapScreen>();
            AssignPrivateField(screen, "sessionManager", sessionManager);
            AssignPrivateField(screen, "mapCatalog", mapCatalog);
            AssignPrivateField(screen, "mapTilesContainer", mapTilesContainer);
            AssignPrivateField(screen, "characterCatalog", characterCatalog);
            AssignPrivateField(screen, "characterTilesContainer", characterTilesContainer);
            AssignPrivateField(screen, "backButton", backButton);
            AssignPrivateField(screen, "createRoomButton", createRoomButton);
            AssignPrivateField(screen, "joinCodeInputField", joinCodeInputField);
            AssignPrivateField(screen, "joinButton", joinButton);
            AssignPrivateField(screen, "statusText", statusText);
            AssignPrivateField(screen, "selectedMapText", selectedMapText);
            AssignPrivateField(screen, "selectedCharacterText", selectedCharacterText);

            if (mapCatalog == null)
            {
                Debug.LogWarning("[CheonMyeong] MapCatalogSO를 못 찾았습니다(" + MapCatalogAssetPath + ") - " +
                                 "먼저 'CheonMyeong/Setup/Split Into Bootstrap + Gameplay Scenes'를 실행해주세요. " +
                                 "지금은 맵 타일이 전부 '추후 공개' placeholder로만 뜹니다.");
            }

            panel.gameObject.SetActive(false);
        }

        /// <summary>맵 카탈로그가 아직 없으면(씬 분리 셋업 전) 예전처럼 4칸 placeholder로 대체 - 빌드가 죽지 않게.</summary>
        private static string[] BuildMapTileLabels(MapCatalogSO mapCatalog)
        {
            if (mapCatalog == null || mapCatalog.maps == null || mapCatalog.maps.Length == 0)
            {
                return new[] { "추후 공개", "추후 공개", "추후 공개", "추후 공개" };
            }

            string[] labels = new string[mapCatalog.maps.Length];
            for (int i = 0; i < mapCatalog.maps.Length; i++)
            {
                MapCatalogSO.MapEntry entry = mapCatalog.maps[i];
                labels[i] = entry != null && entry.isUnlocked && !string.IsNullOrEmpty(entry.gameplaySceneName)
                    ? $"{entry.displayName}\n({entry.bossCaption})"
                    : "추후 공개";
            }
            return labels;
        }

        private const string CharacterDataFolder = "Assets/Data/Characters";
        private const string CharacterCatalogAssetPath = "Assets/Data/Characters/CharacterCatalog.asset";

        /// <summary>
        /// 261014 추가 - MapCatalogSO/GameplaySceneSplitSetup.EnsureMapCatalog와 완전히 같은
        /// 관례: 자산이 없으면 이미 확정된 로스터(wireframe-reference_261013.md 섹션 3-1 -
        /// 하영만 해금, 나머지 7명은 이름만 있는 추후 개발)로 자동 생성해서, 매번 손으로
        /// ScriptableObject를 만들고 8줄을 채워 넣을 필요가 없게 한다. 이미 자산이 있으면
        /// 그대로 재사용(손으로 고친 값을 덮어쓰지 않음).
        /// </summary>
        private static CharacterCatalogSO EnsureCharacterCatalog()
        {
            EnsureFolder(CharacterDataFolder);

            CharacterCatalogSO catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalogSO>(CharacterCatalogAssetPath);
            if (catalog != null)
            {
                return catalog;
            }

            catalog = ScriptableObject.CreateInstance<CharacterCatalogSO>();
            catalog.characters = new[]
            {
                new CharacterCatalogSO.CharacterEntry { characterId = "hayoung", displayName = "하영", weaponCaption = "사인검 · 근접 · 다수", isUnlocked = true },
                new CharacterCatalogSO.CharacterEntry { characterId = "hanjuwon", displayName = "한주원", weaponCaption = "부적 · 원거리 · 단일 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "parkgyuhyeok", displayName = "박규혁", weaponCaption = "삼지창 · 근접 · 단일 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "leeara", displayName = "이아라", weaponCaption = "신칼 · 원거리 · 단일 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "choihwanjo", displayName = "최환조", weaponCaption = "활 · 원거리 · 단일 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "sinjihyeon", displayName = "신지현", weaponCaption = "곡도 · 근접 · 다수 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "imhun", displayName = "임훈", weaponCaption = "창 · 근접 · 단일 (추후 개발)", isUnlocked = false },
                new CharacterCatalogSO.CharacterEntry { characterId = "simyeore", displayName = "심여레", weaponCaption = "부채 · 원거리 · 다수 · 넉백 (추후 개발)", isUnlocked = false },
            };
            AssetDatabase.CreateAsset(catalog, CharacterCatalogAssetPath);
            return catalog;
        }

        /// <summary>캐릭터 카탈로그가 비어있으면(이론상 거의 안 생김 - EnsureCharacterCatalog가 항상 채움)
        /// 빌드가 죽지 않도록 placeholder 1개로 대체.</summary>
        private static string[] BuildCharacterTileLabels(CharacterCatalogSO characterCatalog)
        {
            if (characterCatalog == null || characterCatalog.characters == null || characterCatalog.characters.Length == 0)
            {
                return new[] { "하영" };
            }

            string[] labels = new string[characterCatalog.characters.Length];
            for (int i = 0; i < characterCatalog.characters.Length; i++)
            {
                CharacterCatalogSO.CharacterEntry entry = characterCatalog.characters[i];
                labels[i] = entry != null && entry.isUnlocked
                    ? entry.displayName
                    : $"{entry?.displayName} (잠김)";
            }
            return labels;
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

        private static void BuildWaitRoomScreen(Transform screensRoot, SessionManager sessionManager)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.WaitRoom, PanelColorWaitRoom);

            Button backButton = CreateButton(panel, "BackButton", "← 뒤로", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(140f, -24f));

            CreateLabel(panel, "Player1Label", "PLAYER 1 · 하영\n(방장)", 16, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.4f), new Vector2(0.25f, 0.8f), Vector2.zero, Vector2.zero);
            CreateLabel(panel, "Player2Label", "PLAYER 2 · 하영\n(준비 완료)", 16, TextAnchor.MiddleCenter, new Vector2(0.75f, 0.4f), new Vector2(0.95f, 0.8f), Vector2.zero, Vector2.zero);

            RectTransform roomInfoPanel = NewRect(panel, "RoomInfoPanel", new Vector2(0.3f, 0.25f), new Vector2(0.7f, 0.85f), Vector2.zero, Vector2.zero);
            AddImage(roomInfoPanel, new Color(0.08f, 0.1f, 0.08f, 0.9f));
            CreateLabel(roomInfoPanel, "MapLabel", "선택 맵 · 달밤의 폐장터", 16, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(0f, -32f), Vector2.zero);
            Text joinCodeText = CreateLabel(roomInfoPanel, "JoinCodeText", "초대 코드: --", 18, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f), Vector2.zero, Vector2.zero);
            Button startButton = CreateButton(roomInfoPanel, "StartButton", "START (방장) / READY (참가자)", new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.3f), Vector2.zero, Vector2.zero);
            // 261014 추가 - "맵 선택 안 하면 시작 안 되게, 버튼 누르면 문구 뜨게"(예섭 요청) -
            // WaitRoomScreen.OnStartPressed가 맵 미선택 시 띄울 안내 문구 자리.
            Text statusText = CreateLabel(roomInfoPanel, "StatusText", "", 14, TextAnchor.LowerCenter, new Vector2(0.05f, 0f), new Vector2(0.95f, 0.08f), Vector2.zero, Vector2.zero);

            WaitRoomScreen screen = panel.gameObject.AddComponent<WaitRoomScreen>();
            AssignPrivateField(screen, "sessionManager", sessionManager);
            AssignPrivateField(screen, "joinCodeText", joinCodeText);
            AssignPrivateField(screen, "statusText", statusText);
            AssignPrivateField(screen, "backButton", backButton);
            AssignPrivateField(screen, "startButton", startButton);

            panel.gameObject.SetActive(false);
        }

        private static void BuildLoadingScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.Loading, PanelColorLoading);

            CreateLabel(panel, "LogoText", "천명", 48, TextAnchor.MiddleCenter, new Vector2(0.35f, 0.4f), new Vector2(0.65f, 0.7f), Vector2.zero, Vector2.zero);
            Text tipText = CreateLabel(panel, "TipText", "불러오는 중...", 16, TextAnchor.LowerLeft, new Vector2(0.04f, 0.04f), new Vector2(0.7f, 0.16f), Vector2.zero, Vector2.zero);
            Text progressText = CreateLabel(panel, "ProgressText", "불러오는 중... 0%", 16, TextAnchor.LowerRight, new Vector2(0.7f, 0.04f), new Vector2(0.96f, 0.16f), Vector2.zero, Vector2.zero);

            LoadingScreen screen = panel.gameObject.AddComponent<LoadingScreen>();
            AssignPrivateField(screen, "tipText", tipText);
            AssignPrivateField(screen, "progressText", progressText);

            panel.gameObject.SetActive(false);
        }

        private static void BuildMapStoryScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.MapStory, PanelColorMapStory);

            CreateLabel(panel, "IllustrationPlaceholder", "컷신 일러스트 영역\n(placeholder) - 예: 당산나무 아래 억쇠 등장",
                18, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.9f), Vector2.zero, Vector2.zero);

            RectTransform dialogueBox = NewRect(panel, "DialogueBox", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.35f), Vector2.zero, Vector2.zero);
            AddImage(dialogueBox, new Color(0.05f, 0.05f, 0.05f, 0.85f));
            Text speakerNameText = CreateLabel(dialogueBox, "SpeakerNameText", "억쇠", 18, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(16f, -16f), new Vector2(-16f, -16f));
            Text dialogueText = CreateLabel(dialogueBox, "DialogueText", "한 맺힌 것들이 이렇게나 많은데… 오늘 밤도 배가 부르겠구나.",
                18, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(16f, 8f), new Vector2(-16f, -48f));
            Text skipHintText = CreateLabel(dialogueBox, "SkipHintText", "두 명 모두 눌러야 넘어감 · Space 길게 눌러 건너뛰기 0/2",
                12, TextAnchor.LowerRight, Vector2.zero, Vector2.one, new Vector2(16f, 4f), new Vector2(-16f, 4f));
            Button continueButton = CreateButton(panel, "ContinueButton", "계속하기 (Space)", new Vector2(0.78f, 0.37f), new Vector2(0.96f, 0.45f), Vector2.zero, Vector2.zero);

            MapStoryScreen screen = panel.gameObject.AddComponent<MapStoryScreen>();
            AssignPrivateField(screen, "speakerNameText", speakerNameText);
            AssignPrivateField(screen, "dialogueText", dialogueText);
            AssignPrivateField(screen, "skipHintText", skipHintText);
            AssignPrivateField(screen, "continueButton", continueButton);

            panel.gameObject.SetActive(false);
        }

        // 원한 4단계 색상 - WireFrame.pdf 원본(천명 · UI 와이어프레임, INGAME·인게임 HUD
        // 프레임)에서 그대로 스포이트한 참고값. 수치/효과 로직은 여전히 미정이라 이 색은
        // 순수 정적 레이아웃 장식일 뿐, InGameHudScreen.cs 쪽에서 실제로 채우지 않는다.
        private static readonly Color GrudgeColorClear = new Color(0.55f, 0.85f, 0.95f, 1f);   // 맑음
        private static readonly Color GrudgeColorDeep = new Color(0.20f, 0.40f, 0.75f, 1f);    // 짙음
        private static readonly Color GrudgeColorHan = new Color(0.28f, 0.14f, 0.38f, 1f);     // 한
        private static readonly Color GrudgeColorRampage = new Color(0.55f, 0.08f, 0.12f, 1f); // 폭주

        // 261013 3차 수정 - "다른 뱀서류 게임들 찾아보고 최대한 이쁘게... 어두운 배경이랑
        // 잘 어우러지고" 요청 반영. ui_panel_fill.png/ui_panel_ring.png(Assets/img/ui/,
        // Pillow로 생성한 128x128 둥근 모서리 9-slice 스프라이트, spriteBorder 32/32/32/32)
        // 위에 올릴 색 팔레트 - 기획서 섹션 8 아트 방향("밤의 어두운 톤 위에 무당의 오색과
        // 초롱 불빛", "플레이어는 따뜻한 색, 원귀는 차가운 색")을 그대로 따라 카드 배경은
        // 거의 검정에 가까운 반투명 남색/차콜, 테두리는 따뜻한 금색(초롱 불빛), 텍스트는
        // 순백 대신 살짝 따뜻한 오프화이트로 잡았다. P1=따뜻한 적색/P2=차가운 청색 구분은
        // 기존 색 그대로 유지(이미 그 모티프를 따르고 있었음).
        private static readonly Color HudPanelBgColor = new Color(0.06f, 0.06f, 0.09f, 0.82f);
        private static readonly Color HudPanelRingColor = new Color(0.80f, 0.65f, 0.34f, 0.95f);
        private static readonly Color HudTextWarmWhite = new Color(0.96f, 0.93f, 0.85f, 1f);
        // 261014 수정 - 체력/경험치 바가 다 안 찼을 때 남는 "빈 칸" 색이 거의 순검정(0.10,0.09,0.10)에
        // 가까워서 "가장자리가 시꺼멓게 비어 보인다"는 피드백(예섭). 카드 배경보다 한 톤 밝은
        // 중성 회색조로 바꿔서 "빈 게이지 홈"처럼 보이게 했다 - 완전히 안 보이게(투명) 하지 않은
        // 이유는 그러면 바가 꽉 찼을 때랑 비었을 때 테두리/카드 배경과 구분이 안 가기 때문.
        private static readonly Color HudBarTrackColor = new Color(0.30f, 0.29f, 0.33f, 0.55f);
        private static readonly Color HudBarHighlightColor = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color HudP1BarColor = new Color(0.84f, 0.27f, 0.22f, 1f);
        private static readonly Color HudP2BarColor = new Color(0.32f, 0.58f, 0.88f, 1f);

        // 261013 4차 수정 - 경험치 바 색(체력 바의 적/청과 구분되는 따뜻한 금록색 - 레벨업
        // 연출과 어울리게 HudPanelRingColor 금색 계열에 가깝게 잡음).
        private static readonly Color HudExpBarColor = new Color(0.70f, 0.82f, 0.35f, 1f);

        private const string UiPanelFillSpritePath = "Assets/img/ui/ui_panel_fill.png";
        private const string UiPanelRingSpritePath = "Assets/img/ui/ui_panel_ring.png";

        private static void BuildInGameHudScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.InGame, PanelColorInGame);

            // 261013 1차 수정 - 아래 라벨들이 전부 "화면에 아예 안 보임" 버그였던 원인:
            // 상단에 고정 픽셀 높이로 붙이는 top-anchored 라벨(anchorMin.y == anchorMax.y == 1)은
            // Unity RectTransform 규칙상 실제 높이가 (offsetMax.y - offsetMin.y)로만 정해지는데,
            // offsetMin/offsetMax의 y값이 거꾸로 들어가 있어서(더 작은 값이 offsetMin이 아니라
            // offsetMax 쪽에 가 있었음) 높이가 음수로 계산됐었다 - 음수 높이 Rect는 캔버스가
            // 그릴 영역이 없다고 보고 렌더링을 건너뛴다. 같은 파일의 BuildLobbyScreen 등
            // 정상 동작하는 라벨들의 순서(offsetMin.y가 더 음수/아래쪽, offsetMax.y가 덜
            // 음수/위쪽)를 그대로 맞춰서 수정.
            //
            // 261013 2차 수정 - 원본 WireFrame.pdf(천명 · UI 와이어프레임, INGAME·인게임
            // HUD 프레임)를 다시 열어서 대조해보니 텍스트만으로는 빠진 요소가 셋 있었다:
            // (1) 체력 바 그래픽, (2) 원한 4단계 색상 게이지, (3) 증강 6슬롯 테두리+단계 점.
            // 수치/시스템 자체는 여전히 미정이라 로직 없이 정적 레이아웃만 추가.
            //
            // 261013 3차 수정 - 카드형 레이아웃으로 재구성. CreateCardPanel/
            // CreateRoundedFillBar는 둥근 모서리 스프라이트가 아직 임포트되지 않았어도
            // (null이면) 자동으로 예전처럼 단색 사각형으로 대체되므로 HUD 빌드 자체가
            // 깨지지 않는다(LoadUiSprite 경고 로그로 원인을 알려줌). 수치/시스템 미정
            // 원칙은 그대로 - 이번에도 순수 시각 레이아웃/색/테두리/그림자만 바꿨고,
            // InGameHudScreen.cs의 SetXxx/캐싱 로직은 전혀 건드리지 않았다.
            //
            // 261013 4차 수정 - 실제 인게임 스크린샷으로 확인된 버그 3건 반영(예섭 피드백:
            // "윤곽 너무 두꺼움 / HUD가 서로 겹침 / 경험치 바 안 보임"):
            // (1) 윤곽 두께 - ui_panel_ring.png/ui_panel_fill.png의 9-slice spriteBorder가
            //     32px였는데 실제 그려진 테두리 선은 10px 안팎이었다. Unity의 Sliced Image는
            //     카드 높이가 border*2(64px)보다 작으면 border를 비례로 줄여서 그리는데,
            //     체력 바(기존 높이 24px)/골드 카드(기존 높이 48px)/증강 슬롯(기존 너비
            //     64px)처럼 border보다 작은 요소에서는 그 축소된 "링"이 안쪽 내용을 거의
            //     다 잡아먹어서 가늘어야 할 테두리가 두꺼운 덩어리로 보였다 - 스프라이트
            //     자체를 더 얇은 선(4px)·작은 모서리 반경(16px)으로 다시 그리고 spriteBorder도
            //     16으로 맞췄다(Assets/img/ui/ui_panel_*.png 갱신). 동시에 테두리보다 작았던
            //     요소들(체력 바/골드 카드/증강 슬롯)을 border 대비 충분히 큰 크기로 키워서
            //     같은 문제가 재발하지 않게 했다.
            // (2) P1/P2 카드 사이 간격이 4px뿐이라 그림자+테두리가 겹쳐 붙어 보였다 - 16px로
            //     벌리고, 그 아래에 있던 CenterStatusCard/GoldCard는 X축이 달라 실제 겹침은
            //     없었지만 여유 공간을 더 줬다.
            // (3) 경험치 바 자체가 없었다(레벨 숫자 텍스트만 있었음) - PartyProgress.cs가
            //     이미 들고 있는 currentExp/다음 레벨 필요 경험치를 그대로 시각화하는
            //     ExpBar를 CenterStatusCard에 추가(InGameHudScreen.SetExpProgress 참고) -
            //     레벨 디자인 수치 자체는 여전히 미정 플레이스홀더지만, "이미 존재하는 값을
            //     막대로 보여주는 것"은 새로운 밸런스를 지어내는 게 아니라서 임의 결정 규칙에
            //     어긋나지 않는다고 판단.
            // (4) 위 (2)보다 더 실질적인 "겹침" 원인을 하나 더 찾았다 - P1/P2HealthBar가
            //     anchorMin/Max를 (0,0)-(1,1)(완전 스트레치)로 두고 offsetMax.y에 양수(42)를
            //     썼는데, 완전 스트레치에서 rect의 top = anchorMax.y*parentHeight + offsetMax.y
            //     라서 offsetMax.y가 양수면 카드 "밖"(위쪽)으로 그만큼 튀어나간다(Text는
            //     배경이 없어서 TextAnchor로 글자 위치만 맞으면 티가 안 났지만, 체력 바는
            //     실제 색이 칠해진 Image라 카드 경계를 넘어 그려져 바로 위 카드/텍스트와
            //     겹쳐 보였다). anchorMin.y==anchorMax.y==0(그루지 게이지와 같은 "바닥 고정"
            //     방식)으로 바꿔서 offsetMin.y/offsetMax.y가 카드 바닥 기준 리터럴 좌표가
            //     되게 고쳤다 - 체력 바(P1/P2)와 새로 추가한 ExpBar 전부 동일하게 수정.
            // 261013 5차 수정 - 추가 피드백 반영("체력/재화 GUI가 좀 큰 감 있음" /
            // "원한 바·경험치 바는 더 늘려도 됨"): P1/P2/Gold 카드 높이를 줄이고(안의
            // 텍스트/바 오프셋은 전부 카드 높이보다 작게 잡혀 있어 그대로 둬도 깨지지 않음),
            // CenterStatusCard는 가로 폭을 넓혀서(0.30~0.70 → 0.28~0.72) 원한 게이지/경험치
            // 바가 더 길게 보이게 했다. 두 게이지 모두 가로 insets도 줄여서(여백 축소) 카드
            // 안에서 실제로 차지하는 폭을 늘렸다.
            RectTransform p1Card = CreateCardPanel(panel, "P1Card", new Vector2(0f, 1f), new Vector2(0.26f, 1f), new Vector2(16f, -96f), new Vector2(-8f, -16f));
            Text p1HealthText = CreateLabel(p1Card, "P1HealthText", "P1 --", 16, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12f, -34f), new Vector2(-12f, -10f));
            p1HealthText.color = HudTextWarmWhite;
            Image p1HealthFillImage = CreateRoundedFillBar(p1Card, "P1HealthBar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 10f), new Vector2(-12f, 42f), HudP1BarColor);

            RectTransform p2Card = CreateCardPanel(panel, "P2Card", new Vector2(0f, 1f), new Vector2(0.26f, 1f), new Vector2(16f, -192f), new Vector2(-8f, -112f));
            Text p2HealthText = CreateLabel(p2Card, "P2HealthText", "P2 --", 16, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12f, -34f), new Vector2(-12f, -10f));
            p2HealthText.color = HudTextWarmWhite;
            Image p2HealthFillImage = CreateRoundedFillBar(p2Card, "P2HealthBar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 10f), new Vector2(-12f, 42f), HudP2BarColor);

            RectTransform centerCard = CreateCardPanel(panel, "CenterStatusCard", new Vector2(0.28f, 1f), new Vector2(0.72f, 1f), new Vector2(0f, -184f), new Vector2(0f, -16f));
            Text waveTimerText = CreateLabel(centerCard, "WaveTimerText", "WAVE --/-- --:--", 20, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(8f, -40f), new Vector2(-8f, -10f));
            waveTimerText.color = HudTextWarmWhite;
            Text levelText = CreateLabel(centerCard, "LevelText", "Lv.-- · 영혼 공유", 14, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(8f, -62f), new Vector2(-8f, -42f));
            levelText.color = HudTextWarmWhite;
            Image expFillImage = CreateRoundedFillBar(centerCard, "ExpBar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 86f), new Vector2(-16f, 104f), HudExpBarColor);
            Text grudgeText = CreateLabel(centerCard, "GrudgeText", "원한 -- (보류)", 14, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(8f, -104f), new Vector2(-8f, -84f));
            grudgeText.color = HudTextWarmWhite;

            // 원한 4단계 게이지 - 색/구간 배치만 와이어프레임 그대로, 실제 구간 전환 로직은 없음.
            // 가로 insets를 0.05~0.95에서 0.02~0.98로 줄이고 세로 높이도 20→26px로 키워서
            // "더 늘려도 됨" 피드백대로 눈에 더 잘 띄게 했다.
            RectTransform grudgeGauge = NewRect(centerCard, "GrudgeGauge", new Vector2(0.02f, 0f), new Vector2(0.98f, 0f), new Vector2(0f, 22f), new Vector2(0f, 48f));
            CreateGaugeSegment(grudgeGauge, "Segment_Clear", 0f, 0.25f, GrudgeColorClear);
            CreateGaugeSegment(grudgeGauge, "Segment_Deep", 0.25f, 0.5f, GrudgeColorDeep);
            CreateGaugeSegment(grudgeGauge, "Segment_Han", 0.5f, 0.75f, GrudgeColorHan);
            CreateGaugeSegment(grudgeGauge, "Segment_Rampage", 0.75f, 1f, GrudgeColorRampage);
            CreateLabel(centerCard, "GrudgeLabel_Clear", "맑음", 9, TextAnchor.UpperCenter, new Vector2(0.02f, 0f), new Vector2(0.26f, 0f), new Vector2(0f, 6f), new Vector2(0f, 20f));
            CreateLabel(centerCard, "GrudgeLabel_Deep", "짙음", 9, TextAnchor.UpperCenter, new Vector2(0.26f, 0f), new Vector2(0.50f, 0f), new Vector2(0f, 6f), new Vector2(0f, 20f));
            CreateLabel(centerCard, "GrudgeLabel_Han", "한", 9, TextAnchor.UpperCenter, new Vector2(0.50f, 0f), new Vector2(0.74f, 0f), new Vector2(0f, 6f), new Vector2(0f, 20f));
            CreateLabel(centerCard, "GrudgeLabel_Rampage", "폭주", 9, TextAnchor.UpperCenter, new Vector2(0.74f, 0f), new Vector2(0.98f, 0f), new Vector2(0f, 6f), new Vector2(0f, 20f));

            RectTransform goldCard = CreateCardPanel(panel, "GoldCard", new Vector2(0.80f, 1f), new Vector2(1f, 1f), new Vector2(0f, -68f), new Vector2(-16f, -16f));
            Text goldText = CreateLabel(goldCard, "GoldText", "엽전 --", 16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(8f, 2f), new Vector2(-8f, -2f));
            goldText.color = HudTextWarmWhite;

            Text augmentCaption = CreateLabel(panel, "AugmentSlotsCaption", "증강 6칸 · 5단계 (증강 시스템 미정 - placeholder)",
                14, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0.5f, 0.1f), new Vector2(16f, 16f), Vector2.zero);
            augmentCaption.color = HudTextWarmWhite;

            RectTransform augmentRow = NewRect(panel, "AugmentSlotsContainer", new Vector2(0f, 0.1f), new Vector2(0.5f, 0.24f), new Vector2(16f, 4f), new Vector2(0f, -4f));
            HorizontalLayoutGroup augmentLayout = augmentRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            augmentLayout.spacing = 10f;
            augmentLayout.childControlHeight = true;
            augmentLayout.childControlWidth = true;
            augmentLayout.childForceExpandHeight = true;
            augmentLayout.childForceExpandWidth = false;
            augmentLayout.childAlignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < 6; i++)
            {
                RectTransform slot = CreateCardPanel(augmentRow, $"AugmentSlot_{i}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(0.10f, 0.09f, 0.07f, 0.88f));
                Text stageDots = CreateLabel(slot, "StageDots", "●●●●●", 12, TextAnchor.LowerCenter, Vector2.zero, Vector2.one, new Vector2(4f, 6f), new Vector2(-4f, -4f));
                stageDots.color = HudTextWarmWhite;
                LayoutElement layoutElement = slot.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = 80f;
            }

            InGameHudScreen screen = panel.gameObject.AddComponent<InGameHudScreen>();
            AssignPrivateField(screen, "p1HealthText", p1HealthText);
            AssignPrivateField(screen, "p2HealthText", p2HealthText);
            AssignPrivateField(screen, "p1HealthFillImage", p1HealthFillImage);
            AssignPrivateField(screen, "p2HealthFillImage", p2HealthFillImage);
            AssignPrivateField(screen, "waveTimerText", waveTimerText);
            AssignPrivateField(screen, "levelText", levelText);
            AssignPrivateField(screen, "expFillImage", expFillImage);
            AssignPrivateField(screen, "grudgeText", grudgeText);
            AssignPrivateField(screen, "goldText", goldText);

            panel.gameObject.SetActive(false);
        }

        /// <summary>ui_panel_fill.png/ui_panel_ring.png를 로드 - 아직 임포트 전(null)이면
        /// 경고만 남기고 호출 쪽에서 단색으로 안전하게 대체하도록 null을 그대로 돌려준다.</summary>
        private static Sprite LoadUiSprite(string assetPath)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[CheonMyeong] UI 스프라이트를 찾을 수 없습니다: {assetPath} - Sprite (2D and UI) 임포트 설정을 확인하세요. 일단 단색 사각형으로 대체합니다.");
            }
            return sprite;
        }

        /// <summary>둥근 모서리 스프라이트 배경 + 금색 링 테두리 + 그림자가 있는 카드형 패널 -
        /// "뱀서류 느낌의 예쁜 HUD" 요청(261013) 반영. 스프라이트 미임포트 시 단색 사각형으로
        /// 자동 대체(LoadUiSprite).</summary>
        private static RectTransform CreateCardPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color? bgColorOverride = null)
        {
            RectTransform card = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);

            Image bg = AddImage(card, bgColorOverride ?? HudPanelBgColor);
            Sprite fillSprite = LoadUiSprite(UiPanelFillSpritePath);
            if (fillSprite != null)
            {
                bg.sprite = fillSprite;
                bg.type = Image.Type.Sliced;
            }

            Shadow shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(2f, -3f);

            Sprite ringSprite = LoadUiSprite(UiPanelRingSpritePath);
            if (ringSprite != null)
            {
                RectTransform ringRt = NewRect(card, "Ring", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Image ring = AddImage(ringRt, HudPanelRingColor);
                ring.sprite = ringSprite;
                ring.type = Image.Type.Sliced;
                ring.raycastTarget = false;
            }

            return card;
        }

        /// <summary>카드 안에 들어가는 둥근 체력 바 - 둥근 트랙 배경 위에 Mask로 둥글게 잘라낸
        /// Image.Type.Filled 채움을 얹고, 위쪽에 살짝 밝은 광택 띠, 맨 위에 금색 링 테두리를
        /// 겹친다. Image.Type.Filled 자체는 9-slice/둥근 모서리를 지원하지 않아서(채움이
        /// 사각형으로 직선 클리핑됨) Mask로 둥근 스프라이트 모양을 덮어씌우는 방식을 썼다.</summary>
        private static Image CreateRoundedFillBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color fillColor)
        {
            RectTransform track = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Sprite fillSprite = LoadUiSprite(UiPanelFillSpritePath);

            Image trackImage = AddImage(track, HudBarTrackColor);
            if (fillSprite != null)
            {
                trackImage.sprite = fillSprite;
                trackImage.type = Image.Type.Sliced;
            }

            RectTransform maskRt = NewRect(track, "Mask", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image maskImage = AddImage(maskRt, Color.white);
            if (fillSprite != null)
            {
                maskImage.sprite = fillSprite;
                maskImage.type = Image.Type.Sliced;
            }
            Mask mask = maskRt.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform fillRt = NewRect(maskRt, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image fillImage = AddImage(fillRt, fillColor);
            // 261014 수정 - "체력 바가 안 깎인다" 버그의 진짜 원인. Unity UGUI의 Image.OnPopulateMesh는
            // sprite(source image)가 null이면 type/fillMethod/fillAmount를 전부 무시하고 그냥 꽉 찬
            // 사각형을 그리는 코드 경로(MaskableGraphic.OnPopulateMesh)로 빠진다(Image.cs 소스 확인) -
            // 즉 Type을 Filled로 설정해도 Source Image가 비어 있으면 fillAmount는 내부적으로 계속
            // 바뀌지만 화면엔 항상 "꽉 찬" 모습으로만 보인다. Inspector에서도 Source Image가
            // None(Sprite)일 땐 Image Type/Fill Amount 필드 자체가 아예 안 보이는 것도 같은 증상
            // (스프라이트 없이는 Filled 모드가 의미가 없다고 보고 에디터가 그 필드들을 숨김).
            //
            // 261014 재수정 - 처음엔 Unity 내장 흰색 스프라이트(UI/Skin/UISprite.psd)를 썼는데,
            // 다 찬(100%) 상태에서도 바 양쪽 끝이 검게 물드는 문제가 생겼다(예섭 스크린샷으로
            // 확인) - 확인 못 함(Unity 내장 텍스쳐라 직접 열어볼 수 없음)이지만, 그 내장
            // 스프라이트 자체에 옅은 음영/그라데이션이 구워져 있고 Filled 타입이 그 음영까지
            // 그대로 보여준 것으로 보인다. 대신 이 프로젝트에 이미 있는 fillSprite(ui_panel_fill.png,
            // 트랙/마스크가 쓰는 것과 같은 에셋)로 바꿨다 - 픽셀 직접 확인(RGBA 샘플링) 결과 중앙부가
            // (255,255,255,255) 완전 불투명 흰색이고 가장자리 1~2픽셀만 라운드 모서리 때문에 알파가
            // 깎이는 평평한 스프라이트임을 확인했다(음영 없음). 둥근 테두리는 어차피 부모 Mask가
            // 다시 한번 잘라주므로 이 스프라이트의 Sliced 보더 설정 자체는 Filled 타입에서는
            // 쓰이지 않는다(참고용).
            // fillSprite 로드 자체가 실패한 경우(에셋 경로가 없거나 깨진 경우 - LoadUiSprite가
            // 이미 경고 로그를 남긴다)에도 "스프라이트 없음 = Filled 무시" 버그로 되돌아가지
            // 않도록, 그때만 Unity 내장 스프라이트로 대체한다(이 경우엔 양쪽 끝 음영이 다시
            // 보일 수 있지만, 그래도 바가 아예 안 깎이는 것보단 낫다).
            fillImage.sprite = fillSprite != null
                ? fillSprite
                : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            // 위쪽 45%에 살짝 밝은 띠를 얹어서 유리 광택 느낌 - Mask 자식이라 둥근 모서리를
            // 벗어나지 않는다.
            RectTransform glossRt = NewRect(maskRt, "Gloss", new Vector2(0f, 0.55f), Vector2.one, Vector2.zero, Vector2.zero);
            Image glossImage = AddImage(glossRt, HudBarHighlightColor);
            glossImage.raycastTarget = false;

            if (fillSprite != null)
            {
                Sprite ringSprite = LoadUiSprite(UiPanelRingSpritePath);
                if (ringSprite != null)
                {
                    RectTransform ringRt = NewRect(track, "Ring", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    Image ring = AddImage(ringRt, HudPanelRingColor);
                    ring.sprite = ringSprite;
                    ring.type = Image.Type.Sliced;
                    ring.raycastTarget = false;
                }
            }

            return fillImage;
        }

        /// <summary>원한 게이지 한 구간 - 부모(grudgeGauge) 안에서 x축 비율(0~1)로 폭을 나눠 색을 채운다.</summary>
        private static void CreateGaugeSegment(Transform parent, string name, float xMin, float xMax, Color color)
        {
            RectTransform segment = NewRect(parent, name, new Vector2(xMin, 0f), new Vector2(xMax, 1f), new Vector2(2f, 0f), new Vector2(-2f, 0f));
            AddImage(segment, color);
        }

        private static void BuildUpgradeScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.Upgrade, PanelColorUpgrade);

            CreateLabel(panel, "RerollCountText", "리롤권×1", 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0.2f, 1f), new Vector2(16f, -24f), new Vector2(0f, -48f));
            CreateLabel(panel, "TitleText", "레벨업! 증강을 고르세요", 28, TextAnchor.UpperCenter, new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), Vector2.zero, new Vector2(0f, -56f));
            Text timerText = CreateLabel(panel, "TimerText", "제한시간 --", 16, TextAnchor.UpperRight, new Vector2(0.8f, 1f), new Vector2(1f, 1f), new Vector2(0f, -24f), new Vector2(-16f, -48f));

            string[] cardLabels = { "[공통] 공격력\n스탯 수치 소폭 상승", "[전용] 넓은 베기\n공격 범위 확장", "[일회용] 기도\n이번 판 한정 효과" };
            CreateHorizontalList(panel, "CardsContainer", new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.78f), Vector2.zero, Vector2.zero, cardLabels, 320f);

            Button readyButton = CreateButton(panel, "ReadyButton", "✓ 동료 선택 완료", new Vector2(0.35f, 0.08f), new Vector2(0.65f, 0.2f), Vector2.zero, Vector2.zero);

            UpgradeScreen screen = panel.gameObject.AddComponent<UpgradeScreen>();
            AssignPrivateField(screen, "timerText", timerText);
            AssignPrivateField(screen, "readyButton", readyButton);

            panel.gameObject.SetActive(false);
        }

        private static void BuildRewardScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.Reward, PanelColorReward);

            Text resultTitleText = CreateLabel(panel, "ResultTitleText", "봉인 성공", 32, TextAnchor.UpperCenter, new Vector2(0.25f, 0.78f), new Vector2(0.75f, 0.92f), Vector2.zero, Vector2.zero);

            RectTransform runPanel = NewRect(panel, "RunSummaryPanel", new Vector2(0.3f, 0.45f), new Vector2(0.7f, 0.76f), Vector2.zero, Vector2.zero);
            AddImage(runPanel, new Color(0.06f, 0.06f, 0.04f, 0.9f));
            CreateLabel(runPanel, "RunSummaryText", "달밤의 폐장터 · 10/10 웨이브\n최종 원한: -- / 클리어 보상: --(각자) / 봉인 시간: --:--",
                16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            CreateLabel(panel, "Player1StatsText", "PLAYER1\n처치 수 -- / 획득 엽전 --",
                16, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.45f), new Vector2(0.28f, 0.76f), Vector2.zero, Vector2.zero);
            CreateLabel(panel, "Player2StatsText", "PLAYER2\n처치 수 -- / 획득 엽전 --",
                16, TextAnchor.MiddleCenter, new Vector2(0.72f, 0.45f), new Vector2(0.95f, 0.76f), Vector2.zero, Vector2.zero);

            Button retryButton = CreateButton(panel, "RetryButton", "다시 하기", new Vector2(0.3f, 0.12f), new Vector2(0.48f, 0.26f), Vector2.zero, Vector2.zero);
            Button exitButton = CreateButton(panel, "ExitButton", "나가기", new Vector2(0.52f, 0.12f), new Vector2(0.7f, 0.26f), Vector2.zero, Vector2.zero);

            RewardScreen screen = panel.gameObject.AddComponent<RewardScreen>();
            AssignPrivateField(screen, "resultTitleText", resultTitleText);
            AssignPrivateField(screen, "retryButton", retryButton);
            AssignPrivateField(screen, "exitButton", exitButton);

            panel.gameObject.SetActive(false);
        }

        private static void BuildSettingsScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.Settings, PanelColorSettings);

            Button backButton = CreateButton(panel, "BackButton", "← 뒤로", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(140f, -24f));

            string[] tabLabels = { "그래픽", "오디오 (선택됨)", "키 설정", "기타" };
            CreateVerticalList(panel, "TabsContainer", new Vector2(0f, 0.1f), new Vector2(0.22f, 0.86f), new Vector2(16f, 0f), Vector2.zero, tabLabels, 44f);

            RectTransform audioPanel = NewRect(panel, "AudioTabPanel", new Vector2(0.26f, 0.1f), new Vector2(0.96f, 0.86f), Vector2.zero, Vector2.zero);
            CreateLabel(audioPanel, "MasterLabel", "전체 음량", 16, TextAnchor.MiddleLeft, new Vector2(0f, 0.72f), new Vector2(0.4f, 0.84f), Vector2.zero, Vector2.zero);
            Slider masterVolumeSlider = CreateSlider(audioPanel, "MasterVolumeSlider", new Vector2(0.42f, 0.74f), new Vector2(1f, 0.82f), Vector2.zero, Vector2.zero);
            CreateLabel(audioPanel, "BgmLabel", "배경음 (국악)", 16, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0.4f, 0.62f), Vector2.zero, Vector2.zero);
            Slider bgmVolumeSlider = CreateSlider(audioPanel, "BgmVolumeSlider", new Vector2(0.42f, 0.52f), new Vector2(1f, 0.6f), Vector2.zero, Vector2.zero);
            CreateLabel(audioPanel, "SfxLabel", "효과음", 16, TextAnchor.MiddleLeft, new Vector2(0f, 0.28f), new Vector2(0.4f, 0.4f), Vector2.zero, Vector2.zero);
            Slider sfxVolumeSlider = CreateSlider(audioPanel, "SfxVolumeSlider", new Vector2(0.42f, 0.3f), new Vector2(1f, 0.38f), Vector2.zero, Vector2.zero);

            SettingsScreen screen = panel.gameObject.AddComponent<SettingsScreen>();
            AssignPrivateField(screen, "backButton", backButton);
            AssignPrivateField(screen, "masterVolumeSlider", masterVolumeSlider);
            AssignPrivateField(screen, "bgmVolumeSlider", bgmVolumeSlider);
            AssignPrivateField(screen, "sfxVolumeSlider", sfxVolumeSlider);

            panel.gameObject.SetActive(false);
        }

        private static void BuildEscMenuScreen(Transform screensRoot)
        {
            RectTransform panel = CreateScreenRoot(screensRoot, UIFlowManager.EscMenu, PanelColorEscMenu);

            RectTransform menuPanel = NewRect(panel, "MenuPanel", new Vector2(0.38f, 0.3f), new Vector2(0.62f, 0.7f), Vector2.zero, Vector2.zero);
            AddImage(menuPanel, new Color(0.1f, 0.1f, 0.12f, 0.95f));

            Button resumeButton = CreateButton(menuPanel, "ResumeButton", "계속하기", new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.86f), Vector2.zero, Vector2.zero);
            Button settingsButton = CreateButton(menuPanel, "SettingsButton", "설정", new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.58f), Vector2.zero, Vector2.zero);
            Button exitButton = CreateButton(menuPanel, "ExitButton", "나가기 (로비로)", new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.3f), Vector2.zero, Vector2.zero);
            CreateLabel(menuPanel, "CaptionText", "온라인이라 게임은 멈추지 않음", 12, TextAnchor.LowerCenter, Vector2.zero, new Vector2(1f, 0.1f), Vector2.zero, Vector2.zero);

            EscMenuScreen screen = panel.gameObject.AddComponent<EscMenuScreen>();
            AssignPrivateField(screen, "resumeButton", resumeButton);
            AssignPrivateField(screen, "settingsButton", settingsButton);
            AssignPrivateField(screen, "exitButton", exitButton);

            panel.gameObject.SetActive(false);
        }

        // ----- 저수준 UI 빌더 헬퍼 (레거시 UnityEngine.UI 기준) -----

        private static RectTransform CreateScreenRoot(Transform screensRoot, string screenName, Color backgroundColor)
        {
            Transform existing = screensRoot.Find(screenName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            RectTransform rt = NewRect(screensRoot, screenName, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(rt, backgroundColor);
            return rt;
        }

        private static RectTransform NewRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static Text AddText(RectTransform rt, string content, int fontSize, TextAnchor alignment, Color color)
        {
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.text = content;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // 순수 표시용 텍스트는 레이캐스트를 받을 필요가 없다 - 버튼/인풋필드는 이미 배경
            // Image(raycastTarget=true)가 클릭을 받으므로, 라벨까지 레이캐스트 대상이면
            // GraphicRaycaster가 매 클릭마다 불필요하게 한 겹 더 검사한다(실무 관례 - Unity
            // 기본 UI 프리팹들도 Button 자식 Text는 전부 raycastTarget=false).
            text.raycastTarget = false;
            return text;
        }

        private static Text CreateLabel(Transform parent, string name, string content, int fontSize, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            return AddText(rt, content, fontSize, alignment, Color.white);
        }

        private static Button CreateButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image bg = AddImage(rt, new Color(0.22f, 0.22f, 0.28f, 0.95f));
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;

            RectTransform labelRt = NewRect(rt, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddText(labelRt, label, 18, TextAnchor.MiddleCenter, Color.white);

            return button;
        }

        private static InputField CreateInputField(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string placeholderText)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            AddImage(rt, Color.white);
            InputField field = rt.gameObject.AddComponent<InputField>();

            RectTransform textRt = NewRect(rt, "Text", Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            Text text = AddText(textRt, string.Empty, 18, TextAnchor.MiddleLeft, Color.black);
            field.textComponent = text;

            RectTransform placeholderRt = NewRect(rt, "Placeholder", Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            Text placeholder = AddText(placeholderRt, placeholderText, 18, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.5f));
            field.placeholder = placeholder;

            return field;
        }

        private static Slider CreateSlider(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            AddImage(rt, new Color(0.15f, 0.15f, 0.15f, 1f));
            Slider slider = rt.gameObject.AddComponent<Slider>();

            RectTransform fillArea = NewRect(rt, "Fill Area", Vector2.zero, Vector2.one, new Vector2(5f, 0f), new Vector2(-15f, 0f));
            RectTransform fillRt = NewRect(fillArea, "Fill", Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            Image fillImage = AddImage(fillRt, new Color(0.3f, 0.6f, 0.9f, 1f));
            slider.fillRect = fillRt;

            RectTransform handleArea = NewRect(rt, "Handle Slide Area", Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            RectTransform handleRt = NewRect(handleArea, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            handleRt.sizeDelta = new Vector2(20f, 0f);
            Image handleImage = AddImage(handleRt, Color.white);
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImage;

            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            return slider;
        }

        /// <summary>
        /// 세로로 쌓이는 버튼 목록(스킬트리 캐릭터 리스트, 설정 탭 등). VerticalLayoutGroup을
        /// 써서 자식 수가 바뀌어도 알아서 재배치되게 한다 - 각 버튼엔 LayoutElement로
        /// 고정 높이를 줘서 레이아웃이 0 크기로 찌그러지지 않게 한다.
        /// </summary>
        private static RectTransform CreateVerticalList(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string[] labels, float itemHeight)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            VerticalLayoutGroup layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            for (int i = 0; i < labels.Length; i++)
            {
                Button item = CreateButton(rt, $"Item_{i}", labels[i], Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                LayoutElement layoutElement = item.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredHeight = itemHeight;
            }

            return rt;
        }

        /// <summary>가로로 나열되는 카드 목록(증강 카드 3장 등). HorizontalLayoutGroup 사용.</summary>
        private static RectTransform CreateHorizontalList(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string[] labels, float itemWidth)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            HorizontalLayoutGroup layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform card = NewRect(rt, $"Card_{i}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                AddImage(card, new Color(0.14f, 0.14f, 0.18f, 0.95f));
                // Image와 Text는 둘 다 Graphic이라 같은 GameObject에 공존 불가 - 라벨은 자식으로 분리.
                RectTransform cardLabelRt = NewRect(card, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                AddText(cardLabelRt, labels[i], 16, TextAnchor.MiddleCenter, Color.white);
                LayoutElement layoutElement = card.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = itemWidth;
            }

            return rt;
        }

        /// <summary>격자 목록(스토리 보관함 2x3, 맵 선택 2x2 등). GridLayoutGroup 사용.</summary>
        private static RectTransform CreateGridList(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string[] labels, int columns)
        {
            RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            GridLayoutGroup grid = rt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220f, 130f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            for (int i = 0; i < labels.Length; i++)
            {
                CreateButton(rt, $"Item_{i}", labels[i], Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }

            return rt;
        }

        private static void AssignPrivateField(Object target, string fieldName, Object value)
        {
            if (target == null)
            {
                return;
            }

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
