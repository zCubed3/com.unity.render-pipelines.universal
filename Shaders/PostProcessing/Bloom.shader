Shader "Hidden/Universal Render Pipeline/Bloom"
{
    HLSLINCLUDE
        #pragma exclude_renderers gles
        #pragma multi_compile_local _ _USE_RGBM
        #pragma multi_compile _ _USE_DRAW_PROCEDURAL

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

        TEXTURE2D_X(_SourceTex);
        float4 _SourceTex_TexelSize;
        TEXTURE2D_X(_SourceTexLowMip);
        float4 _SourceTexLowMip_TexelSize;

        float4 _Params; // x: scatter, y: clamp, z: threshold (linear), w: threshold knee

        #define Scatter             _Params.x
        #define ClampMax            _Params.y
        #define Threshold           _Params.z
        #define ThresholdKnee       _Params.w

        half4 EncodeHDR(half3 color)
        {
        #if _USE_RGBM
            half4 outColor = EncodeRGBM(color);
        #else
            half4 outColor = half4(color, 1.0);
        #endif

        #if UNITY_COLORSPACE_GAMMA
            return half4(sqrt(outColor.xyz), outColor.w); // linear to γ
        #else
            return outColor;
        #endif
        }

        half3 DecodeHDR(half4 color)
        {
        #if UNITY_COLORSPACE_GAMMA
            color.xyz *= color.xyz; // γ to linear
        #endif

        #if _USE_RGBM
            return DecodeRGBM(color);
        #else
            return color.xyz;
        #endif
        }

        //#define USE_OLD_URP
        #ifdef USE_OLD_URP
        #define USE_THRESHOLDING
        #define URP_OLD_UPSAMPLE
        #endif

        half4 FragPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        #if _BLOOM_HQ
            float texelSize = _SourceTex_TexelSize.x;
            half4 A = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, -1.0));
            half4 B = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.0, -1.0));
            half4 C = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, -1.0));
            half4 D = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-0.5, -0.5));
            half4 E = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.5, -0.5));
            half4 F = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, 0.0));
            half4 G = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
            half4 H = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, 0.0));
            half4 I = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-0.5, 0.5));
            half4 J = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.5, 0.5));
            half4 K = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, 1.0));
            half4 L = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.0, 1.0));
            half4 M = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, 1.0));

            half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

            half4 o = (D + E + I + J) * div.x;
            o += (A + B + G + F) * div.y;
            o += (B + C + H + G) * div.y;
            o += (F + G + L + K) * div.y;
            o += (G + H + M + L) * div.y;

            half4 color = o;
        #else
            half4 color = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
        #endif

            // User controlled clamp to limit crazy high broken spec
            color = min(ClampMax, color);

            // Thresholding
            //#define USE_THRESHOLDING
            #ifdef USE_THRESHOLDING
            half brightness = Max3(color.r, color.g, color.b);
            half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
            softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
            half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
            color *= multiplier;
            #endif

            // Clamp colors to positive once in prefilter. Encode can have a sqrt, and sqrt(-x) == NaN. Up/Downsample passes would then spread the NaN.
            color = max(color, 0);
            return color;
        }

        half4 FragBlurH(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float texelSize = _SourceTex_TexelSize.x * 2.0;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

            // 9-tap gaussian blur on the downsampled source
            half4 c0 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0));
            half4 c1 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0));
            half4 c2 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0));
            half4 c3 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0));
            half4 c4 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv                               );
            half4 c5 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0));
            half4 c6 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0));
            half4 c7 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0));
            half4 c8 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0));

            half4 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 
                        + c3 * 0.19459459 + c4 * 0.22702703 + c5 * 0.19459459 
                        + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

            return color;
        }

        half4 FragBlurV(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float texelSize = _SourceTex_TexelSize.y;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

            // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
            half4 c0 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923));
            half4 c1 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538));
            half4 c2 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv                                      );
            half4 c3 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538));
            half4 c4 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923));

            half4 color = c0 * 0.07027027 + c1 * 0.31621622
                        + c2 * 0.22702703
                        + c3 * 0.31621622 + c4 * 0.07027027;

            return color;
        }

        // zCubed Additions
        half4 FragBlurRadial(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;

            //https://www.froyok.fr/blog/2021-12-ue4-custom-bloom/
            const float2 COORDS[13] = {
                float2( -1.0f,  1.0f ), float2(  1.0f,  1.0f ),
                float2( -1.0f, -1.0f ), float2(  1.0f, -1.0f ),

                float2(-2.0f, 2.0f), float2( 0.0f, 2.0f), float2( 2.0f, 2.0f),
                float2(-2.0f, 0.0f), float2( 0.0f, 0.0f), float2( 2.0f, 0.0f),
                float2(-2.0f,-2.0f), float2( 0.0f,-2.0f), float2( 2.0f,-2.0f)
            };


            const float WEIGHTS[13] = {
                // 4 samples
                // (1 / 4) * 0.5f = 0.125f
                0.125f, 0.125f,
                0.125f, 0.125f,

                // 9 samples
                // (1 / 9) * 0.5f
                0.0555555f, 0.0555555f, 0.0555555f,
                0.0555555f, 0.0555555f, 0.0555555f,
                0.0555555f, 0.0555555f, 0.0555555f
            };

            half4 color = 0;

            [unroll]
            for( int i = 0; i < 13; i++ )
            {
                float2 currentUV = uv + COORDS[i] * _SourceTex_TexelSize.xy;
                color += WEIGHTS[i] * SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, currentUV);
            }

            return color;
        }
        // ================

        half3 Upsample(float2 uv)
        {
            // zCubed Additions
            //#define URP_OLD_UPSAMPLE
            #ifdef URP_OLD_UPSAMPLE

            half3 highMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv));

        #if _BLOOM_HQ && !defined(SHADER_API_GLES)
            half3 lowMip = DecodeHDR(SampleTexture2DBicubic(TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, unity_StereoEyeIndex));
        #else
            half3 lowMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, uv));
        #endif

            return lerp(highMip, lowMip, Scatter);

            #else

            //https://www.froyok.fr/blog/2021-12-ue4-custom-bloom/
            const float2 COORDS[9] = {
                float2( -1.0f,  1.0f ), float2(  0.0f,  1.0f ), float2(  1.0f,  1.0f ),
                float2( -1.0f,  0.0f ), float2(  0.0f,  0.0f ), float2(  1.0f,  0.0f ),
                float2( -1.0f, -1.0f ), float2(  0.0f, -1.0f ), float2(  1.0f, -1.0f )
            };

            const float WEIGHTS[9] = {
                0.0625f, 0.125f, 0.0625f,
                0.125f,  0.25f,  0.125f,
                0.0625f, 0.125f, 0.0625f
            };

            half4 color = 0;

            [unroll]
            for( int i = 0; i < 9; i++ )
            {
                float2 currentUV = uv + COORDS[i] * _SourceTexLowMip_TexelSize.xy;
                color += WEIGHTS[i] * SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, currentUV);
            }

            half4 highMip = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);

            return lerp(highMip, color, Scatter);

            #endif
        }

        half4 FragUpsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            
            // URP Old
            half3 color = Upsample(UnityStereoTransformScreenSpaceTex(input.uv));

            return EncodeHDR(color);
        }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Bloom Prefilter"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragPrefilter
                #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }

        
        Pass
        {
            Name "Bloom Blur Horizontal"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Vertical"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragBlurV
            ENDHLSL
        }
        

        Pass
        {
            Name "Bloom Upsample"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragUpsample
                #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }

        
        Pass 
        {
            Name "Bloom Blur Alternate"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragBlurRadial
            ENDHLSL
        }
    }
}
