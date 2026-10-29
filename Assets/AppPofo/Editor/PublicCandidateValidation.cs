using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Player;
using UnityEngine;

public static class PublicCandidateValidation
{
    [Serializable] private class Finding { public string path; public int missingScripts; }
    [Serializable] private class CompileResult { public string target; public string status; public int assemblies; public string detail; }
    [Serializable] private class Report
    {
        public int scenes;
        public int prefabs;
        public List<Finding> findings = new List<Finding>();
        public List<CompileResult> playerScripts = new List<CompileResult>();
    }
    private static void Inspect(GameObject gameObject, string path, Report report)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        if (count > 0) report.findings.Add(new Finding { path = path + "/" + gameObject.name, missingScripts = count });
        foreach (Transform child in gameObject.transform) Inspect(child.gameObject, path, report);
    }
    public static void Run()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "PortfolioDemo" ||
            !File.Exists(Path.Combine(Application.dataPath, "../Audit/FILE_CLASSIFICATION.csv")))
            throw new InvalidOperationException("Only the separate public candidate may be validated.");
        var report = new Report();
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
            if (path.EndsWith(".unity", StringComparison.Ordinal))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var gameObject in scene.GetRootGameObjects()) Inspect(gameObject, path, report);
                report.scenes++;
            }
            else if (path.EndsWith(".prefab", StringComparison.Ordinal))
            {
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                try { Inspect(prefab, path, report); report.prefabs++; }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
        }
        foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.Android })
        {
            var result = new CompileResult { target = target.ToString(), status = "NOT_RUN" };
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                { result.status = "UNAVAILABLE"; result.detail = "Build support module is not installed."; }
                else
                {
                    string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/PublicCompileCheck/" + target));
                    Directory.CreateDirectory(folder);
                    var settings = new ScriptCompilationSettings { target = target, group = BuildPipeline.GetBuildTargetGroup(target), options = ScriptCompilationOptions.None };
                    var compiled = PlayerBuildInterface.CompilePlayerScripts(settings, folder);
                    result.assemblies = compiled.assemblies == null ? 0 : compiled.assemblies.Count;
                    result.status = result.assemblies > 0 ? "PASS" : "FAIL";
                    result.detail = "C# player scripts only; no native loading, APK, IL2CPP or device validation.";
                }
            }
            catch (Exception error) { result.status = "FAIL"; result.detail = error.GetType().Name; }
            report.playerScripts.Add(result);
        }
        string output = Environment.GetEnvironmentVariable("APPPOFO_PUBLIC_VALIDATION_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Explicit validation output path required.");
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.findings.Count == 0 ? 0 : 1);
    }
}
