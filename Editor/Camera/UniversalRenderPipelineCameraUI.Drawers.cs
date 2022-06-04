using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<UniversalRenderPipelineSerializedCamera>;

    static partial class UniversalRenderPipelineCameraUI
    {
        [URPHelpURL("camera-component-reference")]
        public enum Expandable
        {
            /// <summary> Projection</summary>
            Projection = 1 << 0,
            /// <summary> Physical</summary>
            Physical = 1 << 1,
            /// <summary> Output</summary>
            Output = 1 << 2,
            /// <summary> Orthographic</summary>
            Orthographic = 1 << 3,
            /// <summary> RenderLoop</summary>
            RenderLoop = 1 << 4,
            /// <summary> Rendering</summary>
            Rendering = 1 << 5,
            /// <summary> Environment</summary>
            Environment = 1 << 6,
            /// <summary> Stack</summary>
            Stack = 1 << 7,

            // zCubed Additions
            Volumetrics = 1 << 8
            // ================
        }

        static readonly ExpandedState<Expandable, Camera> k_ExpandedState = new(Expandable.Projection, "URP");

        public static readonly CED.IDrawer SectionProjectionSettings = CED.FoldoutGroup(
            CameraUI.Styles.projectionSettingsHeaderContent,
            Expandable.Projection,
            k_ExpandedState,
            FoldoutOption.Indent,
            CED.Group(
                DrawerProjection
                ),
            PhysicalCamera.Drawer
        );

        public static readonly CED.IDrawer SectionStackSettings =
            CED.Conditional(
                (serialized, editor) => (CameraRenderType)serialized.cameraType.intValue == CameraRenderType.Base,
                CED.FoldoutGroup(Styles.stackSettingsText, Expandable.Stack, k_ExpandedState, FoldoutOption.Indent, CED.Group(DrawerStackCameras)));

        public static readonly CED.IDrawer[] Inspector =
        {
            CED.Group(
                DrawerCameraType
                ),
            SectionProjectionSettings,
            Rendering.Drawer,
            SectionStackSettings,
            Environment.Drawer,
            Output.Drawer,
            CED.FoldoutGroup(Styles.volumetricsHeader,
                Expandable.Volumetrics,
                k_ExpandedState,
            DrawVolumetricsContent)
        };

        static void DrawerProjection(UniversalRenderPipelineSerializedCamera p, Editor owner)
        {
            var camera = p.serializedObject.targetObject as Camera;
            bool pixelPerfectEnabled = camera.TryGetComponent<UnityEngine.Experimental.Rendering.Universal.PixelPerfectCamera>(out var pixelPerfectCamera) && pixelPerfectCamera.enabled;
            if (pixelPerfectEnabled)
                EditorGUILayout.HelpBox(Styles.pixelPerfectInfo, MessageType.Info);

            using (new EditorGUI.DisabledGroupScope(pixelPerfectEnabled))
                CameraUI.Drawer_Projection(p, owner);
        }

        static void DrawerCameraType(UniversalRenderPipelineSerializedCamera p, Editor owner)
        {
            int selectedRenderer = p.renderer.intValue;
            ScriptableRenderer scriptableRenderer = UniversalRenderPipeline.asset.GetRenderer(selectedRenderer);
            bool isDeferred = scriptableRenderer is UniversalRenderer { renderingMode: RenderingMode.Deferred };

            EditorGUI.BeginChangeCheck();

            CameraRenderType originalCamType = (CameraRenderType)p.cameraType.intValue;
            CameraRenderType camType = (originalCamType != CameraRenderType.Base && isDeferred) ? CameraRenderType.Base : originalCamType;

            camType = (CameraRenderType)EditorGUILayout.EnumPopup(
                Styles.cameraType,
                camType,
                e => !isDeferred || (CameraRenderType)e != CameraRenderType.Overlay,
                false
            );

            if (EditorGUI.EndChangeCheck() || camType != originalCamType)
            {
                p.cameraType.intValue = (int)camType;
            }

            EditorGUILayout.Space();
        }

        static void DrawerStackCameras(UniversalRenderPipelineSerializedCamera p, Editor owner)
        {
            if (owner is UniversalRenderPipelineCameraEditor cameraEditor)
            {
                cameraEditor.DrawStackSettings();
            }
        }

        // zCubed Additions
        static void DrawVolumetricsContent(UniversalRenderPipelineSerializedCamera p, Editor owner)
        {
            p.camerasAdditionalData[0].renderVolumetrics = EditorGUILayout.Toggle("Enabled", p.camerasAdditionalData[0].renderVolumetrics);
            p.camerasAdditionalData[0].volumetricsDownsampling = EditorGUILayout.IntField("Downsampling", p.camerasAdditionalData[0].volumetricsDownsampling);
            p.camerasAdditionalData[0].volumetricsSteps = EditorGUILayout.IntField("Steps", p.camerasAdditionalData[0].volumetricsSteps);
            p.camerasAdditionalData[0].volumetricsFar = EditorGUILayout.FloatField("Far", p.camerasAdditionalData[0].volumetricsFar);
            p.camerasAdditionalData[0].volumetricsDensity = EditorGUILayout.FloatField("Density", p.camerasAdditionalData[0].volumetricsDensity);
            p.camerasAdditionalData[0].volumetricsScattering = EditorGUILayout.Slider("Scattering", p.camerasAdditionalData[0].volumetricsScattering, 0, 1);
        }
    }
}
