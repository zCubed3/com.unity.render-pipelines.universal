using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    public class RealtimeVolumetrics : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public ComputeShader computeShader;
            public Shader blendShader;
            public int tileSize = 32;
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
            if (settings.computeShader == null || settings.blendShader == null)
                return;

            if (!fogPass.blendMaterial)
                fogPass.blendMaterial = new Material(settings.blendShader);

            fogPass.settings = settings;
            renderer.EnqueuePass(fogPass);
        }
    }
}