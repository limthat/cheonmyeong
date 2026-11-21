using CheonMyeong.Player;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CheonMyeong.Maps
{
    /// <summary>
    /// 261121 추가 - 160 맵(달밤의 폐장터 v1.5) 원귀 길찾기. 원귀는 수백 마리라 각자 길을 찾지 않고, 맵 칸(0.5유닛 = 타일 1장)마다
    /// "가장 가까운 플레이어까지 몇 칸"을 한 번에 채운 거리장(flow field, 여러 출발점 BFS)을 같이 쓴다. 원귀는 자기 칸 이웃 8칸 중
    /// 더 가까운 칸 쪽으로만 움직이면 집 · 울타리 · 물을 돌아서 온다.
    /// - 막힌 칸: Collision 타일맵(대숲 · 물) + MapSolid 콜라이더(집 · 나무 · 좌판 등 딱딱한 물체, 맵을 만들 때 한 번만 읽음)
    ///   + 땅 꺼짐 구덩이(BossPitField, 보스전 동안만 - 생길 때마다 다음 갱신에 반영).
    /// - 갱신: refreshSeconds마다(기본 0.25초) 서버에서만. 160×160 = 25,600칸이라 한 번에 1ms 안쪽.
    /// - 원귀는 Map 레이어와 충돌을 꺼 둔 트리거라 Steer로 막힌 칸에 들어가지 않게 속도를 걸러 준다(벽처럼 미끄러짐).
    /// 메모리: 칸 배열 3개(약 128KB)를 맵이 있는 동안만 들고 있고, 매 갱신마다 새로 할당하지 않는다.
    /// </summary>
    public sealed class MapFlowField : MonoBehaviour
    {
        private const string SolidLayerName = "MapSolid";
        private const ushort Unreached = ushort.MaxValue;

        public static MapFlowField Instance { get; private set; }

        [SerializeField] private Tilemap collisionMap;
        [Tooltip("격자 왼쪽 아래 월드 좌표.")]
        [SerializeField] private Vector2 origin = new Vector2(-40f, -40f);
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private int width = 160;
        [SerializeField] private int height = 160;
        [SerializeField] private float refreshSeconds = 0.25f;

        private bool[] staticBlocked;
        private bool[] blocked;
        private ushort[] distance;
        private int[] queue;
        private float nextRefresh;
        private bool built;
        private bool anyPits;

        private static readonly int[] NeighborX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighborY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>pos에서 가까운 플레이어 쪽으로 가는 방향(정규화). 길이 없거나 맵 밖이면 false(직선 추격으로).</summary>
        public bool TryGetDirection(Vector2 pos, out Vector2 direction)
        {
            direction = Vector2.zero;
            EnsureFresh();
            if (!ToCell(pos, out int cx, out int cy))
            {
                return false;
            }
            int index = cy * width + cx;
            ushort here = distance[index];
            if (here == Unreached || here == 0)
            {
                return false;
            }
            int best = -1;
            ushort bestDistance = here;
            for (int n = 0; n < 8; n++)
            {
                int nx = cx + NeighborX[n];
                int ny = cy + NeighborY[n];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }
                if (n >= 4 && (blocked[cy * width + nx] || blocked[ny * width + cx]))
                {
                    continue; // 대각선으로 모서리를 깎아 지나가지 않게
                }
                ushort d = distance[ny * width + nx];
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = n;
                }
            }
            if (best < 0)
            {
                return false;
            }
            Vector2 target = CellCenter(cx + NeighborX[best], cy + NeighborY[best]);
            Vector2 to = target - pos;
            if (to.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            direction = to.normalized;
            return true;
        }

        /// <summary>막힌 칸인지(맵 밖은 막힘).</summary>
        public bool IsBlocked(Vector2 pos)
        {
            EnsureBuilt();
            return !ToCell(pos, out int cx, out int cy) || blocked[cy * width + cx];
        }

        /// <summary>다음 스텝 위치가 막힌 칸이면 그 축 속도를 지워 벽처럼 미끄러지게 한다(원귀 · 보스 서버 이동용).</summary>
        public Vector2 Steer(Vector2 pos, Vector2 velocity, float dt)
        {
            EnsureBuilt();
            Vector2 next = pos + velocity * dt;
            if (!IsBlockedFast(next))
            {
                return velocity;
            }
            if (IsBlockedFast(pos))
            {
                return velocity; // 이미 막힌 칸 안(밀려 들어옴) - 막지 않고 길찾기 방향으로 빠져나가게 둔다
            }
            var xOnly = new Vector2(velocity.x, 0f);
            if (!IsBlockedFast(pos + xOnly * dt))
            {
                return xOnly;
            }
            var yOnly = new Vector2(0f, velocity.y);
            if (!IsBlockedFast(pos + yOnly * dt))
            {
                return yOnly;
            }
            return Vector2.zero;
        }

        private bool IsBlockedFast(Vector2 pos)
        {
            return !ToCell(pos, out int cx, out int cy) || blocked[cy * width + cx];
        }

        private bool ToCell(Vector2 pos, out int cx, out int cy)
        {
            cx = Mathf.FloorToInt((pos.x - origin.x) / cellSize);
            cy = Mathf.FloorToInt((pos.y - origin.y) / cellSize);
            return cx >= 0 && cy >= 0 && cx < width && cy < height;
        }

        private Vector2 CellCenter(int cx, int cy)
        {
            return new Vector2(origin.x + (cx + 0.5f) * cellSize, origin.y + (cy + 0.5f) * cellSize);
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }
            built = true;
            int total = width * height;
            staticBlocked = new bool[total];
            blocked = new bool[total];
            distance = new ushort[total];
            queue = new int[total];

            Physics2D.SyncTransforms();
            int solidMask = LayerMask.GetMask(SolidLayerName);
            var cellBox = new Vector2(cellSize * 0.8f, cellSize * 0.8f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool b = collisionMap != null && collisionMap.HasTile(new Vector3Int(x, y, 0));
                    if (!b && solidMask != 0)
                    {
                        b = Physics2D.OverlapBox(CellCenter(x, y), cellBox, 0f, solidMask) != null;
                    }
                    staticBlocked[y * width + x] = b;
                }
            }
            System.Array.Copy(staticBlocked, blocked, total);
        }

        private void EnsureFresh()
        {
            EnsureBuilt();
            if (Time.time < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.time + refreshSeconds;
            UpdateDynamicBlocks();
            Rebuild();
        }

        /// <summary>땅 꺼짐 구덩이(보스전 동안만)를 막힌 칸에 더한다. 구덩이가 없어지면 원래대로.</summary>
        private void UpdateDynamicBlocks()
        {
            int pits = Boss.BossPitField.Count;
            if (pits == 0 && !anyPits)
            {
                return;
            }
            System.Array.Copy(staticBlocked, blocked, staticBlocked.Length);
            anyPits = pits > 0;
            if (!anyPits)
            {
                return;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (!blocked[i] && Boss.BossPitField.Overlaps(CellCenter(x, y), cellSize * 0.4f))
                    {
                        blocked[i] = true;
                    }
                }
            }
        }

        /// <summary>살아 있는 플레이어 칸들에서 동시에 퍼지는 BFS(가장 가까운 플레이어까지 칸 수).</summary>
        private void Rebuild()
        {
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = Unreached;
            }
            int head = 0;
            int tail = 0;
            for (int p = 0; p < PlayerHealth.ActivePlayers.Count; p++)
            {
                PlayerHealth player = PlayerHealth.ActivePlayers[p];
                if (player == null || player.IsDead || !ToCell(player.transform.position, out int px, out int py))
                {
                    continue;
                }
                int start = py * width + px;
                if (distance[start] != 0)
                {
                    distance[start] = 0;
                    queue[tail++] = start;
                }
            }
            while (head < tail)
            {
                int current = queue[head++];
                int cx = current % width;
                int cy = current / width;
                ushort next = (ushort)(distance[current] + 1);
                for (int n = 0; n < 4; n++)
                {
                    int nx = cx + NeighborX[n];
                    int ny = cy + NeighborY[n];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }
                    int ni = ny * width + nx;
                    if (blocked[ni] || distance[ni] <= next)
                    {
                        continue;
                    }
                    distance[ni] = next;
                    queue[tail++] = ni;
                }
            }
        }
    }
}
