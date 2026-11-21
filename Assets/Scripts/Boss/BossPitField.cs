using System.Collections.Generic;
using CheonMyeong.Combat;
using CheonMyeong.Maps;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261121 - 억쇠 '땅 꺼짐' 구덩이(기획서 v1.2 9장 · 맵 배치 문서 v1.5).
    /// - 구덩이는 벽과 같다: 플레이어 · 원귀 · 보스 모두 못 지나가고 투사체는 지나간다. 최대 6개, 넘으면 가장 오래된 것부터 메워진다.
    /// - 플레이어: 이동이 서버 물리(Rigidbody2D)라 Map 레이어 BoxCollider2D로 막는다(플레이어 투사체는 콜라이더가 아니라 겹침 검사라
    ///   그대로 지나간다).
    /// - 원귀 · 보스: Enemy 레이어는 Map 레이어와 충돌을 꺼 두었으므로(수백 마리 트리거 비용) 콜라이더 대신 Steer/PushOut으로
    ///   매 스텝 속도를 걸러 막는다. 구덩이는 최대 6개라 사각형 6개 검사로 끝난다.
    /// - 그림: Eoksoe_FX의 QUAKE_PIT 1장을 구덩이마다 얹는다(박석 ↔ 구덩이 전환 타일은 안 씀).
    /// 목록 자체는 EoksoeBoss의 NetworkList가 들고 있고, 바뀔 때마다 Rebuild로 모든 화면이 같은 구덩이를 만든다.
    /// </summary>
    public static class BossPitField
    {
        private const string MapLayerName = "Map";
        private const int SortingOrder = -60; // 바닥(-100/-90) 위, 바닥 예고(-50) 아래

        private static readonly List<Rect> Pits = new List<Rect>(8);
        private static readonly List<GameObject> Views = new List<GameObject>(8);
        private static Transform root;
        private static Rect pitSpriteBounds;
        private static Sprite boundsSprite;

        public static int Count => Pits.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pits.Clear();
            Views.Clear();
            root = null;
            boundsSprite = null;
        }

        /// <summary>구덩이 목록을 다시 만든다(가운데 좌표 목록 · 한 변 size). 바뀐 것만 고치지 않고 통째로 - 최대 6개라 충분히 싸다.</summary>
        public static void Rebuild(IReadOnlyList<Vector2> centers, float size, Sprite pitSprite)
        {
            Pits.Clear();
            if (centers != null)
            {
                for (int i = 0; i < centers.Count; i++)
                {
                    Pits.Add(new Rect(centers[i].x - size * 0.5f, centers[i].y - size * 0.5f, size, size));
                }
            }

            if (Pits.Count > 0 && root == null)
            {
                root = new GameObject("BossPits").transform;
            }
            while (Views.Count < Pits.Count)
            {
                Views.Add(CreateView());
            }
            for (int i = 0; i < Views.Count; i++)
            {
                GameObject view = Views[i];
                if (view == null)
                {
                    continue;
                }
                bool on = i < Pits.Count;
                if (view.activeSelf != on)
                {
                    view.SetActive(on);
                }
                if (on)
                {
                    Place(view, Pits[i], pitSprite);
                }
            }
        }

        public static void Clear()
        {
            Rebuild(null, 1f, null);
        }

        /// <summary>반지름 radius 원이 구덩이 하나라도 겹치는지.</summary>
        public static bool Overlaps(Vector2 position, float radius)
        {
            for (int i = 0; i < Pits.Count; i++)
            {
                if (CircleRect(position, radius, Pits[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>사각형 area가 구덩이와 겹치는지(새 구덩이 자리 검사 등).</summary>
        public static bool Overlaps(Rect area)
        {
            for (int i = 0; i < Pits.Count; i++)
            {
                if (Pits[i].Overlaps(area))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 서버 전용 이동 거르기 - 다음 스텝 위치가 구덩이에 들어가면 그 면 쪽 속도 성분을 지워 벽처럼 미끄러지게 한다.
        /// 이미 안에 있으면(밀려 들어감 등) 가장 가까운 면 밖으로 나가는 속도를 준다.
        /// </summary>
        public static Vector2 Steer(Vector2 position, Vector2 velocity, float radius, float dt)
        {
            for (int i = 0; i < Pits.Count; i++)
            {
                Rect r = Inflate(Pits[i], radius);
                if (r.Contains(position))
                {
                    Vector2 outside = NearestOutside(r, position);
                    Vector2 push = outside - position;
                    float speed = Mathf.Max(velocity.magnitude, 3f);
                    return push.sqrMagnitude > 0.000001f ? push.normalized * speed : velocity;
                }
                Vector2 next = position + velocity * dt;
                if (!r.Contains(next))
                {
                    continue;
                }
                // 들어가려는 면: 지금 위치가 사각형의 어느 쪽 바깥에 있는지로 판단.
                if (position.x <= r.xMin && velocity.x > 0f || position.x >= r.xMax && velocity.x < 0f)
                {
                    velocity.x = 0f;
                }
                if (position.y <= r.yMin && velocity.y > 0f || position.y >= r.yMax && velocity.y < 0f)
                {
                    velocity.y = 0f;
                }
            }
            return velocity;
        }

        /// <summary>점이 구덩이 안이면 가장 가까운 바깥(반지름만큼 띄운) 위치, 아니면 그대로.</summary>
        public static Vector2 PushOut(Vector2 position, float radius)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                for (int i = 0; i < Pits.Count; i++)
                {
                    Rect r = Inflate(Pits[i], radius);
                    if (r.Contains(position))
                    {
                        position = NearestOutside(r, position);
                        moved = true;
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
            return position;
        }

        private static Rect Inflate(Rect r, float by)
        {
            return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        }

        private static Vector2 NearestOutside(Rect r, Vector2 p)
        {
            const float eps = 0.02f;
            float left = p.x - r.xMin;
            float right = r.xMax - p.x;
            float down = p.y - r.yMin;
            float up = r.yMax - p.y;
            float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
            if (min == left) return new Vector2(r.xMin - eps, p.y);
            if (min == right) return new Vector2(r.xMax + eps, p.y);
            if (min == down) return new Vector2(p.x, r.yMin - eps);
            return new Vector2(p.x, r.yMax + eps);
        }

        private static bool CircleRect(Vector2 c, float radius, Rect r)
        {
            float dx = Mathf.Max(r.xMin - c.x, 0f, c.x - r.xMax);
            float dy = Mathf.Max(r.yMin - c.y, 0f, c.y - r.yMax);
            return dx * dx + dy * dy < radius * radius;
        }

        private static GameObject CreateView()
        {
            var go = new GameObject("Pit");
            go.transform.SetParent(root, false);
            int mapLayer = LayerMask.NameToLayer(MapLayerName);
            if (mapLayer != -1)
            {
                go.layer = mapLayer;
            }
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = false;
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            var sr = art.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortingOrder;
            return go;
        }

        private static void Place(GameObject view, Rect pit, Sprite pitSprite)
        {
            view.transform.position = new Vector3(pit.center.x, pit.center.y, 0f);
            var box = view.GetComponent<BoxCollider2D>();
            box.size = pit.size;
            box.offset = Vector2.zero;

            Transform art = view.transform.GetChild(0);
            var sr = art.GetComponent<SpriteRenderer>();
            sr.sprite = pitSprite;
            if (pitSprite == null)
            {
                return;
            }
            // QUAKE_PIT의 실제로 그려진 부분이 구덩이 한 변에 꼭 맞게.
            if (boundsSprite != pitSprite)
            {
                boundsSprite = pitSprite;
                pitSpriteBounds = SpriteVisibleBounds.Of(new[] { pitSprite });
            }
            float w = Mathf.Max(0.01f, pitSpriteBounds.width);
            float h = Mathf.Max(0.01f, pitSpriteBounds.height);
            var scale = new Vector3(pit.width / w, pit.height / h, 1f);
            art.localScale = scale;
            art.localPosition = new Vector3(-pitSpriteBounds.center.x * scale.x, -pitSpriteBounds.center.y * scale.y, 0f);
        }

        // ---------- 갇힘 방지 (서버) ----------

        private const float GridStep = 0.5f;
        private static bool[] blockedGrid = new bool[0];
        private static bool[] reachGrid = new bool[0];
        private static readonly Queue<int> Frontier = new Queue<int>(256);

        /// <summary>
        /// 기획서 "갇힘 방지": 구덩이 사이에 갇힌 위치면 가장 가까운 빈 칸(마당 바깥쪽과 이어진 칸)을 돌려준다. 안 갇혔으면 false.
        /// 보스 터 범위를 0.5칸 격자로 나눠 마당 테두리(4칸 띠는 구덩이 금지라 항상 열려 있음)에서부터 채우기(BFS)를 한다 -
        /// 구덩이가 생길 때만 부르므로 비용 걱정 없음(20×20칸 → 1,600칸).
        /// </summary>
        public static bool TryFindEscape(Vector2 position, float radius, out Vector2 escape)
        {
            escape = position;
            if (Pits.Count == 0)
            {
                return false;
            }
            MapBounds bounds = MapBounds.Instance;
            Rect area = bounds != null ? bounds.BossPlaza : new Rect(position - Vector2.one * 15f, Vector2.one * 30f);
            if (!area.Contains(position))
            {
                return false;
            }
            int cols = Mathf.Max(1, Mathf.CeilToInt(area.width / GridStep));
            int rows = Mathf.Max(1, Mathf.CeilToInt(area.height / GridStep));
            int total = cols * rows;
            if (blockedGrid.Length < total)
            {
                blockedGrid = new bool[total];
                reachGrid = new bool[total];
            }
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var c = new Vector2(area.xMin + (x + 0.5f) * GridStep, area.yMin + (y + 0.5f) * GridStep);
                    int i = y * cols + x;
                    blockedGrid[i] = Overlaps(c, radius);
                    reachGrid[i] = false;
                }
            }
            Frontier.Clear();
            for (int x = 0; x < cols; x++)
            {
                Seed(x, 0, cols);
                Seed(x, rows - 1, cols);
            }
            for (int y = 0; y < rows; y++)
            {
                Seed(0, y, cols);
                Seed(cols - 1, y, cols);
            }
            while (Frontier.Count > 0)
            {
                int i = Frontier.Dequeue();
                int x = i % cols;
                int y = i / cols;
                if (x > 0) Visit(i - 1);
                if (x < cols - 1) Visit(i + 1);
                if (y > 0) Visit(i - cols);
                if (y < rows - 1) Visit(i + cols);
            }

            int px = Mathf.Clamp(Mathf.FloorToInt((position.x - area.xMin) / GridStep), 0, cols - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt((position.y - area.yMin) / GridStep), 0, rows - 1);
            if (reachGrid[py * cols + px])
            {
                return false;
            }
            float best = float.MaxValue;
            for (int i = 0; i < total; i++)
            {
                if (!reachGrid[i])
                {
                    continue;
                }
                var c = new Vector2(area.xMin + (i % cols + 0.5f) * GridStep, area.yMin + (i / cols + 0.5f) * GridStep);
                float d = (c - position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    escape = c;
                }
            }
            return best < float.MaxValue;
        }

        private static void Seed(int x, int y, int cols)
        {
            int i = y * cols + x;
            if (!blockedGrid[i] && !reachGrid[i])
            {
                reachGrid[i] = true;
                Frontier.Enqueue(i);
            }
        }

        private static void Visit(int i)
        {
            if (!blockedGrid[i] && !reachGrid[i])
            {
                reachGrid[i] = true;
                Frontier.Enqueue(i);
            }
        }
    }
}
