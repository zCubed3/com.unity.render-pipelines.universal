using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetrics : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("If you're looking for other settings, they're on the lights and cameras!")]
            [Tooltip("If this is set, we use a different compute shader")]
            public ComputeShader computeShader;

            [Tooltip("If this is set, we use a different blending shader")]
            public Shader blendShader;
        }

        [Header("EXPERIMENTAL and WIP!")]
        public Settings settings;

        RealtimeVolumetricsPass fogPass;

        public override void Create()
        {
            fogPass = new RealtimeVolumetricsPass();
            fogPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (fogPass.computeShader == null)
                fogPass.computeShader = settings.computeShader == null ? Resources.Load<ComputeShader>("RealtimeVolumetricsCompute") : settings.computeShader;

            if (fogPass.computeShader == null)
                Debug.LogError("Please assign a compute shader to the Volumetric Pass!");

            if (!fogPass.blendMaterial)
                fogPass.blendMaterial = new Material(settings.blendShader == null ? Shader.Find("zCubed/Volumetrics/BlitFog") : settings.blendShader);

            if (fogPass.blendMaterial == null)
                Debug.LogError("Please assign a blending shader to the Volumetric Pass!");

            fogPass.settings = settings;
            renderer.EnqueuePass(fogPass);
        }
    }
}