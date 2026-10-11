using CheonMyeong.Data;
using CheonMyeong.Input;
using CheonMyeong.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 에디터 전용 셋업 스크립트. 하영 스탯 에셋 생성 + 테스트용 오브젝트에
    /// 컴포넌트를 붙이고 참조를 연결하는 작업을 메뉴 클릭 한 번으로 처리한다.
    /// 수동으로 Inspector에서 드래그&드롭 하는 반복 작업을 줄이기 위한 용도이며,
    /// 플레이어 빌드에는 포함되지 않는다(Editor 폴더 하위).
    /// </summary>
    public static class CheonMyeongPrototypeSetup
    {
        private const string DataFolder = "Assets/Data";
        private const string CharactersFolder = DataFolder + "/Characters";
        private const string HayoungStatsPath = CharactersFolder + "/Hayoung.asset";
        private const string PrototypeObjectName = "Hayoung_Prototype";

        [MenuItem("CheonMyeong/Setup/Create Hayoung Prototype Object")]
        public static void CreateHayoungPrototype()
        {
            CharacterStatsSO stats = EnsureHayoungStats();
            GameObject go = EnsurePrototypeObject();

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var inputReader = go.GetComponent<PlayerInputReader>();
            if (inputReader == null) inputReader = go.AddComponent<PlayerInputReader>();

            var movement = go.GetComponent<PlayerMovement>();
            if (movement == null) movement = go.AddComponent<PlayerMovement>();

            var attack = go.GetComponent<PlayerAutoAttack>();
            if (attack == null) attack = go.AddComponent<PlayerAutoAttack>();

            AssignPrivateField(movement, "inputReader", inputReader);
            AssignPrivateField(movement, "stats", stats);
            AssignPrivateField(attack, "inputReader", inputReader);
            AssignPrivateField(attack, "stats", stats);

            EditorUtility.SetDirty(movement);
            EditorUtility.SetDirty(attack);
            EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            Debug.Log($"[CheonMyeong] '{PrototypeObjectName}' 생성/연결 완료 — 하영 스탯: {HayoungStatsPath}. " +
                      "Console에 에러 없는지 확인 후 Play 눌러서 WASD 이동, 마우스 방향 자동 공격(허공 판정) 테스트하세요.");
        }

        private static CharacterStatsSO EnsureHayoungStats()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Data");
            }
            if (!AssetDatabase.IsValidFolder(CharactersFolder))
            {
                AssetDatabase.CreateFolder(DataFolder, "Characters");
            }

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

        private static GameObject EnsurePrototypeObject()
        {
            var existing = GameObject.Find(PrototypeObjectName);
            if (existing != null)
            {
                return existing;
            }
            return new GameObject(PrototypeObjectName);
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
