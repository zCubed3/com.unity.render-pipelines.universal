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
            public static readonly int _InverseViewProjection = Shader.PropertyToID("_InverseViewProjection");

            public static readonly int _MainShadowmap = Shader.PropertyToID("_MainShadowmap");
            public static readonly int _MainLightWorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
            public static readonly int _MainLightShadowParams = Shader.PropertyToID("_MainLightShadowParams");
            public static readonly int _MainLightFogParams = Shader.PropertyToID("_MainLightFogParams");
            public static readonly int _MainLightPosition = Shader.PropertyToID("_MainLightPosition");
            public static readonly int _MainLightColor = Shader.PropertyToID("_MainLightColor");
            public static readonly int _CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
            public static readonly int _CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
            public static readonly int _CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
            public static readonly int _CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
            public static readonly int _CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");

            public static readonly int _AdditionalShadowmap = Shader.PropertyToID("_AdditionalShadowmap");
            public static readonly int _AdditionalLightsCount = Shader.PropertyToID("_AdditionalLightsCount");
            public static readonly int _AdditionalLightsPosition = Shader.PropertyToID("_AdditionalLightsPosition");
            public static readonly int _AdditionalLightsColor = Shader.PropertyToID("_AdditionalLightsColor");
            public static readonly int _AdditionalLightsAttenuation = Shader.PropertyToID("_AdditionalLightsAttenuation");
            public static readonly int _AdditionalLightsSpotDir = Shader.PropertyToID("_AdditionalLightsSpotDir");
            public static readonly int _AdditionalShadowParams = Shader.PropertyToID("_AdditionalShadowParams");
            public static readonly int _AdditionalLightsWorldToShadow = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
            public static readonly int _AdditionalLightsFogParams = Shader.PropertyToID("_AdditionalLightsFogParams");

            public static readonly int _NoiseTex = Shader.PropertyToID("_NoiseTex");
            public static readonly int _NoiseData = Shader.PropertyToID("_NoiseData");

            public static readonly int _SceneDepth = Shader.PropertyToID("_SceneDepth");
            public static readonly int _Result = Shader.PropertyToID("_Result");
            public static readonly int _ResultOther = Shader.PropertyToID("_ResultOther");

            public static readonly int _FogParams = Shader.PropertyToID("_FogParams");

            public static readonly int _DepthMSAA = Shader.PropertyToID("_DepthMSAA");

            public static readonly int _PassData = Shader.PropertyToID("_PassData");

            public static readonly int _MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int _MainTexOther = Shader.PropertyToID("_MainTexOther");
            public static readonly int _EyeIndex = Shader.PropertyToID("_EyeIndex");
        }

        RenderTargetIdentifier fogIdent, fogCompositeIdent;
        int fogWidth, fogHeight, fogDepth;

        int depthMSAA = 1;

        MainLightShadowCasterPass mainLightPass;
        AdditionalLightsShadowCasterPass additionalLightPass;

        const int FOG_TEX_ID = 2000, FOG_COMPOSITE_TEX_ID = 2001;
        const int TILE_SIZE = 8, COMPOSITE_TILE_SIZE = 32;

        //
        // Properties
        //
        public RealtimeVolumetrics.Settings settings;
        public ComputeShader samplerCS, compositorCS;
        public Material blendPS;
        public Texture2D noisePattern;

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
            desc.volumeDepth = (int)additionalCameraData.volumetricsQuality;

            // We need our own specific buffer
            desc.colorFormat = RenderTextureFormat.ARGBHalf;
            desc.depthBufferBits = 0;
            desc.dimension = TextureDimension.Tex3D;
            desc.enableRandomWrite = true;
            desc.useDynamicScale = false;
            desc.msaaSamples = 1;

            cmd.GetTemporaryRT(FOG_TEX_ID, desc, FilterMode.Bilinear);
            fogIdent = new RenderTargetIdentifier(FOG_TEX_ID);

            cmd.GetTemporaryRT(FOG_COMPOSITE_TEX_ID, desc, FilterMode.Bilinear);
            fogCompositeIdent = new RenderTargetIdentifier(FOG_COMPOSITE_TEX_ID);

            depthMSAA = renderingData.cameraData.cameraTargetDescriptor.msaaSamples;

            fogWidth = desc.width;
            fogHeight = desc.height;
            fogDepth = desc.volumeDepth;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var additionalCameraData = renderingData.cameraData.camera.GetUniversalAdditionalCameraData();
            var renderer = renderingData.cameraData.renderer as UniversalRenderer;

            if (!additionalCameraData.renderVolumetrics)
                return;

            int kernel = samplerCS.FindKernel("CSMain");

            CommandBuffer cmd = CommandBufferPool.Get("RealtimeVolumetricsPass");
            cmd.Clear();

            int tilesX = Mathf.CeilToInt((float)fogWidth / TILE_SIZE);
            int tilesY = Mathf.CeilToInt((float)fogHeight / TILE_SIZE);
            int tilesZ = Mathf.CeilToInt((float)fogDepth / TILE_SIZE);

            bool isXr = renderingData.cameraData.xrRendering;



            Matrix4x4 camToWorld;
            Matrix4x4 worldToCam;
            Matrix4x4 projMatrix;
            Matrix4x4 invProjMatrix;
            Matrix4x4 invViewProjMatrix;

            if (!isXr)
            {
                camToWorld = renderingData.cameraData.camera.cameraToWorldMatrix;
                worldToCam = renderingData.cameraData.camera.worldToCameraMatrix;
                projMatrix = renderingData.cameraData.camera.projectionMatrix;
                invProjMatrix = renderingData.cameraData.camera.projectionMatrix.inverse;

                var correctProj = GL.GetGPUProjectionMatrix(projMatrix, true);
                invViewProjMatrix = (correctProj * worldToCam).inverse;
            }
            else
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                var projectionMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);

                camToWorld = viewMatrix.inverse;
                worldToCam = viewMatrix;
                projMatrix = projectionMatrix;
                invProjMatrix = projectionMatrix.inverse;

                var correctProj = GL.GetGPUProjectionMatrix(projMatrix, true);
                invViewProjMatrix = (correctProj * worldToCam).inverse;
            }

            projMatrix = GL.GetGPUProjectionMatrix(projMatrix, true).inverse;
            projMatrix.m11 *= -1;

            if (mainLightPass != null)
            {
                cmd.SetComputeTextureParam(samplerCS, kernel, Properties._MainShadowmap, new RenderTargetIdentifier(mainLightPass.mainLightShadowmapTexture));
                cmd.SetComputeMatrixArrayParam(samplerCS, Properties._MainLightWorldToShadow, mainLightPass.mainLightShadowMatrices);

                cmd.SetComputeVectorParam(samplerCS, Properties._CascadeShadowSplitSpheres0,
                    mainLightPass.cascadeSplitDistances[0]);
                cmd.SetComputeVectorParam(samplerCS, Properties._CascadeShadowSplitSpheres1,
                    mainLightPass.cascadeSplitDistances[1]);
                cmd.SetComputeVectorParam(samplerCS, Properties._CascadeShadowSplitSpheres2,
                    mainLightPass.cascadeSplitDistances[2]);
                cmd.SetComputeVectorParam(samplerCS, Properties._CascadeShadowSplitSpheres3,
                    mainLightPass.cascadeSplitDistances[3]);
                cmd.SetComputeVectorParam(samplerCS, Properties._CascadeShadowSplitSphereRadii, new Vector4(
                    mainLightPass.cascadeSplitDistances[0].w * mainLightPass.cascadeSplitDistances[0].w,
                    mainLightPass.cascadeSplitDistances[1].w * mainLightPass.cascadeSplitDistances[1].w,
                    mainLightPass.cascadeSplitDistances[2].w * mainLightPass.cascadeSplitDistances[2].w,
                    mainLightPass.cascadeSplitDistances[3].w * mainLightPass.cascadeSplitDistances[3].w));
            }

            if (additionalLightPass != null)
            {
                cmd.SetComputeTextureParam(samplerCS, kernel, Properties._AdditionalShadowmap, new RenderTargetIdentifier(additionalLightPass.additionalLightsShadowmapTexture));
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

                cmd.SetComputeVectorParam(samplerCS, Properties._MainLightPosition, mainLight.light.transform.forward);

                if (!lightData.volumetricsSyncIntensity)
                    cmd.SetComputeVectorParam(samplerCS, Properties._MainLightColor, (mainLight.finalColor / mainLight.light.intensity) * lightData.volumetricsIntensity);
                else
                    cmd.SetComputeVectorParam(samplerCS, Properties._MainLightColor, mainLight.finalColor);

                cmd.SetComputeVectorParam(samplerCS, Properties._MainLightFogParams, new Vector4(lightData.volumetricsPower, 0, 0, 0));
                cmd.SetComputeVectorParam(samplerCS, Properties._MainLightShadowParams, mainLight.light.shadows != LightShadows.None ? Vector4.one : Vector4.zero);

            }
            else
                cmd.SetComputeVectorParam(samplerCS, Properties._MainLightColor, Color.black);

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
                cmd.SetComputeVectorParam(samplerCS, Properties._AdditionalLightsCount, new Vector4(actual, 0, 0, 0));
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalLightsPosition, additionalLightsPosition);
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalLightsColor, additionalLightsColor);
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalLightsAttenuation, additionalLightsAttenuation);
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalLightsSpotDir, additionalLightsSpotDir);
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalShadowParams, additionalLightPass.additionalLightIndexToShadowParams);
                cmd.SetComputeMatrixArrayParam(samplerCS, Properties._AdditionalLightsWorldToShadow, additionalLightPass.additionalLightShadowSliceIndexTo_WorldShadowMatrix);
                cmd.SetComputeVectorArrayParam(samplerCS, Properties._AdditionalLightsFogParams, additionalLightsFogParams);
            }
            else
                cmd.SetComputeVectorParam(samplerCS, Properties._AdditionalLightsCount, Vector4.zero);

            //
            // Light cookies
            //
            if (renderingData.cameraData.renderer is UniversalRenderer urp)
            {
                if (urp.lightCookieManager != null)
                {
                    if (urp.lightCookieManager.additionalLightCookieAtlas != null)
                    {
                        cmd.SetComputeTextureParam(samplerCS, kernel, "_A", urp.lightCookieManager.additionalLightCookieAtlas.AtlasTexture);
                    }
                }
            }

            //
            // Projection
            //
            Matrix4x4[] cameraToWorld = new Matrix4x4[2];
            Matrix4x4[] worldToCamera = new Matrix4x4[2];
            Matrix4x4[] projection = new Matrix4x4[2];
            Matrix4x4[] inverseProjection = new Matrix4x4[2];
            Matrix4x4[] inverseViewProjection = new Matrix4x4[2];
            if (isXr)
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                var projectionMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

                Matrix4x4 camToWorld2 = viewMatrix.inverse;
                Matrix4x4 worldToCam2 = viewMatrix;
                Matrix4x4 projMatrix2 = projectionMatrix;
                Matrix4x4 invProjMatrix2 = projectionMatrix.inverse;

                var correctProj = GL.GetGPUProjectionMatrix(projMatrix2, true);
                Matrix4x4 invViewProjMatrix2 = (correctProj * worldToCam2).inverse;

                projMatrix2 = GL.GetGPUProjectionMatrix(projMatrix, true).inverse;
                projMatrix2.m11 *= -1;

                cameraToWorld = new Matrix4x4[2] { camToWorld, camToWorld2 };
                worldToCamera = new Matrix4x4[2] { worldToCam, worldToCam2 };
                projection = new Matrix4x4[2] { projMatrix, projMatrix2 };
                inverseProjection = new Matrix4x4[2] { invProjMatrix, invProjMatrix2 };
                inverseViewProjection = new Matrix4x4[2] { invViewProjMatrix, invViewProjMatrix2 };
            } 
            else
            {
                cameraToWorld = new Matrix4x4[2] { camToWorld, Matrix4x4.identity };
                worldToCamera = new Matrix4x4[2] { worldToCam, Matrix4x4.identity };
                projection = new Matrix4x4[2] { projMatrix, Matrix4x4.identity };
                inverseProjection = new Matrix4x4[2] { invProjMatrix, Matrix4x4.identity };
                inverseViewProjection = new Matrix4x4[2] { invViewProjMatrix, Matrix4x4.identity };
            }

            //
            // Sampler step
            //
            cmd.SetComputeMatrixArrayParam(samplerCS, Properties._CameraToWorld, cameraToWorld);
            cmd.SetComputeMatrixArrayParam(samplerCS, Properties._WorldToCamera, worldToCamera);
            cmd.SetComputeMatrixArrayParam(samplerCS, Properties._Projection, projection);
            cmd.SetComputeMatrixArrayParam(samplerCS, Properties._InverseProjection, inverseProjection);

            cmd.SetComputeVectorParam(samplerCS, Properties._FogParams, new Vector4(
                fogDepth,
                additionalCameraData.volumetricsFar,
                additionalCameraData.volumetricsDensity,
                additionalCameraData.volumetricsScattering
            ));

            cmd.SetComputeVectorParam(samplerCS, Properties._PassData, new Vector4(fogWidth, fogHeight, fogDepth, 0));
            cmd.SetComputeTextureParam(samplerCS, kernel, Properties._Result, fogIdent);

            cmd.DispatchCompute(samplerCS, kernel, tilesX, tilesY, tilesZ);

            //
            // Compositor step
            //
            int compositorKernel = compositorCS.FindKernel("CSMain");

            cmd.SetComputeTextureParam(compositorCS, compositorKernel, Properties._Result, fogCompositeIdent);           
            cmd.SetComputeTextureParam(compositorCS, compositorKernel, Properties._MainTex, fogIdent);

            cmd.SetComputeTextureParam(compositorCS, compositorKernel, Properties._SceneDepth, renderingData.cameraData.renderer.cameraDepthTarget);

            LocalKeyword mediumQuality = new LocalKeyword(compositorCS, "QUALITY_MEDIUM");
            LocalKeyword highQuality = new LocalKeyword(compositorCS, "QUALITY_HIGH");
            LocalKeyword ultraQuality = new LocalKeyword(compositorCS, "QUALITY_ULTRA");
            switch (additionalCameraData.volumetricsQuality)
            {
                default:
                    cmd.DisableKeyword(compositorCS, mediumQuality);
                    cmd.DisableKeyword(compositorCS, highQuality);
                    cmd.DisableKeyword(compositorCS, ultraQuality);
                    break;

                case RealtimeVolumetrics.BufferQuality.Medium:
                    cmd.EnableKeyword(compositorCS, mediumQuality);
                    cmd.DisableKeyword(compositorCS, highQuality);
                    cmd.DisableKeyword(compositorCS, ultraQuality);
                    break;

                case RealtimeVolumetrics.BufferQuality.High:
                    cmd.DisableKeyword(compositorCS, mediumQuality);
                    cmd.EnableKeyword(compositorCS, highQuality);
                    cmd.DisableKeyword(compositorCS, ultraQuality);
                    break;

                case RealtimeVolumetrics.BufferQuality.Ultra:
                    cmd.DisableKeyword(compositorCS, mediumQuality);
                    cmd.DisableKeyword(compositorCS, highQuality);
                    cmd.EnableKeyword(compositorCS, ultraQuality);
                    break;
            }

            tilesX = Mathf.CeilToInt((float)fogWidth / COMPOSITE_TILE_SIZE);
            tilesY = Mathf.CeilToInt((float)fogHeight / COMPOSITE_TILE_SIZE);
            cmd.DispatchCompute(compositorCS, kernel, tilesX, tilesY, 1);

            //
            // Blending step
            //
            switch (depthMSAA)
            {
                default:
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_2");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_4");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_8");
                    break;

                case 2:
                    cmd.EnableShaderKeyword("_DEPTH_MSAA_2");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_4");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_8");
                    break;

                case 4:
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_2");
                    cmd.EnableShaderKeyword("_DEPTH_MSAA_4");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_8");
                    break;

                case 8:
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_2");
                    cmd.DisableShaderKeyword("_DEPTH_MSAA_4");
                    cmd.EnableShaderKeyword("_DEPTH_MSAA_8");
                    break;
            }

            cmd.SetGlobalTexture(Properties._MainTex, fogCompositeIdent);
            cmd.SetGlobalTexture(Properties._SceneDepth, renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.SetGlobalVector(Properties._PassData, new Vector4(additionalCameraData.volumetricsFar, 0));
            cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendPS, 0, 0);

            // If we're in VR we need to repeat this once again for the other eye!
            if (isXr)
            {
                cmd.SetComputeVectorParam(samplerCS, Properties._PassData, new Vector4(fogWidth, fogHeight, fogDepth, 1));

                tilesX = Mathf.CeilToInt((float)fogWidth / TILE_SIZE);
                tilesY = Mathf.CeilToInt((float)fogHeight / TILE_SIZE);
                cmd.DispatchCompute(samplerCS, kernel, tilesX, tilesY, tilesZ);

                tilesX = Mathf.CeilToInt((float)fogWidth / COMPOSITE_TILE_SIZE);
                tilesY = Mathf.CeilToInt((float)fogHeight / COMPOSITE_TILE_SIZE);
                cmd.DispatchCompute(compositorCS, kernel, tilesX, tilesY, 1);

                cmd.SetGlobalVector(Properties._PassData, new Vector4(additionalCameraData.volumetricsFar, 1));
                cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendPS, 0, 0);
            } 

            cmd.ReleaseTemporaryRT(FOG_TEX_ID);
            cmd.ReleaseTemporaryRT(FOG_COMPOSITE_TEX_ID);

            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}