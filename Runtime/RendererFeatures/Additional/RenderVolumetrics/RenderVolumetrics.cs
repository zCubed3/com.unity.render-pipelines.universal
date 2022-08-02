using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RenderVolumetrics : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("If you're looking for other settings, they're on the lights and cameras!")]
            [Tooltip("If this is set, we use a different sampling shader for baked lighting")]
            public ComputeShader bakedSamplerCS;

            [Tooltip("If this is set, we use a different sampling shader for realtime lighting")]
            public ComputeShader realtimeSamplerCS;

            [Tooltip("If this is set, we use a different compositing shader")]
            public ComputeShader compositorCS;

            [Tooltip("If this is set, we use a different blending shader")]
            public Shader blendShader;

            [Tooltip("If this is set, we use a different noise pattern")]
            public Texture2D noisePattern;
        }

        [Header("EXPERIMENTAL and WIP!")]
        public Settings settings;

        RenderVolumetricsPass fogPass;

        public enum BufferQuality : int {
            VeryLow     = 16,
            Low         = 32,
            Medium      = 64,
            High        = 96,
            Ultra       = 128,
            Overkill    = 256,
        }

        [System.Flags]
        public enum RenderFlags
        {
            None = 0,

            Realtime = 1,
            Baked = 2,

            All = ~0
        }

        public override void Create()
        {
            fogPass = new RenderVolumetricsPass();
            fogPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var additionalCameraData = renderingData.cameraData.camera.GetUniversalAdditionalCameraData();

            if (!additionalCameraData.renderVolumetrics)
                return;


            if (fogPass.realtimeSamplerCS == null)
                fogPass.realtimeSamplerCS = settings.realtimeSamplerCS == null ? Resources.Load<ComputeShader>("RealtimeVolumetricSampler") : settings.realtimeSamplerCS;

            if (fogPass.realtimeSamplerCS == null)
                Debug.LogError("Please assign a realtime sampler compute shader to the Volumetric Pass!");


            if (fogPass.bakedSamplerCS == null)
                fogPass.bakedSamplerCS = settings.bakedSamplerCS == null ? Resources.Load<ComputeShader>("BakedVolumetricSampler") : settings.bakedSamplerCS;

            if (fogPass.bakedSamplerCS == null)
                Debug.LogError("Please assign a baked sampler compute shader to the Volumetric Pass!");


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


            var stack = VolumeManager.instance.stack;
            var volumetricsProfile = stack.GetComponent<RenderVolumetricsProfile>();


            fogPass.settings = settings;
            fogPass.profile = volumetricsProfile;

            renderer.EnqueuePass(fogPass);
        }
    }
}