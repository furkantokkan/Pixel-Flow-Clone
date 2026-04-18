Shader "Core/ColorAtlasPicker"
{
    Properties
    {
        _ColorAtlas ("Color Atlas", 2D) = "white" {}
        _ColorIndex ("Color (Vertical)", Range(0, 15)) = 0
        _ToneIndex ("Tone (Horizontal)", Range(0, 15)) = 0
        [Toggle] _EnableOutline ("Enable Outline", Float) = 0
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _Brightness ("Brightness", Range(0, 2)) = 1.2
        _Saturation ("Saturation", Range(0, 2)) = 1.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        // OUTLINE
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            half4 _OutlineColor;
            half _OutlineWidth;
            half _EnableOutline;

            v2f vert (appdata v)
            {
                v2f o;
                float3 expandedPos = v.vertex.xyz + v.normal * (_OutlineWidth * _EnableOutline);
                o.vertex = UnityObjectToClipPos(expandedPos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                return _OutlineColor * _EnableOutline;
            }
            ENDCG
        }

        // MAIN
        Pass
        {
            Name "Main"
            Cull Back
            ZWrite On
            ZTest LEqual
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _ColorAtlas;
            float _Brightness;
            float _Saturation;
            half _ColorIndex;
            half _ToneIndex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                const half invGridSize = 0.0625; // 1.0 / 16.0
                const half cellCenter = 0.03125; // 0.5 / 16.0
                
                half2 atlasUV = half2(
                    _ToneIndex * invGridSize + cellCenter,
                    _ColorIndex * invGridSize + cellCenter
                );
                
                half4 col = tex2D(_ColorAtlas, atlasUV);

                // Brightness & saturation adjust
                col.rgb *= _Brightness;
                half gray = dot(col.rgb, half3(0.3, 0.59, 0.11));
                col.rgb = lerp(gray.xxx, col.rgb, _Saturation);

                return saturate(col);
            }
            ENDCG
        }
    }
    
    Fallback "Mobile/Diffuse"
}
