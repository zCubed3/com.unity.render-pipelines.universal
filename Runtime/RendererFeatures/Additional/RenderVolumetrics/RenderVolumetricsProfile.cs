using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    [Serializable, VolumeComponentMenu("URP Additions/Volumetrics")]
    public sealed class RenderVolumetricsProfile : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("The strength of sampled fog.")]
        public MinFloatParameter density = new MinFloatParameter(1F, 0F);

        [Tooltip("The scattering factor (changes how realtime light is scattered).")]
        public ClampedFloatParameter scattering = new ClampedFloatParameter(0.5F, 0F, 1F);

        /// <summary>
        /// Is the component active?
        /// </summary>
        /// <returns>True is the component is active</returns>
        public bool IsActive() => density.value > 0f;

        /// <summary>
        /// Is the component compatible with on tile rendering
        /// </summary>
        /// <returns>false</returns>
        public bool IsTileCompatible() => false;
    }
}
