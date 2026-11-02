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

        private static readonly Color AreaColor = new Color(0.85f, 0.08f, 0.06f, 0.22f);
        private static readonly Color FillColor = new Color(0.95f, 0.12f, 0.08f, 0.45f);
        private static readonly Color FlashColor = new Color(1f, 0.55f, 0.4f, 0.75f);

        private static readonly Stack<BossTelegraph> Pool = new Stack<BossTelegraph>();
        private static Transform poolRoot;

        private MeshFilter areaFilter;
        private MeshRenderer areaRenderer;
        private MeshFilter fillFilter;
        private MeshRenderer fillRenderer;
        private Mesh mesh;
        private Transform fillTransform;
        private MaterialPropertyBlock block;
        private float duration;
        private float elapsed;

        /// <summary>바닥 예고를 띄운다. arcDegrees가 360이면 원.</summary>
        public static void Show(Material material, Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds)
        {
            if (material == null || radius <= 0f)
            {
                return;
            }

            BossTelegraph telegraph = null;
            while (telegraph == null && Pool.Count > 0)
            {
                telegraph = Pool.Pop(); // 파괴된 항목(에디터 플레이 재시작 등)은 건너뛴다
            }
            if (telegraph == null)
            {
                telegraph = Create(material);
            }
            telegraph.Begin(center, direction, radius, arcDegrees, seconds);
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

        private void Begin(Vector2 center, Vector2 direction, float radius, float arcDegrees, float seconds)
        {
            BuildFan(mesh, Mathf.Clamp(arcDegrees, 1f, 360f));
            transform.position = new Vector3(center.x, center.y, 0f);
            float angle = direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = new Vector3(radius, radius, 1f);
            fillTransform.localScale = Vector3.zero;
            duration = Mathf.Max(0.05f, seconds);
            elapsed = 0f;
            SetColor(areaRenderer, AreaColor);
            SetColor(fillRenderer, FillColor);
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
                fillTransform.localScale = new Vector3(s, s, 1f);
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
            Color a = AreaColor;
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

        /// <summary>반지름 1, +X 방향을 가운데로 하는 부채꼴(360이면 원) 메시.</summary>
        private static readonly List<Vector3> VertexBuffer = new List<Vector3>(Segments + 2);
        private static readonly List<int> TriangleBuffer = new List<int>(Segments * 3);
        private static readonly List<Color32> ColorBuffer = new List<Color32>(Segments + 2);

        private static void BuildFan(Mesh target, float arcDegrees)
        {
            VertexBuffer.Clear();
            TriangleBuffer.Clear();
            int segments = Mathf.Max(3, Mathf.CeilToInt(Segments * arcDegrees / 360f));
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            VertexBuffer.Add(Vector3.zero);
            for (int i = 0; i <= segments; i++)
            {
                float a = -half + (2f * half) * i / segments;
                VertexBuffer.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            for (int i = 1; i <= segments; i++)
            {
                TriangleBuffer.Add(0);
                TriangleBuffer.Add(i + 1);
                TriangleBuffer.Add(i);
            }
            // 정점 색을 흰색으로 채워 둔다(Sprites/Default 셰이더가 정점 색 × _Color로 그리므로).
            ColorBuffer.Clear();
            for (int i = 0; i < VertexBuffer.Count; i++)
            {
                ColorBuffer.Add(new Color32(255, 255, 255, 255));
            }
            target.Clear();
            target.SetVertices(VertexBuffer);
            target.SetColors(ColorBuffer);
            target.SetTriangles(TriangleBuffer, 0);
            target.RecalculateBounds();
        }
    }
}
