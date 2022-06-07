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

            Texture3D _MainTex;
            SAMPLER(sampler_MainTex);

            TEXTURE2D_X(_SceneDepth);
            SAMPLER(sampler_SceneDepth);

            CBUFFER_START(FOG_SETTINGS);

            float4x4 _CameraToWorld;
            float4x4 _WorldToCamera;
            float4x4 _Projection;
            float4x4 _InverseProjection;
            float4 _FogParams;
            float _EyeIndex;
            int _FogSteps;

            CBUFFER_END

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                #ifdef UNITY_STEREO_INSTANCING_ENABLED
                if (_EyeIndex != unity_StereoEyeIndex)
                    return 0;
                #endif

                float2 uv = i.uv;
                float2 coords = (uv - 0.5) * 2.0;

                // Project screen coordinates back into world space
                float d = SAMPLE_TEXTURE2D_X(_SceneDepth, sampler_SceneDepth, uv);
                float4 clipPos = float4(coords, d, 1.0);
                float4 viewPos = mul(_Projection, clipPos);
                viewPos /= viewPos.w;
                float3 wPos = mul(_CameraToWorld, viewPos).xyz;

                float3 sStart = _WorldSpaceCameraPos;
                float3 sEnd = mul(_InverseProjection, float4(coords, 0, 1)).xyz;
                sEnd = mul(_CameraToWorld, float4(sEnd, 0)).xyz;
                sEnd = sStart + normalize(sEnd) * _FogParams.y;

                float3 sVector = sStart - wPos;
                float sOcclude = dot(sVector, sVector);

                float per = 1.0 / _FogSteps;

                half3 fog = (0).rrr;

                [loop]
                for (int f = 0; f < _FogSteps; f++) {
                    float s = per * f;

                    float3 sPoint = lerp(sStart, sEnd, s * s);
                    float3 sVector = sStart - sPoint;

                    if (dot(sVector, sVector) > sOcclude)
                        break;

                    fog += _MainTex.Sample(sampler_MainTex, float3(uv, per * f)) * per;
                }

                return half4(fog, 1);
            }
            ENDHLSL
        }
    }
}
