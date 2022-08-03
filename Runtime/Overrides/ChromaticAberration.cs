using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing/Chromatic Aberration", typeof(UniversalRenderPipeline))]
    public sealed class ChromaticAberration : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Use the slider to set the strength of the Chromatic Aberration effect.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        // zCubed Additions
        [Tooltip("How prevalent is the red channel?")]
        public Vector3Parameter chromaRed = new Vector3Parameter(new Vector3(1, 0, 0));

        [Tooltip("How prevalent is the green channel?")]
        public Vector3Parameter chromaGreen = new Vector3Parameter(new Vector3(0, 1, 0));

        [Tooltip("How prevalent is the blue channel?")]
        public Vector3Parameter chromaBlue = new Vector3Parameter(new Vector3(0, 0, 1));
        // ================

        public bool IsActive() => intensity.value > 0f;

        public bool IsTileCompatible() => false;
    }
}
