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

        /// <summary>
        /// pos에서 가까운 플레이어 쪽으로 가는 방향(정규화). 길이 없거나 맵 밖이면 false(직선 추격으로).
        /// 261123 - 지원 피드백 "원귀가 일자로 접근": 예전엔 이웃 8칸 중 가장 가까운 한 칸만 골라 방향이 8가지(45도 단위)뿐이라
        /// 같은 칸 근처 원귀가 똑같은 대각선 · 직선 위로 모였다. 이제 더 가까운 이웃 칸들을 '얼마나 더 가까운지'로 가중 평균해
        /// 방향이 연속적으로 바뀐다(거리장 기울기). 대각선은 모서리를 깎지 않게 예전처럼 막힌 옆 칸이 있으면 뺀다.
        /// </summary>
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
            Vector2 sum = Vector2.zero;
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
                if (d >= here)
                {
                    continue;
                }
                Vector2 to = CellCenter(nx, ny) - pos;
                if (to.sqrMagnitude > 0.000001f)
                {
                    sum += to.normalized * (here - d);
                }
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
            if (sum.sqrMagnitude < 0.000001f)
            {
                Vector2 to = CellCenter(cx + NeighborX[best], cy + NeighborY[best]) - pos;
                if (to.sqrMagnitude < 0.000001f)
                {
                    return false;
                }
                sum = to;
            }
            direction = sum.normalized;
            return true;
        }

        /// <summary>
        /// 261123 - from → to 직선이 막힌 칸(물 · 물체 · 구덩이)을 지나지 않는지. 반 칸 간격으로 짚어 본다.
        /// 원귀가 길찾기 없이 곧장 쫓아가도 되는지(벽을 끼고 돌 필요가 없는지) 판단용 - 원귀마다 0.25초에 한 번 정도만 부른다.
        /// </summary>
        public bool HasClearLine(Vector2 from, Vector2 to)
        {
            EnsureBuilt();
            float length = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (cellSize * 0.5f)));
            for (int i = 1; i <= steps; i++)
            {
                if (IsBlockedFast(Vector2.Lerp(from, to, i / (float)steps)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>261123 - 플레이어에게 걸어서 갈 수 있는 빈 칸인지(막힘 · 울타리 안 고립 지역 제외). 원귀 생성 위치 검사용.</summary>
        public bool IsReachable(Vector2 pos)
        {
            EnsureFresh();
            if (!ToCell(pos, out int cx, out int cy))
            {
                return false;
            }
            int index = cy * width + cx;
            return !blocked[index] && distance[index] != Unreached;
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

        /// <summary>
        /// 261122 - 보스가 플레이어를 내던질 때 떨어질 자리(지원 버그: 억쇠가 대숲 · 울타리 너머 맵 밖으로 던짐).
        /// from → to 직선에서 to에 가장 가까운 "걸을 수 있는" 점(막힌 칸 · 구덩이를 몸 반지름만큼 피함)을 고른다.
        /// 직선 위에 하나도 없으면 to 주변에서 가장 가까운 빈 칸, 그것도 없으면 from.
        /// </summary>
        public Vector2 ResolveLanding(Vector2 from, Vector2 to, float bodyRadius)
        {
            EnsureBuilt();
            float distance = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (cellSize * 0.5f)));
            for (int i = steps; i >= 0; i--)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                if (IsFree(p, bodyRadius))
                {
                    return p;
                }
            }
            return TryFindNearestFree(to, bodyRadius, LandingSearchCells, out Vector2 free) ? free : from;
        }

        /// <summary>261122 - 이 자리가 막혀 있으면(벽 · 물체 · 구덩이 안) 가장 가까운 빈 자리로, 아니면 그대로.</summary>
        public Vector2 ResolveStuck(Vector2 position, float bodyRadius)
        {
            EnsureBuilt();
            if (IsFree(position, bodyRadius))
            {
                return position;
            }
            return TryFindNearestFree(position, bodyRadius, LandingSearchCells, out Vector2 free) ? free : position;
        }

        private const int LandingSearchCells = 24; // 12유닛

        private bool IsFree(Vector2 p, float r)
        {
            return !IsBlockedFast(p)
                   && !IsBlockedFast(p + new Vector2(r, 0f)) && !IsBlockedFast(p - new Vector2(r, 0f))
                   && !IsBlockedFast(p + new Vector2(0f, r)) && !IsBlockedFast(p - new Vector2(0f, r))
                   && !Boss.BossPitField.Overlaps(p, r);
        }

        /// <summary>center 칸에서 바깥으로 한 겹씩(정사각 고리) 넓혀 가며 처음 찾은 빈 칸 가운데. 고리 안에서는 가장 가까운 칸.</summary>
        private bool TryFindNearestFree(Vector2 center, float bodyRadius, int maxRing, out Vector2 result)
        {
            result = center;
            ToCell(center, out int cx, out int cy);
            for (int ring = 1; ring <= maxRing; ring++)
            {
                float best = float.MaxValue;
                bool found = false;
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring)
                        {
                            continue; // 고리 테두리만
                        }
                        int x = cx + dx;
                        int y = cy + dy;
                        if (x < 0 || y < 0 || x >= width || y >= height)
                        {
                            continue;
                        }
                        Vector2 p = CellCenter(x, y);
                        float d = (p - center).sqrMagnitude;
                        if (d < best && IsFree(p, bodyRadius))
                        {
                            best = d;
                            result = p;
                            found = true;
                        }
                    }
                }
                if (found)
                {
                    return true;
                }
            }
            return false;
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
