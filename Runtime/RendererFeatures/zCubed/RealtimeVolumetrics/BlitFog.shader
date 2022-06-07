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

                #define BLUR

                #ifdef BLUR
                // _MainTex texel size was wrong!
                uint width, height;
                _MainTex.GetDimensions(width, height);
                float4 texelSize = float4(1.0 / width, 1.0 / height, 0, 0);

                // 5 x 5
                // https://www.researchgate.net/figure/Discrete-approximation-of-the-Gaussian-kernels-3x3-5x5-7x7_fig2_325768087
                const float WEIGHTS[25] = {
                    1 / 273.0, 4 / 273.0, 7 / 273.0, 4 / 273.0, 1 / 273.0,
                    4 / 273.0, 16 / 273.0, 26 / 273.0, 16 / 273.0, 4 / 273.0,
                    7 / 273.0, 26 / 273.0, 41 / 273.0, 26 / 273.0, 7 / 273.0,
                    4 / 273.0, 16 / 273.0, 26 / 273.0, 16 / 273.0, 4 / 273.0,
                    1 / 273.0, 4 / 273.0, 7 / 273.0, 4 / 273.0, 1 / 273.0
                };

                const float RADIUS = 1.5;

                // Because of texel rounding, we need to shift the uv slightly
                uv += texelSize * 0.5;

                [unroll]
                for (int r = 0; r < 5; r++) {
                    float yShift = ((r - 2.0) / 2.0) * texelSize.y * RADIUS;

                    [unroll]
                    for (int c = 0; c < 5; c++) {
                        float xShift = ((c - 2.0) / 2.0) * texelSize.x * RADIUS;

                        fog += SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + float2(xShift, yShift)) * WEIGHTS[r * 5 + c];
                    }
                }

                #else
                fog = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv);
                #endif

                return half4(fog, 1);
            }
            ENDHLSL
        }
    }
}
