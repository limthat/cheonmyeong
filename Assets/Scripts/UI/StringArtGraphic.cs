using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261114 신내림 노드 실 - 스트링 아트처럼 노드 테두리에 못(16개)을 박고 실을 건다.
    /// - Links(노드 아래): 노드끼리 마주 보는 변의 못 5개를 엇갈려 이어 모래시계 모양으로 교차시킨다.
    /// - Wraps(노드 위): 열린 노드는 테두리 못을 따라 실을 여러 바퀴 감고 모서리를 엇걸어 감긴 모양을 만든다. 못은 모든 노드에 찍는다.
    /// 261117 - 전부 금줄: 칸 사이는 세 가닥 꼬인 굵은 새끼줄 + 크기가 제각각인 한지 술(Ornaments), 감는 실은 짚 색.
    /// 메시는 상태가 바뀔 때와 감기는 연출(0.8초) 동안만 다시 만든다 - 평소 프레임 비용 0, 할당 없음.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class StringArtGraphic : MaskableGraphic
    {
        public enum Layer
        {
            Links,
            Wraps
        }

        public const byte NodeHidden = 255;
        public const byte NodeBlocked = 0;
        public const byte NodeClosed = 1;
        public const byte NodeOpen = 2;

        private const int PegCount = 16;
        private const float AnimDuration = 0.8f;
        private const float ThreadWidth = 3f;
        private const float ShadowWidth = 5f;
        private const float TwistLength = 8f;
        private const float RopeWidth = 9f;
        private const float RopeTwist = 6f;

        private static readonly Vector2[] PegUnits =
        {
            new Vector2(-1f, -1f), new Vector2(-0.5f, -1f), new Vector2(0f, -1f), new Vector2(0.5f, -1f),
            new Vector2(1f, -1f), new Vector2(1f, -0.5f), new Vector2(1f, 0f), new Vector2(1f, 0.5f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(-0.5f, 1f),
            new Vector2(-1f, 1f), new Vector2(-1f, 0.5f), new Vector2(-1f, 0f), new Vector2(-1f, -0.5f)
        };

        private static readonly int[][] SidePegs =
        {
            new[] { 0, 1, 2, 3, 4 },
            new[] { 4, 5, 6, 7, 8 },
            new[] { 12, 11, 10, 9, 8 },
            new[] { 0, 15, 14, 13, 12 }
        };

        private const int SideBottom = 0;
        private const int SideRight = 1;
        private const int SideTop = 2;
        private const int SideLeft = 3;

        private static readonly Color32 Shadow = new Color32(45, 28, 14, 200);
        private static readonly Color32 NailDark = new Color32(34, 24, 16, 230);
        private static readonly Color32 NailBrass = new Color32(198, 158, 76, 255);
        private static readonly Color32 Paper = new Color32(250, 248, 240, 255);
        private static readonly Color32 SelectGold = new Color32(255, 196, 40, 255);

        [SerializeField] private Layer layer = Layer.Links;

        private Vector2[] centers = new Vector2[0];
        private int[] edgeFrom = new int[0];
        private int[] edgeTo = new int[0];
        private byte[] states = new byte[0];
        private Color32[] palette = { new Color32(219, 184, 107, 255) };
        private bool straw;
        private float pegRadius = 55f;
        private int animNode = -1;
        private int selectedNode = -1;
        private float animT = 1f;

        public Layer DrawLayer
        {
            get => layer;
            set
            {
                layer = value;
                SetVerticesDirty();
            }
        }

        public void SetLayout(Vector2[] nodeCenters, int[] from, int[] to, float radius)
        {
            centers = nodeCenters ?? new Vector2[0];
            edgeFrom = from ?? new int[0];
            edgeTo = to ?? new int[0];
            pegRadius = radius;
            if (states.Length != centers.Length)
            {
                states = new byte[centers.Length];
            }
            SetVerticesDirty();
        }

        public void SetState(byte[] nodeStates, Color32[] threadPalette, bool strawRope, int windNode)
        {
            if (nodeStates != null)
            {
                int n = Mathf.Min(nodeStates.Length, states.Length);
                for (int i = 0; i < n; i++)
                {
                    states[i] = nodeStates[i];
                }
            }
            if (threadPalette != null && threadPalette.Length > 0)
            {
                palette = threadPalette;
            }
            straw = strawRope;
            if (windNode >= 0)
            {
                animNode = windNode;
                animT = 0f;
            }
            else if (windNode == -2)
            {
                animNode = -1;
                animT = 1f;
            }
            SetVerticesDirty();
        }

        /// <summary>Wraps 층에만 그린다 - 고른 칸 바깥에 금색 테두리.</summary>
        public void SetSelected(int node)
        {
            if (selectedNode == node)
            {
                return;
            }
            selectedNode = node;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (animT >= 1f)
            {
                return;
            }
            animT = Mathf.Min(1f, animT + Time.unscaledDeltaTime / AnimDuration);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (centers.Length == 0)
            {
                return;
            }
            if (layer == Layer.Links)
            {
                // 줄을 전부 그린 뒤 매달린 장식(숯 · 한지 · 고추)을 그려 다른 줄에 가려지지 않게 한다.
                for (int e = 0; e < edgeFrom.Length && e < edgeTo.Length; e++)
                {
                    BuildLink(vh, edgeFrom[e], edgeTo[e], false);
                }
                if (straw)
                {
                    for (int e = 0; e < edgeFrom.Length && e < edgeTo.Length; e++)
                    {
                        BuildLink(vh, edgeFrom[e], edgeTo[e], true);
                    }
                }
            }
            else
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    if (states[i] == NodeOpen)
                    {
                        float progress = i == animNode ? Ease(animT) : 1f;
                        BuildWrap(vh, i, progress, 0);
                        BuildWrap(vh, i, progress, 1);
                    }
                }
                for (int i = 0; i < centers.Length; i++)
                {
                    BuildNails(vh, i);
                }
                BuildSelection(vh);
            }
        }

        private void BuildLink(VertexHelper vh, int a, int b, bool ornaments)
        {
            if (a < 0 || b < 0 || a >= centers.Length || b >= centers.Length || states[a] == NodeHidden || states[b] == NodeHidden)
            {
                return;
            }
            Vector2 dir = centers[b] - centers[a];
            int sideA;
            int sideB;
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            {
                sideA = dir.x > 0f ? SideRight : SideLeft;
                sideB = dir.x > 0f ? SideLeft : SideRight;
            }
            else
            {
                sideA = dir.y > 0f ? SideTop : SideBottom;
                sideB = dir.y > 0f ? SideBottom : SideTop;
            }
            bool vivid = states[a] == NodeOpen && states[b] == NodeOpen;
            // 261117 - 둘 다 연 칸 사이에만 금줄을 맨다(반투명하게 미리 깔던 줄은 없앰).
            if (!vivid)
            {
                return;
            }
            bool growing = b == animNode && animT < 1f;
            // 261117 - 칸 사이 연결은 가운데 못끼리 굵은 금줄 하나(세 가닥 꼬임).
            Vector2 p = Peg(a, SidePegs[sideA][2]);
            Vector2 q = Peg(b, SidePegs[sideB][2]);
            if (ornaments)
            {
                // 줄이 다 이어진 뒤(animT 0.5)에 장식이 걸린다.
                if (!growing || animT >= 0.5f)
                {
                    Ornaments(vh, p, q, a, b);
                }
                return;
            }
            Vector2 end = growing ? Vector2.Lerp(p, q, Ease(Mathf.Clamp01(animT * 2f))) : q;
            Rope(vh, p, end, 0);
            Rope(vh, p, end, 1);
        }

        private void BuildWrap(VertexHelper vh, int node, float progress, int pass)
        {
            int laps = straw ? 3 : palette.Length;
            int chordCount = 12;
            int total = laps * PegCount + chordCount;
            float budget = progress * total;
            int drawn = 0;
            for (int lap = 0; lap < laps; lap++)
            {
                float radius = pegRadius + (lap - (laps - 1) * 0.5f) * 2f;
                for (int s = 0; s < PegCount; s++)
                {
                    float part = Mathf.Clamp01(budget - drawn);
                    drawn++;
                    if (part <= 0f)
                    {
                        return;
                    }
                    Vector2 p = centers[node] + PegUnits[s] * radius;
                    Vector2 q = centers[node] + PegUnits[(s + 1) % PegCount] * radius;
                    Thread(vh, p, Vector2.Lerp(p, q, part), lap, 1f, pass);
                }
            }
            for (int corner = 0; corner < PegCount; corner += 4)
            {
                for (int k = 0; k < 3; k++)
                {
                    float part = Mathf.Clamp01(budget - drawn);
                    drawn++;
                    if (part <= 0f)
                    {
                        return;
                    }
                    int from = corner - (k == 1 ? 2 : 1);
                    int to = corner + (k == 2 ? 2 : 1);
                    Vector2 p = Peg(node, (from + PegCount) % PegCount);
                    Vector2 q = Peg(node, to % PegCount);
                    Thread(vh, p, Vector2.Lerp(p, q, part), corner / 4 + k, 1f, pass);
                }
            }
        }

        private void BuildSelection(VertexHelper vh)
        {
            if (selectedNode < 0 || selectedNode >= centers.Length || states[selectedNode] == NodeHidden)
            {
                return;
            }
            float r = pegRadius + 7f;
            for (int pass = 0; pass < 2; pass++)
            {
                float width = pass == 0 ? 7f : 4f;
                Color32 c = pass == 0 ? Fade(Shadow, 1f) : Fade(SelectGold, 1f);
                for (int s = 0; s < 4; s++)
                {
                    Vector2 p = centers[selectedNode] + PegUnits[s * 4] * r;
                    Vector2 q = centers[selectedNode] + PegUnits[(s * 4 + 4) % PegCount] * r;
                    Line(vh, p, q, width, c);
                }
            }
        }

        private void BuildNails(VertexHelper vh, int node)
        {
            if (states[node] == NodeHidden)
            {
                return;
            }
            float alpha = states[node] == NodeOpen ? 1f : states[node] == NodeClosed ? 0.8f : 0.4f;
            for (int s = 0; s < PegCount; s++)
            {
                Vector2 p = Peg(node, s);
                Square(vh, p, 6f, Fade(NailDark, alpha));
                Square(vh, p + new Vector2(-0.5f, 0.5f), 3.5f, Fade(NailBrass, alpha));
            }
        }

        private Vector2 Peg(int node, int index)
        {
            return centers[node] + PegUnits[index] * pegRadius;
        }

        /// <summary>pass 0 = 그림자만, 1 = 실만. 그림자를 먼저 다 깔아야 겹친 실이 그림자에 안 가려진다.</summary>
        private void Thread(VertexHelper vh, Vector2 a, Vector2 b, int colorIndex, float alpha, int pass)
        {
            if (pass == 0)
            {
                Line(vh, a, b, ShadowWidth, Fade(Shadow, alpha));
                return;
            }
            Color32 main = palette[colorIndex % palette.Length];
            if (!straw)
            {
                Line(vh, a, b, ThreadWidth, Fade(main, alpha));
                return;
            }
            Color32 second = palette[(colorIndex + 1) % palette.Length];
            Vector2 d = b - a;
            float length = d.magnitude;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(length / TwistLength));
            for (int i = 0; i < pieces; i++)
            {
                Vector2 p = a + d * (i / (float)pieces);
                Vector2 q = a + d * ((i + 1) / (float)pieces);
                Line(vh, p, q, ThreadWidth, Fade((i & 1) == 0 ? main : second, alpha));
            }
        }

        /// <summary>
        /// 261117 - 꼬인 새끼줄: 어두운 바탕 줄 위에 비스듬한 가닥을 일정 간격으로 얹는다(밝음 · 중간 · 어두운 중간 3가닥 반복).
        /// 가닥 사이로 바탕이 1~2px 보여 꼬임이 드러난다. 가닥 하나 = 사각형 하나라 줄 길이 300px에 약 50개.
        /// </summary>
        private void Rope(VertexHelper vh, Vector2 a, Vector2 b, int pass)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f)
            {
                return;
            }
            if (pass == 0)
            {
                Line(vh, a, b, RopeWidth + 4f, Fade(Shadow, 1f));
                return;
            }
            Color32 mid = palette[0];
            Color32 dark = palette[1 % palette.Length];
            Color32 light = palette[2 % palette.Length];
            Color32 deep = Color32.Lerp(mid, dark, 0.5f);
            Line(vh, a, b, RopeWidth, Fade(dark, 1f));

            Vector2 dir = d / length;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            Vector2 across = normal * (RopeWidth * 0.5f - 1f) + dir * (RopeWidth * 0.45f);
            float strandWidth = RopeTwist * 0.62f;
            Vector2 trim = across.normalized * (strandWidth * 0.5f);
            int count = Mathf.FloorToInt(length / RopeTwist);
            for (int i = 0; i <= count; i++)
            {
                Vector2 c = a + dir * (i * RopeTwist);
                int k = i % 3;
                Color32 col = k == 0 ? light : k == 1 ? mid : deep;
                Line(vh, c - across + trim, c + across - trim, strandWidth, Fade(col, 1f));
            }
        }

        // 261117 - 금줄에 꽂은 한지(종이 술)를 픽셀 그림으로. 한 칸 = OrnamentPixel px.
        // 줄 길이에 맞춰 여러 장을 고르게 걸고, 장마다 폭 · 길이 · 겹침 · 찢긴 끝을 다르게 한다.
        // 모양은 두 노드 번호로 만든 해시로 정해서 메시를 다시 만들어도 그대로다(난수 · 할당 없음).
        private const float OrnamentPixel = 2f;
        private const float SlipSpacing = 30f;
        private const int MaxSlips = 8;

        private static readonly Color32 PaperShade = new Color32(220, 212, 194, 255);
        private static readonly Color32 PaperFold = new Color32(232, 226, 210, 255);

        private void Ornaments(VertexHelper vh, Vector2 p, Vector2 q, int nodeA, int nodeB)
        {
            if (p.x > q.x)
            {
                Vector2 swap = p;
                p = q;
                q = swap;
            }
            Vector2 d = q - p;
            float length = d.magnitude;
            if (length < 20f)
            {
                return;
            }
            uint seed = Hash((uint)Mathf.Min(nodeA, nodeB) * 7919u + (uint)Mathf.Max(nodeA, nodeB) * 104729u + 1u);
            if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.5f)
            {
                // 거의 세로인 줄: 줄 오른쪽 옆에 한두 장.
                int sideCount = Mathf.Clamp(Mathf.FloorToInt(length / 45f), 1, 3);
                Vector2 low = p.y < q.y ? p : q;
                Vector2 high = p.y < q.y ? q : p;
                for (int i = 0; i < sideCount; i++)
                {
                    uint h = Hash(seed + (uint)i * 2654435761u);
                    Vector2 at = Vector2.Lerp(high, low, (i + 0.35f) / sideCount);
                    Slip(vh, at.x + RopeWidth * 0.5f + 4f + (h % 3u) * OrnamentPixel, at.y + 6f, h);
                }
                return;
            }
            int count = Mathf.Clamp(Mathf.FloorToInt((length - 12f) / SlipSpacing), 1, MaxSlips);
            for (int i = 0; i < count; i++)
            {
                uint h = Hash(seed + (uint)i * 2654435761u);
                float jitter = ((int)(h % 7u) - 3) * 1.5f;
                float t = Mathf.Clamp01((i + 0.5f) / count + jitter / length);
                Vector2 at = p + d * t;
                Slip(vh, at.x, at.y - RopeWidth * 0.2f, h);
            }
        }

        /// <summary>한지 한 장(가끔 두 장 겹침). top = 줄에 꽂힌 위쪽 가운데, 아래로 매달린다(UI 좌표는 위가 +y).</summary>
        private void Slip(VertexHelper vh, float centerX, float top, uint h)
        {
            int width = 2 + (int)(h % 3u);
            int length = 7 + (int)((h >> 4) % 10u);
            bool pair = (h >> 8) % 5u < 2u;
            if (pair)
            {
                int width2 = 2 + (int)((h >> 12) % 2u);
                int length2 = Mathf.Max(5, length - 3 - (int)((h >> 14) % 4u));
                SlipShape(vh, centerX - (width2 + 1) * OrnamentPixel * 0.5f, top, width2, length2, h >> 16);
                SlipShape(vh, centerX + (width + 1) * OrnamentPixel * 0.5f, top, width, length, h);
                return;
            }
            SlipShape(vh, centerX, top, width, length, h);
        }

        private void SlipShape(VertexHelper vh, float centerX, float top, int width, int length, uint h)
        {
            float k = OrnamentPixel;
            float x0 = Mathf.Round(centerX - width * k * 0.5f);
            float x1 = x0 + width * k;
            float y0 = Mathf.Round(top);
            float fullBottom = y0 - (length - 1) * k;
            // 끝이 찢긴 줄: 마지막 칸은 한쪽만 남는다.
            int lastWidth = Mathf.Max(1, width - 1 - (int)((h >> 2) % 2u));
            bool tearLeft = ((h >> 3) & 1u) == 0u;
            float lx0 = tearLeft ? x0 : x1 - lastWidth * k;
            float lx1 = lx0 + lastWidth * k;

            Color32 shade = Fade(Shadow, 1f);
            Box(vh, x0 - k, fullBottom - k, x1 + k, y0 + k, shade);
            Box(vh, lx0 - k, fullBottom - 2f * k, lx1 + k, fullBottom, shade);

            Color32 paper = Fade(Paper, 1f);
            Box(vh, x0, fullBottom, x1, y0, paper);
            Box(vh, lx0, fullBottom - k, lx1, fullBottom, paper);
            if (width >= 3)
            {
                Box(vh, x1 - k, fullBottom, x1, y0, Fade(PaperShade, 1f));
            }
            int fold = 2 + (int)((h >> 5) % 3u);
            Color32 foldColor = Fade(PaperFold, 1f);
            float foldRight = width >= 3 ? x1 - k : x1;
            while (fold < length - 2)
            {
                Box(vh, x0, y0 - (fold + 1) * k, foldRight, y0 - fold * k, foldColor);
                fold += 3 + (int)((h >> (7 + fold % 5)) % 3u);
            }
            // 줄에 꽂은 짚 한 가닥.
            float tieX = x0 + (width / 2) * k;
            Box(vh, tieX, y0, tieX + k, y0 + 2f * k, Fade(palette[1 % palette.Length], 1f));
        }

        private static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        private static void Box(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            if (c.a == 0)
            {
                return;
            }
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;
            v.position = new Vector3(x0, y0);
            vh.AddVert(v);
            v.position = new Vector3(x0, y1);
            vh.AddVert(v);
            v.position = new Vector3(x1, y1);
            vh.AddVert(v);
            v.position = new Vector3(x1, y0);
            vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private Color32 Fade(Color32 c, float alpha)
        {
            Color tint = color;
            return new Color32(
                (byte)(c.r * tint.r),
                (byte)(c.g * tint.g),
                (byte)(c.b * tint.b),
                (byte)Mathf.Clamp(c.a * alpha * tint.a, 0f, 255f));
        }

        private static float Ease(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 c)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f || c.a == 0)
            {
                return;
            }
            Vector2 along = d / length * (width * 0.5f);
            Vector2 normal = new Vector2(-along.y, along.x);
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;
            v.position = a - along - normal;
            vh.AddVert(v);
            v.position = a - along + normal;
            vh.AddVert(v);
            v.position = b + along + normal;
            vh.AddVert(v);
            v.position = b + along - normal;
            vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private static void Square(VertexHelper vh, Vector2 center, float size, Color32 c)
        {
            if (c.a == 0)
            {
                return;
            }
            float h = size * 0.5f;
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;
            v.position = center + new Vector2(-h, -h);
            vh.AddVert(v);
            v.position = center + new Vector2(-h, h);
            vh.AddVert(v);
            v.position = center + new Vector2(h, h);
            vh.AddVert(v);
            v.position = center + new Vector2(h, -h);
            vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
