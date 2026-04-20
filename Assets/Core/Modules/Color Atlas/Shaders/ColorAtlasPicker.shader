Shader "Core/ColorAtlasPicker"
{
    Properties
    {
        _ColorAtlas ("Color Atlas", 2D) = "white" {}
        _ColorIndex ("Color (Vertical)", Range(0, 15)) = 0
        _ToneIndex ("Tone (Horizontal)", Range(0, 15)) = 0
        [Toggle] _EnableOutline ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0.12,0.14,0.18,1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _Brightness ("Brightness", Range(0, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1
        _ShadeStrength ("Shade Strength", Range(0, 1)) = 0.7
        _BandSoftness ("Band Softness", Range(0.001, 0.25)) = 0.04
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.22
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _TopHighlight ("Top Highlight", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ColorAtlas);
            SAMPLER(sampler_ColorAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
                float _Brightness;
                float _Saturation;
                float _ColorIndex;
                float _ToneIndex;
                float _EnableOutline;
                float _ShadeStrength;
                float _BandSoftness;
                float _RimStrength;
                float _RimPower;
                float _TopHighlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                float3 expandedPositionWS = positionWS + normalWS * (_OutlineWidth * _EnableOutline);
                output.positionHCS = TransformWorldToHClip(expandedPositionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0h) * _EnableOutline;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Main"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ColorAtlas);
            SAMPLER(sampler_ColorAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
                float _Brightness;
                float _Saturation;
                float _ColorIndex;
                float _ToneIndex;
                float _EnableOutline;
                float _ShadeStrength;
                float _BandSoftness;
                float _RimStrength;
                float _RimPower;
                float _TopHighlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            half ResolveToonBand(half ndotl, half softness)
            {
                const half shadowThreshold = 0.28h;
                const half highlightThreshold = 0.7h;

                half midBlend = smoothstep(shadowThreshold - softness, shadowThreshold + softness, ndotl);
                half highlightBlend = smoothstep(highlightThreshold - softness, highlightThreshold + softness, ndotl);

                half band = lerp(0.45h, 0.72h, midBlend);
                return lerp(band, 1.0h, highlightBlend);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const half invGridSize = 0.0625h;
                const half cellCenter = 0.03125h;

                half2 atlasUV = half2(
                    _ToneIndex * invGridSize + cellCenter,
                    _ColorIndex * invGridSize + cellCenter);

                half4 colorSample = SAMPLE_TEXTURE2D(_ColorAtlas, sampler_ColorAtlas, atlasUV);
                colorSample.rgb *= _Brightness;

                half grayscale = dot(colorSample.rgb, half3(0.3h, 0.59h, 0.11h));
                colorSample.rgb = lerp(grayscale.xxx, colorSample.rgb, _Saturation);
                colorSample.rgb = saturate(colorSample.rgb);

                half3 normalWS = normalize(input.normalWS);
                half3 lightDirWS = normalize(half3(-0.35h, 0.88h, 0.32h));
                half ndotl = saturate(dot(normalWS, lightDirWS));
                half toonBand = ResolveToonBand(ndotl, _BandSoftness);
                half lighting = lerp(1.0h, toonBand, _ShadeStrength);

                half topHighlight = saturate(normalWS.y) * _TopHighlight;

                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;

                half3 shadedColor = colorSample.rgb * lighting;
                shadedColor = lerp(shadedColor, shadedColor + (1.0h.xxx - shadedColor) * 0.4h, topHighlight);
                shadedColor = lerp(shadedColor, saturate(shadedColor + 0.2h.xxx), rim);

                colorSample.rgb = saturate(shadedColor);
                colorSample.a = 1.0h;
                return colorSample;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
