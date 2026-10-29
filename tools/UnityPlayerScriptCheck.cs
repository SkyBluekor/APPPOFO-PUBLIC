using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;
public static class S510CompileChecks {
 public static void Run() {
  if(!Application.isBatchMode || PlayerSettings.companyName!="AppPofoSyntheticTests" || PlayerSettings.productName!="S510IsolatedTests") throw new InvalidOperationException("Isolated synthetic project required.");
  try {
   foreach(var target in new[]{BuildTarget.StandaloneWindows64,BuildTarget.Android}) {
    var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../.utmp/player-compile/"+target));Directory.CreateDirectory(folder);
    var settings=new ScriptCompilationSettings{target=target,group=BuildPipeline.GetBuildTargetGroup(target),options=ScriptCompilationOptions.None};
    var result=PlayerBuildInterface.CompilePlayerScripts(settings,folder);
    if(result.assemblies==null || result.assemblies.Count==0)throw new Exception("No player assemblies.");
    File.WriteAllText(folder+".result.txt","PASS "+target+" assemblies="+result.assemblies.Count);
    Debug.Log("S510_PLAYER_SCRIPTS_PASS "+target+" assemblies="+result.assemblies.Count);
   }
   EditorApplication.Exit(0);
  }catch(Exception error){Debug.LogError("S510 player compilation failed: "+error.GetType().Name);EditorApplication.Exit(1);}
 }
}
