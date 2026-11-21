using System;
using System.Collections.Generic;
using System.IO;
using CheonMyeong.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace CheonMyeong.EditorTools
{
    /// <summary>
    /// 261121 추가 - 새 맵 「달밤의 폐장터」 160×160 타일(맵 배치 문서 v1.5 · 기획서 v1.2) 한 번에 깔기.
    /// 메뉴: CheonMyeong/Setup/Build Map - Dalbam 160 (달밤의 폐장터 v1.5)
    /// 배치 문서 7장 「자동 배치 절차」 그대로:
    /// 1) Maptile-Sheet(32px · PPU 64 · Point · 압축 없음, 칸 이름 = Maptile-Sheet_(프레임×64 + 행×8 + 열))로 Tile 에셋을 만든다.
    /// 2) Grid(칸 0.5, 위치 (−40, −40)) + Ground · Collision 타일맵. GroundTiles.csv "f:c:r"을 셀 (x, 159 − y)에.
    /// 3) TerrainVertex.csv에서 타일 네 모서리 중 하나라도 4(대숲) · 5(물)이면 Collision에 칠하고, 맵 끝 테두리도 막는다.
    /// 4) Placement.json 물체 468개(Obj_* 스프라이트, 피벗 아래 가운데 · PPU 64)를 위치 · 배율 · 뒤집기 · 회전대로 놓고,
    ///    5-1 표대로 밑동 콜라이더를 MapSolid 레이어에(투사체도 막음). 풀숲 · 멍석 · 디딘돌 · 바구니는 콜라이더 없음.
    /// 5) MapBounds(플레이 영역 · 시작 = 남문 안쪽 · 카메라 = Ground 범위 · 박석 마당 · 당산나무 · 원귀 생성 문 3곳) +
    ///    MapFlowField(원귀 길찾기). 당산나무에는 잎 반투명(CanopyFade).
    /// 6) 2D 렌더러 Transparency Sort Mode = Custom Axis (0,1,0) - 물체 · 캐릭터 · 원귀가 Y로 앞뒤 정렬(모두 Default 정렬 · 순서 0).
    /// 예전 40×40 맵(Map_DalbamPyejangteo)은 씬에서 지운다(그 메뉴로 언제든 다시 만들 수 있다). 다시 실행해도 안전.
    /// </summary>
    public static partial class CheonMyeongPrototypeSetup
    {
        private const string Map160RootName = "Map_Dalbam160";
        private const string Map160TileSheetPath = "Assets/Art/Tiles/Maptile-Sheet.png";
        private const string Map160ObjectSheetPath = "Assets/Art/Tiles/Sprite-0003-Sheet.png";
        private const string Map160TileAssetFolder = "Assets/Art/Tiles/Dalbam160";
        private const string Map160GroundCsvPath = "Assets/Data/Maps/Dalbam160_GroundTiles.csv";
        private const string Map160VertexCsvPath = "Assets/Data/Maps/Dalbam160_TerrainVertex.csv";
        private const string Map160PlacementPath = "Assets/Data/Maps/Dalbam160_Placement.json";
        private const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
        private const int Map160Size = 160;
        private const float Map160Cell = 0.5f;
        private const float Map160Ppu = 64f;

        [Serializable]
        private class Map160Placement
        {
            public Map160Object[] objects;
        }

        [Serializable]
        private class Map160Object
        {
            public string id;
            public int[] sheet_rect;
            public float rotate;
            public float scale;
            public bool flip_x;
            public float unity_x;
            public float unity_y;
        }

        [MenuItem("CheonMyeong/Setup/Build Map - Dalbam 160 (달밤의 폐장터 v1.5)")]
        public static void BuildDalbam160Map()
        {
            if (!File.Exists(Map160GroundCsvPath) || !File.Exists(Map160VertexCsvPath) || !File.Exists(Map160PlacementPath))
            {
                Debug.LogError("[CheonMyeong] Assets/Data/Maps/Dalbam160_* 데이터 파일 3개가 필요합니다.");
                return;
            }

            EnsureMap160SheetImport(Map160TileSheetPath, SpriteMeshType.FullRect);
            EnsureMap160SheetImport(Map160ObjectSheetPath, SpriteMeshType.Tight);
            Dictionary<string, Sprite> tileSprites = LoadSpritesByName(Map160TileSheetPath);
            Dictionary<string, Sprite> objectSprites = LoadSpritesByName(Map160ObjectSheetPath);
            if (tileSprites.Count == 0 || objectSprites.Count == 0)
            {
                Debug.LogError("[CheonMyeong] Maptile-Sheet · Sprite-0003-Sheet 스프라이트를 찾지 못했습니다 - 두 그림의 .meta(자르기)를 확인하세요.");
                return;
            }

            string[] groundRows = File.ReadAllLines(Map160GroundCsvPath);
            int[,] vertices = ReadVertexCsv(Map160VertexCsvPath);
            var placement = JsonUtility.FromJson<Map160Placement>(File.ReadAllText(Map160PlacementPath));
            if (placement == null || placement.objects == null || vertices == null)
            {
                Debug.LogError("[CheonMyeong] 160 맵 데이터를 읽지 못했습니다.");
                return;
            }

            EnsureLayer(MapLayerName);
            EnsureLayer(MapObstacles.SolidLayerName);
            int mapLayer = LayerMask.NameToLayer(MapLayerName);
            int solidLayer = LayerMask.NameToLayer(MapObstacles.SolidLayerName);
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
            if (enemyLayer != -1)
            {
                if (mapLayer != -1) Physics2D.IgnoreLayerCollision(enemyLayer, mapLayer, true);
                if (solidLayer != -1) Physics2D.IgnoreLayerCollision(enemyLayer, solidLayer, true);
            }
            SetCustomAxisSort();

            Dictionary<string, Tile> tiles = EnsureMap160Tiles(tileSprites);
            Tile blockTile = EnsureMap160BlockTile(tileSprites);

            int sceneCount = ForEachSceneContaining(SpawnerObjectName, (scene, spawnerGo) =>
            {
                GameObject old = FindInScene(scene, DalbamMapRootName);
                if (old != null) Object.DestroyImmediate(old);
                GameObject existing = FindInScene(scene, Map160RootName);
                if (existing != null) Object.DestroyImmediate(existing);

                var root = new GameObject(Map160RootName);
                SceneManager.MoveGameObjectToScene(root, scene);

                var gridGo = new GameObject("Grid");
                gridGo.transform.SetParent(root.transform, false);
                gridGo.transform.position = new Vector3(-Map160Size * Map160Cell * 0.5f, -Map160Size * Map160Cell * 0.5f, 0f);
                var grid = gridGo.AddComponent<Grid>();
                grid.cellSize = new Vector3(Map160Cell, Map160Cell, 0f);

                Tilemap ground = CreateTilemapLayer(gridGo.transform, "Ground", -100, -1);
                Tilemap collision = CreateTilemapLayer(gridGo.transform, "Collision", -90, mapLayer);
                collision.GetComponent<TilemapRenderer>().enabled = false; // 대숲 · 물 그림은 Ground에 있고, 이건 막는 판정만
                FillMap160Ground(ground, groundRows, tiles);
                FillMap160Collision(collision, vertices, blockTile);

                var body = collision.gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var tilemapCollider = collision.gameObject.AddComponent<TilemapCollider2D>();
                tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                collision.gameObject.AddComponent<CompositeCollider2D>();

                var objectsRoot = new GameObject("Objects");
                objectsRoot.transform.SetParent(root.transform, false);
                PlaceMap160Objects(objectsRoot.transform, placement.objects, objectSprites, solidLayer);

                SetupMap160Bounds(root, ground);
                var flow = root.AddComponent<MapFlowField>();
                var flowSo = new SerializedObject(flow);
                flowSo.FindProperty("collisionMap").objectReferenceValue = collision;
                flowSo.FindProperty("origin").vector2Value = gridGo.transform.position;
                flowSo.FindProperty("cellSize").floatValue = Map160Cell;
                flowSo.FindProperty("width").intValue = Map160Size;
                flowSo.FindProperty("height").intValue = Map160Size;
                flowSo.ApplyModifiedPropertiesWithoutUndo();
            });

            if (sceneCount == 0)
            {
                Debug.LogError($"[CheonMyeong] '{SpawnerObjectName}'가 있는 Gameplay 씬을 찾지 못했습니다.");
                return;
            }
            Debug.Log($"[CheonMyeong] 달밤의 폐장터 160×160 완료(씬 {sceneCount}개) - 바닥 타일 {tiles.Count}종, 물체 {placement.objects.Length}개. " +
                      "원귀는 무너진 문 3곳에서 나오고, 판은 남문 안쪽에서 시작합니다.");
        }

        /// <summary>그림 임포트 값만 맞춘다(자르기는 .meta에 이미 들어 있음 - 칸 이름이 배치 데이터와 맞아야 해서 다시 자르지 않는다).</summary>
        private static void EnsureMap160SheetImport(string path, SpriteMeshType meshType)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }
            bool changed = false;
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            int needed = Mathf.Min(16384, Mathf.NextPowerOfTwo(Mathf.Max(w, h)));
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Multiple) { importer.spriteImportMode = SpriteImportMode.Multiple; changed = true; }
            if (importer.maxTextureSize < needed) { importer.maxTextureSize = needed; changed = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, Map160Ppu)) { importer.spritePixelsPerUnit = Map160Ppu; changed = true; }
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != meshType)
            {
                settings.spriteMeshType = meshType;
                importer.SetTextureSettings(settings);
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Sprite> LoadSpritesByName(string path)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite && !result.ContainsKey(sprite.name))
                {
                    result.Add(sprite.name, sprite);
                }
            }
            return result;
        }

        private static int[,] ReadVertexCsv(string path)
        {
            string[] lines = File.ReadAllLines(path);
            int rows = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0) rows++;
            }
            if (rows == 0)
            {
                return null;
            }
            int cols = lines[0].Split(',').Length;
            var result = new int[cols, rows];
            int y = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] cells = line.Split(',');
                for (int x = 0; x < cols && x < cells.Length; x++)
                {
                    int.TryParse(cells[x], out result[x, y]);
                }
                y++;
            }
            return result;
        }

        /// <summary>쓰는 칸만 Tile 에셋으로(이미 있으면 그림만 다시 맞춘다).</summary>
        private static Dictionary<string, Tile> EnsureMap160Tiles(Dictionary<string, Sprite> sprites)
        {
            EnsureFolderRecursive(Map160TileAssetFolder);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Map160GroundCsvPath))
            {
                foreach (string cell in line.Split(','))
                {
                    string token = cell.Trim();
                    if (token.Length > 0) used.Add(token);
                }
            }
            var tiles = new Dictionary<string, Tile>(StringComparer.Ordinal);
            foreach (string token in used)
            {
                string[] p = token.Split(':');
                if (p.Length != 3 || !int.TryParse(p[0], out int f) || !int.TryParse(p[1], out int c) || !int.TryParse(p[2], out int r))
                {
                    continue;
                }
                string spriteName = $"Maptile-Sheet_{f * 64 + r * 8 + c}";
                if (!sprites.TryGetValue(spriteName, out Sprite sprite))
                {
                    Debug.LogWarning($"[CheonMyeong] '{spriteName}' 스프라이트가 없습니다(칸 {token}).");
                    continue;
                }
                string tilePath = $"{Map160TileAssetFolder}/{spriteName}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                tiles[token] = tile;
            }
            AssetDatabase.SaveAssets();
            return tiles;
        }

        private static Tile EnsureMap160BlockTile(Dictionary<string, Sprite> sprites)
        {
            string path = $"{Map160TileAssetFolder}/Block.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            sprites.TryGetValue("Maptile-Sheet_0", out Sprite sprite);
            tile.sprite = sprite;                        // 렌더러는 꺼 둔다 - 그림은 안 보임
            tile.colliderType = Tile.ColliderType.Grid; // 칸 모양 그대로 막음
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            return tile;
        }

        private static void FillMap160Ground(Tilemap tilemap, string[] rows, Dictionary<string, Tile> tiles)
        {
            int y = 0;
            for (int i = 0; i < rows.Length && y < Map160Size; i++)
            {
                string line = rows[i].Trim();
                if (line.Length == 0) continue;
                string[] cells = line.Split(',');
                for (int x = 0; x < cells.Length && x < Map160Size; x++)
                {
                    if (tiles.TryGetValue(cells[x].Trim(), out Tile tile))
                    {
                        tilemap.SetTile(new Vector3Int(x, Map160Size - 1 - y, 0), tile); // CSV y는 아래로 → Unity는 위로
                    }
                }
                y++;
            }
        }

        /// <summary>대숲(4) · 물(5)이 네 모서리 중 하나라도 있는 칸 + 맵 끝 테두리 한 줄(남문 · 무너진 문 밖으로 못 나가게).</summary>
        private static void FillMap160Collision(Tilemap tilemap, int[,] v, Tile block)
        {
            int vw = v.GetLength(0);
            int vh = v.GetLength(1);
            for (int y = 0; y < Map160Size; y++)
            {
                for (int x = 0; x < Map160Size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == Map160Size - 1 || y == Map160Size - 1;
                    bool blocked = edge;
                    if (!blocked && x + 1 < vw && y + 1 < vh)
                    {
                        blocked = IsBlockingTerrain(v[x, y]) || IsBlockingTerrain(v[x + 1, y])
                                  || IsBlockingTerrain(v[x, y + 1]) || IsBlockingTerrain(v[x + 1, y + 1]);
                    }
                    if (blocked)
                    {
                        tilemap.SetTile(new Vector3Int(x, Map160Size - 1 - y, 0), block);
                    }
                }
            }
        }

        private static bool IsBlockingTerrain(int id) => id == 4 || id == 5;

        private static void PlaceMap160Objects(Transform parent, Map160Object[] objects, Dictionary<string, Sprite> sprites, int solidLayer)
        {
            int index = 0;
            foreach (Map160Object o in objects)
            {
                string kind = o.id.StartsWith("rubble", StringComparison.Ordinal) ? "rubble" : o.id;
                if (!sprites.TryGetValue("Obj_" + kind, out Sprite sprite))
                {
                    Debug.LogWarning($"[CheonMyeong] 물체 그림 'Obj_{kind}'가 없습니다.");
                    continue;
                }
                var go = new GameObject($"{o.id}_{index++:000}");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(o.unity_x, o.unity_y, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, o.rotate);
                float s = o.scale > 0f ? o.scale : 1f;
                go.transform.localScale = new Vector3(s, s, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.flipX = o.flip_x;
                sr.sortingOrder = 0; // 캐릭터 · 원귀와 같은 칸 - Y축 정렬로 앞뒤가 정해진다
                if (AddMap160Collider(go, kind, sprite.rect.width / Map160Ppu, sprite.rect.height / Map160Ppu, s) && solidLayer != -1)
                {
                    go.layer = solidLayer;
                }
                if (kind == "tree")
                {
                    go.AddComponent<CanopyFade>();
                }
            }
        }

        /// <summary>
        /// 배치 문서 5-1 표 콜라이더(밑동만 작게 - 지붕 · 잎 · 그늘은 없음). w · h = 그림 크기(로컬 유닛), scale = 배율.
        /// 콜라이더가 없는 장식(풀숲 · 멍석 · 디딘돌 · 바구니)은 false.
        /// </summary>
        private static bool AddMap160Collider(GameObject go, string kind, float w, float h, float scale)
        {
            switch (kind)
            {
                case "tree":
                    AddCircle(go, 0.25f / scale, 0.25f / scale); // 줄기 밑동 1칸(0.5×0.5 유닛) 원
                    return true;
                case "gate":
                    AddBox(go, new Vector2(w * 0.12f, h * 0.35f), new Vector2(-w * 0.42f, h * 0.175f)); // 기둥
                    AddBox(go, new Vector2(w * 0.12f, h * 0.35f), new Vector2(w * 0.42f, h * 0.175f));
                    AddBox(go, new Vector2(w, h * 0.1f), new Vector2(0f, h * 0.05f));                   // 문 아래쪽(밖으로 못 나감)
                    return true;
                case "rubble":
                    AddBox(go, new Vector2(w, h), new Vector2(0f, h * 0.5f)); // 전체 막음
                    return true;
                case "jumak":
                case "house_a":
                case "house_b":
                case "shrine":
                    AddBox(go, new Vector2(w * 0.9f, h / 3f), new Vector2(0f, h / 6f)); // 아래 1/3
                    return true;
                case "pavilion":
                    AddCircle(go, w * 0.06f, h * 0.05f, -w * 0.4f);
                    AddCircle(go, w * 0.06f, h * 0.05f, w * 0.4f);
                    AddCircle(go, w * 0.06f, h * 0.32f, -w * 0.4f);
                    AddCircle(go, w * 0.06f, h * 0.32f, w * 0.4f);
                    return true;
                case "stall_a":
                case "stall_b":
                    AddBox(go, new Vector2(w * 0.9f, h * 0.5f), new Vector2(0f, h * 0.25f));
                    return true;
                case "bamboo_a":
                case "bamboo_b":
                    AddBox(go, new Vector2(w * 0.5f, h * 0.2f), new Vector2(0f, h * 0.1f));
                    return true;
                case "fence_h":
                    AddBox(go, new Vector2(w, h * 0.35f), new Vector2(0f, h * 0.175f));
                    return true;
                case "fence_v":
                    AddBox(go, new Vector2(w * 0.45f, h), new Vector2(0f, h * 0.5f));
                    return true;
                case "jang_m":
                case "jang_f":
                case "lamp_on":
                case "lamp_off":
                case "sotdae":
                    AddCircle(go, 0.12f / scale, 0.12f / scale);
                    return true;
                case "stree":
                    AddCircle(go, 0.15f / scale, 0.15f / scale);
                    return true;
                case "doltap":
                case "rock1":
                case "rock2":
                case "jar":
                {
                    float r = Mathf.Min(w, h) * 0.35f;
                    AddCircle(go, r, r);
                    return true;
                }
                default:
                    return false; // tuft1 · tuft2 · mat · step1~4 · basket
            }
        }

        private static void AddBox(GameObject go, Vector2 size, Vector2 offset)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.offset = offset;
        }

        private static void AddCircle(GameObject go, float radius, float offsetY, float offsetX = 0f)
        {
            var circle = go.AddComponent<CircleCollider2D>();
            circle.radius = radius;
            circle.offset = new Vector2(offsetX, offsetY);
        }

        /// <summary>
        /// 맵 배치 문서 1장 · 6장 좌표(타일 → Unity = ((x − 80) × 0.5, (80 − y) × 0.5)):
        /// 플레이 영역 = 대숲 띠(8타일 = 4유닛) 안쪽, 시작 = 남쪽 큰길(타일 x 77~82) 남문 안쪽, 박석 마당 = 타일 60~99,
        /// 당산나무 밑동 = 타일 (80, 85), 원귀 생성 = 무너진 문 3곳 앞 흙길(북 (79.5, 8) · 서 (6, 80) · 동 (154, 80) 근처).
        /// </summary>
        private static void SetupMap160Bounds(GameObject root, Tilemap ground)
        {
            var bounds = root.AddComponent<MapBounds>();
            float half = Map160Size * Map160Cell * 0.5f; // 40
            SetSerializedRect(bounds, "playableArea", new Rect(-half + 4f, -half + 4f, (half - 4f) * 2f, (half - 4f) * 2f));
            SetSerializedVector2(bounds, "startPoint", new Vector2(0f, -34f));
            SetSerializedRect(bounds, "cameraArea", new Rect(-half, -half, half * 2f, half * 2f));
            var so = new SerializedObject(bounds);
            so.FindProperty("cameraAreaSource").objectReferenceValue = ground;
            so.FindProperty("bossPlaza").rectValue = new Rect(-10f, -10f, 20f, 20f);
            so.FindProperty("useBossSpawnPoint").boolValue = true;
            so.FindProperty("bossSpawnPoint").vector2Value = new Vector2(0f, 4f); // 당산나무(밑동 (0, −2.5)) 북쪽
            so.FindProperty("hasBossTree").boolValue = true;
            so.FindProperty("bossTreeCenter").vector2Value = new Vector2(0f, -2.5f);
            so.FindProperty("pitTreeClearRadius").floatValue = 3f;
            so.FindProperty("pitEdgeBand").floatValue = 4f;
            SerializedProperty gates = so.FindProperty("enemyGateAreas");
            gates.arraySize = 3;
            gates.GetArrayElementAtIndex(0).rectValue = new Rect(-1.5f, 34f, 3f, 2f);   // 북 - 타일 x 77~82, y 8~12
            gates.GetArrayElementAtIndex(1).rectValue = new Rect(-37f, -1f, 2f, 2.5f);  // 서 - 타일 x 6~10, y 77~82
            gates.GetArrayElementAtIndex(2).rectValue = new Rect(35f, -1f, 2f, 2.5f);   // 동 - 타일 x 150~154, y 77~82
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>2D 렌더러 Transparency Sort Mode = Custom Axis (0, 1, 0) - 아래(화면 앞)에 있는 것이 앞에 그려진다.</summary>
        private static void SetCustomAxisSort()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Renderer2DPath);
            if (renderer == null)
            {
                Debug.LogWarning($"[CheonMyeong] '{Renderer2DPath}'가 없어 정렬 축을 못 바꿨습니다 - 2D Renderer Data의 Transparency Sort Mode를 Custom Axis (0,1,0)로 직접 바꾸세요.");
                return;
            }
            var so = new SerializedObject(renderer);
            SerializedProperty mode = so.FindProperty("m_TransparencySortMode");
            SerializedProperty axis = so.FindProperty("m_TransparencySortAxis");
            if (mode != null) mode.intValue = 3; // TransparencySortMode.CustomAxis
            if (axis != null) axis.vector3Value = new Vector3(0f, 1f, 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
        }
    }
}
