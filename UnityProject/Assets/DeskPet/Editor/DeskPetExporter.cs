using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Copy this single editor script into an avatar project's Assets/Editor directory.
public static class DeskPetExporter
{
    [MenuItem("Assets/桌宠/导出选中的角色")]
    public static void Export()
    {
        var selected=Selection.activeObject as GameObject;
        if(!selected) { EditorUtility.DisplayDialog("桌宠","请先选择角色 Prefab 或场景中的角色根对象。","好"); return; }
        string target=EditorUtility.SaveFilePanel("导出桌宠角色","",selected.name,"petmodel");
        if(target.Length>0) ExportTo(selected,target);
    }
    public static void ExportTo(GameObject selected,string target,Func<GameObject,string,string> prepareControls=null)
    {
        string scratch="Assets/DesktopPetExport_"+Guid.NewGuid().ToString("N");
        string output=Path.Combine(Path.GetTempPath(),"DeskPetBundle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch); Directory.CreateDirectory(output);
        GameObject copy=null;
        try {
            copy=UnityEngine.Object.Instantiate(selected); copy.name="Character";
            copy.SetActive(false);
            foreach(var t in copy.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach(var component in copy.GetComponentsInChildren<Component>(true)) {
                if(!component || component is Transform || component is Renderer || component is MeshFilter ||
                   component is Animator || component is Animation || component is LODGroup) continue;
                UnityEngine.Object.DestroyImmediate(component);
            }
            copy.SetActive(true);AssetDatabase.Refresh();
            string metadata=prepareControls?.Invoke(copy,scratch);
            string path=scratch+"/Character.prefab";
            PrefabUtility.SaveAsPrefabAsset(copy,path);
            AssetDatabase.SaveAssets();
            string bundleName=Guid.NewGuid().ToString("N");
            var assets=string.IsNullOrEmpty(metadata)?new[]{path}:new[]{path,metadata};
            var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName=bundleName,assetNames=assets}},
                BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
            if(!manifest) throw new IOException("角色导出失败，请检查 Console 中的材质和依赖错误。");
            File.Copy(Path.Combine(output,bundleName),target,true);
            Debug.Log("桌宠角色已导出 "+target);
        } finally {
            if(copy) UnityEngine.Object.DestroyImmediate(copy);
            AssetDatabase.DeleteAsset(scratch);
            Directory.Delete(output,true);
        }
    }
}
