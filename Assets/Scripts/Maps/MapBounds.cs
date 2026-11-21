using UnityEngine;
using UnityEngine.Tilemaps;

namespace CheonMyeong.Maps
{
    /// <summary>
    /// 261027 추가 - 맵 하나의 "안쪽" 정보(기획서 5-2: "몬스터는 화면 밖, 맵 경계 안쪽에서 등장" / 시작 지점 - 기획서 "남쪽 입구
    /// (시작 지점)"). 값은 CheonMyeong/Setup/Build Map 메뉴가 타일맵을 만들 때 같이 채운다 - 맵마다 Gameplay 씬에
    /// 하나씩 두는 씬 싱글턴(PartyProgress/WaveManager와 같은 관례).
    /// - EnemySpawner: 스폰 위치를 playableArea 안으로 제한.
    /// - WaveManager: 새 판 시작 때 플레이어를 startPoint(남쪽 입구)로 옮김.
    /// - PlayerCameraRig: 카메라가 CameraArea(타일맵 전체 범위) 밖을 비추지 않게 함(261101). 맵마다 크기가 달라도
    ///   cameraAreaSource 타일맵에 칠해진 범위에서 자동으로 계산한다.
    /// </summary>
    public class MapBounds : MonoBehaviour
    {
        public static MapBounds Instance { get; private set; }

        [Tooltip("원귀가 나타날 수 있는 맵 안쪽 영역(월드 좌표). 담/대숲 안쪽 땅.")]
        [SerializeField] private Rect playableArea = new Rect(-10f, -10f, 20f, 20f);

        [Tooltip("판 시작 위치(월드 좌표) - 기획서: 남쪽 입구.")]
        [SerializeField] private Vector2 startPoint;

        [Tooltip("플레이어가 여럿일 때 시작 위치를 좌우로 벌리는 간격(유닛).")]
        [SerializeField] private float startSpacing = 1f;

        [Tooltip("261101 - 카메라가 비출 수 있는 범위를 이 타일맵의 칠해진 영역으로 자동 계산한다(보통 바닥 타일맵).\n" +
                 "비어 있으면 cameraArea 값을 그대로 쓴다.")]
        [SerializeField] private Tilemap cameraAreaSource;

        [Tooltip("cameraAreaSource가 없을 때 쓰는 카메라 범위(월드 좌표). 크기가 0이면 제한 없음.")]
        [SerializeField] private Rect cameraArea;

        [Header("보스 터 (261121 · 기획서 v1.2 땅 꺼짐 구덩이 규칙)")]
        [Tooltip("박석 마당(보스 터) 범위(월드). 크기가 0이면 cameraArea(맵 전체)를 보스 터로 본다.")]
        [SerializeField] private Rect bossPlaza;
        [Tooltip("보스가 나타나는 곳(월드). useBossSpawnPoint가 꺼져 있으면 보스 터 한가운데.")]
        [SerializeField] private Vector2 bossSpawnPoint;
        [SerializeField] private bool useBossSpawnPoint;
        [Tooltip("당산나무 줄기 중심(월드) - 구덩이 금지 원의 중심. hasBossTree가 꺼져 있으면 쓰지 않는다.")]
        [SerializeField] private Vector2 bossTreeCenter;
        [SerializeField] private bool hasBossTree;
        [Tooltip("구덩이 금지 반지름(칸) - 나무 중심에서.")]
        [SerializeField] private float pitTreeClearRadius = 3f;
        [Tooltip("구덩이 금지 띠(칸) - 박석 마당 테두리에서 안쪽으로.")]
        [SerializeField] private float pitEdgeBand = 4f;
        [Tooltip("구덩이 금지 구역(월드) - 마당으로 들어오는 디딤돌 십자길 입구.")]
        [SerializeField] private Rect[] pitForbiddenAreas = new Rect[0];

        [Header("원귀 생성 문 (261121 · 맵 배치 문서 v1.5)")]
        [Tooltip("원귀가 밀려 들어오는 곳(월드) - 무너진 문 3곳(북 · 서 · 동) 앞 흙길. 비어 있으면 예전처럼 화면 밖 둘레에서 나온다.")]
        [SerializeField] private Rect[] enemyGateAreas = new Rect[0];

        public Rect PlayableArea => playableArea;

        /// <summary>261121 - 무너진 문 쪽에서 원귀가 나오는 맵인지.</summary>
        public bool HasGateSpawns => enemyGateAreas != null && enemyGateAreas.Length > 0;
        public int GateCount => enemyGateAreas != null ? enemyGateAreas.Length : 0;

        /// <summary>261121 - gate번째 문 앞(음수면 아무 문) 범위 안 무작위 지점.</summary>
        public Vector2 RandomGatePoint(int gate = -1)
        {
            if (!HasGateSpawns)
            {
                return playableArea.center;
            }
            int index = gate >= 0 ? gate % enemyGateAreas.Length : Random.Range(0, enemyGateAreas.Length);
            Rect r = enemyGateAreas[index];
            return new Vector2(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMax));
        }

        /// <summary>261121 - 박석 마당(보스 터). 따로 정하지 않았으면 카메라 범위(맵 전체), 그것도 없으면 플레이 영역.</summary>
        public Rect BossPlaza => bossPlaza.width > 0f && bossPlaza.height > 0f ? bossPlaza
            : (cameraArea.width > 0f ? cameraArea : playableArea);

        /// <summary>261121 - 보스가 나타나는 곳.</summary>
        public Vector2 BossSpawnPoint => useBossSpawnPoint ? bossSpawnPoint : BossPlaza.center;

        /// <summary>
        /// 261121 - 기획서 v1.2 "구덩이 규칙": 박석 마당 테두리 안쪽 4칸 띠 · 십자길 입구 · 당산나무 반지름 3칸 안에는 구덩이를
        /// 만들지 않는다. 구덩이(한 변 size 칸, 가운데 center)가 금지 구역에 걸리면 false.
        /// </summary>
        public bool CanPlacePit(Vector2 center, float size)
        {
            float half = size * 0.5f;
            Rect plaza = BossPlaza;
            Rect allowed = Rect.MinMaxRect(plaza.xMin + pitEdgeBand, plaza.yMin + pitEdgeBand, plaza.xMax - pitEdgeBand, plaza.yMax - pitEdgeBand);
            if (center.x - half < allowed.xMin || center.x + half > allowed.xMax || center.y - half < allowed.yMin || center.y + half > allowed.yMax)
            {
                return false;
            }
            var pit = new Rect(center.x - half, center.y - half, size, size);
            if (hasBossTree)
            {
                // 사각 구덩이에서 나무 중심까지 가장 가까운 거리.
                float dx = Mathf.Max(pit.xMin - bossTreeCenter.x, 0f, bossTreeCenter.x - pit.xMax);
                float dy = Mathf.Max(pit.yMin - bossTreeCenter.y, 0f, bossTreeCenter.y - pit.yMax);
                if (dx * dx + dy * dy < pitTreeClearRadius * pitTreeClearRadius)
                {
                    return false;
                }
            }
            if (pitForbiddenAreas != null)
            {
                for (int i = 0; i < pitForbiddenAreas.Length; i++)
                {
                    if (pitForbiddenAreas[i].Overlaps(pit))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>카메라가 보여줄 수 있는 맵 전체 범위(월드). 크기가 0이면 제한 없음.</summary>
        public Rect CameraArea => cameraArea;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CheonMyeong] MapBounds가 두 개 이상 있습니다 - 맵 하나에 하나만 두세요.");
            }
            Instance = this;
            RefreshCameraArea();
        }

        /// <summary>타일맵을 런타임에 바꿨다면 다시 호출. 칠해진 셀 범위(cellBounds)를 월드 좌표 Rect로 바꾼다.</summary>
        public void RefreshCameraArea()
        {
            if (cameraAreaSource == null)
            {
                return;
            }

            cameraAreaSource.CompressBounds();
            BoundsInt cells = cameraAreaSource.cellBounds;
            if (cells.size.x <= 0 || cells.size.y <= 0)
            {
                return;
            }

            Vector3 min = cameraAreaSource.CellToWorld(cells.min);
            Vector3 max = cameraAreaSource.CellToWorld(cells.max);
            cameraArea = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool Contains(Vector2 position)
        {
            return playableArea.Contains(position);
        }

        public Vector2 ClampInside(Vector2 position)
        {
            return new Vector2(
                Mathf.Clamp(position.x, playableArea.xMin, playableArea.xMax),
                Mathf.Clamp(position.y, playableArea.yMin, playableArea.yMax));
        }

        /// <summary>index번째 플레이어 시작 위치 - 0번은 왼쪽, 1번은 오른쪽(2인 기준), 그 이상은 계속 벌어진다.</summary>
        public Vector2 GetStartPosition(int index)
        {
            float side = index % 2 == 0 ? -1f : 1f;
            float step = (index / 2 + 0.5f) * startSpacing;
            return startPoint + new Vector2(side * step, 0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(playableArea.center, playableArea.size);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(cameraArea.center, cameraArea.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(startPoint, 0.5f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(BossPlaza.center, BossPlaza.size);
            if (hasBossTree)
            {
                Gizmos.DrawWireSphere(bossTreeCenter, pitTreeClearRadius);
            }
        }
    }
}
