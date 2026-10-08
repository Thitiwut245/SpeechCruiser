// The sea (SeaSurface): Gerstner waves in the vertex stage, the same four as SeaWaves.cs, so
// floating things ride the drawn waves. Colour by water depth (turquoise over shallows, deep
// blue-green beyond, clear enough in the shallows to see the bottom), sun glints, a soft
// fresnel sky, foam where the water touches anything (rocks, hulls, the shore), on wave
// crests and in ship wakes (ShipWake), and the scene's fog and shadows.
// Self-contained: the noise is computed here, no textures.
Shader "SpeechCruiser/Sea"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.16, 0.66, 0.68, 1)
        _DeepColor ("Deep", Color) = (0.04, 0.29, 0.42, 1)
        _SkyColor ("Sky reflection", Color) = (0.72, 0.86, 0.92, 1)
        _FoamColor ("Foam", Color) = (0.96, 0.98, 0.98, 1)
        _DepthRange ("Depth to full colour (m)", Float) = 7
        _Clarity ("Depth seen through (m)", Float) = 2.5
        _ShoreFoam ("Contact foam width (m)", Float) = 1.4
        _CrestFoam ("Crest foam", Range(0, 1)) = 0.35
        _Glint ("Sun glint", Range(0, 2)) = 0.8
        _Pattern ("Light pattern", Range(0, 1)) = 0.07
        _Swell ("Swell shading", Range(0, 1)) = 0.32
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Sea"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyColor;
                half4 _FoamColor;
                float _DepthRange;
                float _Clarity;
                float _ShoreFoam;
                half _CrestFoam;
                half _Glint;
                half _Pattern;
                half _Swell;
            CBUFFER_END

            // Set by SeaSurface / ShipWake every frame.
            float4 _SeaWaveA[4]; // dir.x, dir.z, k, amplitude
            float4 _SeaWaveB[4]; // phase speed, steepness
            float _SeaTime;
            float4 _SeaWake[32]; // x, z, time laid, strength 0..1
            float _SeaWakeCount;
            float _SeaRough; // SeaWaves.Roughness: storms foam more

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float crest : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            // Same sum as SeaWaves.Displacement, plus the surface's tangent frame.
            float3 Gerstner(float2 p, out float3 normal, out float crest)
            {
                float3 d = 0;
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float amp = 0;
                for (int i = 0; i < 4; i++)
                {
                    float2 dir = _SeaWaveA[i].xy;
                    float k = _SeaWaveA[i].z;
                    float a = _SeaWaveA[i].w;
                    float s = _SeaWaveB[i].y;
                    float f = k * (dot(dir, p) - _SeaWaveB[i].x * _SeaTime);
                    float sn = sin(f), cs = cos(f);
                    d += float3(dir.x * a * cs, a * sn, dir.y * a * cs);
                    tangent += float3(-dir.x * dir.x * s * sn, dir.x * s * cs, -dir.x * dir.y * s * sn);
                    binormal += float3(-dir.x * dir.y * s * sn, dir.y * s * cs, -dir.y * dir.y * s * sn);
                    amp += a;
                }
                normal = normalize(cross(binormal, tangent));
                crest = amp > 0.001 ? d.y / amp : 0;
                return d;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float3 n;
                float crest;
                world += Gerstner(world.xz, n, crest);
                o.positionWS = world;
                o.normalWS = n;
                o.crest = crest;
                o.positionCS = TransformWorldToHClip(world);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // ---- noise

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // Distance to the nearest cell edge of a drifting voronoi: thin bright lines, like
            // sunlight focused by ripples.
            float VoronoiEdge(float2 p, float t)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8, d2 = 8;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = float2(Hash(i + g), Hash(i + g + 17.1));
                    o = 0.5 + 0.5 * sin(t + 6.2831 * o);
                    float d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return d2 - d1;
            }

            // ---- wake

            float Wake(float2 p)
            {
                float foam = 0;
                int n = (int)_SeaWakeCount;
                for (int i = 0; i < 31; i++)
                {
                    if (i + 1 >= n) break;
                    float4 a = _SeaWake[i], b = _SeaWake[i + 1];
                    float2 ab = b.xy - a.xy;
                    float len2 = max(dot(ab, ab), 1e-4);
                    float t = saturate(dot(p - a.xy, ab) / len2);
                    float side = length(p - (a.xy + ab * t));
                    float age = _SeaTime - lerp(a.z, b.z, t);
                    float strength = lerp(a.w, b.w, t);
                    float life = saturate(1 - age / 9);
                    float fade = life * life * strength;
                    // Churned water straight behind the stern, widening as it ages...
                    float width = 2.2 + age * 0.55;
                    float core = smoothstep(width, width * 0.3, side) * fade;
                    // ...and the two arms of the Kelvin wake, spreading out to either side.
                    float reach = 2.6 + age * (0.6 + 2.2 * strength);
                    float arm = smoothstep(1.1, 0.0, abs(side - reach)) * fade * 0.75;
                    foam = max(foam, max(core, arm));
                }
                return foam;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Small ripples on top of the big waves: two drifting noise layers bend the normal.
                float2 p = i.positionWS.xz;
                float e = 0.35;
                float2 q1 = p * 0.35 + float2(_SeaTime * 0.11, _SeaTime * 0.07);
                float2 q2 = p * 0.8 - float2(_SeaTime * 0.09, -_SeaTime * 0.13);
                float h0 = ValueNoise(q1) + 0.5 * ValueNoise(q2);
                float hx = ValueNoise(q1 + float2(e, 0)) + 0.5 * ValueNoise(q2 + float2(e, 0));
                float hz = ValueNoise(q1 + float2(0, e)) + 0.5 * ValueNoise(q2 + float2(0, e));
                float3 normal = normalize(i.normalWS + float3(h0 - hx, 0, h0 - hz) * 0.9);

                // How deep the water is here, from the scene's depth behind it.
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float raw = SampleSceneDepth(screenUV);
                float sceneEye;
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    sceneEye = lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                else sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                float surfaceEye = -TransformWorldToView(i.positionWS).z;
                float along = max(sceneEye - surfaceEye, 0);
                float depth = along * max(abs(viewDir.y), 0.2);

                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / _DepthRange));
                // The swell, readable from above: troughs darker, crests lighter.
                col *= 1.0 + _Swell * i.crest * 0.5;

                // Light: a soft wrap of the sun, its shadows, a fresnel sky and a glint.
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord);
                half shadow = lerp(0.62, 1.0, sun.shadowAttenuation);
                half wrap = saturate(dot(normal, sun.direction) * 0.5 + 0.5);
                col *= (0.72 + 0.38 * wrap) * shadow * sun.color;
                half fresnel = pow(1 - saturate(dot(normal, viewDir)), 4);
                col = lerp(col, _SkyColor.rgb, fresnel * 0.45);
                half3 halfDir = normalize(sun.direction + viewDir);
                col += sun.color * pow(saturate(dot(normal, halfDir)), 180) * _Glint * shadow;

                // Light focused by ripples, strongest in shallow water.
                float lines = VoronoiEdge(p * 0.11 + normal.xz * 1.2, _SeaTime * 0.5);
                col += smoothstep(0.12, 0.0, lines) * _Pattern * (1 - saturate(depth / (_DepthRange * 1.5)) * 0.6);

                // Foam: where the water touches something, on the crests, and in wakes.
                float bubbles = ValueNoise(p * 1.7 + _SeaTime * 0.3) * 0.6 + ValueNoise(p * 4.3 - _SeaTime * 0.5) * 0.4;
                float contact = 1 - saturate(along / _ShoreFoam);
                float lap = 0.5 + 0.5 * sin(along * 5 - _SeaTime * 2.2);
                float edge = saturate(contact * 1.8 - bubbles * 0.7) + saturate(contact * lap * 1.2 - 0.5) * 0.6;
                // Whitecaps: thin, broken streaks on the highest crests; more of them in a storm.
                float caps = ValueNoise(p * 0.9 + float2(_SeaTime * 0.4, 0)) * ValueNoise(p * 2.6 - _SeaTime * 0.2);
                float crest = saturate((i.crest - lerp(0.62, 0.42, saturate(_SeaRough - 1))) * 3) * _CrestFoam * saturate(caps * 3 - 0.4) * (0.6 + 0.6 * saturate(_SeaRough - 1));
                float wake = Wake(p);
                float foam = saturate(max(edge, max(saturate(crest * 2 - bubbles * 0.5), saturate(wake * 1.7 - bubbles * 0.8))));
                col = lerp(col, _FoamColor.rgb * (0.75 + 0.25 * shadow), foam);

                // See-through in the shallows, so the bottom shows.
                half alpha = lerp(0.55, 1.0, saturate(depth / _Clarity));
                alpha = max(alpha, foam);

                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
