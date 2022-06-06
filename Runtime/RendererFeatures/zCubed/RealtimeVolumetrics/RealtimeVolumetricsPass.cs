using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetricsPass : ScriptableRenderPass
    {
        // Precomputed shader properties so we waste less cycles
        public static class Properties
        {
            public static readonly int _CameraToWorld = Shader.PropertyToID("_CameraToWorld");
            public static readonly int _WorldToCamera = Shader.PropertyToID("_WorldToCamera");
            public static readonly int _Projection = Shader.PropertyToID("_Projection");
            public static readonly int _InverseProjection = Shader.PropertyToID("_InverseProjection");

            public static readonly int _MainShadowmap = Shader.PropertyToID("_MainShadowmap");
            public static readonly int _MainLightWorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
            public static readonly int _MainLightShadowParams = Shader.PropertyToID("_MainLightShadowParams");
            public static readonly int _MainLightFogParams = Shader.PropertyToID("_MainLightFogParams");
            public static readonly int _MainLightPosition = Shader.PropertyToID("_MainLightPosition");
            public static readonly int _MainLightColor = Shader.PropertyToID("_MainLightColor");

            public static readonly int _AdditionalShadowmap = Shader.PropertyToID("_AdditionalShadowmap");
            public static readonly int _AdditionalLightsCount = Shader.PropertyToID("_AdditionalLightsCount");
            public static readonly int _AdditionalLightsPosition = Shader.PropertyToID("_AdditionalLightsPosition");
            public static readonly int _AdditionalLightsColor = Shader.PropertyToID("_AdditionalLightsColor");
            public static readonly int _AdditionalLightsAttenuation = Shader.PropertyToID("_AdditionalLightsAttenuation");
            public static readonly int _AdditionalLightsSpotDir = Shader.PropertyToID("_AdditionalLightsSpotDir");
            public static readonly int _AdditionalShadowParams = Shader.PropertyToID("_AdditionalShadowParams");
            public static readonly int _AdditionalLightsWorldToShadow = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
            public static readonly int _AdditionalLightsFogParams = Shader.PropertyToID("_AdditionalLightsFogParams");

            public static readonly int _SceneDepth = Shader.PropertyToID("_SceneDepth");
            public static readonly int _Result = Shader.PropertyToID("_Result");

            public static readonly int _FogParams = Shader.PropertyToID("_FogParams");

            public static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int _EyeIndex = Shader.PropertyToID("_EyeIndex");
        }

        public RealtimeVolumetrics.Settings settings;

        public Material blendMaterial;

        RenderTargetIdentifier fogIdent;
        int fogWidth, fogHeight;

        MainLightShadowCasterPass mainLightPass;
        AdditionalLightsShadowCasterPass additionalLightPass;

        const int FOG_TEX_ID = 2000;

        //
        // Properties
        //
        public ComputeShader computeShader;

        //
        // Sync with URP values!
        //
        const int MAX_VISIBLE_LIGHTS = 256;

        Vector4[] additionalLightsPosition = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsColor = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsAttenuation = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsSpotDir = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsFogParams = new Vector4[MAX_VISIBLE_LIGHTS];

        int PowerOf2(int exp)
        {
            int e = 1;

            for (int i = 0; i < exp; i++)
                e *= 2;

            return e;
        }

        public override void SetupRenderer(ScriptableRenderer renderer)
        {
            foreach (ScriptableRenderPass pass in renderer.allRenderPassQueue)
            {
                if (pass is MainLightShadowCasterPass mainPass)
                    mainLightPass = mainPass;

                if (pass is AdditionalLightsShadowCasterPass additionalPass)
                    additionalLightPass = additionalPass;
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var additionalCameraData = renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            if (!additionalCameraData.renderVolumetrics)
                return;

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;

            int p2 = PowerOf2(additionalCameraData.volumetricsDownsampling);

            desc.width /= p2;
            desc.height /= p2;
            desc.enableRandomWrite = true;
            desc.useDynamicScale = false;
            desc.msaaSamples = 1;
            
            // Because single pass instanced causes issues we need to ensure the color buffer we get is only a Texture2D
            desc.dimension = TextureDimension.Tex2D;
            desc.volumeDepth = 0;

            cmd.GetTemporaryRT(FOG_TEX_ID, desc, FilterMode.Bilinear);
            fogIdent = new RenderTargetIdentifier(FOG_TEX_ID);

            fogWidth = desc.width;
            fogHeight = desc.height;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var additionalCameraData = renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            if (!additionalCameraData.renderVolumetrics)
                return;

            int kernel = computeShader.FindKernel("CSMain");

            CommandBuffer cmd = CommandBufferPool.Get("RealtimeVolumetricsPass");
            cmd.Clear();

            int tilesX = Mathf.CeilToInt((float)fogWidth / settings.tileSize);
            int tilesY = Mathf.CeilToInt((float)fogHeight / settings.tileSize);

            var xrKeyword = new LocalKeyword(computeShader, "UNITY_STEREO_INSTANCING_ENABLED");
            cmd.SetComputeFloatParam(computeShader, Properties._EyeIndex, 0);

            bool isXr = renderingData.cameraData.xrRendering;

            if (isXr)
                cmd.EnableKeyword(computeShader, xrKeyword);
            else
                cmd.DisableKeyword(computeShader, xrKeyword);

            cmd.SetComputeTextureParam(computeShader, kernel, Properties._SceneDepth, renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.SetComputeTextureParam(computeShader, kernel, Properties._Result, fogIdent);

            if (!isXr)
            {
                cmd.SetComputeMatrixParam(computeShader, Properties._CameraToWorld, renderingData.cameraData.camera.cameraToWorldMatrix);
                cmd.SetComputeMatrixParam(computeShader, Properties._WorldToCamera, renderingData.cameraData.camera.worldToCameraMatrix);
                cmd.SetComputeMatrixParam(computeShader, Properties._Projection, GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, Properties._InverseProjection, renderingData.cameraData.camera.projectionMatrix.inverse);
            }
            else
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                var projMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);

                cmd.SetComputeMatrixParam(computeShader, Properties._CameraToWorld, viewMatrix.inverse);
                cmd.SetComputeMatrixParam(computeShader, Properties._WorldToCamera, viewMatrix);
                cmd.SetComputeMatrixParam(computeShader, Properties._Projection, GL.GetGPUProjectionMatrix(projMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, Properties._InverseProjection, projMatrix.inverse);
            }

            if (mainLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel, Properties._MainShadowmap, new RenderTargetIdentifier(mainLightPass.mainLightShadowmapTexture));
                cmd.SetComputeMatrixArrayParam(computeShader, Properties._MainLightWorldToShadow, mainLightPass.mainLightShadowMatrices);
            }

            if (additionalLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel, Properties._AdditionalShadowmap, new RenderTargetIdentifier(additionalLightPass.additionalLightsShadowmapTexture));
            }

            //
            // Main light
            //
            bool skipMain = false;
            int mainLightIdx = renderingData.lightData.mainLightIndex;
            if (mainLightIdx >= 0)
            {
                skipMain = !renderingData.lightData.visibleLights[mainLightIdx].light.GetUniversalAdditionalLightData().volumetricsEnabled;
            }

            if (mainLightIdx >= 0 && !skipMain)
            {
                var mainLight = renderingData.lightData.visibleLights[mainLightIdx];
                var lightData = mainLight.light.GetUniversalAdditionalLightData();

                cmd.SetComputeVectorParam(computeShader, Properties._MainLightPosition, mainLight.light.transform.forward);

                if (!lightData.volumetricsSyncIntensity)
                    cmd.SetComputeVectorParam(computeShader, Properties._MainLightColor, (mainLight.finalColor / mainLight.light.intensity) * lightData.volumetricsIntensity);
                else
                    cmd.SetComputeVectorParam(computeShader, Properties._MainLightColor, mainLight.finalColor);

                cmd.SetComputeVectorParam(computeShader, Properties._MainLightFogParams, new Vector4(lightData.volumetricsPower, 0, 0, 0));
                cmd.SetComputeVectorParam(computeShader, Properties._MainLightShadowParams, mainLight.light.shadows != LightShadows.None ? Vector4.one : Vector4.zero);

            }
            else
                cmd.SetComputeVectorParam(computeShader, Properties._MainLightColor, Color.black);

            //
            // Additional lights
            //
            int actual = 0;
            for (int l = 0; l < MAX_VISIBLE_LIGHTS; l++)
            {
                if (l != mainLightIdx)
                {
                    bool dontContribute = false;
                    UniversalAdditionalLightData lightData = null;

                    if (l < renderingData.lightData.visibleLights.Length)
                    {
                        lightData = renderingData.lightData.visibleLights[l].light.GetUniversalAdditionalLightData();
                        dontContribute = !lightData.volumetricsEnabled;
                    }

                    if (!(l >= renderingData.lightData.visibleLights.Length || dontContribute))
                    {
                        Vector4 temp;
                        UniversalRenderPipeline.InitializeLightConstants_Common(
                            renderingData.lightData.visibleLights,
                            l,
                            out additionalLightsPosition[actual],
                            out additionalLightsColor[actual],
                            out additionalLightsAttenuation[actual],
                            out additionalLightsSpotDir[actual],
                            out temp
                        );

                        VisibleLight light = renderingData.lightData.visibleLights[l];
                        if (!lightData.volumetricsSyncIntensity)
                            additionalLightsColor[actual] = (light.light.color / light.light.intensity) * lightData.volumetricsIntensity;

                        additionalLightsFogParams[actual] = new Vector4(lightData.volumetricsPower, 0, 0, 0);

                        actual++;
                    }
                }
            }


            if (actual > 0)
            {
                cmd.SetComputeVectorParam(computeShader, Properties._AdditionalLightsCount, new Vector4(actual, 0, 0, 0));
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalLightsPosition, additionalLightsPosition);
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalLightsColor, additionalLightsColor);
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalLightsAttenuation, additionalLightsAttenuation);
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalLightsSpotDir, additionalLightsSpotDir);
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalShadowParams, additionalLightPass.additionalLightIndexToShadowParams);
                cmd.SetComputeMatrixArrayParam(computeShader, Properties._AdditionalLightsWorldToShadow, additionalLightPass.additionalLightShadowSliceIndexTo_WorldShadowMatrix);
                cmd.SetComputeVectorArrayParam(computeShader, Properties._AdditionalLightsFogParams, additionalLightsFogParams);
            }
            else
                cmd.SetComputeVectorParam(computeShader, Properties._AdditionalLightsCount, Vector4.zero);

            cmd.SetComputeVectorParam(computeShader, Properties._FogParams, new Vector4(
                additionalCameraData.volumetricsSteps,
                additionalCameraData.volumetricsFar,
                additionalCameraData.volumetricsDensity,
                additionalCameraData.volumetricsScattering
            ));

            //
            // Presentation
            //

            // I know this isn't good for performance but it's the best option for right now!
            // If we're doing XR rendering we need to dispatch twice and blit twice
            cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, 1);

            // Blit alternative for XR
            cmd.SetGlobalFloat(Properties._EyeIndex, 0);
            cmd.SetGlobalTexture(Properties._MainTex, fogIdent);
            cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            
            if (isXr)
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                var projMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

                cmd.SetComputeMatrixParam(computeShader, Properties._CameraToWorld, viewMatrix.inverse);
                cmd.SetComputeMatrixParam(computeShader, Properties._WorldToCamera, viewMatrix);
                cmd.SetComputeMatrixParam(computeShader, Properties._Projection, GL.GetGPUProjectionMatrix(projMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, Properties._InverseProjection, projMatrix.inverse);

                cmd.SetComputeFloatParam(computeShader, Properties._EyeIndex, 1);
                cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, 1);

                cmd.SetGlobalFloat(Properties._EyeIndex, 1);
                cmd.SetGlobalTexture(Properties._MainTex, fogIdent);
                cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
                cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            }

            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}