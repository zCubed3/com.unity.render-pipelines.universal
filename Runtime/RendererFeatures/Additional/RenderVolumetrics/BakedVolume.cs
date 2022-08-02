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

            Direct = 1,
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

        public Material shadowCasterMaterial;
        public RenderTexture tempVis;
        public ComputeShader bakeCS;

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

        void BakeDirect(ref Texture3D texture)
        {
            List<Light> bakedLights = new List<Light>();
            List<Renderer> staticRenderers = new List<Renderer>();

            foreach (Light light in Object.FindObjectsOfType<Light>())
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy)
                    continue;

                var additionalData = light.GetUniversalAdditionalLightData();

                if (additionalData.volumetricsLightMode == RenderVolumetrics.VolumeLightMode.Baked)
                    bakedLights.Add(light);
            }

            /*
            foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.gameObject.isStatic || renderer.staticShadowCaster)
                    staticRenderers.Add(renderer);
            }
            */

            // For each light we need to render the shadows, then process the light inside the volume w/ shadowing
            //RenderTexture shadowBuffer = new RenderTexture(1024, 1024, 32, RenderTextureFormat.RFloat);
            //shadowBuffer.Create();

            RenderTexture volumeBuffer = new RenderTexture(bufferWidth, bufferHeight, 0, RenderTextureFormat.ARGBHalf);
            volumeBuffer.enableRandomWrite = true;
            volumeBuffer.Create();

            //CommandBuffer buffer = CommandBufferPool.Get("BAKED VOLUME SHADOWS");

            foreach (Light light in bakedLights)
            {
                //buffer.Clear();

                //buffer.SetViewport(new Rect(0, 0, shadowBuffer.width, shadowBuffer.height));
                //buffer.SetRenderTarget(shadowBuffer);

                Vector3 origin = light.transform.position;
                Vector3 forward = light.transform.forward;
                Vector3 up = light.transform.up;

                Matrix4x4 look = Matrix4x4.LookAt(origin, origin + forward, -up);
                Matrix4x4 view = Matrix4x4.LookAt(origin, origin - forward, up).inverse;
                Matrix4x4 proj = Matrix4x4.Perspective(light.spotAngle, 1, 0.01F, 100F);

                /*
                buffer.SetViewProjectionMatrices(view, proj);

                foreach (Renderer renderer in staticRenderers)
                    buffer.DrawRenderer(renderer, shadowCasterMaterial);

                Graphics.ExecuteCommandBuffer(buffer);
                */

                UniversalRenderPipeline.GetLightAttenuationAndSpotDirection(light.type, light.range, look, light.spotAngle, light.innerSpotAngle, out Vector4 atten, out Vector4 dir);

                int kernel = bakeCS.FindKernel("CSMain");
                bakeCS.GetKernelThreadGroupSizes(kernel, out uint xTileSize, out uint yTileSize, out uint zTileSize);

                int tilesX = Mathf.CeilToInt(bufferWidth / (float)xTileSize);
                int tilesY = Mathf.CeilToInt(bufferDepth / (float)yTileSize);
                bakeCS.SetTexture(kernel, "_Result", volumeBuffer);

                bakeCS.SetMatrix("_BakeMatrix", Matrix4x4.TRS(transform.position + bounds.center, transform.rotation, bounds.extents));

                bakeCS.SetVector("_LightAtten", atten);
                bakeCS.SetVector("_LightDir", dir);
                bakeCS.SetVector("_LightPos", new Vector4(origin.x, origin.y, origin.z, 1.0F));

                for (int s = 0; s < bufferDepth; s++) 
                {
                    bakeCS.SetVector("_SliceData", new Vector4(s, bufferHeight));
                    bakeCS.Dispatch(kernel, tilesX, tilesY, 1);

                    RenderTexture.active = volumeBuffer;
                    Texture2D dupe = new Texture2D(volumeBuffer.width, volumeBuffer.height, TextureFormat.RGBAHalf, false);
                    dupe.ReadPixels(new Rect(0, 0, dupe.width, dupe.height), 0, 0);

                    for (int x = 0; x < bufferWidth; x++)
                        for (int y = 0; y < bufferDepth; y++)
                        {
                            Color color = dupe.GetPixel(x, y) * light.color * light.intensity + texture.GetPixel(x, y, s);
                            texture.SetPixel(x, y, s, color);
                        }

                    texture.Apply();
                }
            }

            RenderTexture.active = null;

            //shadowBuffer.Release();
            volumeBuffer.Release();
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

        Color SaturateColor(Color color) => new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), Mathf.Clamp01(color.a));

        [ContextMenu("Bake Volume")]
        public void BakeVolume()
        {
            Debug.Log("Baking volumetrics!");

            Texture3D texture = new Texture3D(bufferWidth, bufferHeight, bufferDepth, TextureFormat.RGBAHalf, false);
            texture.name = "BakedVolumeResult";

            for (int x = 0; x < bufferWidth; x++)
                for (int y = 0; y < bufferHeight; y++)
                    for (int z = 0; z < bufferDepth; z++)
                        texture.SetPixel(x, y, z, new Color(0, 0, 0, 1));

            texture.Apply();

            if (passFlags.HasFlag(PassFlags.Indirect))
                BakeIndirect(ref texture);

            if (passFlags.HasFlag(PassFlags.Direct))
                BakeDirect(ref texture);

            /*
            for (int x = 0; x < bufferWidth; x++)
                for (int y = 0; y < bufferHeight; y++)
                    for (int z = 0; z < bufferDepth; z++)
                        texture.SetPixel(x, y, z, SaturateColor(texture.GetPixel(x, y, z)));

            texture.Apply();
            */

            buffer = texture;
        }

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            Gizmos.color = Color.red;
            foreach (Vector3 direction in GetSampleDirections())
                Gizmos.DrawRay(bounds.center, direction);
        }
#endif
    }
}