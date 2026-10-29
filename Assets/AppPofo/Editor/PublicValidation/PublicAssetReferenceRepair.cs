using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PublicAssetReferenceRepair
{
    [Serializable] private class Entry { public string property; public string replacement; public string status; }
    [Serializable] private class Report { public List<Entry> changes = new List<Entry>(); public string missingLegacyField; }
    public static void Run()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "PortfolioDemo") throw new InvalidOperationException("Public candidate only.");
        const string rendererPath = "Assets/Settings/Renderer2D.asset";
        var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rendererPath);
        if (renderer == null) throw new InvalidOperationException("Renderer2D asset missing.");
        var so = new SerializedObject(renderer);
        var report = new Report();
        var names = new[] { "probeVolumeDebugShader", "probeVolumeFragmentationDebugShader", "probeVolumeOffsetDebugShader", "probeVolumeSamplingDebugShader", "probeSamplingDebugMesh", "probeSamplingDebugTexture" };
        var resources = new[] { "ProbeVolumeDebug.shader", "ProbeVolumeFragmentationDebug.shader", "ProbeVolumeOffsetDebug.shader", "ProbeVolumeSamplingDebug.shader", "ProbeSamplingDebugMesh.fbx", "ProbeVolumeNumbersDisplayTex.png" };
        for (int i = 0; i < names.Length; i++)
        {
            string key = "probeVolumeResources." + names[i];
            var property = so.FindProperty(key);
            var entry = new Entry { property = key, replacement = "Packages/com.unity.render-pipelines.core/Runtime/Debug/" + resources[i] };
            report.changes.Add(entry);
            if (property == null) { entry.status = "NOT_SERIALIZED_BY_CURRENT_UNITY"; continue; }
            UnityEngine.Object replacement = i == 4 ? (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<Mesh>(entry.replacement) : AssetDatabase.LoadMainAssetAtPath(entry.replacement);
            if (replacement == null) throw new InvalidOperationException("Current package resource missing: " + entry.replacement);
            property.objectReferenceValue = replacement;
            entry.status = "REBOUND_TO_CURRENT_OFFICIAL_CORE_RESOURCE";
        }
        report.missingLegacyField = so.FindProperty("m_FallOffLookup") == null ? "m_FallOffLookup is not a serialized field of current Renderer2DData; ForceReserialize removes obsolete YAML while preserving supported settings." : "Legacy field still serialized; requires review.";
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);
        AssetDatabase.ForceReserializeAssets(new[] { rendererPath });
        File.WriteAllText(Environment.GetEnvironmentVariable("APPPOFO_PUBLIC_REFERENCE_REPAIR_OUTPUT"), JsonUtility.ToJson(report, true));
        EditorApplication.Exit(0);
    }
}
