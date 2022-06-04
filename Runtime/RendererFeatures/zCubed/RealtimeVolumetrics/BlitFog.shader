Shader "zCubed/Experiments/BlitFog"
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
                // Because multi sampling is performance heavy, manually doing this is somehow more performant?
                const float RADIUS = 0.0002;
                const float3 OFFSET = float3(-1, 0, 1) * RADIUS;
                const float3x3 weights = float3x3(
                    0.0625, 0.1, 0.0625,
                    0.1, 0.35, 0.1,
                    0.0625, 0.1, 0.0625
                );

                half3 l00 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.xx).rgb;
                half3 l01 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.yx).rgb;
                half3 l02 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.zx).rgb;

                half3 l10 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.xy).rgb;
                half3 l11 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.yy).rgb;
                half3 l12 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.zy).rgb;

                half3 l20 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.xz).rgb;
                half3 l21 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.yz).rgb;
                half3 l22 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + OFFSET.zz).rgb;

                fog = (l00 * weights[0][0]) + (l01 * weights[0][1]) + (l02 * weights[0][2]) 
                    + (l10 * weights[1][0]) + (l11 * weights[1][1]) + (l12 * weights[1][2])
                    + (l20 * weights[2][0]) + (l21 * weights[2][1]) + (l22 * weights[2][2]);

                #else
                fog = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv);
                #endif

                return half4(fog, 1);
            }
            ENDHLSL
        }
    }
}
