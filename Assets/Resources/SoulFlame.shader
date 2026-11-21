// 261122 추가 - 영혼 불(도깨비불). 억쇠 '정신병' 차원 바깥 사람 화면에서 끌려간 플레이어 · 분신을 이 불꽃으로 표시한다(모데카이저 궁극기처럼).
// 그림 없이 셰이더로 그린다: 스프라이트 UV(0~1, 피벗 아래 가운데)에 아래가 둥글고 위로 갈수록 가늘게 흔들리는 불꽃.
// 색 = SpriteRenderer.color(정점 색). 가운데는 하얗게 밝다.
Shader "CheonMyeong/SoulFlame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Speed ("Rise Speed", Float) = 2.6
        _CoreWhite ("Core Whiteness", Range(0, 1)) = 0.75
        _Glow ("Outer Glow", Range(0, 1)) = 0.35
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
                float _Speed;
                float _CoreWhite;
                float _Glow;
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
                float seed : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.seed = frac(dot(floor(origin.xy * 3.0), float2(0.1031, 0.1137))) * 37.0; // 불꽃마다 흔들림이 다르게
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

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float x = (input.uv.x - 0.5) * 2.0;   // -1 ~ 1
                float y = input.uv.y;                 // 0(발) ~ 1(끝)

                float n = ValueNoise(float2(x * 2.5 + input.seed, y * 3.5 - t * _Speed));
                float n2 = ValueNoise(float2(x * 5.0 - input.seed, y * 7.0 - t * _Speed * 1.7));
                float sway = (sin(y * 5.5 - t * 4.0 + input.seed) * 0.16 + (n - 0.5) * 0.3) * y;

                // 아래는 둥근 몸통(반지름 0.55, 중심 y 0.3), 위로는 혀처럼 가늘어지는 꼬리.
                float body = length(float2(x - sway * 0.4, (y - 0.3) * 1.4)) / 0.55;
                float tailWidth = max(0.001, (1.0 - y) * 0.85 * (0.7 + 0.6 * n2));
                float tail = abs(x - sway) / tailWidth + (1.0 - smoothstep(0.25, 0.4, y)) * 2.0;
                float d = min(body, tail);
                d += (n2 - 0.5) * 0.25;

                float shape = 1.0 - smoothstep(0.75, 1.0, d);
                float glow = (1.0 - smoothstep(1.0, 1.6, d)) * _Glow;
                float core = 1.0 - smoothstep(0.0, 0.55, d);
                float flicker = 0.85 + 0.15 * sin(t * 13.0 + input.seed * 3.0);

                half3 col = lerp(input.color.rgb, half3(1, 1, 1), core * _CoreWhite);
                float alpha = saturate(max(shape, glow) * flicker) * input.color.a * saturate(1.15 - y);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
