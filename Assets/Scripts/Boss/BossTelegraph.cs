using System.Collections.Generic;
using UnityEngine;

namespace CheonMyeong.Boss
{
    /// <summary>
    /// 261101 추가 - 보스 공격 바닥 예고(기획서: "모든 스킬은 바닥에 먼저 붉게 표시된다").
    /// 요즘 액션 게임에서 흔히 쓰는 방식: 범위 전체를 옅은 빨강으로 깔고, 그 안을 진한 빨강이 가운데에서 바깥으로
    /// 차오르다가 가장자리에 닿는 순간이 곧 타격 시점. 끝나면 짧게 번쩍이고 사라진다.
    /// 원(부채꼴 360도) / 부채꼴을 같은 메시 하나로 그린다.
    ///
    /// 순수 연출이라 각 화면에서 로컬로만 만든다(서버가 Rpc로 모양·위치·길이를 알려줌). 오브젝트/메시는 풀링해서
    /// 스킬마다 새로 만들지 않는다.
    /// </summary>
    public class BossTelegraph : MonoBehaviour
    {
        private const int Segments = 48;
        private const float FlashSeconds = 0.15f;
        private const int SortingOrder = -50; // 바닥 타일(-100/-90) 위, 캐릭터(0 근처) 아래

        // 261122 - 다른 게임들처럼 테두리만 또렷하고 안쪽은 바닥이 비쳐 보이게: 색 알파 = 테두리 알파, 안쪽 = 그 × InteriorAlpha(정점 색).
        private static readonly Color AreaColor = new Color(0.85f, 0.08f, 0.06f, 0.55f);
        private static readonly Color FillColor = new Color(0.95f, 0.12f, 0.08f, 0.6f);
        private const float InteriorAlpha = 0.22f;
        private const float BorderWidth = 0.09f; // 테두리 두께(유닛)
        private static readonly Color FlashColor = new Color(1f, 0.55f, 0.4f, 0.75f);

        private static readonly Stack<BossTelegraph> Pool = new Stack<BossTelegraph>();
        private static Transform poolRoot;

        private enum Shape : byte
        {
            Fan,
            Arrow,
        }

        private MeshFilter areaFilter;
        private MeshRenderer areaRenderer;
        private MeshFilter fillFilter;
        private MeshRenderer fillRenderer;
        private Mesh mesh;
        private Transform fillTransform;
        private MaterialPropertyBlock block;
        private float duration;
        private float elapsed;
        private Shape shape;
        private Color areaColor;
        private Color fillColor;

        /// <summary>바닥 예고를 띄운다. arcDegrees가 360이면 원.</summary>
        public static void Show(Material material, Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds)
        {
            Show(material, center, direction, radius, arcDegrees, seconds, 1f);
        }

        /// <summary>261121 - strength &gt; 1이면 더 진하게(땅 꺼짐 가운데 즉사 원).</summary>
        public static void Show(Material material, Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds, float strength)
        {
            Show(material, center, direction, radius, arcDegrees, seconds, strength, 0);
        }

        /// <summary>261122 - 정렬 레이어 지정(억쇠 차원 안 분신 예고는 막 위 RealmTop). 0 = Default.</summary>
        public static void Show(Material material, Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds, float strength, int sortingLayerId)
        {
            if (material == null || radius <= 0f)
            {
                return;
            }
            BossTelegraph telegraph = Get(material);
            telegraph.shape = Shape.Fan;
            BuildFan(telegraph.mesh, Mathf.Clamp(arcDegrees, 1f, 360f), radius);
            telegraph.SetSortingLayer(sortingLayerId);
            telegraph.Begin(center, direction, Vector3.one, seconds, strength);
        }

        private void SetSortingLayer(int sortingLayerId)
        {
            areaRenderer.sortingLayerID = sortingLayerId;
            fillRenderer.sortingLayerID = sortingLayerId;
        }

        /// <summary>
        /// 261121 - 화살표 예고(어린 억쇠 빗자루 비행 · 회오리). start에서 direction으로 length만큼, 폭 width.
        /// 진한 빨강은 시작점에서 화살표 끝까지 차오른다.
        /// </summary>
        public static void ShowArrow(Material material, Vector2 start, Vector2 direction, float width, float length, float seconds)
        {
            if (material == null || width <= 0f || length <= 0f)
            {
                return;
            }
            BossTelegraph telegraph = Get(material);
            telegraph.shape = Shape.Arrow;
            BuildArrow(telegraph.mesh, width, length);
            telegraph.SetSortingLayer(0);
            telegraph.Begin(start, direction, Vector3.one, seconds, 1f);
        }

        private static BossTelegraph Get(Material material)
        {
            BossTelegraph telegraph = null;
            while (telegraph == null && Pool.Count > 0)
            {
                telegraph = Pool.Pop(); // 파괴된 항목(에디터 플레이 재시작 등)은 건너뛴다
            }
            return telegraph != null ? telegraph : Create(material);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            poolRoot = null;
        }

        /// <summary>씬 전환 등으로 화면에 남은 예고를 전부 거둔다.</summary>
        public static void HideAll()
        {
            if (poolRoot == null)
            {
                return;
            }
            foreach (Transform child in poolRoot)
            {
                if (child.gameObject.activeSelf && child.TryGetComponent(out BossTelegraph telegraph))
                {
                    telegraph.Release();
                }
            }
        }

        private static BossTelegraph Create(Material material)
        {
            if (poolRoot == null)
            {
                Pool.Clear();
                poolRoot = new GameObject("BossTelegraphPool").transform;
                DontDestroyOnLoad(poolRoot.gameObject);
            }

            var go = new GameObject("BossTelegraph");
            go.transform.SetParent(poolRoot, false);
            var telegraph = go.AddComponent<BossTelegraph>();
            telegraph.mesh = new Mesh { name = "BossTelegraphFan" };
            telegraph.block = new MaterialPropertyBlock();

            telegraph.areaFilter = go.AddComponent<MeshFilter>();
            telegraph.areaRenderer = go.AddComponent<MeshRenderer>();
            telegraph.areaRenderer.sharedMaterial = material;
            telegraph.areaRenderer.sortingOrder = SortingOrder;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            telegraph.fillTransform = fill.transform;
            telegraph.fillFilter = fill.AddComponent<MeshFilter>();
            telegraph.fillRenderer = fill.AddComponent<MeshRenderer>();
            telegraph.fillRenderer.sharedMaterial = material;
            telegraph.fillRenderer.sortingOrder = SortingOrder + 1;

            telegraph.areaFilter.sharedMesh = telegraph.mesh;
            telegraph.fillFilter.sharedMesh = telegraph.mesh;
            return telegraph;
        }

        private void Begin(Vector2 center, Vector2 direction, Vector3 scale, float seconds, float strength)
        {
            transform.position = new Vector3(center.x, center.y, 0f);
            float angle = direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = scale;
            fillTransform.localScale = shape == Shape.Arrow ? new Vector3(0f, 1f, 1f) : Vector3.zero;
            duration = Mathf.Max(0.05f, seconds);
            elapsed = 0f;
            areaColor = AreaColor;
            fillColor = FillColor;
            if (strength > 1f)
            {
                areaColor.a = Mathf.Clamp01(areaColor.a * strength);
                fillColor = new Color(0.55f, 0.02f, 0.02f, Mathf.Clamp01(FillColor.a * strength));
            }
            SetColor(areaRenderer, areaColor);
            SetColor(fillRenderer, fillColor);
            gameObject.SetActive(true);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed < duration)
            {
                // 처음엔 천천히, 끝에 빨라지게(ease-in) - 타격 직전 긴장감.
                float t = elapsed / duration;
                float s = t * t;
                fillTransform.localScale = shape == Shape.Arrow ? new Vector3(s, 1f, 1f) : new Vector3(s, s, 1f);
                return;
            }

            float flash = (elapsed - duration) / FlashSeconds;
            if (flash >= 1f)
            {
                Release();
                return;
            }
            fillTransform.localScale = Vector3.one;
            Color c = FlashColor;
            c.a *= 1f - flash;
            SetColor(fillRenderer, c);
            Color a = areaColor;
            a.a *= 1f - flash;
            SetColor(areaRenderer, a);
        }

        private void Release()
        {
            gameObject.SetActive(false);
            Pool.Push(this);
        }

        private void SetColor(Renderer target, Color color)
        {
            target.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            target.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private static readonly List<Vector3> VertexBuffer = new List<Vector3>((Segments + 2) * 3);
        private static readonly List<int> TriangleBuffer = new List<int>(Segments * 9);
        private static readonly List<Color32> ColorBuffer = new List<Color32>((Segments + 2) * 3);
        private static readonly List<Vector2> OuterLoop = new List<Vector2>(Segments + 2);
        private static readonly List<Vector2> InnerLoop = new List<Vector2>(Segments + 2);
        private static readonly List<int> LoopTriangles = new List<int>(Segments * 3);

        /// <summary>
        /// 261121 - 화살표(+X 방향, 시작점 0 → 끝 length, 폭 width). 261122 - 실제 크기(유닛)로 만든다(테두리 두께를 일정하게).
        /// 머리(삼각형) 길이 ≈ 폭의 0.9배, 몸통 폭 = 전체 폭의 60%.
        /// </summary>
        private static void BuildArrow(Mesh target, float width, float length)
        {
            float head = Mathf.Clamp(width * 0.9f, length * 0.08f, length * 0.5f);
            float body = length - head;
            float shaft = width * 0.3f;
            float half = width * 0.5f;
            OuterLoop.Clear();
            OuterLoop.Add(new Vector2(0f, -shaft));
            OuterLoop.Add(new Vector2(body, -shaft));
            OuterLoop.Add(new Vector2(body, -half));
            OuterLoop.Add(new Vector2(length, 0f));
            OuterLoop.Add(new Vector2(body, half));
            OuterLoop.Add(new Vector2(body, shaft));
            OuterLoop.Add(new Vector2(0f, shaft));
            LoopTriangles.Clear();
            LoopTriangles.Add(0); LoopTriangles.Add(1); LoopTriangles.Add(5);
            LoopTriangles.Add(0); LoopTriangles.Add(5); LoopTriangles.Add(6);
            LoopTriangles.Add(2); LoopTriangles.Add(3); LoopTriangles.Add(4);
            BuildOutlined(target, Mathf.Min(BorderWidth, shaft * 0.5f));
        }

        /// <summary>반지름 radius(유닛), +X 방향을 가운데로 하는 부채꼴(360이면 원) 메시. 261122 - 테두리 띠 + 옅은 안쪽.</summary>
        private static void BuildFan(Mesh target, float arcDegrees, float radius)
        {
            OuterLoop.Clear();
            LoopTriangles.Clear();
            bool full = arcDegrees >= 359.9f;
            int segments = Mathf.Max(3, Mathf.CeilToInt(Segments * arcDegrees / 360f));
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            if (full)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    OuterLoop.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                for (int i = 1; i < segments - 1; i++)
                {
                    LoopTriangles.Add(0); LoopTriangles.Add(i); LoopTriangles.Add(i + 1);
                }
            }
            else
            {
                OuterLoop.Add(Vector2.zero); // 꼭짓점(보스 위치)
                for (int i = 0; i <= segments; i++)
                {
                    float a = -half + (2f * half) * i / segments;
                    OuterLoop.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                for (int i = 1; i <= segments; i++)
                {
                    LoopTriangles.Add(0); LoopTriangles.Add(i); LoopTriangles.Add(i + 1);
                }
            }
            BuildOutlined(target, Mathf.Min(BorderWidth, radius * 0.25f));
        }

        /// <summary>
        /// 261122 - OuterLoop(반시계 방향 닫힌 다각형)을 안쪽으로 border만큼 줄인 InnerLoop를 만들고,
        /// 바깥~안쪽 사이 띠(정점 알파 1) + 안쪽 면(LoopTriangles, 정점 알파 InteriorAlpha)으로 메시를 채운다.
        /// 정점: [0, n) 바깥 · [n, 2n) 안쪽(진하게) · [2n, 3n) 안쪽(옅게). 매번 같은 버퍼를 다시 써서 할당이 없다.
        /// </summary>
        private static void BuildOutlined(Mesh target, float border)
        {
            int n = OuterLoop.Count;
            InnerLoop.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = OuterLoop[(i + n - 1) % n];
                Vector2 cur = OuterLoop[i];
                Vector2 next = OuterLoop[(i + 1) % n];
                Vector2 e0 = (cur - prev).normalized;
                Vector2 e1 = (next - cur).normalized;
                var n0 = new Vector2(-e0.y, e0.x); // 반시계 다각형의 안쪽 법선
                var n1 = new Vector2(-e1.y, e1.x);
                Vector2 bisector = n0 + n1;
                if (bisector.sqrMagnitude < 0.000001f)
                {
                    bisector = n1;
                }
                bisector.Normalize();
                float d = Vector2.Dot(bisector, n1);
                float miter = d > 0.2f ? border / d : border * 3f;
                InnerLoop.Add(cur + bisector * Mathf.Min(miter, border * 3f));
            }

            VertexBuffer.Clear();
            ColorBuffer.Clear();
            TriangleBuffer.Clear();
            var solid = new Color32(255, 255, 255, 255);
            var faint = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(InteriorAlpha * 255f));
            for (int i = 0; i < n; i++)
            {
                VertexBuffer.Add(OuterLoop[i]);
                ColorBuffer.Add(solid);
            }
            for (int i = 0; i < n; i++)
            {
                VertexBuffer.Add(InnerLoop[i]);
                ColorBuffer.Add(solid);
            }
            for (int i = 0; i < n; i++)
            {
                VertexBuffer.Add(InnerLoop[i]);
                ColorBuffer.Add(faint);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                TriangleBuffer.Add(i); TriangleBuffer.Add(j); TriangleBuffer.Add(n + j);
                TriangleBuffer.Add(i); TriangleBuffer.Add(n + j); TriangleBuffer.Add(n + i);
            }
            for (int i = 0; i < LoopTriangles.Count; i++)
            {
                TriangleBuffer.Add(2 * n + LoopTriangles[i]);
            }
            target.Clear();
            target.SetVertices(VertexBuffer);
            target.SetColors(ColorBuffer);
            target.SetTriangles(TriangleBuffer, 0);
            target.RecalculateBounds();
        }
    }
}
