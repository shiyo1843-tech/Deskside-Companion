#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

// A single exporter for avatars built with the VRChat Avatars 3 SDK.
// No lookup by avatar name, mesh name, language or clothing brand.
public static class DeskPetVRChat
{
    [Serializable] class Entry {public string id,label,parameter;public float value; public bool initial;}
    [Serializable] class Node {public string id,parent,label,kind,parameter,icon;public string[] axes,axisLabels;public float value,initial;public bool available;}
    [Serializable] class DriverOperation {public string type,name,source;public float value,valueMin,valueMax,chance,sourceMin,sourceMax,destMin,destMax;public bool convertRange;}
    [Serializable] class Driver {public int layer,hash;public List<DriverOperation> operations=new List<DriverOperation>();}
    [Serializable] class Hair {public string root,parameter;public string[] ignore;public Vector3 endpoint;public float radius,pull,spring;public bool grab,collision;}
    [Serializable] class Contact {public string root,owner,parameter,mode;public Vector3 position;public Quaternion rotation;public float radius,height;}
    [Serializable] class Menu {public bool vrchat=true,curated;public List<Entry> actions=new List<Entry>(),faces=new List<Entry>(),clothes=new List<Entry>();public List<Node> nodes=new List<Node>();public List<Driver> drivers=new List<Driver>();public List<Hair> hair,parts;public List<Contact> contacts;public string eyeMesh,blinkShape;}
    [MenuItem("Assets/桌宠/导出 VRChat 角色与菜单")]
    static void Export(){
        var go=Selection.activeGameObject;if(!go)return;
        string path=EditorUtility.SaveFilePanel("导出角色和菜单","",go.name,"petmodel");
        if(path.Length>0)ExportTo(go,path);
    }
    public static void ExportTo(GameObject source,string path){
        var avatar=source.GetComponent<VRCAvatarDescriptor>();if(!avatar)throw new InvalidOperationException("请选择带有 Avatar Descriptor 的角色根对象。");
        GameObject baked=null;
        try{
            // NDMF is optional. When available, include its outfit and menu build output.
            var processor=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("nadena.dev.ndmf.AvatarProcessor")).FirstOrDefault(t=>t!=null);
            var bake=processor?.GetMethods().FirstOrDefault(m=>m.Name=="ManualProcessAvatar" && m.GetParameters().Length==2);
            if(bake!=null){baked=(GameObject)bake.Invoke(null,new object[]{source,null});avatar=baked.GetComponent<VRCAvatarDescriptor>();}
            var descriptor=avatar;
            DeskPetExporter.ExportTo(baked?baked:source,path,(copy,dir)=>Build(copy,dir,descriptor));
        }finally{if(baked)UnityEngine.Object.DestroyImmediate(baked);}
    }
    public static string Build(GameObject root,string dir,VRCAvatarDescriptor descriptor,string personal=null){
        var manifest=string.IsNullOrEmpty(personal)?new Menu():JsonUtility.FromJson<Menu>(File.ReadAllText(personal));
        manifest.nodes=new List<Node>();manifest.drivers=new List<Driver>();manifest.vrchat=true;manifest.curated=!string.IsNullOrEmpty(personal);
        var merged=AnimatorController.CreateAnimatorControllerAtPath(dir+"/AvatarMenu.controller");
        var parameters=new Dictionary<string,AnimatorControllerParameter>();var layers=new List<AnimatorControllerLayer>();
        var sources=descriptor.baseAnimationLayers.OrderBy(l=>(int)l.type);
        foreach(var playable in sources){
            if(!(playable.animatorController is AnimatorController src))continue;
            string prefix=playable.type.ToString();
            foreach(var p in src.parameters)if(!parameters.ContainsKey(p.name))parameters.Add(p.name,p);
            var originalLayers=src.layers;
            for(int index=0;index<originalLayers.Length;index++){
                var layer=originalLayers[index];
                // Synced layers reference layer indices local to their source controller.
                int offset=layers.Count-index;
                layer.name=prefix+" / "+layer.name;
                if(layer.syncedLayerIndex>=0)layer.syncedLayerIndex+=offset;
                if(index==0)layer.defaultWeight=1;
                // Unity hashes state paths from the serialized machine, including an empty root name.
                ReadDrivers(layer.stateMachine,layer.stateMachine.name,layers.Count,manifest);
                layers.Add(layer);
            }
        }
        var rig=root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.isHuman)??root.GetComponent<Animator>();
        if(manifest.curated && rig && rig.runtimeAnimatorController is AnimatorController authored){
            foreach(var layer in authored.layers){if(layer.name=="Base Layer"){layer.name="DeskPet Body";layer.defaultWeight=1;}if(layer.name=="Face")layer.defaultWeight=0;layers.Add(layer);}
        }
        if(layers.Count>0 && rig){
            var defaults=descriptor.expressionParameters;
            foreach(var p in parameters.Values){
                var configured=defaults?.parameters?.FirstOrDefault(x=>x.name==p.name);
                bool forceTrue=p.name=="IsLocal" || p.name=="Grounded";
                if(configured==null && !forceTrue)continue;
                float v=forceTrue?1:configured.defaultValue;
                if(p.type==AnimatorControllerParameterType.Bool)p.defaultBool=v>.5f;
                else if(p.type==AnimatorControllerParameterType.Float)p.defaultFloat=v;
                else if(p.type==AnimatorControllerParameterType.Int)p.defaultInt=Mathf.RoundToInt(v);
            }
            merged.parameters=parameters.Values.ToArray();merged.layers=layers.ToArray();rig.runtimeAnimatorController=merged;
        }
        if(descriptor.expressionsMenu)Collect(descriptor.expressionsMenu,"","",new HashSet<VRCExpressionsMenu>(),parameters,descriptor,manifest);
        manifest.hair=new List<Hair>();manifest.parts=new List<Hair>();manifest.contacts=new List<Contact>();
        foreach(var p in descriptor.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>(true)){
            if(!p.enabled)continue;
            var t=p.rootTransform?p.rootTransform:p.transform;
            string path=AnimationUtility.CalculateTransformPath(t,descriptor.transform);
            bool hair=System.Text.RegularExpressions.Regex.IsMatch(path,@"hair|ahoge|bang|twin.?tail|pony.?tail|髪|前髮|发梢|头发|머리",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var settings=new SerializedObject(p);
            bool Allows(string flag,string filter)=>flag=="True" || (flag!="False" && (settings.FindProperty(filter)?.FindPropertyRelative("allowOthers")?.boolValue??true));
            (hair?manifest.hair:manifest.parts).Add(new Hair{root=path,parameter=p.parameter,endpoint=p.endpointPosition,radius=p.radius,pull=p.pull,spring=p.spring,
                grab=Allows(p.allowGrabbing.ToString(),"grabFilter"),collision=Allows(p.allowCollision.ToString(),"collisionFilter"),
                ignore=p.ignoreTransforms.Where(x=>x).Select(x=>AnimationUtility.CalculateTransformPath(x,descriptor.transform)).ToArray()});
        }
        foreach(var c in descriptor.GetComponentsInChildren<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>(true)){
            if(!c.enabled||!c.allowOthers||string.IsNullOrEmpty(c.parameter)||!parameters.ContainsKey(c.parameter))continue;
            if(!c.collisionTags.Any(t=>t!=null && (t.StartsWith("Hand",StringComparison.Ordinal)||t.StartsWith("Finger",StringComparison.Ordinal))))continue;
            manifest.contacts.Add(new Contact{root=AnimationUtility.CalculateTransformPath(c.rootTransform?c.rootTransform:c.transform,descriptor.transform),
                owner=AnimationUtility.CalculateTransformPath(c.transform,descriptor.transform),parameter=c.parameter,mode=c.receiverType.ToString(),
                position=c.position,rotation=c.rotation,radius=c.radius,height=c.shapeType.ToString()=="Capsule"?c.height:0});
        }
        var eyes=descriptor.customEyeLookSettings;var skin=eyes.eyelidsSkinnedMesh;
        if(skin && skin.sharedMesh && eyes.eyelidsBlendshapes!=null && eyes.eyelidsBlendshapes.Length>0){
            int i=eyes.eyelidsBlendshapes[0];if(i>=0 && i<skin.sharedMesh.blendShapeCount){
                manifest.eyeMesh=AnimationUtility.CalculateTransformPath(skin.transform,descriptor.transform);
                manifest.blinkShape=skin.sharedMesh.GetBlendShapeName(i);
            }
        }
        string file=dir+"/desktop-controls.json";File.WriteAllText(file,JsonUtility.ToJson(manifest));AssetDatabase.ImportAsset(file);AssetDatabase.SaveAssets();return file;
    }
    static void ReadDrivers(AnimatorStateMachine machine,string prefix,int layer,Menu menu){
        if(!machine)return;
        foreach(var child in machine.states){
            string name=prefix+"."+child.state.name;
            foreach(var behaviour in child.state.behaviours){
                if(!behaviour || behaviour.GetType().Name!="VRCAvatarParameterDriver")continue;
                var array=new SerializedObject(behaviour).FindProperty("parameters");if(array==null)continue;
                var driver=new Driver{layer=layer,hash=Animator.StringToHash(name)};
                for(int i=0;i<array.arraySize;i++){
                    var p=array.GetArrayElementAtIndex(i);var value=p.FindPropertyRelative("type");
                    string op=value!=null && value.enumValueIndex>=0 && value.enumValueIndex<value.enumNames.Length?value.enumNames[value.enumValueIndex]:"Set";
                    string S(string key)=>p.FindPropertyRelative(key)?.stringValue??"";
                    float F(string key)=>p.FindPropertyRelative(key)?.floatValue??0;
                    driver.operations.Add(new DriverOperation{type=op,name=S("name"),source=S("source"),value=F("value"),valueMin=F("valueMin"),valueMax=F("valueMax"),chance=F("chance"),sourceMin=F("sourceMin"),sourceMax=F("sourceMax"),destMin=F("destMin"),destMax=F("destMax"),convertRange=p.FindPropertyRelative("convertRange")?.boolValue??false});
                }
                if(driver.operations.Count>0)menu.drivers.Add(driver);
            }
        }
        foreach(var child in machine.stateMachines)ReadDrivers(child.stateMachine,prefix+"."+child.stateMachine.name,layer,menu);
    }
    static string Icon(Texture2D tex){
        if(!tex)return "";var rt=RenderTexture.GetTemporary(72,72,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;var png=new Texture2D(72,72,TextureFormat.RGBA32,false);
        try{Graphics.Blit(tex,rt);RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,72,72),0,0);png.Apply();return Convert.ToBase64String(png.EncodeToPNG());}
        finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(png);}
    }
    static void Collect(VRCExpressionsMenu menu,string prefix,string parent,HashSet<VRCExpressionsMenu> visited,Dictionary<string,AnimatorControllerParameter> parameters,VRCAvatarDescriptor descriptor,Menu output){
        if(!menu || !visited.Add(menu))return;
        foreach(var item in menu.controls){
            string text=System.Text.RegularExpressions.Regex.Replace(item.name??"","<[^>]+>","").Trim();if(text.Length==0)continue;
            string label=prefix+text;
            var name=item.parameter?.name??"";var axis=item.subParameters?.Select(p=>p.name??"").ToArray()??new string[0];
            var node=new Node{id="node"+output.nodes.Count,parent=parent,label=text,kind=item.type.ToString(),parameter=name,value=item.value,icon=Icon(item.icon),
                axes=axis,axisLabels=item.labels?.Select(l=>l.name??"").ToArray()??new string[0],
                available=item.type==VRCExpressionsMenu.Control.ControlType.SubMenu || parameters.ContainsKey(name)||axis.Any(parameters.ContainsKey),
                initial=descriptor.expressionParameters?.parameters?.FirstOrDefault(p=>p.name==name)?.defaultValue??0};
            output.nodes.Add(node);
            if(item.type==VRCExpressionsMenu.Control.ControlType.SubMenu){Collect(item.subMenu,label+" / ",node.id,visited,parameters,descriptor,output);continue;}
            if(string.IsNullOrEmpty(name)||!parameters.ContainsKey(name)||output.curated)continue;
            bool toggle=item.type==VRCExpressionsMenu.Control.ControlType.Toggle;
            if(!toggle && item.type!=VRCExpressionsMenu.Control.ControlType.Button)continue;
            var e=new Entry{id="vrc/"+name+"/"+item.value.ToString("R",System.Globalization.CultureInfo.InvariantCulture),label=label,parameter=name,value=item.value};
            var p=descriptor.expressionParameters?.parameters?.FirstOrDefault(x=>x.name==name);e.initial=p!=null && Mathf.Approximately(p.defaultValue,item.value);
            var target=toggle?output.clothes:output.actions;if(!target.Any(o=>o.id==e.id))target.Add(e);
        }
        visited.Remove(menu);
    }
}
#endif
