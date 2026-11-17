using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261115 정리 도구 - 지워도 게임에 영향이 없는 것만 한곳에 모은다(지우지는 않음, 지우는 건 사람이 직접).
    /// 메뉴: CheonMyeong/정리/1. 미리보기 → 콘솔에 "옮길 것 / 건너뛴 것과 이유"만 출력(아무것도 안 바꿈).
    ///       CheonMyeong/정리/2. 실행 → 확인 후 실제로 옮긴다.
    ///
    /// 하이어라키(SampleScene): 아래 목록의 오브젝트를 씬 맨 아래 "=====" 절취선 밑으로 옮긴다(Undo 가능, 씬 저장은 직접).
    ///   목록은 씬 · 스크립트 전체를 대조해서 고른 것(꺼져 있음 + 아무 데서도 참조 안 함 + 스크립트가 이름으로 안 찾음).
    ///   실행 때 한 번 더 검사: 꺼져 있지 않거나(빈 오브젝트 제외) 씬의 다른 컴포넌트가 하나라도 참조하면 옮기지 않는다.
    /// 에셋: 아래 목록을 Assets/_ToDelete 로 옮긴다(AssetDatabase.MoveAsset - GUID 유지라 혹시 참조가 있어도 안 끊김).
    ///   실행 때 한 번 더 검사: 프로젝트의 다른 에셋 · 빌드 씬 · 프로젝트 설정 중 하나라도 참조하면 옮기지 않는다.
    /// </summary>
    public static class CleanupCandidatesTool
    {
        private const string TrashFolder = "Assets/_ToDelete";
        private const string MainScenePath = "Assets/Scenes/SampleScene.unity";

        // 261117 글꼴은 Assets/Fonts의 Galmuri11 · Galmuri11 Bold만 쓴다.
        // 씬 템플릿 둘은 "새 씬" 창에서만 쓰는 에디터 전용 파일. (261115 커서 그림은 이제 UICanvas/CursorStyle이 씀 - 목록에서 뺌)
        private static readonly string[] AssetCandidates =
        {
            "Assets/font",
            "Assets/Settings/Lit2DSceneTemplate.scenetemplate",
            "Assets/Settings/Scenes/URP2DSceneTemplate.unity",
            "Assets/TextMesh Pro/Fonts/Galmuri-v2.40.4",
            "Assets/TextMesh Pro/Fonts/Pretendard-1.3.9",
            // 261117 2차 - 글꼴을 전부 Galmuri11로 바꿔 Pretendard는 안 씀.
            "Assets/Fonts/Pretendard-Regular.ttf",
            "Assets/Fonts/Pretendard-Bold.ttf",
            "Assets/Fonts/Pretendard-LICENSE.txt",
        };

        // 261117 글꼴 - 받은 묶음에서 게임이 쓰는 3개(+ 라이선스)만 Assets/Fonts 로 먼저 빼고, 나머지 묶음 폴더는 위 목록대로 _ToDelete 로.
        // MoveAsset이라 GUID가 그대로여서 씬 · Resources/GameFonts.asset 참조는 끊기지 않는다.
        private const string FontFolder = "Assets/Fonts";
        private static readonly string[,] KeptFonts =
        {
            { "Assets/TextMesh Pro/Fonts/Galmuri-v2.40.4/Galmuri11-Bold.ttf", FontFolder + "/Galmuri11-Bold.ttf" },
            { "Assets/TextMesh Pro/Fonts/Galmuri-v2.40.4/LICENSE.txt", FontFolder + "/Galmuri-LICENSE.txt" },
            { "Assets/TextMesh Pro/Fonts/Pretendard-1.3.9/public/static/alternative/Pretendard-Regular.ttf", FontFolder + "/Pretendard-Regular.ttf" },
            { "Assets/TextMesh Pro/Fonts/Pretendard-1.3.9/public/static/alternative/Pretendard-Bold.ttf", FontFolder + "/Pretendard-Bold.ttf" },
            { "Assets/TextMesh Pro/Fonts/Pretendard-1.3.9/LICENSE.txt", FontFolder + "/Pretendard-LICENSE.txt" },
        };

        private static readonly string[] HierarchyCandidates =
        {
            "UICanvas/Screens/InGame/P1Card/P1HealthBar/Mask/Gloss",
            "UICanvas/Screens/InGame/P2Card/P2HealthBar/Mask/Gloss",
            "UICanvas/Screens/InGame/ExpBar/Mask/Gloss",
            "UICanvas/Screens/InGame/P1Card/Ring",
            "UICanvas/Screens/InGame/P2Card/Ring",
            "UICanvas/Screens/InGame/CenterStatusCard/Ring",
            "TitleTalismans/Gate/Start_Talisman/Label",
            "TitleTalismans/Gate/Settings_Talisman/Label",
            "TitleTalismans/Gate/Exit_Talisman/Label",
            "UICanvas/Screens/Lobby/SettingsButton/Content",
            "UICanvas/Screens/Lobby/CurrencyBox/Content/Icon",
            "UICanvas/Screens/Lobby/PlayerInfoBox/Content",
            "UICanvas/Screens/ChoiceMap/BackButton/Label",
            "UICanvas/Screens/ChoiceMap/MapInfoPanel/Label",
            "UICanvas/Screens/WaitRoom/BackButton/Label",
            "UICanvas/Screens/WaitRoom/RoomInfoPanel/CodeBox/Text (TMP)",
            "UICanvas/Screens/StoryStorage/BackButton/Label",
            "UICanvas/Screens/CharSkillTree/BackButton/Label",
            "UICanvas/Screens/CharSkillTree/SkillTreeContainer/NodeScroll",
            "UICanvas/Screens/CharSkillTree/SkillTreeContainer/Caption",
            "UICanvas/Screens/CharSkillTree/RecruitPanel/NameBoard/Text (TMP)",
            "UICanvas/Screens/Reward/TitleBoard/TitleText",
        };

        [MenuItem("CheonMyeong/정리/1. 미리보기 (아무것도 안 바꿈)")]
        private static void Preview()
        {
            Run(false);
        }

        [MenuItem("CheonMyeong/정리/2. 실행 (절취선 밑 · _ToDelete 로 모으기)")]
        private static void Execute()
        {
            if (!EditorUtility.DisplayDialog("정리 실행",
                    "검사를 통과한 것만 옮깁니다(지우지 않음).\n- 하이어라키: SampleScene 절취선 밑\n- 에셋: Assets/_ToDelete\n\n실행 후 씬 저장(Ctrl+S)은 직접 해주세요.",
                    "실행", "취소"))
            {
                return;
            }
            Run(true);
        }

        private static void Run(bool apply)
        {
            var log = new StringBuilder();
            log.AppendLine(apply ? "[정리] 실행 결과" : "[정리] 미리보기 (변경 없음)");
            int movedObjects = RunHierarchy(apply, log);
            if (movedObjects < 0)
            {
                Debug.LogWarning(log.ToString());
                return;
            }
            KeepFonts(apply, log);
            int movedAssets = RunAssets(apply, log);
            log.AppendLine($"요약: 하이어라키 {movedObjects}개 · 에셋 {movedAssets}개 {(apply ? "옮김" : "옮길 예정")}");
            Debug.Log(log.ToString());
            if (apply)
            {
                EditorUtility.DisplayDialog("정리 완료", $"하이어라키 {movedObjects}개, 에셋 {movedAssets}개를 옮겼습니다.\n자세한 내용은 콘솔을 보세요.\nSampleScene을 저장(Ctrl+S)해 주세요.", "확인");
            }
        }

        // ───────── 하이어라키 ─────────

        private static int RunHierarchy(bool apply, StringBuilder log)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    log.AppendLine("취소됨 - 열린 씬 저장을 취소했습니다.");
                    return -1;
                }
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            Transform separator = FindSeparator(scene);
            if (separator == null)
            {
                log.AppendLine("하이어라키: 절취선(===)을 못 찾아서 건너뜀.");
                return 0;
            }

            var targets = new List<Transform>();
            log.AppendLine("── 하이어라키 (SampleScene) ──");
            foreach (string path in HierarchyCandidates)
            {
                Transform t = FindUnique(scene, path, out string why);
                if (t == null)
                {
                    log.AppendLine($"  건너뜀 {path} - {why}");
                    continue;
                }
                if (!IsInert(t))
                {
                    log.AppendLine($"  건너뜀 {path} - 켜져 있고 비어 있지 않음");
                    continue;
                }
                targets.Add(t);
            }

            // 참조 검사: 씬의 모든 컴포넌트를 훑어 대상(하위 포함)을 가리키는 칸이 있으면 제외.
            var blocked = FindReferenced(scene, targets);
            int moved = 0;
            foreach (Transform t in targets)
            {
                string path = PathOf(t);
                if (blocked.TryGetValue(t, out string by))
                {
                    log.AppendLine($"  건너뜀 {path} - 참조 있음: {by}");
                    continue;
                }
                log.AppendLine($"  {(apply ? "옮김" : "옮길 것")} {path}");
                if (apply)
                {
                    Undo.SetTransformParent(t, null, "정리: 절취선 밑으로");
                    t.SetAsLastSibling();
                }
                moved++;
            }
            if (apply && moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return moved;
        }

        private static Transform FindSeparator(Scene scene)
        {
            Transform found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                string n = root.name.Trim();
                if (n.Length >= 3 && n.Trim('=').Length == 0)
                {
                    found = root.transform;
                }
            }
            return found;
        }

        private static Transform FindUnique(Scene scene, string path, out string why)
        {
            string[] parts = path.Split('/');
            Transform current = null;
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == parts[0])
                {
                    current = root.transform;
                    count++;
                }
            }
            if (count != 1)
            {
                why = count == 0 ? "없음(이미 옮겼거나 이름이 바뀜)" : "같은 이름이 여러 개";
                return null;
            }
            for (int i = 1; i < parts.Length; i++)
            {
                Transform next = null;
                count = 0;
                for (int c = 0; c < current.childCount; c++)
                {
                    Transform child = current.GetChild(c);
                    if (child.name == parts[i])
                    {
                        next = child;
                        count++;
                    }
                }
                if (count != 1)
                {
                    why = count == 0 ? "없음(이미 옮겼거나 이름이 바뀜)" : "같은 이름이 여러 개";
                    return null;
                }
                current = next;
            }
            why = null;
            return current;
        }

        /// <summary>꺼져 있거나, 켜져 있어도 Transform 말고 아무 컴포넌트 · 자식이 없는 빈 오브젝트.</summary>
        private static bool IsInert(Transform t)
        {
            if (!t.gameObject.activeSelf)
            {
                return true;
            }
            return t.childCount == 0 && t.GetComponents<Component>().Length == 1;
        }

        private static Dictionary<Transform, string> FindReferenced(Scene scene, List<Transform> targets)
        {
            var owner = new Dictionary<Object, Transform>();
            foreach (Transform target in targets)
            {
                foreach (Transform member in target.GetComponentsInChildren<Transform>(true))
                {
                    owner[member.gameObject] = target;
                    foreach (Component c in member.GetComponents<Component>())
                    {
                        if (c != null)
                        {
                            owner[c] = target;
                        }
                    }
                }
            }

            var blocked = new Dictionary<Transform, string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform)
                    {
                        continue; // 빠진 스크립트 · Transform(부모-자식 연결만 들고 있음)
                    }
                    owner.TryGetValue(c, out Transform self);
                    var so = new SerializedObject(c);
                    SerializedProperty p = so.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null
                            || p.propertyPath == "m_GameObject")
                        {
                            continue;
                        }
                        if (owner.TryGetValue(p.objectReferenceValue, out Transform hit) && hit != self && !blocked.ContainsKey(hit))
                        {
                            blocked[hit] = PathOf(c.transform) + " (" + c.GetType().Name + "." + p.propertyPath + ")";
                        }
                    }
                }
            }
            return blocked;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                path = p.name + "/" + path;
            }
            return path;
        }

        // ───────── 글꼴 ─────────

        private static bool IsKeptFont(string path)
        {
            for (int i = 0; i < KeptFonts.GetLength(0); i++)
            {
                if (KeptFonts[i, 0] == path)
                {
                    return true;
                }
            }
            return false;
        }

        private static void KeepFonts(bool apply, StringBuilder log)
        {
            log.AppendLine("── 쓰는 글꼴 → " + FontFolder + " ──");
            for (int i = 0; i < KeptFonts.GetLength(0); i++)
            {
                string from = KeptFonts[i, 0];
                string to = KeptFonts[i, 1];
                if (!File.Exists(from))
                {
                    log.AppendLine($"  건너뜀 {from} - 없음(이미 옮김)");
                    continue;
                }
                if (File.Exists(to))
                {
                    log.AppendLine($"  건너뜀 {from} - {to} 가 이미 있음");
                    continue;
                }
                log.AppendLine($"  {(apply ? "옮김" : "옮길 것")} {from} → {to}");
                if (!apply)
                {
                    continue;
                }
                if (!AssetDatabase.IsValidFolder(FontFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "Fonts");
                }
                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error))
                {
                    log.AppendLine($"    실패: {error}");
                }
            }
        }

        // ───────── 에셋 ─────────

        private static int RunAssets(bool apply, StringBuilder log)
        {
            log.AppendLine("── 에셋 → " + TrashFolder + " ──");
            // 후보 파일 전부(폴더면 안의 파일까지).
            var candidateFiles = new Dictionary<string, string>(); // 파일 경로 → 후보 루트
            foreach (string root in AssetCandidates)
            {
                if (AssetDatabase.IsValidFolder(root))
                {
                    foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { root }))
                    {
                        string file = AssetDatabase.GUIDToAssetPath(guid);
                        if (!AssetDatabase.IsValidFolder(file) && !IsKeptFont(file))
                        {
                            candidateFiles[file] = root;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(root)) && File.Exists(root))
                {
                    candidateFiles[root] = root;
                }
            }

            var blockedRoots = new Dictionary<string, string>();
            void Block(string file, string reason)
            {
                if (candidateFiles.TryGetValue(file, out string r) && !blockedRoots.ContainsKey(r))
                {
                    blockedRoots[r] = reason;
                }
            }

            foreach (string file in candidateFiles.Keys)
            {
                if (file.EndsWith(".cs") || file.Contains("/Resources/") || file.Contains("/Editor/"))
                {
                    Block(file, "스크립트 · Resources · Editor 폴더 파일은 대상에서 제외");
                }
            }

            // 0) 쓰는 글꼴이 아직 묶음 폴더 안에 있으면(옮기기 실패 · 미리보기) 그 폴더는 통째로 옮기지 않는다 - 실행 때만 해당.
            if (apply)
            {
                for (int i = 0; i < KeptFonts.GetLength(0); i++)
                {
                    string kept = KeptFonts[i, 0];
                    foreach (string root in AssetCandidates)
                    {
                        if (File.Exists(kept) && kept.StartsWith(root + "/") && !blockedRoots.ContainsKey(root))
                        {
                            blockedRoots[root] = "쓰는 글꼴이 아직 안에 있음: " + kept;
                        }
                    }
                }
            }

            // 1) 프로젝트의 다른 모든 에셋(빌드 씬 포함)이 후보를 직접 참조하는지.
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (candidateFiles.ContainsKey(path) || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }
                if (!path.StartsWith("Assets/") && !path.StartsWith("ProjectSettings/"))
                {
                    continue;
                }
                foreach (string dep in AssetDatabase.GetDependencies(path, false))
                {
                    if (dep != path && candidateFiles.ContainsKey(dep))
                    {
                        Block(dep, "참조됨: " + path);
                    }
                }
            }

            // 2) 빌드 씬 목록 · 프로젝트 설정(렌더 파이프라인 · 미리 로드 · 기본 커서).
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                Block(s.path, "Build Settings 씬 목록에 있음");
            }
            var settingsObjects = new List<Object> { GraphicsSettings.defaultRenderPipeline, PlayerSettings.defaultCursor };
            for (int i = 0; i < QualitySettings.count; i++)
            {
                settingsObjects.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            }
            settingsObjects.AddRange(PlayerSettings.GetPreloadedAssets());
            foreach (Object o in settingsObjects)
            {
                if (o == null)
                {
                    continue;
                }
                string p = AssetDatabase.GetAssetPath(o);
                Block(p, "프로젝트 설정에서 사용");
                foreach (string dep in AssetDatabase.GetDependencies(p, true))
                {
                    Block(dep, "프로젝트 설정에서 사용(" + p + ")");
                }
            }

            int moved = 0;
            bool editing = false;
            try
            {
                foreach (string root in AssetCandidates)
                {
                    bool exists = AssetDatabase.IsValidFolder(root) || File.Exists(root);
                    if (!exists)
                    {
                        log.AppendLine($"  건너뜀 {root} - 없음(이미 옮겼거나 지움)");
                        continue;
                    }
                    if (blockedRoots.TryGetValue(root, out string reason))
                    {
                        log.AppendLine($"  건너뜀 {root} - {reason}");
                        continue;
                    }
                    log.AppendLine($"  {(apply ? "옮김" : "옮길 것")} {root}");
                    if (apply)
                    {
                        if (!AssetDatabase.IsValidFolder(TrashFolder))
                        {
                            AssetDatabase.CreateFolder("Assets", "_ToDelete");
                        }
                        if (!editing)
                        {
                            AssetDatabase.StartAssetEditing();
                            editing = true;
                        }
                        string target = AssetDatabase.GenerateUniqueAssetPath(TrashFolder + "/" + Path.GetFileName(root));
                        string error = AssetDatabase.MoveAsset(root, target);
                        if (!string.IsNullOrEmpty(error))
                        {
                            log.AppendLine($"    실패: {error}");
                            continue;
                        }
                    }
                    moved++;
                }
            }
            finally
            {
                if (editing)
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                }
            }
            return moved;
        }
    }
}
