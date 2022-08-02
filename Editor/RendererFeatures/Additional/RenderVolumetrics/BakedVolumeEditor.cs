using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using System;

namespace UnityEngine.Rendering.Universal.Additions
{
    [CustomEditor(typeof(BakedVolume))]
    public class BakedVolumeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUILayout.Label("3D Textures get large very fast! Use smaller numbers like (16 / 32 / 64)!", EditorStyles.boldLabel);

            DrawDefaultInspector();

            BakedVolume targetVolume = target as BakedVolume;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake"))
                    targetVolume.BakeVolume();

                if (GUILayout.Button("Bake All"))
                    foreach (BakedVolume volume in FindObjectsOfType<BakedVolume>())
                        volume.BakeVolume();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}