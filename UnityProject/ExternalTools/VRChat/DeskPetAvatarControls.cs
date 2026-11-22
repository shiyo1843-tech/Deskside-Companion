// Install in the source avatar project's Assets/Editor next to DeskPetExporter.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class DeskPetAvatarControls
{
    [Serializable] public class Option { public string id,label;public bool initial; public Option(string i,string l,bool d=false){id=i;label=l;initial=d;} }
    [Serializable] public class Manifest { public List<Option> actions=new List<Option>(),faces=new List<Option>(),clothes=new List<Option>(); }
    static readonly Dictionary<string,AnimationClip> clips=new Dictionary<string,AnimationClip>();
    static AnimationClip Find(string name) { if(!clips.TryGetValue(name,out var c))throw new Exception("Missing original clip "+name);return c; }
    static AnimatorState State(AnimatorStateMachine machine,string name,AnimationClip clip){
        var s=machine.AddState(name);s.motion=clip;s.writeDefaultValues=false;return s;
    }
    static AnimatorStateMachine Layer(AnimatorController c,string name){
        c.AddLayer(name);var layers=c.layers;layers[layers.Length-1].defaultWeight=1;c.layers=layers;return layers.Last().stateMachine;
    }
    static void Save(AnimationClip c,string dir,string name){c.name=name;AssetDatabase.CreateAsset(c,dir+"/"+name+".anim");}
    static void Loop(AnimationClip c,bool loop){var settings=AnimationUtility.GetAnimationClipSettings(c);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(c,settings);}
    static AnimationClip Fixed(AnimationClip src,float time,string folder,string name){
        var clip=new AnimationClip();var keys=AnimationUtility.GetCurveBindings(src);
        var curves=keys.Select(k=>AnimationCurve.Constant(0,1,AnimationUtility.GetEditorCurve(src,k).Evaluate(time))).ToArray();
        AnimationUtility.SetEditorCurves(clip,keys,curves);Save(clip,folder,name);return clip;
    }
    // Retarget the original humanoid pose and finger muscles, then animate into and out of the pose.
    static AnimationClip Gesture(AnimationClip idle,AnimationClip stance,AnimationClip hand,string dir,string name){
        var curves=new Dictionary<EditorCurveBinding,AnimationCurve>();
        foreach(var b in AnimationUtility.GetCurveBindings(idle))if(b.type==typeof(Animator))curves[b]=AnimationCurve.Constant(0,2.2f,AnimationUtility.GetEditorCurve(idle,b).Evaluate(0));
        foreach(var src in new[]{stance,hand}.Where(x=>x)){
            foreach(var b in AnimationUtility.GetCurveBindings(src)){
                if(b.type!=typeof(Animator) || b.propertyName.StartsWith("Root") || b.propertyName.Contains("FootT") || b.propertyName.Contains("FootQ"))continue;
                float from=curves.TryGetValue(b,out var old)?old.Evaluate(0):0;
                float to=AnimationUtility.GetEditorCurve(src,b).Evaluate(0);
                curves[b]=new AnimationCurve(new Keyframe(0,from),new Keyframe(.42f,to),new Keyframe(1.65f,to),new Keyframe(2.2f,from));
            }
        }
        if(name=="wave"){
            var k=EditorCurveBinding.FloatCurve("",typeof(Animator),"Right Forearm Twist In-Out");
            float center=curves.TryGetValue(k,out var orig)?orig.Evaluate(.6f):0;
            curves[k]=new AnimationCurve(new Keyframe(0,0),new Keyframe(.42f,center),new Keyframe(.67f,center+.30f),new Keyframe(.95f,center-.3f),new Keyframe(1.23f,center+.3f),new Keyframe(1.56f,center-.2f),new Keyframe(2.2f,0));
        }
        var clip=new AnimationClip();AnimationUtility.SetEditorCurves(clip,curves.Keys.ToArray(),curves.Values.ToArray());Loop(clip,true);Save(clip,dir,name);return clip;
    }
    public static string Build(GameObject root,string model,string dir){
        if(model!="Hikarun" && model!="Lime")throw new ArgumentException("This preset supports Lime and Hikarun.");
        bool h=model=="Hikarun"; var manifest=new Manifest();
        clips.Clear();
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.EndsWith(".anim",StringComparison.OrdinalIgnoreCase)&&
          (p.StartsWith("Assets/Amatousagi/Lime/")||p.StartsWith("Assets/KululuShippo_Avatar/")||p.StartsWith("Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/")));
        foreach(var p in paths)clips[Path.GetFileNameWithoutExtension(p)]=AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        var controller=AnimatorController.CreateAnimatorControllerAtPath(dir+"/Desktop.controller");
        var rig=root.GetComponentsInChildren<Animator>(true).First(a=>a.isHuman);rig.runtimeAnimatorController=controller;rig.applyRootMotion=false;
        var body=controller.layers[0].stateMachine;
        var idle=UnityEngine.Object.Instantiate(Find(h?"Hikarun_stand_still":"proxy_stand_still"));
        if(h){var breathing=Find("Hikarun_breathing");foreach(var b in AnimationUtility.GetCurveBindings(breathing))AnimationUtility.SetEditorCurve(idle,b,AnimationUtility.GetEditorCurve(breathing,b));}
        Loop(idle,true);Save(idle,dir,"standing");body.defaultState=State(body,"idle",idle);
        foreach(var def in new[]{new[]{"wave","挥手",h?"Hikarun_hands_open":"Lime_Open"},new[]{"peace","比耶",h?"Hikarun_hands_peace":"Lime_Peace"},new[]{"thumb","点赞",h?"Hikarun_hands_thumbs_up":"Lime_Thumbsup"}}){
            State(body,def[0],Gesture(idle,Find("proxy_stand_wave"),Find(def[2]),dir,def[0]));manifest.actions.Add(new Option(def[0],def[1]));
        }
        if(h){var sit=Find("Hikarun_sit");State(body,"sit",sit);manifest.actions.Add(new Option("sit","坐下"));}
        var expression=Layer(controller,"Face");var empty=new AnimationClip();Save(empty,dir,"empty");
        var fxClips=h?new[]{new[]{"smile","微笑","Hikarun_facial_happy"},new[]{"wink","眨单眼","Hikarun_facial_wink_1.12"},new[]{"heart","爱心眼","Hikarun_facial_heartlove_1.12"},new[]{"puff","鼓脸","Hikarun_facial_pukku_1.12"}}:
            new[]{new[]{"smile","微笑","F_smile_1"},new[]{"wink","眨单眼","F_wink_1"},new[]{"heart","爱心眼","F_heart"},new[]{"shy","害羞","F_shy"}};
        var neutral=new AnimationClip();var registers=new Dictionary<EditorCurveBinding,AnimationCurve>();
        foreach(var def in fxClips){
            var original=Find(def[2]);int valid=0;
            foreach(var b in AnimationUtility.GetCurveBindings(original)){
                if(b.type!=typeof(SkinnedMeshRenderer) || !b.propertyName.StartsWith("blendShape."))continue;
                var t=root.transform.Find(b.path);var mesh=t?t.GetComponent<SkinnedMeshRenderer>():null;
                int index=mesh && mesh.sharedMesh?mesh.sharedMesh.GetBlendShapeIndex(b.propertyName.Substring(11)):-1;
                if(index<0)continue;valid++;
                if(!registers.ContainsKey(b))registers[b]=AnimationCurve.Constant(0,1,mesh.GetBlendShapeWeight(index));
            }
            if(valid==0)throw new Exception("Unbound face: "+def[2]);
            State(expression,def[0],original);manifest.faces.Add(new Option(def[0],def[1]));
            Debug.Log("FACE "+def[2]+" bound "+valid);
        }
        AnimationUtility.SetEditorCurves(neutral,registers.Keys.ToArray(),registers.Values.ToArray());Save(neutral,dir,"neutral");expression.defaultState=State(expression,"off",neutral);
        var wardrobe=h?new[]{new[]{"outfit","主服装","main","Outfit_Main"},new[]{"shoes","乐福鞋","shoe","Outfit_Loafers"},new[]{"socks","袜子","sock","Outfit_Socks"},new[]{"controller","游戏手柄","controller","Outfit_Controller"},new[]{"collar","项圈","collar","Outfit_Collar"},new[]{"lead","牵引带","lead","Outfit_Lead"}}:
            new[]{new[]{"cardigan","开衫","Cardigan","Cardigan"},new[]{"blouse","上衣","Blouse","Blouse"},new[]{"boots","靴子","Boots","Boots"},new[]{"socks","长袜","Knee-Sock","Knee-Socks"},new[]{"ears","兽耳","Ear","Ear"},new[]{"tail","尾巴","Tail","Tail"}};
        foreach(var def in wardrobe){
            var target=root.transform.Find(def[3]);if(!target)continue;
            var layer=Layer(controller,"Wear_"+def[0]);
            var off=h?Fixed(Find("Hikarun_clothes_"+def[2]+"_toggle"),0,dir,def[0]+"0"):Find(def[2]+"_OFF");
            var on=h?Fixed(Find("Hikarun_clothes_"+def[2]+"_toggle"),1,dir,def[0]+"1"):Find(def[2]+"_ON");
            // Preserve the user's authored scene until this specific control is touched.
            layer.defaultState=State(layer,"saved",empty);State(layer,"on",on);State(layer,"off",off);
            manifest.clothes.Add(new Option(def[0],def[1],target.gameObject.activeSelf));
        }
        string metadata=dir+"/desktop-controls.json";File.WriteAllText(metadata,JsonUtility.ToJson(manifest));AssetDatabase.ImportAsset(metadata);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();return metadata;
    }
}
