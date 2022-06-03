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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                return o;
            }

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
            half4 _MainTex_ST;

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_FogTex);
            half4 _FogTex_TexelSize;

            float4x4 _InverseView;

            #define PI 3.141592654
            #define G_SCATTERING 0.00001

            float mod(float x, float y) {
                return x - y * floor(x/y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fixed3 fog = (0).rrr;

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

                fixed3 l00 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.xx).rgb;
                fixed3 l01 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.yx).rgb;
                fixed3 l02 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.zx).rgb;

                fixed3 l10 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.xy).rgb;
                fixed3 l11 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.yy).rgb;
                fixed3 l12 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.zy).rgb;

                fixed3 l20 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.xz).rgb;
                fixed3 l21 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.yz).rgb;
                fixed3 l22 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv + OFFSET.zz).rgb;

                fog = (l00 * weights[0][0]) + (l01 * weights[0][1]) + (l02 * weights[0][2]) 
                    + (l10 * weights[1][0]) + (l11 * weights[1][1]) + (l12 * weights[1][2])
                    + (l20 * weights[2][0]) + (l21 * weights[2][1]) + (l22 * weights[2][2]);

                #else
                fog = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, i.uv).rgb;
                #endif

                return fixed4(fog, 1);
            }
            ENDCG
        }
    }
}
