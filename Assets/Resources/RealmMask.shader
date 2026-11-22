// 261122 추가 · 261123 도트 스타일 · 261123c 씨름터 - 억쇠 '정신병' 차원 막.
// 예섭: "물결 같은 건 다 빼고, 원 밖은 맵 풀밭을 약간 투명하게, 원 안은 씨름터 같은 느낌으로".
// - 원 밖(끌려간 본인): 어두운 바탕 위에 맵 풀밭 타일(Maptile-Sheet 풀 칸)을 반투명하게 깐다 - 바깥의 원귀 · 동료 · 진짜 억쇠는 안 보인다.
// - 원 안: 씨름판 모래 - 맵 흙 타일 무늬를 모래 4단계 팔레트로(디더링) 바꿔 불투명하게 깐다. 짚 둘레 가까이는 모래가 살짝 쌓여 진하다.
// - 경계: 씨름판 짚 둘레(꼰 새끼줄 무늬) - 먹색 외곽선 · 짚 밝은/어두운 사선 · 위쪽 하이라이트, 바깥으로 디더링 그림자.
// - _RingOnly = 1: 경계(+ 그림자)만, 나머지는 투명 - 바깥 사람 화면의 경계 원(Default 레이어).
// - _FogOnly = 1: 원 밖(짚 둘레 바깥)만 안개 - 장식 위에 한 장 더 그린다. 261123d: 안개 낀 저녁 숲 - 군데군데 옅게 흐르는 안개 덩어리.
// 도트 규칙: 모든 계산을 월드 픽셀(_PixelsPerUnit = 맵 타일과 같은 64) 칸 가운데로, 타일은 점 샘플링, 색 섞기는 4×4 Bayer 디더링.
// 화면 텍스처(Camera Sorting Layer Texture)는 쓰지 않는다. 그리는 순서: 막 = RealmFx, 차원 안 내 캐릭터 · 분신 = RealmTop.
Shader "CheonMyeong/RealmMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HoleUV ("Hole Radius (UV)", Float) = 0.0625
        _PixelsPerUnit ("Pixels Per Unit", Float) = 64
        [Toggle] _RingOnly ("Ring Only", Float) = 0
        [Toggle] _FogOnly ("Fog Only", Float) = 0

        [NoScaleOffset] _TileTex ("Map Tile Sheet", 2D) = "white" {}
        [NoScaleOffset] _CloudTex ("Pixel Clouds (RealmClouds)", 2D) = "black" {}
        _TileWorldSize ("Tile World Size", Float) = 0.5
        _TilePixels ("Tile Pixels", Float) = 32
        _GrassRect ("Grass Tile UV Rect (x, y, w, h)", Vector) = (0.375, 0.625, 0.0208333, 0.125)
        _DirtRect ("Dirt Tile UV Rect (x, y, w, h)", Vector) = (0.2083333, 0.625, 0.0208333, 0.125)

        _OutsideBase ("Outside Dark Base", Color) = (0.03, 0.04, 0.05, 1)
        _GrassAlpha ("Outside Grass Opacity", Range(0, 1)) = 0.85
        _OutsideDesaturate ("Outside Desaturate", Range(0, 1)) = 0.15
        _DuskTint ("Evening Tint", Color) = (0.58, 0.62, 0.78, 1)
        _FogColor ("Fog Color", Color) = (0.72, 0.74, 0.84, 1)
        _FogAlpha ("Fog Max Opacity", Range(0, 1)) = 0.42
        _CloudPixel ("Cloud Pixel Size (map pixels)", Float) = 4
        _CloudCell ("Cloud Spacing (units)", Float) = 5.2
        _CloudChance ("Cloud Chance", Range(0, 1)) = 0.5
        _FogSpeed ("Fog Drift (units/s)", Float) = 0.12

        _Sand0 ("Sand Shadow", Color) = (0.55, 0.42, 0.27, 1)
        _Sand1 ("Sand Dark", Color) = (0.7, 0.56, 0.37, 1)
        _Sand2 ("Sand", Color) = (0.82, 0.7, 0.49, 1)
        _Sand3 ("Sand Light", Color) = (0.92, 0.84, 0.63, 1)
        _SandBrightness ("Sand Brightness", Range(0.5, 3)) = 1.9

        _RopeOutline ("Rope Outline", Color) = (0.24, 0.15, 0.07, 1)
        _RopeDark ("Rope Dark Straw", Color) = (0.62, 0.45, 0.2, 1)
        _RopeLight ("Rope Straw", Color) = (0.86, 0.71, 0.38, 1)
        _RopeHighlight ("Rope Highlight", Color) = (0.98, 0.9, 0.62, 1)
        _RopePixels ("Rope Width (pixels)", Float) = 12
        _TwistPixels ("Rope Twist Length (pixels)", Float) = 8
        _ShadowPixels ("Outside Shadow (pixels)", Float) = 6
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

            TEXTURE2D(_TileTex);
            TEXTURE2D(_CloudTex);
            SamplerState sampler_RealmPointClamp; // 이름에 Point · Clamp → 점 샘플링(도트가 번지지 않게)

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _HoleUV;
                float _PixelsPerUnit;
                float _RingOnly;
                float _FogOnly;
                half4 _FogColor;
                float _FogAlpha;
                float _OutsideDesaturate;
                half4 _DuskTint;
                float _CloudPixel;
                float _CloudCell;
                float _CloudChance;
                float _FogSpeed;
                float _TileWorldSize;
                float _TilePixels;
                float4 _GrassRect;
                float4 _DirtRect;
                half4 _OutsideBase;
                float _GrassAlpha;
                half4 _Sand0;
                half4 _Sand1;
                half4 _Sand2;
                half4 _Sand3;
                float _SandBrightness;
                half4 _RopeOutline;
                half4 _RopeDark;
                half4 _RopeLight;
                half4 _RopeHighlight;
                float _RopePixels;
                float _TwistPixels;
                float _ShadowPixels;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXY : TEXCOORD0;
                float3 centerRadius : TEXCOORD1; // xy = 차원 가운데(월드), z = 반지름(월드)
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.worldXY = world.xy;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                // 스프라이트 = 512px(PPU 1) 정사각, 구멍 = UV _HoleUV → 월드 반지름 = 512 × _HoleUV × 크기.
                float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                o.centerRadius = float3(center.xy, 512.0 * _HoleUV * scale);
                return o;
            }

            // 4×4 Bayer 행렬 - 픽셀 좌표로 고정되는 순서 디더링 문턱값((값 + 0.5) / 16).
            static const float BayerMatrix[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Bayer4(float2 pixel)
            {
                int2 p = int2(pixel - 4.0 * floor(pixel / 4.0)); // 음수 칸도 0~3
                return (BayerMatrix[p.x + p.y * 4] + 0.5) / 16.0;
            }

            // 0~1 값을 팔레트 4단계로(단계 사이는 디더링).
            half3 Palette4(float v, float threshold, half3 c0, half3 c1, half3 c2, half3 c3)
            {
                float x = saturate(v) * 3.0;
                float step = floor(x);
                float level = step + ((x - step) > threshold ? 1.0 : 0.0);
                return level < 0.5 ? c0 : (level < 1.5 ? c1 : (level < 2.5 ? c2 : c3));
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

            // 261123i 예섭: "보낸 도트 구름을 그대로 찍어" - Resources/RealmClouds.png(참고 그림의 구름 7개를 원래 도트 크기로 옮긴 것, 280×24)를
            // 칸(_CloudCell 유닛)마다 확률로 하나씩 찍는다. 텍셀 1개 = 맵 픽셀 _CloudPixel칸. 그림 색 그대로(× _FogColor), 점 샘플링.
            static const float4 CloudRects[7] =   // (x, y, 폭, 높이) 텍셀, 아래 기준
            {
                float4(1, 1, 50, 22), float4(52, 1, 46, 21), float4(99, 1, 37, 22), float4(137, 1, 46, 16),
                float4(184, 1, 42, 15), float4(227, 1, 30, 15), float4(258, 1, 15, 10)
            };
            static const float2 CloudTexSize = float2(280, 24);

            half4 CloudSample(float2 w, float cloudPpu)
            {
                float cellSize = max(_CloudCell, 0.5);
                float2 cell = floor(w / cellSize);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 c = cell + float2(dx, dy);
                        if (Hash21(c + 0.37) > _CloudChance) continue;
                        int variant = min(6, (int)(Hash21(c + 2.7) * 7.0));
                        float4 rect = CloudRects[variant];
                        float2 origin = (c + 0.1 + 0.5 * float2(Hash21(c + 1.3), Hash21(c + 7.1))) * cellSize; // 그림 왼쪽 아래
                        float2 texel = floor((w - origin) * cloudPpu);
                        if (any(texel < 0.0) || any(texel >= rect.zw)) continue;
                        float2 uv = (rect.xy + texel + 0.5) / CloudTexSize;
                        half4 t = SAMPLE_TEXTURE2D(_CloudTex, sampler_RealmPointClamp, uv);
                        if (t.a > 0.5) return t;
                    }
                }
                return half4(0, 0, 0, 0);
            }

            // 맵 타일 한 칸(rect = 시트 안 UV 사각형)을 월드 좌표로 바둑판처럼 이어 붙여 점 샘플링.
            half3 SampleTile(float2 world, float4 rect)
            {
                float2 tile = frac(world / max(_TileWorldSize, 0.0001));
                float2 texel = (floor(tile * _TilePixels) + 0.5) / max(_TilePixels, 1.0); // 타일 텍셀 가운데
                return SAMPLE_TEXTURE2D(_TileTex, sampler_RealmPointClamp, rect.xy + texel * rect.zw).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float ppu = max(_PixelsPerUnit, 1.0);
                float2 pixel = floor(input.worldXY * ppu);   // 월드 픽셀 칸 번호
                float2 snapped = (pixel + 0.5) / ppu;        // 칸 가운데(월드)
                float dither = Bayer4(pixel);

                float2 rel = snapped - input.centerRadius.xy;
                float radius = input.centerRadius.z;
                float distPx = (length(rel) - radius) * ppu; // 경계에서 픽셀 수(바깥 +)

                // ── 경계: 씨름판 짚 둘레(꼰 새끼줄) ──  안쪽 끝 = 0, 바깥 끝 = _RopePixels
                float rope = _RopePixels;
                half4 ring = half4(0, 0, 0, 0);
                if (distPx >= 0.0 && distPx < rope)
                {
                    if (distPx < 1.0 || distPx >= rope - 1.0)
                    {
                        ring = half4(_RopeOutline.rgb, 1); // 위아래 먹색 외곽선
                    }
                    else
                    {
                        // 둘레를 따라 비스듬히 꼬인 짚 - 둘레 길이(픽셀) + 두께 위치로 사선 줄무늬.
                        float arcPx = atan2(rel.y, rel.x) * radius * ppu;
                        float twist = frac((arcPx + distPx) / max(_TwistPixels, 1.0));
                        half3 straw = twist < 0.5 ? _RopeLight.rgb : _RopeDark.rgb;
                        if (twist < 0.5 && distPx >= rope - 4.0 && distPx < rope - 2.0) straw = _RopeHighlight.rgb; // 바깥쪽 위 하이라이트
                        if (abs(twist - 0.5) < 0.09 || twist > 0.91) straw = lerp(straw, _RopeOutline.rgb, 0.5); // 꼬임 사이 홈
                        ring = half4(straw, 1);
                    }
                }
                // 회상 안개(_FogOnly): 짚 둘레 바깥만 옅은 회색으로 덮는다 - 장식(나무 · 꽃) 위에 그려 원 밖 전체가 흐릿한 기억처럼.
                if (_FogOnly > 0.5)
                {
                    // 261123f 예섭: 굵고 각진 도트 구름(옅은 구름 느낌). 점무늬 · 번짐 없이 딱 떨어지는 가장자리.
                    //  - 굵은 픽셀: 맵 픽셀 4칸 = 구름 1칸(_CloudPixel)
                    //  - 모양 · 음영: 예섭이 준 도트 구름 그림(Resources/RealmClouds.png)을 그대로 찍는다(CloudSample)
                    //  - 정수 칸 단위로 옆으로 흐름
                    if (distPx < rope) return half4(0, 0, 0, 0);
                    float cloudPpu = ppu / max(_CloudPixel, 1.0);
                    float tf = floor(_Time.y * 6.0) / 6.0;
                    float shift = floor(tf * _FogSpeed * cloudPpu) / cloudPpu;   // 정수 칸만큼만 이동
                    float2 cp = (floor(input.worldXY * cloudPpu) + 0.5) / cloudPpu; // 구름 칸 가운데
                    cp.x -= shift;
                    half4 cloud = CloudSample(cp, cloudPpu);
                    if (cloud.a < 0.5) return half4(0, 0, 0, 0);
                    half3 col = cloud.rgb * _FogColor.rgb;
                    float alpha = 1.0;
                    return half4(col, _FogAlpha * alpha);
                }
                float shadow = distPx >= rope ? saturate(1.0 - (distPx - rope) / max(_ShadowPixels, 1.0)) : 0.0;
                bool shadowDot = shadow * 0.75 > dither;

                if (_RingOnly > 0.5)
                {
                    if (ring.a > 0.0) return ring;
                    return shadowDot ? half4(0, 0, 0, 0.45) : half4(0, 0, 0, 0);
                }
                if (ring.a > 0.0)
                {
                    return ring;
                }

                if (distPx < 0.0)
                {
                    // ── 원 안: 씨름판 모래(흙 타일 무늬 → 모래 팔레트) ──
                    half3 dirt = SampleTile(snapped, _DirtRect);
                    float luma = dot(dirt, half3(0.299, 0.587, 0.114)) * _SandBrightness;
                    // 짚 둘레 쪽 10px은 모래가 쌓여 한 단계 진하게(디더링으로 넘어감).
                    float rim = saturate((distPx + 12.0) / 10.0);
                    luma -= rim * 0.22;
                    half3 sand = Palette4(luma, dither, _Sand0.rgb, _Sand1.rgb, _Sand2.rgb, _Sand3.rgb);
                    return half4(sand, 1);
                }

                // ── 원 밖: 어두운 바탕 + 반투명 풀밭 ──
                half3 grass = SampleTile(snapped, _GrassRect);
                half3 col = lerp(_OutsideBase.rgb, grass, _GrassAlpha);
                col = lerp(col, dot(col, half3(0.299, 0.587, 0.114)).xxx, _OutsideDesaturate);
                col *= _DuskTint.rgb; // 저녁 숲: 푸르스름하게 어둡게
                if (shadowDot)
                {
                    col *= 0.55; // 짚 둘레 그림자
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
