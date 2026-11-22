using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Embedded by the Windows host and installed only in its disposable editor project.
public static class DeskPetPackageBuild {
    [Serializable] class Result {public string error;public string warning;}
    static string Arg(string key){var args=Environment.GetCommandLineArgs();return args[Array.IndexOf(args,key)+1];}
    public static void ConvertSelected(){
        var result=new Result();GameObject instance=null;
        try{
            string asset=Arg("-deskPetAsset");string output=Arg("-deskPetOutput");
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if(!model)throw new InvalidDataException("Unity 无法读取选中的模型："+asset);
            result.warning=ValidateDependencies(asset,model);
            if(result.warning.Length>0)Debug.LogWarning(result.warning);
            instance=UnityEngine.Object.Instantiate(model);instance.name=model.name;
            foreach(var t in instance.GetComponentsInChildren<Transform>(true)){
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)
                    throw new InvalidDataException(t.name+" 缺少脚本。请选择装好该角色插件的原 Unity 工程。");
            }
            var meshes=instance.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer||r is SkinnedMeshRenderer).ToArray();
            if(!meshes.Any(r=>r is SkinnedMeshRenderer skin?skin.sharedMesh!=null:r.GetComponent<MeshFilter>()?.sharedMesh!=null))
                throw new InvalidDataException("选中的对象没有可显示的网格。请选择角色根 Prefab 或完整模型。");
            foreach(var r in meshes)foreach(var mat in r.sharedMaterials)
                if(!mat||!mat.shader||mat.shader.name=="Hidden/InternalErrorShader")
                    throw new InvalidDataException("网格 "+r.name+" 缺少材质或着色器，请指定已装好依赖的 Unity 工程。");
#if VRC_SDK_VRCSDK3
            if(instance.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>())DeskPetVRChat.ExportTo(instance,output);
            else
#endif
                DeskPetExporter.ExportTo(instance,output);
        }catch(Exception e){result.error=e.GetBaseException().Message;}
        finally{
            if(instance)UnityEngine.Object.DestroyImmediate(instance);
            File.WriteAllText(Arg("-deskPetResult"),JsonUtility.ToJson(result));
        }
        if(!string.IsNullOrEmpty(result.error))EditorApplication.Exit(2);
    }
    static readonly Regex GuidRef=new Regex(@"\bguid:\s*([a-fA-F0-9]{32})\b");
    static readonly Regex Section=new Regex(@"^--- !u!\d+ &(-?\d+)\b");
    static readonly Regex Pointer=new Regex(@"\{\s*fileID:\s*(-?\d+)\s*(?:,\s*guid:\s*([a-fA-F0-9]{32})\b|(?=\}))");

    // EditorUtility reports runtime dependencies, but omits editor state machines and
    // blend trees. Follow their serialized local IDs even when a target script is missing.
    sealed class ControllerGraph {
        public readonly HashSet<long> Roots=new HashSet<long>();
        public readonly Dictionary<long,List<long>> Edges=new Dictionary<long,List<long>>();
        public readonly List<KeyValuePair<long,string>> Missing=new List<KeyValuePair<long,string>>();
        public bool Complete=true;
        public long Main;
        public HashSet<long> Reachable(){
            var visited=new HashSet<long>();
            var stack=new Stack<long>(Roots);
            while(stack.Count>0){
                long current=stack.Pop();
                if(!visited.Add(current)||!Edges.TryGetValue(current,out var edges))continue;
                foreach(long target in edges)stack.Push(target);
            }
            return visited;
        }
    }
    static string ValidateDependencies(string asset,GameObject model){
        string[] paths=AssetDatabase.GetDependencies(asset,true);
        var controllers=new Dictionary<string,ControllerGraph>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths.Where(p=>p.StartsWith("Assets/")&&p.EndsWith(".controller",StringComparison.OrdinalIgnoreCase))){
            var main=AssetDatabase.LoadMainAssetAtPath(path);
            if(!main||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main,out string guid,out long id))continue;
            var graph=new ControllerGraph{Main=id};
            graph.Roots.Add(id);controllers[guid]=graph;
        }
        if(controllers.Count>0){
            foreach(var obj in EditorUtility.CollectDependencies(new UnityEngine.Object[]{model})){
                if(obj&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long id)&&controllers.TryGetValue(guid,out var graph))graph.Roots.Add(id);
            }
        }
        var textures=new SortedSet<string>(StringComparer.Ordinal);
        var inactiveMenuFields=new SortedSet<string>(StringComparer.Ordinal);
        foreach(string path in paths)CheckReferences(path,controllers,textures,inactiveMenuFields);
        var orphanFiles=new List<string>();
        foreach(var pair in controllers){
            var graph=pair.Value;
            if(graph.Missing.Count==0)continue;
            var reachable=graph.Reachable();
            foreach(var missing in graph.Missing){
                if(!graph.Complete||!graph.Edges.ContainsKey(graph.Main)||!graph.Edges.ContainsKey(missing.Key)||reachable.Contains(missing.Key))
                    throw new InvalidDataException(missing.Value);
            }
            string name=Path.GetFileName(AssetDatabase.GUIDToAssetPath(pair.Key));
            int objects=graph.Missing.Select(item=>item.Key).Distinct().Count();
            orphanFiles.Add(name+"（"+objects+" 个未连接对象）");
            foreach(string detail in graph.Missing.Select(item=>item.Value).Distinct())Debug.Log("控制器未使用的引用："+detail);
        }
        var warnings=new List<string>();
        if(orphanFiles.Count>0)warnings.Add("已跳过控制器未连接的旧对象中的失效引用："+string.Join("、",orphanFiles.OrderBy(n=>n,StringComparer.Ordinal))+"。");
        if(inactiveMenuFields.Count>0)warnings.Add("以下菜单项保留了未使用的子菜单引用："+string.Join("、",inactiveMenuFields)+"。");
        if(textures.Count>0)warnings.Add("以下可选贴图使用着色器默认值："+string.Join("、",textures)+"。");
        return string.Join("\n",warnings);
    }
    static void CheckReferences(string path,Dictionary<string,ControllerGraph> controllers,SortedSet<string> missingTextures,SortedSet<string> inactiveMenuFields){
        // YAML assets preserve unresolved GUIDs even when GetDependencies omits their targets.
        if(!File.Exists(path))return;
        bool validate=path.StartsWith("Assets/",StringComparison.Ordinal);
        string ownGuid=AssetDatabase.AssetPathToGUID(path);
        controllers.TryGetValue(ownGuid,out var graph);
        using(var reader=File.OpenText(path)){
            // Avoid reading binary textures or meshes as a single line.
            if(reader.Peek()!='%' || !(reader.ReadLine()??"").StartsWith("%YAML"))return;
            long section=0;
#if VRC_SDK_VRCSDK3
            var menu=validate&&path.EndsWith(".asset",StringComparison.OrdinalIgnoreCase)
                ?AssetDatabase.LoadMainAssetAtPath(path) as VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu:null;
            int controlIndex=-1;
            bool inControls=false;
#endif
            string line,textureSlot=null;while((line=reader.ReadLine())!=null){
#if VRC_SDK_VRCSDK3
                if(menu){
                    if(line.StartsWith("  controls:",StringComparison.Ordinal)){inControls=true;controlIndex=-1;}
                    else if(inControls && line.StartsWith("  - ",StringComparison.Ordinal))controlIndex++;
                    else if(inControls && line.Length>0 && !line.StartsWith("    ",StringComparison.Ordinal))inControls=false;
                }
#endif
                if(graph!=null && line.StartsWith("---",StringComparison.Ordinal)){
                    var header=Section.Match(line);
                    if(!header.Success||!long.TryParse(header.Groups[1].Value,out section)||graph.Edges.ContainsKey(section))graph.Complete=false;
                    else graph.Edges.Add(section,new List<long>());
                }
                if(controllers.Count>0 && line.Contains("fileID:")){
                    var pointers=Pointer.Matches(line);
                    if(graph!=null && pointers.Count==0)graph.Complete=false;
                    foreach(Match pointer in pointers){
                        if(!long.TryParse(pointer.Groups[1].Value,out long target)||target==0)continue;
                        string targetGuid=pointer.Groups[2].Value;
                        if(targetGuid.Length==0||string.Equals(ownGuid,targetGuid,StringComparison.OrdinalIgnoreCase)){
                            if(graph!=null&&graph.Edges.TryGetValue(section,out var edges))edges.Add(target);
                        }else if(controllers.TryGetValue(targetGuid,out var external))external.Roots.Add(target);
                    }
                }
                if(!validate)continue;
                var trimmed=line.Trim();
                if(trimmed.StartsWith("- ") && trimmed.EndsWith(":"))textureSlot=trimmed.Substring(2,trimmed.Length-3);
                foreach(Match match in GuidRef.Matches(line)){
                string guid=match.Groups[1].Value;
                if(guid.StartsWith("0000000000000000",StringComparison.Ordinal)||!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))continue;
#if VRC_SDK_VRCSDK3
                if(menu&&inControls&&controlIndex>=0&&controlIndex<menu.controls.Count&&line.StartsWith("    subMenu:",StringComparison.Ordinal)){
                    var control=menu.controls[controlIndex];
                    if(control!=null&&Enum.IsDefined(typeof(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType),control.type)
                        &&control.type!=VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu){
                        inactiveMenuFields.Add(Path.GetFileName(path)+" / "+control.name);continue;
                    }
                }
#endif
                if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase) && trimmed.StartsWith("m_Texture:") &&
                    textureSlot!="_MainTex" && textureSlot!="_BaseMap" && textureSlot!="_BaseColorMap"){
                    missingTextures.Add(Path.GetFileName(path)+" "+textureSlot);continue;
                }
                string detail=Path.GetFileName(path)+" 缺少 "+(line.Contains("m_Shader:")?"着色器":line.Contains("m_Script:")?"脚本":line.Contains("m_Motion:")?"动画":"引用资源")+"（GUID "+guid+"）。请指定装好原依赖的 Unity 工程。";
                if(graph==null)throw new InvalidDataException(detail);
                graph.Missing.Add(new KeyValuePair<long,string>(section,detail));
                }
            }
        }
    }
}
