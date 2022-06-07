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
            public static readonly int _FogSteps = Shader.PropertyToID("_FogSteps");

            public static readonly int _PassData = Shader.PropertyToID("_PassData");

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
        const int TILE_SIZE = 8;

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

            desc.width = Mathf.CeilToInt((float)desc.width * additionalCameraData.volumetricsPercent);
            desc.height = Mathf.CeilToInt((float)desc.height * additionalCameraData.volumetricsPercent);
            desc.enableRandomWrite = true;
            desc.useDynamicScale = false;
            desc.msaaSamples = 1;

            // We need our own specific buffer
            desc.colorFormat = RenderTextureFormat.ARGBHalf;
            desc.depthBufferBits = 0;
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = additionalCameraData.volumetricsSlices;

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

            int kernel = computeShader.FindKernel("VolumetricMain");

            CommandBuffer cmd = CommandBufferPool.Get("RealtimeVolumetricsPass");
            cmd.Clear();

            int tilesX = Mathf.CeilToInt((float)fogWidth / TILE_SIZE);
            int tilesY = Mathf.CeilToInt((float)fogHeight / TILE_SIZE);
            int tilesZ = Mathf.CeilToInt((float)additionalCameraData.volumetricsSlices / TILE_SIZE);

            var xrKeyword = new LocalKeyword(computeShader, "UNITY_STEREO_INSTANCING_ENABLED");
            cmd.SetComputeFloatParam(computeShader, Properties._EyeIndex, 0);

            bool isXr = renderingData.cameraData.xrRendering;

            if (isXr)
                cmd.EnableKeyword(computeShader, xrKeyword);
            else
                cmd.DisableKeyword(computeShader, xrKeyword);

            cmd.SetComputeTextureParam(computeShader, kernel, Properties._Result, fogIdent);

            cmd.SetComputeVectorParam(computeShader, Properties._PassData, new Vector4(fogWidth, fogHeight, 0, 0));
            cmd.SetGlobalVector(Properties._PassData, new Vector4(fogWidth, fogHeight, 0, additionalCameraData.volumetricsSteps));

            Matrix4x4 camToWorld;
            Matrix4x4 worldToCam;
            Matrix4x4 projMatrix;
            Matrix4x4 invProjMatrix;

            if (!isXr)
            {
                camToWorld = renderingData.cameraData.camera.cameraToWorldMatrix;
                worldToCam = renderingData.cameraData.camera.worldToCameraMatrix;
                projMatrix = renderingData.cameraData.camera.projectionMatrix;
                invProjMatrix = renderingData.cameraData.camera.projectionMatrix.inverse; 
            }
            else
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                var projectionMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);

                camToWorld = viewMatrix.inverse;
                worldToCam = viewMatrix;
                projMatrix = projectionMatrix;
                invProjMatrix = projectionMatrix.inverse;
            }

            projMatrix = GL.GetGPUProjectionMatrix(projMatrix, true).inverse;
            projMatrix.m11 *= -1;

            cmd.SetComputeMatrixParam(computeShader, Properties._CameraToWorld, camToWorld);
            cmd.SetComputeMatrixParam(computeShader, Properties._WorldToCamera, worldToCam);
            cmd.SetComputeMatrixParam(computeShader, Properties._Projection, projMatrix);
            cmd.SetComputeMatrixParam(computeShader, Properties._InverseProjection, invProjMatrix);
            cmd.SetGlobalMatrix(Properties._CameraToWorld, camToWorld);
            cmd.SetGlobalMatrix(Properties._WorldToCamera, worldToCam);
            cmd.SetGlobalMatrix(Properties._Projection, projMatrix);
            cmd.SetGlobalMatrix(Properties._InverseProjection, invProjMatrix);

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

            var fogParams = new Vector4(
                additionalCameraData.volumetricsSlices,
                additionalCameraData.volumetricsFar,
                additionalCameraData.volumetricsDensity,
                additionalCameraData.volumetricsScattering
            );

            cmd.SetComputeVectorParam(computeShader, Properties._FogParams, fogParams);

            fogParams.x *= 4;
            cmd.SetGlobalVector(Properties._FogParams, fogParams);

            //
            // Presentation
            //

            // I know this isn't good for performance but it's the best option for right now!
            // If we're doing XR rendering we need to dispatch twice and blit twice
            cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, tilesZ);

            // Blit alternative for XR
            cmd.SetGlobalInt(Properties._FogSteps, additionalCameraData.volumetricsSteps);
            cmd.SetGlobalTexture(Properties._MainTex, fogIdent);
            cmd.SetGlobalTexture(Properties._SceneDepth, renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            
            if (isXr)
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                var projectionMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

                camToWorld = viewMatrix.inverse;
                worldToCam = viewMatrix;
                projMatrix = projectionMatrix;
                invProjMatrix = projectionMatrix.inverse;

                projMatrix = GL.GetGPUProjectionMatrix(projMatrix, true).inverse;
                projMatrix.m11 *= -1;

                cmd.SetComputeMatrixParam(computeShader, Properties._CameraToWorld, camToWorld);
                cmd.SetComputeMatrixParam(computeShader, Properties._WorldToCamera, worldToCam);
                cmd.SetComputeMatrixParam(computeShader, Properties._Projection, projMatrix);
                cmd.SetComputeMatrixParam(computeShader, Properties._InverseProjection, invProjMatrix);
                cmd.SetGlobalMatrix(Properties._CameraToWorld, camToWorld);
                cmd.SetGlobalMatrix(Properties._WorldToCamera, worldToCam);
                cmd.SetGlobalMatrix(Properties._Projection, projMatrix);
                cmd.SetGlobalMatrix(Properties._InverseProjection, invProjMatrix);

                cmd.SetComputeFloatParam(computeShader, Properties._EyeIndex, 1);
                cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, tilesZ);

                cmd.SetGlobalVector(Properties._PassData, new Vector4(fogWidth, fogHeight, 1, additionalCameraData.volumetricsSteps));
                cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
                cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            }

            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}