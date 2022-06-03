using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetrics : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public ComputeShader fogShader;
            public Shader blendShader;

            [Header("TODO: Integrate into camera!")]
            public float far = 10;
            public float density = 1;
            public int steps = 256;

            public int downsample = 0;
        }

        [Header("EXPERIMENTAL!")]
        public Settings settings;

        RealtimeVolumetricsPass fogPass;

        public override void Create()
        {
            fogPass = new RealtimeVolumetricsPass();
            fogPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.fogShader == null)
                return;

            if (!fogPass.kernel.HasValue)
                fogPass.kernel = settings.fogShader.FindKernel("CSMain");

            if (!fogPass.kernel.HasValue)
                return;

            if (!fogPass.blendMaterial)
                fogPass.blendMaterial = new Material(settings.blendShader);

            fogPass.computeShader = settings.fogShader;
            fogPass.downsample = settings.downsample;
            fogPass.steps = settings.steps;
            fogPass.far = settings.far;
            fogPass.density = settings.density;

            renderer.EnqueuePass(fogPass);
        }
    }
}