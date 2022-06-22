using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    internal class UniversalRenderPipelineSerializedLight : ISerializedLight
    {
        /// <summary>The base settings of the light</summary>
        public LightEditor.Settings settings { get; }
        /// <summary>The light serialized</summary>
        public SerializedObject serializedObject { get; }
        /// <summary>The additional light data serialized</summary>
        public SerializedObject serializedAdditionalDataObject { get; private set; }

        public UniversalAdditionalLightData additionalLightData => lightsAdditionalData[0];
        public UniversalAdditionalLightData[] lightsAdditionalData { get; private set; }

        // Common SRP's Lights properties
        public SerializedProperty intensity { get; }

        // URP Light Properties
        public SerializedProperty useAdditionalDataProp { get; }                     // Does light use shadow bias settings defined in UniversalRP asset file?
        public SerializedProperty additionalLightsShadowResolutionTierProp { get; }  // Index of the AdditionalLights ShadowResolution Tier
        public SerializedProperty lightCookieSizeProp { get; }                       // Multi dimensional light cookie size replacing `cookieSize` in legacy light.
        public SerializedProperty lightCookieOffsetProp { get; }                     // Multi dimensional light cookie offset.

        // Light layers related
        public SerializedProperty lightLayerMask { get; }
        public SerializedProperty customShadowLayers { get; }
        public SerializedProperty shadowLayerMask { get; }

        // zCubed Additions
        public SerializedProperty volumetricsEnabled { get; }
        public SerializedProperty volumetricsSyncIntensity { get; }
        public SerializedProperty volumetricsIntensity { get; }
        public SerializedProperty volumetricsPower { get; }

        public SerializedProperty specularRadius { get; }
        // ================


        /// <summary>Method that updates the <see cref="SerializedObject"/> of the Light and the Additional Light Data</summary>
        public void Update()
        {
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
            settings.Update();
        }

        /// <summary>Method that applies the modified properties the <see cref="SerializedObject"/> of the Light and the Light Camera Data</summary>
        public void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
            settings.ApplyModifiedProperties();
        }

        /// <summary>Constructor</summary>
        /// <param name="serializedObject"><see cref="SerializedObject"/> with the light</param>
        /// <param name="settings"><see cref="LightEditor.Settings"/>with the settings</param>
        public UniversalRenderPipelineSerializedLight(SerializedObject serializedObject, LightEditor.Settings settings)
        {
            this.settings = settings;
            settings.OnEnable();

            this.serializedObject = serializedObject;

            lightsAdditionalData = CoreEditorUtils
                .GetAdditionalData<UniversalAdditionalLightData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(lightsAdditionalData);

            intensity = serializedObject.FindProperty("m_Intensity");

            useAdditionalDataProp = serializedAdditionalDataObject.FindProperty("m_UsePipelineSettings");
            additionalLightsShadowResolutionTierProp = serializedAdditionalDataObject.FindProperty("m_AdditionalLightsShadowResolutionTier");
            lightCookieSizeProp = serializedAdditionalDataObject.FindProperty("m_LightCookieSize");
            lightCookieOffsetProp = serializedAdditionalDataObject.FindProperty("m_LightCookieOffset");

            lightLayerMask = serializedAdditionalDataObject.FindProperty("m_LightLayerMask");
            customShadowLayers = serializedAdditionalDataObject.FindProperty("m_CustomShadowLayers");
            shadowLayerMask = serializedAdditionalDataObject.FindProperty("m_ShadowLayerMask");

            volumetricsEnabled = serializedAdditionalDataObject.FindProperty("m_VolumetricsEnabled");
            volumetricsSyncIntensity = serializedAdditionalDataObject.FindProperty("m_VolumetricsSyncIntensity");
            volumetricsIntensity = serializedAdditionalDataObject.FindProperty("m_VolumetricsIntensity");
            volumetricsPower = serializedAdditionalDataObject.FindProperty("m_VolumetricsPower");

            specularRadius = serializedAdditionalDataObject.FindProperty("m_SpecularRadius");

            settings.ApplyModifiedProperties();
        }
    }
}
