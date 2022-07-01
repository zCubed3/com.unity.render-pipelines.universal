using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetrics : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("If you're looking for other settings, they're on the lights and cameras!")]
            [Tooltip("If this is set, we use a different sampling shader")]
            public ComputeShader samplerCS;

            [Tooltip("If this is set, we use a different compositing shader")]
            public ComputeShader compositorCS;

            [Tooltip("If this is set, we use a different blending shader")]
            public Shader blendShader;

            [Tooltip("If this is set, we use a different noise pattern")]
            public Texture2D noisePattern;
        }

        [Header("EXPERIMENTAL and WIP!")]
        public Settings settings;

        RealtimeVolumetricsPass fogPass;

        public enum BufferQuality : int {
            Low     = 32,
            Medium  = 64,
            High    = 96,
            Ultra   = 128
        }

        public override void Create()
        {
            fogPass = new RealtimeVolumetricsPass();
            fogPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var additionalCameraData = renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            if (!additionalCameraData.renderVolumetrics)
                return;


            if (fogPass.samplerCS == null)
                fogPass.samplerCS = settings.samplerCS == null ? Resources.Load<ComputeShader>("VolumetricSampler") : settings.samplerCS;

            if (fogPass.samplerCS == null)
                Debug.LogError("Please assign a compute shader to the Volumetric Pass!");


            if (fogPass.compositorCS == null)
                fogPass.compositorCS = settings.compositorCS == null ? Resources.Load<ComputeShader>("VolumetricCompositor") : settings.compositorCS;

            if (fogPass.compositorCS == null)
                Debug.LogError("Please assign a compute shader to the Volumetric Pass!");


            if (!fogPass.blendPS)
                fogPass.blendPS = new Material(settings.blendShader == null ? Shader.Find("Hidden/Volumetrics/FogBlend") : settings.blendShader);

            if (fogPass.blendPS == null)
                Debug.LogError("Please assign a blending shader to the Volumetric Pass!");


            if (!fogPass.noisePattern)
                fogPass.noisePattern = settings.noisePattern == null ? Resources.Load<Texture2D>("VolumeNoisePattern") : settings.noisePattern;

            if (fogPass.noisePattern == null)
                Debug.LogError("Please assign a noise pattern to the Volumetric Pass!");

            fogPass.settings = settings;
            renderer.EnqueuePass(fogPass);
        }
    }
}