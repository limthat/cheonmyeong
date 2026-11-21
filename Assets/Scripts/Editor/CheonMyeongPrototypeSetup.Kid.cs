using System.Collections.Generic;
using CheonMyeong.Boss;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261121 추가 - 어린 억쇠(5웨이브 중간 보스, 기획서 v1.2 9-1) 한 번에 만들기.
    /// 메뉴: CheonMyeong/Setup/Create Eorin Eoksoe Boss (어린 억쇠)
    /// 1) EORIN_EOKSOE-Sheet(128px, 태그마다 한 줄) · EORIN_EOKSOE_FX-Sheet를 128칸으로 자른다(원본 해상도 · Point · 압축 없음 · PPU 100).
    /// 2) 줄별 클립(125ms = 8fps) + 상태 이름만 있는 애니메이터(Assets/Animations/EorinEoksoe).
    /// 3) 수치 에셋 Assets/Data/Boss/EorinEoksoe.asset(있으면 유지 - Inspector에서 고친 값 보호).
    /// 4) Gameplay 씬(ProgressionSystems가 있는 씬)에 'Boss_EorinEoksoe' in-scene NetworkObject 배치/갱신.
    /// 이펙트: EORIN_EOKSOE_FX는 ATK_FX 한 줄뿐이라, 배지기 내던지기 흙 터짐은 억쇠 Eoksoe_FX의 SLAM_FX를 갈색으로 칠해 쓴다.
    /// 체력 바 틀: Bar-Sheet_12(머리 + 빗자루). 다시 실행해도 안전.
    /// </summary>
    public static partial class CheonMyeongPrototypeSetup
    {
        private const string KidSheetPath = BossArtFolder + "/EORIN_EOKSOE-Sheet.png";
        private const string KidFxSheetPath = BossArtFolder + "/EORIN_EOKSOE_FX-Sheet.png";
        private const string KidAnimFolder = "Assets/Animations/EorinEoksoe";
        private const string KidStatsPath = BossDataFolder + "/EorinEoksoe.asset";
        private const string KidObjectName = "Boss_EorinEoksoe";
        private const string KidBarSheetPath = "Assets/Art/ui/Bar-Sheet.png";
        private const string KidBarSpriteName = "Bar-Sheet_12";
        private const int KidFrameSize = 128;
        private const float KidFps = 8f; // 기획서 v1.2: 125ms

        // 시트 줄 순서(위에서부터) = EorinEoksoeBoss.AnimStateNames = 기획서 TAG 순서. 'GRAB '(끝에 띄어쓰기)은 Trim해서 GRAB.
        private static readonly string[] KidRowNames =
            { "IDLE", "WALK", "ATK", "GRAB", "HOLD", "SLAM", "FLY", "RIDE", "SPIN", "DIE" };
        private static readonly bool[] KidRowLoops =
            { true, true, false, false, true, false, false, true, true, false };

        [MenuItem("CheonMyeong/Setup/Create Eorin Eoksoe Boss (어린 억쇠)")]
        public static void CreateEorinEoksoeBoss()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(KidSheetPath) == null)
            {
                Debug.LogError($"[CheonMyeong] '{KidSheetPath}'이 없습니다.");
                return;
            }

            PrepareBossSheet(KidSheetPath, KidFrameSize);
            bool hasKidFx = AssetDatabase.LoadAssetAtPath<Texture2D>(KidFxSheetPath) != null;
            if (hasKidFx)
            {
                PrepareBossSheet(KidFxSheetPath, EoksoeFxFrameSize);
            }
            bool hasEoksoeFx = AssetDatabase.LoadAssetAtPath<Texture2D>(EoksoeFxSheetPath) != null;
            if (hasEoksoeFx)
            {
                PrepareBossSheet(EoksoeFxSheetPath, EoksoeFxFrameSize);
            }

            List<Sprite[]> rows = LoadSheetRows(KidSheetPath);
            if (rows.Count < KidRowNames.Length)
            {
                Debug.LogError($"[CheonMyeong] 어린 억쇠 시트 줄이 {rows.Count}개뿐입니다(필요 {KidRowNames.Length}개) - 시트를 확인하세요.");
                return;
            }

            AnimatorController controller = CreateKidAnimator(rows);
            KidBossStatsSO stats = EnsureKidStats();
            var telegraphMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            Vector2 visualOffset = ComputeFeetOffset(KidSheetPath, rows[0][0]);

            List<Sprite[]> kidFxRows = hasKidFx ? LoadSheetRows(KidFxSheetPath) : new List<Sprite[]>();
            List<Sprite[]> eoksoeFxRows = hasEoksoeFx ? LoadSheetRows(EoksoeFxSheetPath) : new List<Sprite[]>();
            Sprite[] slashFx = kidFxRows.Count > 0 ? kidFxRows[0] : (eoksoeFxRows.Count > 0 ? eoksoeFxRows[0] : new Sprite[0]);
            Sprite[] burstFx = kidFxRows.Count > 1 ? kidFxRows[1] : (eoksoeFxRows.Count > 1 ? eoksoeFxRows[1] : new Sprite[0]);
            Sprite[] tornadoFrames = rows[8];
            Sprite barFrame = FindSprite(KidBarSheetPath, KidBarSpriteName);
            if (barFrame == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{KidBarSheetPath}'에서 '{KidBarSpriteName}'를 찾지 못했습니다 - 체력 바는 억쇠 틀로 나옵니다.");
            }

            EnsureLayer(EnemyLayerName);
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);

            int sceneCount = ForEachSceneContaining(ProgressionRootObjectName, (scene, progressionRoot) =>
            {
                GameObject root = FindInScene(scene, KidObjectName);
                if (root == null)
                {
                    root = new GameObject(KidObjectName);
                    SceneManager.MoveGameObjectToScene(root, scene);
                }
                root.layer = enemyLayer;
                root.transform.position = Vector3.zero;

                var rb = root.GetComponent<Rigidbody2D>();
                if (rb == null) rb = root.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.gravityScale = 0f;
                rb.freezeRotation = true;

                var collider = root.GetComponent<CircleCollider2D>();
                if (collider == null) collider = root.AddComponent<CircleCollider2D>();
                collider.isTrigger = true;
                collider.radius = stats.bodyRadius;
                collider.offset = new Vector2(0f, stats.bodyRadius);

                if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
                if (root.GetComponent<NetworkTransform>() == null) root.AddComponent<NetworkTransform>();

                GameObject shadow = EnsureChildObject(root.transform, "Shadow");
                shadow.layer = enemyLayer;
                shadow.transform.localPosition = Vector3.zero;
                var shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                if (shadowRenderer == null) shadowRenderer = shadow.AddComponent<SpriteRenderer>();
                shadowRenderer.sortingOrder = -40;
                shadowRenderer.color = new Color(0f, 0f, 0f, 0.4f);

                GameObject visual = EnsureChildObject(root.transform, "Visual");
                visual.layer = enemyLayer;
                visual.transform.localPosition = visualOffset;
                var bodyRenderer = visual.GetComponent<SpriteRenderer>();
                if (bodyRenderer == null) bodyRenderer = visual.AddComponent<SpriteRenderer>();
                bodyRenderer.sprite = rows[0][0];
                bodyRenderer.sortingOrder = 5;
                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                visual.SetActive(false); // 등장 전엔 숨김(EorinEoksoeBoss가 켠다)

                var boss = root.GetComponent<EorinEoksoeBoss>();
                if (boss == null) boss = root.AddComponent<EorinEoksoeBoss>();
                AssignPrivateField(boss, "stats", stats);
                AssignPrivateField(boss, "visualRoot", visual.transform);
                AssignPrivateField(boss, "bodyRenderer", bodyRenderer);
                AssignPrivateField(boss, "animator", animator);
                AssignPrivateField(boss, "bodyCollider", collider);
                AssignPrivateField(boss, "shadowRenderer", shadowRenderer);
                AssignPrivateField(boss, "telegraphMaterial", telegraphMaterial);
                AssignPrivateObjectArrayField(boss, "slashFxFrames", slashFx);
                AssignPrivateObjectArrayField(boss, "burstFxFrames", burstFx);
                AssignPrivateObjectArrayField(boss, "tornadoFrames", tornadoFrames);
                AssignPrivateField(boss, "barFrameSprite", barFrame);
                SetSerializedVector2(boss, "visualBaseOffset", visualOffset);
                EditorUtility.SetDirty(boss);
                EditorUtility.SetDirty(root);
            });

            if (sceneCount == 0)
            {
                Debug.LogError($"[CheonMyeong] '{ProgressionRootObjectName}'이 있는 씬을 찾지 못했습니다 - " +
                               "'CheonMyeong/Setup/Create Progression Prototype (Loot + Party Progress)'를 먼저 실행하세요.");
                return;
            }

            Selection.activeObject = stats;
            Debug.Log($"[CheonMyeong] 어린 억쇠 생성/갱신 완료(씬 {sceneCount}개) - 수치: {KidStatsPath}, 애니메이션: {KidAnimFolder}. " +
                      "DefaultWaves의 5웨이브 boss = Mid이면 어린 억쇠가 나온다.");
        }

        private static AnimatorController CreateKidAnimator(List<Sprite[]> rows)
        {
            EnsureFolderRecursive(KidAnimFolder);
            string controllerPath = KidAnimFolder + "/EorinEoksoe.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            for (int i = 0; i < KidRowNames.Length; i++)
            {
                AnimationClip clip = CreateHeldSpriteClip($"{KidAnimFolder}/{KidRowNames[i]}.anim", rows[i], KidFps, KidRowLoops[i]);
                AnimatorState state = stateMachine.AddState(KidRowNames[i]);
                state.motion = clip;
                if (i == 0)
                {
                    stateMachine.defaultState = state;
                }
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static KidBossStatsSO EnsureKidStats()
        {
            EnsureFolderRecursive(BossDataFolder);
            var stats = AssetDatabase.LoadAssetAtPath<KidBossStatsSO>(KidStatsPath);
            if (stats == null)
            {
                stats = ScriptableObject.CreateInstance<KidBossStatsSO>();
                AssetDatabase.CreateAsset(stats, KidStatsPath);
                AssetDatabase.SaveAssets();
            }
            return stats;
        }

        private static Sprite FindSprite(string sheetPath, string spriteName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }
            return null;
        }
    }
}
