Shader "PrismaRP/Volumetrics/FogCompositor"
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

            #pragma target 5.0

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

            #pragma multi_compile _ _DEPTH_MSAA_2 _DEPTH_MSAA_4 _DEPTH_MSAA_8
            #pragma multi_compile _ _USE_DRAW_PROCEDURAL

#if defined(_DEPTH_MSAA_2)
    #define MSAA_SAMPLES 2
#elif defined(_DEPTH_MSAA_4)
    #define MSAA_SAMPLES 4
#elif defined(_DEPTH_MSAA_8)
    #define MSAA_SAMPLES 8
#else
    #define MSAA_SAMPLES 1
#endif

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    #define DEPTH_TEXTURE_MS(name, samples) Texture2DMSArray<float, samples> name
    #define DEPTH_TEXTURE(name) TEXTURE2D_ARRAY_FLOAT(name)
    #define LOAD(uv, sampleIndex) LOAD_TEXTURE2D_ARRAY_MSAA(_SceneDepth, uv, unity_StereoEyeIndex, sampleIndex)
#else
    #define DEPTH_TEXTURE_MS(name, samples) Texture2DMS<float, samples> name
    #define DEPTH_TEXTURE(name) TEXTURE2D_FLOAT(name)
    #define LOAD(uv, sampleIndex) LOAD_TEXTURE2D_MSAA(_SceneDepth, uv, sampleIndex)
#endif

#if MSAA_SAMPLES == 1
            TEXTURE2D_X(_SceneDepth);
            SAMPLER(sampler_SceneDepth);
#else
            DEPTH_TEXTURE_MS(_SceneDepth, MSAA_SAMPLES);
            float4 _SceneDepth_TexelSize;
#endif

#if UNITY_REVERSED_Z
    #define DEPTH_DEFAULT_VALUE 1.0
    #define DEPTH_OP min
#else
    #define DEPTH_DEFAULT_VALUE 0.0
    #define DEPTH_OP max
#endif

            CBUFFER_START(FOG_SETTINGS);

            float4x4 _CameraToWorld[2];
            float4x4 _WorldToCamera[2];
            float4x4 _Projection[2];
            float4x4 _InverseProjection[2];
            float4 _FogParams;
            float4 _PassData;

            CBUFFER_END

            Texture3D _MainTex;
            SAMPLER(sampler_MainTex);

            Texture3D _MainTexOther;
            SAMPLER(sampler_MainTexOther);

            SAMPLER(sampler_LinearClamp);
            SAMPLER(sampler_PointClamp);

            float SampleDepth(float2 uv)
            {
            #if MSAA_SAMPLES == 1
                return SAMPLE_TEXTURE2D_X(_SceneDepth, sampler_SceneDepth, uv).r;
            #else
                int2 coord = int2(uv * _SceneDepth_TexelSize.zw);
                float outDepth = DEPTH_DEFAULT_VALUE;

                UNITY_UNROLL
                for (int i = 0; i < MSAA_SAMPLES; ++i)
                    outDepth = DEPTH_OP(LOAD(coord, i), outDepth);
                return outDepth;
            #endif
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.uv;
                float2 coords = (uv - 0.5) * 2.0;

                // Project screen coordinates back into world space
                float d = SampleDepth(uv);

                float3 sStart = _WorldSpaceCameraPos;
                float3 oPoint = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);
            
                float3 oVec = oPoint - sStart;
                float3 sEnd = normalize(oVec) * _FogParams.y; 
                sEnd += sStart;
                
                float oFar = dot(oVec, oVec);

                float per = 1.0 / _PassData.w;
                float2 texel = 1.0 / _PassData.xy;

                half3 fog = (0).rrr;
                uv += texel * 0.5;

                [loop]
                for (int f = 0; f < _PassData.w; f++) {
                    float s = per * f;

                    float3 sPoint = lerp(sStart, sEnd, s * s);
                    float3 sVec = sPoint - sStart;

                    if (dot(sVec, sVec) >= oFar)
                        break;

                #if defined(UNITY_STEREO_INSTANCING_ENABLED)
                    UNITY_BRANCH
                    if (unity_StereoEyeIndex == 0)
                        fog += _MainTex.Sample(sampler_LinearClamp, float3(uv, s)).rgb * per;
                    else
                        fog += _MainTexOther.Sample(sampler_LinearClamp, float3(uv, s)).rgb * per;
                #else
                    fog += _MainTex.Sample(sampler_LinearClamp, float3(uv, s)).rgb * per;
                #endif
                }

                //return half4(sEnd, 1);
                //return half4(d.rrr, 1);
                return half4(fog, 1);
            }
            ENDHLSL
        }
    }
}
