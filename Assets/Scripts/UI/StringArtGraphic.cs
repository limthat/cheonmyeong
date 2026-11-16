using UnityEngine;
using UnityEngine.UI;

namespace CheonMyeong.UI
{
    /// <summary>
    /// 261114 신내림 노드 실 - 스트링 아트처럼 노드 테두리에 못(16개)을 박고 실을 건다.
    /// - Links(노드 아래): 노드끼리 마주 보는 변의 못 5개를 엇갈려 이어 모래시계 모양으로 교차시킨다.
    /// - Wraps(노드 위): 열린 노드는 테두리 못을 따라 실을 여러 바퀴 감고 모서리를 엇걸어 감긴 모양을 만든다. 못은 모든 노드에 찍는다.
    /// 근접 캐릭터 = 금줄(짚 두 가닥 꼬임 + 흰 종이 술), 원거리 = 오방색 끈(청 · 적 · 황 · 백 · 흑).
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
                for (int e = 0; e < edgeFrom.Length && e < edgeTo.Length; e++)
                {
                    BuildLink(vh, edgeFrom[e], edgeTo[e]);
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

        private void BuildLink(VertexHelper vh, int a, int b)
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
            bool blocked = states[a] == NodeBlocked || states[b] == NodeBlocked;
            float baseAlpha = vivid ? 1f : blocked ? 0.3f : 0.7f;
            bool growing = vivid && b == animNode && animT < 1f;
            if (growing)
            {
                baseAlpha = 0.7f;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < 5; i++)
                {
                    Vector2 p = Peg(a, SidePegs[sideA][i]);
                    Vector2 q = Peg(b, SidePegs[sideB][4 - i]);
                    Thread(vh, p, q, i, baseAlpha, pass);
                    if (growing)
                    {
                        Thread(vh, p, Vector2.Lerp(p, q, Ease(Mathf.Clamp01(animT * 2f))), i, 1f, pass);
                    }
                }
            }
            if (straw)
            {
                Vector2 mid = (Peg(a, SidePegs[sideA][2]) + Peg(b, SidePegs[sideB][2])) * 0.5f;
                PaperStrip(vh, mid, vivid && !growing ? 1f : baseAlpha);
            }
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

        private void PaperStrip(VertexHelper vh, Vector2 top, float alpha)
        {
            Vector2 p0 = top;
            Vector2 p1 = top + new Vector2(5f, -6f);
            Vector2 p2 = top + new Vector2(-4f, -12f);
            Vector2 p3 = top + new Vector2(5f, -18f);
            Vector2 p4 = top + new Vector2(-2f, -24f);
            Color32 shade = Fade(Shadow, alpha);
            Line(vh, p0, p1, 6f, shade);
            Line(vh, p1, p2, 6f, shade);
            Line(vh, p2, p3, 6f, shade);
            Line(vh, p3, p4, 6f, shade);
            Color32 white = Fade(Paper, alpha);
            Line(vh, p0, p1, 4f, white);
            Line(vh, p1, p2, 4f, white);
            Line(vh, p2, p3, 4f, white);
            Line(vh, p3, p4, 4f, white);
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
