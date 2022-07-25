using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Additions
{
    [Serializable, VolumeComponentMenu("zCubed/Realtime Volumetrics")]
    public sealed class RealtimeVolumetricProfile : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// The strength of the motion blur filter. Acts as a multiplier for velocities.
        /// </summary>
        [Tooltip("The strength of the motion blur filter. Acts as a multiplier for velocities.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        
        /// <summary>
        /// Is the component active?
        /// </summary>
        /// <returns>True is the component is active</returns>
        public bool IsActive() => intensity.value > 0f;

        /// <summary>
        /// Is the component compatible with on tile rendering
        /// </summary>
        /// <returns>false</returns>
        public bool IsTileCompatible() => false;
    }
}
