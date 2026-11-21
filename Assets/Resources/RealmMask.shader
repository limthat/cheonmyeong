// 261122 추가 - 억쇠 '정신병' 차원 막(끌려간 본인 화면에만 그린다).
// - 원 안: 물에 잠긴 느낌 - Default 정렬 레이어까지 그려진 화면(_CameraSortingLayerTexture)을 물결로 일렁이게 굴절시키고
//   약하게 흐린 뒤 청록 물빛 + 일렁이는 빛 무늬(코스틱) + 가장자리 어둡게.
// - 원 밖: 검푸른 노이즈 - 가로로 길게 늘어진 물결 띠가 옆으로 흐르며 일렁인다(+ 옅은 지지직). 경계엔 보랏빛 테두리.
// 261122 2차 - 예섭 요청: 밖은 세로 말고 가로로 일렁이게 · 푸른색 더 많이, 안은 물에 잠긴 듯.
// 필요 설정: Renderer2D 'Camera Sorting Layer Texture' 켜기 + Foremost Sorting Layer = Default,
//           이 막은 RealmFx 레이어, 차원 안 플레이어 · 분신 · 경계 원은 RealmTop 레이어(Default 다음에 그려야 텍스처를 읽을 수 있다).
// 원 모양은 스프라이트 UV로 계산한다(EoksoeBoss가 512px 정사각 스프라이트를 구멍 반지름 32px에 맞춰 늘림).
Shader "CheonMyeong/RealmMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HoleUV ("Hole Radius (UV)", Float) = 0.0625
        _FeatherUV ("Edge Feather (UV)", Float) = 0.006
        _BlurPixels ("Inside Blur (screen px)", Range(0, 4)) = 1.25
        _InsideTint ("Inside Water Tint (a = amount)", Color) = (0.08, 0.36, 0.52, 0.28)
        _WavePixels ("Inside Refraction (screen px)", Range(0, 8)) = 3
        _WaveSpeed ("Inside Wave Speed", Float) = 1.6
        _CausticColor ("Caustic Light", Color) = (0.55, 0.9, 1, 0.22)
        _CausticScale ("Caustic Scale (per unit)", Float) = 1.3
        _NoiseDark ("Noise Dark", Color) = (0.01, 0.035, 0.11, 1)
        _NoiseLight ("Noise Light", Color) = (0.07, 0.2, 0.46, 1)
        _NoiseCells ("Grain Cells per Unit", Float) = 8
        _NoiseFps ("Grain Frames per Second", Float) = 12
        _BandScale ("Band Stretch (x, y per unit)", Vector) = (0.25, 2.2, 0, 0)
        _BandSpeed ("Band Flow Speed", Float) = 0.6
        _RimColor ("Rim Color", Color) = (0.55, 0.3, 0.95, 1)
        _RimWidthUV ("Rim Width (UV)", Float) = 0.012
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

            TEXTURE2D(_CameraSortingLayerTexture);
            SAMPLER(sampler_CameraSortingLayerTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _HoleUV;
                float _FeatherUV;
                float _BlurPixels;
                half4 _InsideTint;
                float _WavePixels;
                float _WaveSpeed;
                half4 _CausticColor;
                float _CausticScale;
                float4 _BandScale;
                float _BandSpeed;
                half4 _NoiseDark;
                half4 _NoiseLight;
                float _NoiseCells;
                float _NoiseFps;
                half4 _RimColor;
                float _RimWidthUV;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 screenUV : TEXCOORD1;
                float2 worldXY : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                // URP 2D 기본 스프라이트 셰이더가 라이트 텍스처를 읽을 때와 같은 방식(직교 카메라라 w = 1).
                o.screenUV = ComputeScreenPos(o.positionCS / o.positionCS.w).xy;
                o.worldXY = world.xy;
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

            half3 SampleBlurred(float2 uv)
            {
                // 9탭(가운데 + 십자 + 대각) 가중 평균 - 텍스처를 2배 줄여 받으므로(Bilinear 2x) 이 정도면 '약한' 블러.
                float2 o = _BlurPixels / _ScreenParams.xy;
                half3 c = SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv).rgb * 4.0;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + float2(o.x, 0)).rgb * 2.0;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv - float2(o.x, 0)).rgb * 2.0;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + float2(0, o.y)).rgb * 2.0;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv - float2(0, o.y)).rgb * 2.0;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + o).rgb;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv - o).rgb;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + float2(o.x, -o.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, sampler_CameraSortingLayerTexture, uv + float2(-o.x, o.y)).rgb;
                return c / 16.0;
            }

            // 물 위에 비치는 빛 무늬 - 두 겹의 비틀린 사인 격자(값이 가늘게 밝은 선만 남도록 pow).
            float Caustic(float2 p, float t)
            {
                float2 q = p + float2(sin(p.y * 1.7 + t * 0.9), cos(p.x * 1.3 - t * 0.7)) * 0.6;
                float a = abs(sin(q.x * 2.1 + t * 0.6) + sin(q.y * 2.4 - t * 0.8));
                float2 r = p * 1.6 + float2(cos(p.y * 2.3 - t * 1.1), sin(p.x * 1.9 + t)) * 0.5;
                float b = abs(sin(r.x * 1.8 - t * 0.5) + sin(r.y * 2.0 + t * 0.7));
                return pow(saturate(1.0 - a * 0.5), 6.0) + pow(saturate(1.0 - b * 0.5), 6.0) * 0.6;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = length(input.uv - 0.5);
                float outside = smoothstep(_HoleUV - _FeatherUV, _HoleUV + _FeatherUV, d);
                float t = _Time.y;

                // 원 안 - 물에 잠긴 화면: 물결 굴절 → 약한 블러 → 청록 물빛 + 빛 무늬 + 가장자리로 갈수록 깊고 어둡게.
                float wt = t * _WaveSpeed;
                float2 refract = float2(sin(input.worldXY.y * 2.6 + wt) + 0.5 * sin(input.worldXY.y * 5.3 - wt * 1.3),
                                        cos(input.worldXY.x * 2.2 + wt * 0.8) + 0.5 * cos(input.worldXY.x * 4.7 + wt * 1.1)) / 1.5;
                float2 screenUV = input.screenUV + refract * (_WavePixels / _ScreenParams.xy);
                half3 inside = SampleBlurred(screenUV);
                inside = lerp(inside, inside * _InsideTint.rgb * 2.2, _InsideTint.a);
                inside += _CausticColor.rgb * (Caustic(input.worldXY * _CausticScale, t) * _CausticColor.a);
                float depth = saturate(d / max(_HoleUV, 0.0001));
                inside *= lerp(1.0, 0.72, depth * depth);

                // 원 밖 - 가로로 길게 늘어진 물결 띠가 옆으로 흐르며 위아래로 출렁인다 + 옅은 지지직.
                float2 bp = input.worldXY * _BandScale.xy;
                bp.y += sin(input.worldXY.x * 0.55 + t * 1.2) * 0.9 + sin(input.worldXY.x * 1.3 - t * 0.8) * 0.35;
                bp.x -= t * _BandSpeed;
                float band = ValueNoise(bp) * 0.65 + ValueNoise(bp * float2(2.1, 1.9) + float2(t * 0.35, 0)) * 0.35;
                band = smoothstep(0.25, 0.9, band);
                float frame = floor(t * _NoiseFps);
                float grain = Hash21(floor(input.worldXY * _NoiseCells) + frame * float2(17.13, 31.71));
                half3 noise = lerp(_NoiseDark.rgb, _NoiseLight.rgb, saturate(band * 0.85 + grain * 0.18));

                // 경계 바로 바깥 보랏빛 테두리(안쪽으로 번지지 않게 outside에만).
                float rim = saturate(1.0 - (d - _HoleUV) / max(_RimWidthUV, 0.0001)) * outside;
                noise = lerp(noise, _RimColor.rgb, rim * 0.6);

                return half4(lerp(inside, noise, outside), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
