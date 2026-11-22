using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PetBuild
{
    [MenuItem("DeskPet/Build Windows renderer")]
    public static void Windows()
    {
        const string scenePath="Assets/DeskPet/Desktop.unity";
        if(!File.Exists(scenePath)){
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Desktop pet renderer").AddComponent<PetRenderer>();
            EditorSceneManager.SaveScene(scene,scenePath);
        }
        PlayerSettings.companyName="DeskPet"; PlayerSettings.productName="DeskPet Renderer";
        PlayerSettings.runInBackground=true; PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth=360; PlayerSettings.defaultScreenHeight=440;
        PlayerSettings.colorSpace=ColorSpace.Gamma;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
        var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders=graphics.FindProperty("m_AlwaysIncludedShaders");
        foreach(var name in new[]{"Standard","UniGLTF/UniUnlit","VRM10/MToon10","Unlit/Texture","Unlit/Color"}) {
            var shader=Shader.Find(name); if(!shader)continue;
            if(Enumerable.Range(0,shaders.arraySize).Any(i=>shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader))continue;
            int i=shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue=shader;
        }
        graphics.ApplyModifiedProperties(); AssetDatabase.SaveAssets();
        var args=Environment.GetCommandLineArgs();int output=Array.IndexOf(args,"-petBuildPath");
        var target=Path.GetFullPath(output>=0 && output+1<args.Length?args[output+1]:"../Windows/Renderer/Portrait.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        var result=BuildPipeline.BuildPlayer(new[]{scenePath},target,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Windows player build failed");
    }
}
