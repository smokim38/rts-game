using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class UnityOrganizationSettingsBuilder
    {
        public static void Build()
        {
            PlayerSettings settings = Resources.FindObjectsOfTypeAll<PlayerSettings>().Single();
            using var serialized = new SerializedObject(settings);
            SerializedProperty organization = serialized.FindProperty("organizationId");
            if (organization == null || organization.propertyType != SerializedPropertyType.String)
                throw new InvalidOperationException("Player settings have no organizationId string field.");

            organization.stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
