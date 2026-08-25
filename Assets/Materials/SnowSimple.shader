Shader "Custom/URP/SnowSimple"
{
    Properties
    {
        [Header(Base)]
        _BaseColor("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _NoiseScale("Noise Scale", Range(1.0, 64.0)) = 12.0
        _BumpStrength("Bump Strength", Range(0.0, 2.0)) = 0.7
        _Wrap("Soft Wrap", Range(0.0, 1.0)) = 0.65
        _RimPower("Rim Power", Range(0.5, 8.0)) = 2.8
        _RimStrength("Rim Strength", Range(0.0, 2.0)) = 0.6
        _SparkleStrength("Sparkle Strength", Range(0.0, 1.0)) = 0.12

        [Header(Ambient)]
        _AmbientColor("Ambient Color", Color) = (0.18, 0.20, 0.22, 1.0)
        _AmbientInfluence("Ambient Influence", Range(0.0, 1.0)) = 0.10
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 100
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _NoiseScale;
                float _BumpStrength;
                float _Wrap;
                float _RimPower;
                float _RimStrength;
                float _SparkleStrength;
                float4 _AmbientColor;
                float _AmbientInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            float Hash3(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash3(i + float3(0.0, 0.0, 0.0));
                float n100 = Hash3(i + float3(1.0, 0.0, 0.0));
                float n010 = Hash3(i + float3(0.0, 1.0, 0.0));
                float n110 = Hash3(i + float3(1.0, 1.0, 0.0));
                float n001 = Hash3(i + float3(0.0, 0.0, 1.0));
                float n101 = Hash3(i + float3(1.0, 0.0, 1.0));
                float n011 = Hash3(i + float3(0.0, 1.0, 1.0));
                float n111 = Hash3(i + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            float SnowHeight(float3 p)
            {
                float large = ValueNoise3(p * _NoiseScale);
                float detail = ValueNoise3(p * _NoiseScale * 2.7 + 19.7);
                return large * 0.65 + detail * 0.35;
            }

            float3 MakeTangent(float3 n)
            {
                float3 up = abs(n.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                return normalize(cross(up, n));
            }

            float3 ProceduralSnowNormal(float3 normalWS, float3 positionWS)
            {
                float3 T = MakeTangent(normalWS);
                float3 B = normalize(cross(normalWS, T));

                float step = 0.12 / max(_NoiseScale, 0.001);
                float h0 = SnowHeight(positionWS);
                float hx = SnowHeight(positionWS + T * step);
                float hb = SnowHeight(positionWS + B * step);

                float3 bump = normalWS + (T * (h0 - hx) + B * (h0 - hb)) * _BumpStrength;
                return normalize(bump);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.viewDirWS = GetCameraPositionWS() - posInputs.positionWS;
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                N = ProceduralSnowNormal(N, IN.positionWS);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #else
                    float4 shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                #endif

                Light mainLight = GetMainLight(shadowCoord);
                half3 lightColor = mainLight.color;
                half atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                float wrap = _Wrap;
                half NdotL = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                half3 directDiffuse = lightColor * atten * NdotL;

                // Reduced environment light influence.
                half3 envAmbient = SampleSH(N);
                half3 ambient = lerp(_AmbientColor.rgb, envAmbient, saturate(_AmbientInfluence));
                half3 indirectDiffuse = ambient;

                float fresnel = pow(saturate(1.0 - dot(N, V)), _RimPower);
                half3 rim = lightColor * atten * fresnel * _RimStrength;

                // Rim ambient contribution is also kept small.
                rim += indirectDiffuse * fresnel * _RimStrength * 0.15;

                float sparkleNoise = ValueNoise3(IN.positionWS * _NoiseScale * 8.0 + 31.1);
                half sparkle = smoothstep(0.90, 1.0, sparkleNoise) * _SparkleStrength;
                half3 sparkles = lightColor * atten * sparkle * saturate(dot(N, mainLight.direction));

                half3 color = _BaseColor.rgb * (directDiffuse + indirectDiffuse);
                color += rim * _BaseColor.rgb;
                color += sparkles;

                #ifdef _ADDITIONAL_LIGHTS
                    int additionalLightCount = (int)GetAdditionalLightsCount();
                    for (int li = 0; li < additionalLightCount; ++li)
                    {
                        Light additionalLight = GetAdditionalLight((uint)li, IN.positionWS);
                        half addAtten = additionalLight.distanceAttenuation * additionalLight.shadowAttenuation;
                        half addNdotL = saturate((dot(N, additionalLight.direction) + wrap) / (1.0 + wrap));
                        color += _BaseColor.rgb * additionalLight.color * addAtten * addNdotL;
                    }
                #endif

                color = MixFog(color, IN.fogCoord);
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float3 lightDirectionWS = normalize(_LightDirection);
                float3 biasedPositionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                float4 positionCS = TransformWorldToHClip(biasedPositionWS);

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}