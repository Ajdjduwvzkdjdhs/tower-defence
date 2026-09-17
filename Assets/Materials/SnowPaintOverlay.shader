Shader "Custom/SnowPaintOverlay"
{
    Properties
    {
        _PaintColor    ("Paint Color",  Color)            = (1.0, 1.0, 1.0, 0.55)
        _PaintFadeSec  ("Paint Fade Sec", Range(0.1, 30))  = 3.0
        _SectorDeg     ("Sector Degrees", Range(1, 90))    = 15.0
        _MaxRadius     ("Max Radius (mountain edge)", Range(0.1, 30)) = 10.0
        _CrestShade    ("Crest Shading Strength", Range(0, 1)) = 0.45
        _SectorFeather ("Sector Boundary Feather", Range(0, 0.5)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+10"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "SnowPaint"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 山中心（XZ原点）からの方位角で 360/_SectorDeg 個の扇形に分割し、雪崩が通った
            // 扇形をまるごと白く塗る。頂点変位で実際に盛り上げる方式は、山メッシュの頂点密度が
            // 粗い箇所でスパイク状に破綻することを実験で確認したため不採用。代わりに
            // [innerRadius, _MaxRadius] の中間を明るく・両端を少し暗くする疑似陰影で立体感を出す。
            //
            // 扇形どうしの境界（15度ごと）が硬い縁として目立つ問題への対処として、境界付近では
            // 隣の扇形の状態ともブレンドする（隣が塗られていれば少しにじみ出て、硬い縁を緩和する）。
            #define SECTOR_MAX 24

            CBUFFER_START(UnityPerMaterial)
                half4 _PaintColor;
                float _PaintFadeSec;
                float _SectorDeg;
                float _MaxRadius;     // 山の裾までの目安半径。これを超えたXZ距離は塗らない（円形にクリップ）
                float _CrestShade;    // 疑似陰影の強さ（0=フラット、1=強いコントラスト）
                float _SectorFeather; // 扇形幅に対する境界フェザーの割合（0=フェザーなし、0.5=隣と全面ブレンド）
                // 各扇形の最終塗布時刻（.x）・雪玉が現れた起点半径（.y）・雪玉が実際に到達した
                // 最遠半径（.z、積もる範囲の広がり。雪崩本体より先に扇形全体が塗られて見えないよう
                // 到達分だけを塗る）。素の float[] は cbuffer 内で要素ごとに16バイトへパディングされ、C#側の
                // Material.SetFloatArray と噛み合わずズレる（別変数と衝突する）ことがあるため、
                // 実績のある float4[]（SetVectorArray）で受ける。
                float4 _SectorTimes[SECTOR_MAX];
                float _PaintNow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            // 指定した扇形インデックスにおける、この距離での塗り量（時間フェード×内外径クリップ）。
            float AmountAt(int sector, float dist)
            {
                float paintedAt = _SectorTimes[sector].x;
                float innerRadius = _SectorTimes[sector].y;
                float reachedRadius = min(_SectorTimes[sector].z, _MaxRadius); // 雪玉の到達分だけ塗る
                float age = _PaintNow - paintedAt;
                float timeFactor = (age >= 0.0 && age <= _PaintFadeSec)
                    ? saturate(1.0 - age / _PaintFadeSec)
                    : 0.0;

                float edgeFeather = 0.5;
                float outerFactor = saturate((reachedRadius - dist) / edgeFeather);
                float innerFactor = saturate((dist - innerRadius) / edgeFeather);
                return timeFactor * outerFactor * innerFactor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float angleDeg = atan2(IN.positionWS.z, IN.positionWS.x) * 57.29578; // rad→deg
                if (angleDeg < 0.0) angleDeg += 360.0;

                float sectorDeg = max(_SectorDeg, 1.0);
                float sectorFloat = angleDeg / sectorDeg;
                int sector = (int)floor(sectorFloat) % SECTOR_MAX;
                if (sector < 0) sector += SECTOR_MAX;
                float frac = saturate(sectorFloat - floor(sectorFloat)); // 0..1（扇形内の位置）

                float dist = length(IN.positionWS.xz);
                float amount = AmountAt(sector, dist);

                // 境界付近（frac が 0 または 1 に近い）では隣の扇形ともブレンドし、
                // 隣が塗られていれば少しにじみ出させて硬い縁を緩和する。
                float feather = max(_SectorFeather, 0.0001);
                if (frac < feather)
                {
                    int prev = (sector - 1 + SECTOR_MAX) % SECTOR_MAX;
                    float amountPrev = AmountAt(prev, dist);
                    float w = 0.5 * (1.0 - frac / feather); // 境界に近いほど強く（最大0.5）
                    amount = max(amount, lerp(amount, amountPrev, w));
                }
                else if (frac > 1.0 - feather)
                {
                    int next = (sector + 1) % SECTOR_MAX;
                    float amountNext = AmountAt(next, dist);
                    float w = 0.5 * (1.0 - (1.0 - frac) / feather);
                    amount = max(amount, lerp(amount, amountNext, w));
                }

                // 疑似陰影：[innerRadius, _MaxRadius] 区間の中央を明るく、両端をやや暗くして
                // 「盛り上がった雪の稜線」に見せる（ジオメトリは不変）。
                float innerRadius = _SectorTimes[sector].y;
                float reachedRadius = min(_SectorTimes[sector].z, _MaxRadius);
                float band = max(reachedRadius - innerRadius, 0.01);
                float t = saturate((dist - innerRadius) / band);
                float crest = 1.0 - abs(t - 0.5) * 2.0; // 0=両端, 1=中央
                float shade = lerp(1.0 - _CrestShade, 1.0 + _CrestShade * 0.5, crest);

                return half4(_PaintColor.rgb * shade, amount * _PaintColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
