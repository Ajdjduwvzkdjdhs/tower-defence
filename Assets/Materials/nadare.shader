Shader "Custom/FluffySnow"
{
    Properties
    {
        [Header(Base)]
        _BaseColor   ("Base Color",        Color)         = (1.0, 1.0, 1.0, 1.0)
        _BaseTint    ("Shadow Tint",       Color)         = (0.75, 0.82, 0.95, 1.0) // 雪の影は青み

        [Header(Fluffiness)]
        _RimColor    ("Rim Color",         Color)         = (1.0, 1.0, 1.0, 1.0)
        _RimPower    ("Rim Power",         Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength",      Range(0, 2))   = 0.9

        [Header(Subsurface)]
        _SSSColor    ("SSS Color",         Color)         = (0.95, 0.98, 1.0, 1.0)
        _SSSStrength ("SSS Strength",      Range(0, 1.5)) = 0.6
        _SSSDistortion("SSS Distortion",   Range(0, 1))   = 0.25

        [Header(Surface Bump)]
        _BumpScale   ("Bump Scale",        Range(0, 2))   = 0.6
        _NoiseFreq   ("Noise Frequency",   Range(1, 50))  = 12
        _NoiseOctaves("Noise Octaves",     Range(1, 6))   = 4

        [Header(Sparkle)]
        _SparkleIntensity("Sparkle Intensity", Range(0, 3)) = 0.8
        _SparkleSize ("Sparkle Size",      Range(50, 800))= 280
        _SparkleThreshold("Sparkle Threshold", Range(0.85, 1)) = 0.97

        [Header(Lighting)]
        _Smoothness  ("Smoothness",        Range(0, 1))   = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ _CLUSTERED_RENDERING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ----------------------------
            // パラメータ
            // ----------------------------
            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _BaseTint;
                half4  _RimColor;
                half   _RimPower;
                half   _RimStrength;
                half4  _SSSColor;
                half   _SSSStrength;
                half   _SSSDistortion;
                half   _BumpScale;
                float  _NoiseFreq;
                int    _NoiseOctaves;
                half   _SparkleIntensity;
                float  _SparkleSize;
                half   _SparkleThreshold;
                half   _Smoothness;
            CBUFFER_END

            // ----------------------------
            // ハッシュ / ノイズ
            // ----------------------------
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float n000 = hash31(i + float3(0,0,0));
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                float y0 = lerp(x00, x10, f.y);
                float y1 = lerp(x01, x11, f.y);

                return lerp(y0, y1, f.z) * 2.0 - 1.0; // [-1, 1]
            }

            float fbm(float3 p, int octaves)
            {
                float v = 0.0;
                float a = 0.5;
                float3 shift = float3(17.3, 91.7, 53.2);
                for (int i = 0; i < octaves; ++i)
                {
                    v += a * valueNoise(p);
                    p = p * 2.03 + shift;
                    a *= 0.5;
                }
                return v;
            }

            // 法線導出（中心差分）
            float3 computeNormal(float3 p, float eps)
            {
                float dx = fbm(p + float3(eps,0,0), _NoiseOctaves)
                         - fbm(p - float3(eps,0,0), _NoiseOctaves);
                float dy = fbm(p + float3(0,eps,0), _NoiseOctaves)
                         - fbm(p - float3(0,eps,0), _NoiseOctaves);
                float dz = fbm(p + float3(0,0,eps), _NoiseOctaves)
                         - fbm(p - float3(0,0,eps), _NoiseOctaves);
                float3 grad = float3(dx, dy, dz) / (2.0 * eps);
                return normalize(-grad * _BumpScale + float3(0,0,1));
            }

            // スパークル（視点依存のキラキラ）
            float sparkle(float3 p, float3 v)
            {
                float3 q = floor(p * _SparkleSize);
                float h = hash31(q);
                if (h < _SparkleThreshold) return 0.0;

                // 微小な法線方向をハッシュで与え、ハイライト条件を満たす時だけ光らせる
                float3 n = normalize(hash31(q + 1.0) - 0.5);
                float3 r = reflect(-v, n);
                float spec = pow(saturate(r.z), 48.0);
                return spec * (h - _SparkleThreshold) / (1.0 - _SparkleThreshold);
            }

            // ----------------------------
            // 頂点 / フラグメント
            // ----------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float2 uv         : TEXCOORD4;
                float  fogFactor  : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS  = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.bitangentWS= cross(OUT.normalWS, OUT.tangentWS) * IN.tangentOS.w;
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- サーフェス座標 ---
                float3 pWorld = IN.positionWS;
                float3 pNoise = pWorld * _NoiseFreq;

                // --- プロシージャル法線（Tangent Space → World Space） ---
                float3 nTS = computeNormal(pNoise, 0.05);
                float3 N = normalize(
                    IN.tangentWS   * nTS.x +
                    IN.bitangentWS * nTS.y +
                    IN.normalWS    * nTS.z
                );

                // --- 幾何情報 ---
                float3 V = normalize(GetWorldSpaceViewDir(pWorld));
                float  NdotV = saturate(dot(N, V));

                // --- メインライト ---
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(pWorld));
                float3 L = normalize(mainLight.direction);
                float3 lightColor = mainLight.color * mainLight.distanceAttenuation
                                  * mainLight.shadowAttenuation;

                // --- 拡散（Lambert + 半球アンビエント） ---
                float NdotL = saturate(dot(N, L));
                float3 diffuse = lerp(_BaseTint.rgb, _BaseColor.rgb, NdotL);

                // --- 簡易サブサーフェススキャッタリング ---
                float3 sssDir = L + N * _SSSDistortion;
                float  sssDot = pow(saturate(dot(V, -sssDir)), 2.0);
                // 裏からの光も少し拾う（雪の透明感）
                float  backLight = saturate(dot(-N, L)) * 0.35;
                float3 sss = _SSSColor.rgb * (sssDot + backLight) * _SSSStrength;

                // --- リムライト（ふわふわの輪郭） ---
                float  rim = pow(1.0 - NdotV, _RimPower);
                float3 rimLight = _RimColor.rgb * rim * _RimStrength;

                // --- スペキュラ（微細な光沢） ---
                float3 H = normalize(L + V);
                float  spec = pow(saturate(dot(N, H)), lerp(8.0, 128.0, _Smoothness));
                float3 specular = lightColor * spec * _Smoothness * 0.5;

                // --- スパークル ---
                float sp = sparkle(pWorld, V);
                float3 sparkleCol = float3(1.0, 1.0, 1.0) * sp * _SparkleIntensity * NdotL;

                // --- 合成 ---
                float3 color = diffuse * lightColor;        // 直接光ディフューズ
                color += diffuse * half3(0.35, 0.40, 0.48); // 環境光（寒色）
                color += sss * lightColor;                  // サブサーフェス
                color += rimLight;                          // ふわふわリム
                color += specular;                          // 光沢
                color += sparkleCol;                        // キラキラ

                // --- 追加ライト対応 ---
                #ifdef _ADDITIONAL_LIGHTS
                    uint numLights = GetAdditionalLightsCount();
                    for (uint i = 0; i < numLights; ++i)
                    {
                        Light addL = GetAdditionalLight(i, pWorld);
                        float3 La = addL.direction;
                        float3 ac = addL.color * addL.distanceAttenuation * addL.shadowAttenuation;
                        float aNdotL = saturate(dot(N, La));
                        color += diffuse * ac * aNdotL;
                    }
                #endif

                // フォグ
                color = MixFog(color, IN.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // シャドウキャスト
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}