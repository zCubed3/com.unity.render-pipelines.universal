using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal.Additions
{
    [ExecuteAlways]
    public class BakedVolume : MonoBehaviour
    {
        public static List<BakedVolume> bakedVolumes = new List<BakedVolume>();

        [System.Flags]
        public enum PassFlags 
        {
            None = 0,

            //Direct = 1,
            Indirect = 2,

            All = ~0
        }

        public enum SH9SampleShape
        {
            Octagonal,
            OctagonalCorners,
        }

        [Header("Shape")]
        public Bounds bounds = new Bounds(Vector3.zero, Vector3.one);

        [Min(0)]
        [Tooltip("What is the pixel width of the buffer?")]
        public int bufferWidth = 64;
        [Min(0)]
        [Tooltip("What is the pixel height of the buffer?")]
        public int bufferHeight = 64;
        [Min(0)]
        [Tooltip("What is the pixel depth of the buffer?")]
        public int bufferDepth = 64;

        [Header("Bake Settings")]
        [Tooltip("What passes are rendered into the bake?")]
        public PassFlags passFlags = PassFlags.All;

        [Tooltip("What shape do we sample indirect info in? (Octagonal = Left, Right, Up, Down, Forward, Back / OctagonalCorners = Rotated octagonal")]
        public SH9SampleShape sampleShape = SH9SampleShape.Octagonal;

        [Tooltip("Multiplies the final output")]
        [Min(0)]
        public float density = 1;

        [ColorUsage(false, true)]
        [Tooltip("An HDR color to filter the output by")]
        public Color filter = Color.white;

        [Header("Behavior")]
        public bool bakeAfterLightmapBake = true;

        [Header("Result")]
        public Texture3D buffer;

        public void OnEnable()
        {
            bakedVolumes.Add(this);

#if UNITY_EDITOR
            if (bakeAfterLightmapBake)
                Lightmapping.bakeCompleted += BakeVolume;
#endif
        }

        public void OnDisable()
        {
            bakedVolumes.Remove(this);

#if UNITY_EDITOR
            if (bakeAfterLightmapBake)
                Lightmapping.bakeCompleted -= BakeVolume;
#endif
        }

        public Vector3[] GetSampleDirections()
        {
            switch (sampleShape)
            {
                default:
                    return new Vector3[] { Vector3.forward };

                case SH9SampleShape.Octagonal:
                    return new Vector3[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left };

                case SH9SampleShape.OctagonalCorners:
                    {
                        Vector3[] directions = new Vector3[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left };
                        Quaternion quat = Quaternion.Euler(-45, -45, 0);

                        for (int d = 0; d < directions.Length; d++)
                            directions[d] = quat * directions[d];

                        return directions;
                    }
            }
        }

        public Vector3 GetPointInBounds(int x, int y, int z)
        {
            float xDelta = (float)x / ((float)bufferWidth - 1);
            float yDelta = (float)y / ((float)bufferHeight - 1);
            float zDelta = (float)z / ((float)bufferDepth - 1);

            Vector3 delta = new Vector3(xDelta, yDelta, zDelta);

            Vector3 interior = Vector3.Scale(bounds.size, delta) - bounds.extents;
            Vector3 point = bounds.center + interior;
            return transform.TransformPoint(point);
        }

        // TODO?
        void BakeDirect(ref Texture3D texture)
        {

        }

        // CPU only!
        // This uses the SH9 probes inside of Unity!
        void BakeIndirect(ref Texture3D texture)
        {
            for (int x = 0; x < bufferWidth; x++)
            {
                for (int y = 0; y < bufferHeight; y++)
                {
                    for (int z = 0; z < bufferDepth; z++)
                    {
                        Vector3 point = GetPointInBounds(x, y, z);
                        LightProbes.GetInterpolatedProbe(point, null, out SphericalHarmonicsL2 probe);

                        Vector3[] directions = GetSampleDirections();
                        Color[] colors = new Color[directions.Length];

                        probe.Evaluate(directions, colors);

                        Color average = new Color(0, 0, 0, 0);

                        for (int i = 0; i < colors.Length; i++)
                            average += colors[i];

                        average /= (float)colors.Length;
                        average *= density;
                        average *= filter;

                        texture.SetPixel(x, y, z, average);
                    }
                }
            }

            texture.Apply();
        }

        [ContextMenu("Bake Volume")]
        public void BakeVolume()
        {
            Debug.Log("Baking volumetrics!");

            Texture3D texture = new Texture3D(bufferWidth, bufferHeight, bufferDepth, TextureFormat.RGBAHalf, false);
            texture.name = "BakedVolumeResult";

            //if (passFlags.HasFlag(PassFlags.Direct))
            //    BakeDirect(ref texture);

            if (passFlags.HasFlag(PassFlags.Indirect))
                BakeIndirect(ref texture);

            buffer = texture;
        }

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.TransformPoint(bounds.center), transform.TransformVector(bounds.size));

            Gizmos.color = Color.red;
            foreach (Vector3 direction in GetSampleDirections())
                Gizmos.DrawRay(transform.position, direction);
        }
#endif
    }
}