using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using K13A.TSMP.Udon;

namespace K13A.TSMP.Editor
{
    [CustomEditor(typeof(TSMPCharacterCrcGroup))]
    public sealed class TSMPCharacterCrcGroupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (InspectorUI.DrawUdonSharpHeader(targets)) return;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pose"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fingerProtection"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("additionalTransforms"), true);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Body protects the body/root pose and all additional Transforms as one snapshot. Fingers can be excluded or checked separately. Expressions are excluded. Register this component in the Encoder's Character CRC Groups list.", MessageType.Info);
            var group = (TSMPCharacterCrcGroup)target;
            if (group.pose == null) EditorGUILayout.HelpBox("Assign Humanoid Pose Sync.", MessageType.Error);
            var seen = new HashSet<TSMPNetworkTransformSync>();
            if (group.additionalTransforms != null)
                foreach (var transform in group.additionalTransforms)
                    if (transform == null || !seen.Add(transform)) { EditorGUILayout.HelpBox("Additional Transforms must not contain missing or duplicate references.", MessageType.Error); break; }
        }
    }
}
