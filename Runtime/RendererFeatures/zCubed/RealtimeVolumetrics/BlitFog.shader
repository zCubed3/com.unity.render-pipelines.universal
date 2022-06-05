Shader "zCubed/Volumetrics/BlitFog"
{
    Properties
    {
        _MainTex ("Texture", any) = "" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = float4(v.vertex.xyz, 1);
                o.uv = v.uv;

                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
                #endif

                return o;
            }

            Texture2D _MainTex;
            SAMPLER(sampler_MainTex);

            float _EyeIndex;

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 fog = (0).rrr;

                float2 uv = i.uv;

                #ifdef UNITY_STEREO_INSTANCING_ENABLED
                //uv = UnityStereoTransformScreenSpaceTex(uv);

                if (_EyeIndex != unity_StereoEyeIndex)
                    return 0;
                #endif

                #define MULTISAMPLE

                #ifdef MULTISAMPLE
                const float2 radius = float2(0.005, 0.005);

                for (int r = 0; r < 5; r++) {
                    float ud = radius.y * (r - 1);
            
                    float3 l00 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(-radius.x, ud));
                    float3 l01 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(-radius.x / 2, ud));
                    float3 l02 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(0, ud));
                    float3 l03 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(radius.x / 2, ud));
                    float3 l04 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(radius.x, ud));
            
                    fog += l00 + l01 + l02 + l03 + l04;
                }
            
                fog /= 5 * 5;
                #else
                fog = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv);
                #endif

                return half4(fog, 1);
            }
            ENDHLSL
        }
    }
}
