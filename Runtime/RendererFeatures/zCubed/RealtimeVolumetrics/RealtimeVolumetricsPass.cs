using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetricsPass : ScriptableRenderPass
    {
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
        ComputeShader computeShader { get => settings.computeShader; }

        //
        // Sync with URP values!
        //
        const int MAX_VISIBLE_LIGHTS = 256;

        Vector4[] additionalLightsPosition = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsColor = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsAttenuation = new Vector4[MAX_VISIBLE_LIGHTS];
        Vector4[] additionalLightsSpotDir = new Vector4[MAX_VISIBLE_LIGHTS];

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
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;

            int p2 = PowerOf2(settings.downsample);

            desc.width /= p2;
            desc.height /= p2;
            desc.enableRandomWrite = true;
            desc.dimension = TextureDimension.Tex2D;
            desc.volumeDepth = 0;

            cmd.GetTemporaryRT(FOG_TEX_ID, desc, FilterMode.Trilinear);
            fogIdent = new RenderTargetIdentifier(FOG_TEX_ID);

            fogWidth = desc.width;
            fogHeight = desc.height;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            int kernel = computeShader.FindKernel("CSMain");

            renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            CommandBuffer cmd = CommandBufferPool.Get("RealtimeComputeVolumetrics");
            cmd.Clear();

            int tilesX = Mathf.CeilToInt((float)fogWidth / settings.tileSize);
            int tilesY = Mathf.CeilToInt((float)fogHeight / settings.tileSize);

            var xrKeyword = new LocalKeyword(computeShader, "UNITY_STEREO_INSTANCING_ENABLED");
            cmd.SetComputeFloatParam(computeShader, "_EyeIndex", 0);

            bool isXr = renderingData.cameraData.xrRendering;

            if (isXr)
            {
                cmd.EnableKeyword(computeShader, xrKeyword);
            }
            else
            {
                cmd.DisableKeyword(computeShader, xrKeyword);
            }

            cmd.SetComputeTextureParam(computeShader, kernel, "_SceneDepth", renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.SetComputeTextureParam(computeShader, kernel, "_Result", fogIdent);

            if (!isXr)
            {
                cmd.SetComputeMatrixParam(computeShader, "_CameraToWorld", renderingData.cameraData.camera.cameraToWorldMatrix);
                cmd.SetComputeMatrixParam(computeShader, "_WorldToCamera", renderingData.cameraData.camera.worldToCameraMatrix);
                cmd.SetComputeMatrixParam(computeShader, "_Projection", GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, "_InverseProjection", renderingData.cameraData.camera.projectionMatrix.inverse);
            }
            else
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                var projMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);

                cmd.SetComputeMatrixParam(computeShader, "_CameraToWorld", viewMatrix.inverse);
                cmd.SetComputeMatrixParam(computeShader, "_WorldToCamera", viewMatrix);
                cmd.SetComputeMatrixParam(computeShader, "_Projection", GL.GetGPUProjectionMatrix(projMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, "_InverseProjection", projMatrix.inverse);
            }

            if (mainLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel, "_MainShadowmap", new RenderTargetIdentifier(mainLightPass.mainLightShadowmapTexture));
                cmd.SetComputeMatrixArrayParam(computeShader, "_MainLightWorldToShadow", mainLightPass.mainLightShadowMatrices);
            }

            if (additionalLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel, "_AdditionalShadowmap", new RenderTargetIdentifier(additionalLightPass.additionalLightsShadowmapTexture));
            }

            //
            // Main light
            //
            int mainLightIdx = renderingData.lightData.mainLightIndex;
            if (mainLightIdx >= 0)
            {
                var mainLight = renderingData.lightData.visibleLights[mainLightIdx];
                cmd.SetComputeVectorParam(computeShader, "_MainLightPosition", mainLight.light.transform.forward);
                cmd.SetComputeVectorParam(computeShader, "_MainLightColor", mainLight.finalColor);
            }
            else
                cmd.SetComputeVectorParam(computeShader, "_MainLightColor", Color.black);

            //
            // Additional lights
            //
            cmd.SetComputeVectorParam(computeShader, "_AdditionalLightsCount", new Vector4(renderingData.lightData.additionalLightsCount, 0, 0, 0));

            int actual = 0;
            for (int l = 0; l < MAX_VISIBLE_LIGHTS; l++)
            {
                if (l != mainLightIdx)
                {
                    if (l >= renderingData.lightData.visibleLights.Length)
                    {
                        additionalLightsPosition[actual] =
                        additionalLightsColor[actual] =
                        additionalLightsAttenuation[actual] =
                        additionalLightsSpotDir[actual] = Vector4.zero;
                    }
                    else
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
                    }
                    actual++;
                }
            }

            cmd.SetComputeVectorArrayParam(computeShader, "_AdditionalLightsPosition", additionalLightsPosition);
            cmd.SetComputeVectorArrayParam(computeShader, "_AdditionalLightsColor", additionalLightsColor);
            cmd.SetComputeVectorArrayParam(computeShader, "_AdditionalLightsAttenuation", additionalLightsAttenuation);
            cmd.SetComputeVectorArrayParam(computeShader, "_AdditionalLightsSpotDir", additionalLightsSpotDir);
            cmd.SetComputeVectorArrayParam(computeShader, "_AdditionalShadowParams", additionalLightPass.additionalLightIndexToShadowParams);
            cmd.SetComputeMatrixArrayParam(computeShader, "_AdditionalLightsWorldToShadow", additionalLightPass.additionalLightShadowSliceIndexTo_WorldShadowMatrix);

            cmd.SetComputeVectorParam(computeShader, "_FogParams", new Vector4(settings.steps, settings.far, settings.density, settings.scattering));

            //
            // Presentation
            //

            // If we're doing XR rendering we need to dispatch twice and blit twice
            cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, 1);

            // Blit alternative for XR
            cmd.SetGlobalFloat("_EyeIndex", 0);
            cmd.SetGlobalTexture("_MainTex", fogIdent);
            cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            
            if (isXr)
            {
                var viewMatrix = renderingData.cameraData.camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                var projMatrix = renderingData.cameraData.camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);

                cmd.SetComputeMatrixParam(computeShader, "_CameraToWorld", viewMatrix.inverse);
                cmd.SetComputeMatrixParam(computeShader, "_WorldToCamera", viewMatrix);
                cmd.SetComputeMatrixParam(computeShader, "_Projection", GL.GetGPUProjectionMatrix(projMatrix, true).inverse);
                cmd.SetComputeMatrixParam(computeShader, "_InverseProjection", projMatrix.inverse);

                cmd.SetComputeFloatParam(computeShader, "_EyeIndex", 1);
                cmd.DispatchCompute(computeShader, kernel, tilesX, tilesY, 1);

                cmd.SetGlobalFloat("_EyeIndex", 1);
                cmd.SetGlobalTexture("_MainTex", fogIdent);
                cmd.SetRenderTarget(new RenderTargetIdentifier(renderingData.cameraData.renderer.cameraColorTarget, 0, CubemapFace.Unknown, -1));
                cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, blendMaterial);
            }

            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}