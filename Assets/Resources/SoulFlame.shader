// 261122 추가 · 261123 도트 스타일로 다시 그림 · 261124 연한 그라데이션(외곽선 없음) - 영혼 불(도깨비불).
// 억쇠 '정신병' 차원 바깥 사람 화면에서 끌려간 플레이어 · 분신을 이 불꽃으로 표시한다(모데카이저 궁극기처럼).
// 도트 게임에 맞게: 월드 픽셀 격자(_PixelsPerUnit)로 칸을 나눠 계산하고, 색은 4단계(어두운 테두리 · 몸 · 밝은 속 · 흰 심지)만,
// 가장자리는 부드러운 알파 대신 4×4 Bayer 디더링 점, 움직임은 초당 _AnimFps 칸으로 끊는다.
// 색 = SpriteRenderer.color(정점 색). 스프라이트 = 1유닛 정사각(피벗 아래 가운데)을 크기로 늘려 쓴다.
Shader "CheonMyeong/SoulFlame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _PixelsPerUnit ("Pixels Per Unit", Float) = 64
        _AnimFps ("Animation Steps Per Second", Float) = 10
        _Speed ("Rise Speed", Float) = 2.6
        _OutlineColor ("Outline", Color) = (0.06, 0.08, 0.2, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _PixelsPerUnit;
                float _AnimFps;
                float _Speed;
                half4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 info : TEXCOORD1; // x = 흔들림 씨앗, yz = 불꽃 크기(픽셀 수)
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                o.info = float3(frac(dot(floor(origin.xy * 3.0), float2(0.1031, 0.1137))) * 37.0,
                                max(1.0, round(sx * _PixelsPerUnit)), max(1.0, round(sy * _PixelsPerUnit)));
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            static const float BayerMatrix[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Bayer4(float2 pixel)
            {
                int2 p = int2(pixel - 4.0 * floor(pixel / 4.0));
                return (BayerMatrix[p.x + p.y * 4] + 0.5) / 16.0;
            }

            // 불꽃 모양 거리(0 = 가운데, 1 = 가장자리): 아래 둥근 몸통 + 위로 흔들리며 가늘어지는 꼬리.
            float FlameDistance(float2 uv, float t, float seed)
            {
                float x = (uv.x - 0.5) * 2.0;
                float y = uv.y;
                float n = ValueNoise(float2(x * 2.5 + seed, y * 3.5 - t * _Speed));
                float n2 = ValueNoise(float2(x * 5.0 - seed, y * 7.0 - t * _Speed * 1.7));
                float sway = (sin(y * 5.5 - t * 4.0 + seed) * 0.16 + (n - 0.5) * 0.3) * y;
                float body = length(float2(x - sway * 0.4, (y - 0.3) * 1.4)) / 0.55;
                float tailWidth = max(0.001, (1.0 - y) * 0.85 * (0.7 + 0.6 * n2));
                float tail = abs(x - sway) / tailWidth + (1.0 - smoothstep(0.25, 0.4, y)) * 2.0;
                return min(body, tail) + (n2 - 0.5) * 0.2;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 size = input.info.yz;
                float2 pixel = floor(input.uv * size);
                float2 uv = (pixel + 0.5) / size;                       // 픽셀 칸 가운데
                float t = floor(_Time.y * _AnimFps) / max(_AnimFps, 1.0); // 끊어지는 시간
                float dither = Bayer4(pixel);
                float d = FlameDistance(uv, t, input.info.x);

                // 261124 - 예섭: 짙은 단색 · 어두운 외곽선 대신 연한 그라데이션. 가장자리 = 색을 흰색 쪽으로 25% 섞은 연한 색,
                // 가운데로 갈수록 더 밝게(60%) - 색(하늘 · 보라)은 남게. 계단은 5단계를 Bayer 디더링으로 섞어 도트 느낌은 유지, 가장자리는 반투명.
                half3 tint = input.color.rgb;
                half3 edgeColor = lerp(tint, half3(1, 1, 1), 0.25);
                half3 coreColor = lerp(tint, half3(1, 1, 1), 0.6);
                if (d > 1.0)
                {
                    float fade = saturate(1.0 - (d - 1.0) / 0.3);
                    return fade * 0.5 > dither ? half4(edgeColor, input.color.a * 0.35) : half4(0, 0, 0, 0);
                }
                float glow = saturate(1.0 - d);
                glow = saturate(floor(glow * 5.0 + dither) / 5.0);
                half3 col = lerp(edgeColor, coreColor, glow);
                return half4(col, input.color.a * lerp(0.5, 0.95, glow));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
