using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetricsPass : ScriptableRenderPass
    {
        public int downsample = 0;
        public int? kernel;

        public int steps;
        public float far, density;

        public ComputeShader computeShader;
        public Material blendMaterial;

        RenderTargetIdentifier fogIdent;
        int fogWidth, fogHeight;

        MainLightShadowCasterPass mainLightPass;
        AdditionalLightsShadowCasterPass additionalLightPass;

        //
        // Sync with compute shader
        //
        const int FOG_TEX_ID = 2000;
        const int TILE_SIZE = 32;

        //
        // Sync with URP values!
        //
        const int MAX_SHADOW_CASCADES = 4;
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

            int p2 = PowerOf2(downsample);

            desc.width /= p2;
            desc.height /= p2;

            desc.enableRandomWrite = true;

            cmd.GetTemporaryRT(FOG_TEX_ID, desc);
            fogIdent = new RenderTargetIdentifier(FOG_TEX_ID);

            fogWidth = desc.width;
            fogHeight = desc.height;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            CommandBuffer cmd = CommandBufferPool.Get("RealtimeComputeVolumetrics");
            cmd.Clear();

            int tilesX = Mathf.CeilToInt((float)fogWidth / TILE_SIZE);
            int tilesY = Mathf.CeilToInt((float)fogHeight / TILE_SIZE);

            cmd.SetComputeTextureParam(computeShader, kernel.Value, "_SceneDepth", renderingData.cameraData.renderer.cameraDepthTarget);
            cmd.SetComputeTextureParam(computeShader, kernel.Value, "_Result", fogIdent);

            cmd.SetComputeMatrixParam(computeShader, "_CameraToWorld", renderingData.cameraData.camera.cameraToWorldMatrix);
            cmd.SetComputeMatrixParam(computeShader, "_WorldToCamera", renderingData.cameraData.camera.worldToCameraMatrix);
            cmd.SetComputeMatrixParam(computeShader, "_Projection", GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true).inverse);
            cmd.SetComputeMatrixParam(computeShader, "_InverseProjection", renderingData.cameraData.camera.projectionMatrix.inverse);

            if (mainLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel.Value, "_MainShadowmap", new RenderTargetIdentifier(mainLightPass.mainLightShadowmapTexture));
                cmd.SetComputeMatrixArrayParam(computeShader, "_MainLightWorldToShadow", mainLightPass.mainLightShadowMatrices);
            }

            if (additionalLightPass != null)
            {
                cmd.SetComputeTextureParam(computeShader, kernel.Value, "_AdditionalShadowmap", new RenderTargetIdentifier(additionalLightPass.additionalLightsShadowmapTexture));
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

            cmd.SetComputeIntParam(computeShader, "_FogSteps", steps);
            cmd.SetComputeFloatParam(computeShader, "_FogFar", far);
            cmd.SetComputeFloatParam(computeShader, "_Density", density);

            //
            // Presentation
            //
            cmd.DispatchCompute(computeShader, kernel.Value, tilesX, tilesY, 1);
            cmd.Blit(fogIdent, renderingData.cameraData.renderer.cameraColorTarget, blendMaterial);

            context.ExecuteCommandBuffer(cmd);

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}