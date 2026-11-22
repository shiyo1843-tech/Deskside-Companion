using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UniVRM10;

[Serializable] public class PetOption { public string id,label,parameter; public float value; public bool initial,advanced; public PetOption(){} public PetOption(string key,string title,bool on=false,bool extra=false){id=key;label=title;initial=on;advanced=extra;} }
[Serializable] public class RadialNode {public string id,parent,label,kind,parameter,icon;public string[] axes,axisLabels;public float value,initial;public bool available;}
[Serializable] public class DriverOperation {public string type,name,source;public float value,valueMin,valueMax,chance,sourceMin,sourceMax,destMin,destMax;public bool convertRange;}
[Serializable] public class PetDriver {public int layer,hash;public List<DriverOperation> operations;}
[Serializable] public class HairChain {public string root,parameter;public string[] ignore;public Vector3 endpoint;public float radius,pull,spring;public bool grab,collision;}
[Serializable] public class PetContact {public string root,owner,parameter,mode;public Vector3 position;public Quaternion rotation;public float radius,height;}
[Serializable] public class PetMenu {public List<PetOption> actions=new List<PetOption>(),faces=new List<PetOption>(),clothes=new List<PetOption>();public List<RadialNode> nodes=new List<RadialNode>();public List<PetDriver> drivers;public List<HairChain> hair,parts;public List<PetContact> contacts;public string eyeMesh,blinkShape;public bool vrchat,curated,humanoidMotion;}
[Serializable] public class ParamValue {public string name;public float value;}
[Serializable] public class WearValue {public string id;public bool on;}
[Serializable] public class PetChoice {public int serial,seq,pulse,lightPercent;public string action,face;public bool loop;public WearValue[] wear;public ParamValue[] parameters;}

[DefaultExecutionOrder(9500)]
public sealed class ModelControls:MonoBehaviour {
    public PetMenu Menu {get;private set;}
    Animator rig; Animation legacy; Vrm10Instance vrm;
    readonly Dictionary<string,AnimationClip> clips=new Dictionary<string,AnimationClip>();
    readonly Dictionary<string,ExpressionKey> vrmFaces=new Dictionary<string,ExpressionKey>();
    readonly Dictionary<string,Tuple<SkinnedMeshRenderer,int,float>> shapes=new Dictionary<string,Tuple<SkinnedMeshRenderer,int,float>>();
    readonly Dictionary<string,Renderer> renderers=new Dictionary<string,Renderer>();
    PlayableGraph graph; bool preset; float until,actionAt,activityAt;
    string face="",playing="",shown="",previous="",happy="",relaxed="",wink="",shy="";
    int lastPulse,cycle,activity=int.MinValue,faceLayer=-1;
    float strength,previousStrength;
    const string BounceId="deskpet/bounce";
    Action bounce;
    string travelClip;
    bool ownsTravel;
    float nextBounce;
    public bool NaturalFace=>string.IsNullOrEmpty(face);
    public string ShownFace=>shown;
    public string PlayingAction=>playing;
    public float FaceWeight=>strength;
    SkinnedMeshRenderer faceMesh;
    public float MeasuredFace(){
        if(!faceMesh)faceMesh=GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.sharedMesh && s.sharedMesh.blendShapeCount>0).OrderByDescending(s=>s.sharedMesh.blendShapeCount).FirstOrDefault();
        if(!faceMesh || !faceMesh.sharedMesh)return 0;
        float sum=0;
        for(int i=0;i<faceMesh.sharedMesh.blendShapeCount;i++){float w=faceMesh.GetBlendShapeWeight(i);sum+=w*w;}
        return Mathf.Sqrt(sum);
    }
    readonly Dictionary<string,bool> applied=new Dictionary<string,bool>();
    readonly Dictionary<string,float> inputs=new Dictionary<string,float>();
    readonly Dictionary<string,AnimatorControllerParameter> parameters=new Dictionary<string,AnimatorControllerParameter>();
    readonly Dictionary<int,Dictionary<int,List<DriverOperation>>> drivers=new Dictionary<int,Dictionary<int,List<DriverOperation>>>();
    readonly Dictionary<int,int> entered=new Dictionary<int,int>();
    public void Prepare(GameObject root,TextAsset meta){
        rig=root.GetComponentsInChildren<Animator>().OrderByDescending(a=>a.isHuman).FirstOrDefault();
        legacy=root.GetComponentInChildren<Animation>();vrm=root.GetComponentInChildren<Vrm10Instance>();
        Menu=meta?JsonUtility.FromJson<PetMenu>(meta.text):new PetMenu();preset=meta && rig && rig.runtimeAnimatorController && (!Menu.vrchat || Menu.curated);
        if(!string.IsNullOrEmpty(Menu.eyeMesh))faceMesh=root.transform.Find(Menu.eyeMesh)?.GetComponent<SkinnedMeshRenderer>();
        if(rig && rig.runtimeAnimatorController)foreach(var p in rig.parameters)parameters[p.name]=p;
        if(Menu.drivers!=null)foreach(var d in Menu.drivers){
            if(!drivers.TryGetValue(d.layer,out var states))drivers[d.layer]=states=new Dictionary<int,List<DriverOperation>>();
            if(!states.TryGetValue(d.hash,out var ops))states[d.hash]=ops=new List<DriverOperation>();
            ops.AddRange(d.operations);
        }
        if(preset) {rig.cullingMode=AnimatorCullingMode.AlwaysAnimate;faceLayer=rig.GetLayerIndex("Face");FindNaturalFaces();return;}
        if(legacy)foreach(AnimationState state in legacy)RegisterClip(state.clip);
        if(rig && rig.runtimeAnimatorController && !Menu.vrchat)foreach(var clip in rig.runtimeAnimatorController.animationClips.Distinct())RegisterClip(clip);
        if(vrm && vrm.Vrm)foreach(var clip in vrm.Vrm.Expression.Clips.Select(pair=>pair.Clip).Where(c=>c)){
            var key=vrm.Vrm.Expression.CreateKey(clip);
            if(!clip.MorphTargetBindings.Any()&&!clip.MaterialColorBindings.Any()&&!clip.MaterialUVBindings.Any())continue;
            string id="vrm/"+key.Name;vrmFaces[id]=key;Menu.faces.Add(new PetOption(id,VrmLabel(key.Name)));
        }
        int index=0;
        int visibleParts=root.GetComponentsInChildren<Renderer>().Count(r=>r is MeshRenderer || r is SkinnedMeshRenderer);
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){
            if(!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))continue;
            string name=renderer.name,id="mesh/"+index++;renderers[id]=renderer;
            bool garment=new[]{"shirt","coat","shoe","sock","dress","skirt","jacket","衣","靴","袜","帽","裙","服","outfit","accessory","sweater","necklace","ribbon","pumps","pants","trousers","shorts","boot","spats","hat"}.Any(s=>name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0) || name.StartsWith("Acc",StringComparison.OrdinalIgnoreCase);
            if(visibleParts>1 && renderer.gameObject.activeInHierarchy)Menu.clothes.Add(new PetOption(id,name,renderer.enabled,!garment));
            if(renderer is SkinnedMeshRenderer skin && skin.sharedMesh && (!vrm || vrmFaces.Count==0)){
                for(int j=0;j<skin.sharedMesh.blendShapeCount;j++){
                    string n=skin.sharedMesh.GetBlendShapeName(j),k=id+"/shape/"+j;
                    if(string.IsNullOrWhiteSpace(n)||n.Trim('-').Length==0)continue;
                    shapes[k]=Tuple.Create(skin,j,skin.GetBlendShapeWeight(j));Menu.faces.Add(new PetOption(k,name+" · "+n,false,true));
                }
            }
        }
        FindNaturalFaces();
    }
    static string VrmLabel(string name){
        switch(name){
            case "happy":return "开心";case "angry":return "生气";case "sad":return "难过";case "relaxed":return "放松";case "surprised":return "惊讶";
            case "blink":return "闭眼";case "blinkLeft":return "闭左眼";case "blinkRight":return "闭右眼";case "neutral":return "平静";
            case "lookUp":return "向上看";case "lookDown":return "向下看";case "lookLeft":return "向左看";case "lookRight":return "向右看";
            case "aa":case "ih":case "ou":case "ee":case "oh":return "口型 "+name.ToUpperInvariant();
            default:return name;
        }
    }
    void FindNaturalFaces(){
        string Find(params string[] names){
            foreach(string name in names)foreach(var item in Menu.faces){
                string id=item.id.StartsWith("vrm/")?item.id.Substring(4):item.id;
                string label=item.label.Substring(item.label.LastIndexOf('·')+1).Trim();
                if((preset || shapes.ContainsKey(item.id) || vrmFaces.ContainsKey(item.id)) &&
                   (string.Equals(id,name,StringComparison.OrdinalIgnoreCase)||string.Equals(label,name,StringComparison.OrdinalIgnoreCase)))return item.id;
            }
            return "";
        }
        happy=Find("smile","happy","joy","微笑","笑顔");
        relaxed=Find("relaxed","fun");wink=Find("wink","winkLeft","blinkLeft","眨单眼");
        shy=Find("shy","害羞");
    }
    void RegisterClip(AnimationClip clip){if(!clip||clips.ContainsKey(clip.name))return;clips[clip.name]=clip;Menu.actions.Add(new PetOption(clip.name,clip.name));}
    public void AddGenericMovement(Action onBounce){
        bounce=onBounce;Menu.actions.Add(new PetOption(BounceId,"轻轻弹一下"));
        travelClip=clips.Keys.FirstOrDefault(name=>{
            string n=name.ToLowerInvariant();
            return (n.Contains("walk")||n.Contains("trot")||n.Contains("歩行")||n.Contains("行走")) &&
                !(n.Contains("back")||n.Contains("turn")||n.Contains("start")||n.Contains("stop")||n.Contains("left")||n.Contains("right"));
        });
    }
    public bool ModelTravel(int mode){
        if(string.IsNullOrEmpty(travelClip))return false;
        if(Math.Abs(mode)==1 && !ownsTravel && playing.Length==0){Play(travelClip,true);ownsTravel=true;}
        else if(Math.Abs(mode)!=1 && ownsTravel){ownsTravel=false;if(playing==travelClip)StopAction();}
        return ownsTravel && playing==travelClip;
    }
    void Parameter(PetOption opt,bool on){
        SetParameter(opt.parameter,on?opt.value:0);
    }
    float ReadParameter(string name){
        if(string.IsNullOrEmpty(name)||!parameters.TryGetValue(name,out var p))return 0;
        switch(p.type){case AnimatorControllerParameterType.Bool:return rig.GetBool(name)?1:0;case AnimatorControllerParameterType.Int:return rig.GetInteger(name);case AnimatorControllerParameterType.Float:return rig.GetFloat(name);default:return 0;}
    }
    public void SetParameter(string name,float value){if(!rig||string.IsNullOrEmpty(name)||!parameters.TryGetValue(name,out var p))return;
        switch(p.type){case AnimatorControllerParameterType.Bool:rig.SetBool(name,value!=0);break;case AnimatorControllerParameterType.Int:rig.SetInteger(name,Mathf.RoundToInt(value));break;case AnimatorControllerParameterType.Float:rig.SetFloat(name,value);break;}}
    int BodyLayer=>Mathf.Max(0,rig.GetLayerIndex("DeskPet Body"));
    const string Cheer="deskpet/celebration";
    float cheerSeconds;
    public bool SharedGesture=>playing==Cheer;
    public void AddSharedGesture(AnimationClip clip){
        if(preset || !clip || Menu.actions.Any(x=>x.id==Cheer))return;
        cheerSeconds=clip.length;Menu.actions.Add(new PetOption(Cheer,"庆祝"));
    }
    public void Apply(PetChoice choice){
        if(choice.parameters!=null)foreach(var p in choice.parameters){
            if(inputs.TryGetValue(p.name,out var old)&&old==p.value)continue;
            SetParameter(p.name,p.value);inputs[p.name]=p.value;
        }
        if(choice.wear!=null)foreach(var w in choice.wear){
            if(applied.TryGetValue(w.id,out bool last)&&last==w.on)continue;
            var opt=Menu.clothes.FirstOrDefault(x=>x.id==w.id);if(opt==null)continue;
            if(renderers.TryGetValue(w.id,out var r))r.enabled=w.on;
            else if(preset){string name="Wear_"+w.id;int layer=rig.GetLayerIndex(name);if(layer>=0)rig.Play(name+"."+(w.on?"on":"off"),layer,0);}
            else Parameter(opt,w.on);
            applied[w.id]=w.on;
        }
        face=choice.face??"";
        if(choice.pulse!=lastPulse){lastPulse=choice.pulse;Play(choice.action,choice.loop);}
    }
    void WriteFace(string id,float weight){
        if(shapes.TryGetValue(id,out var shape))shape.Item1.SetBlendShapeWeight(shape.Item2,Mathf.Lerp(shape.Item3,100,weight));
        else if(vrmFaces.TryGetValue(id,out var key))vrm.Runtime.Expression.SetWeight(key,weight);
    }
    public void UpdateExpression(int mode,float headTurn){
        if(mode!=activity){activity=mode;activityAt=Time.time;}
        float age=Time.time-activityAt,amount=1;
        string id=face;
        if(NaturalFace){
            string soft=relaxed.Length!=0?relaxed:happy;
            float beat=Time.time-actionAt;
            if(Menu.vrchat && (ReadParameter("SetExpression")!=0 || ReadParameter("VRCEmote")!=0))id="";
            else if(playing=="peace"){id=beat<1.25f && wink.Length!=0?wink:happy;amount=.93f;}
            else if(playing=="wave" || playing=="thumb" || SharedGesture){id=happy;amount=.9f;}
            else if(playing=="sit"){id=soft;amount=.48f;}
            else if(playing.Length!=0){id="";}
            else if(mode==9){id=shy.Length!=0?shy:happy;amount=age<.35f?.18f:.53f;}
            else if(Math.Abs(mode)==1){id=soft;amount=.28f;}
            else if(mode==4 && age>.22f){id=shy.Length!=0?shy:soft;amount=.62f;}
            else if(mode==5 && age>.35f){id=soft;amount=Mathf.Lerp(.28f,.73f,Mathf.Abs(headTurn)/27f);}
            else if(mode==2){id=soft;amount=.5f;}
            else if(mode==0 && Mathf.Repeat(age,19f)>9f && Mathf.Repeat(age,19f)<11.5f){id=soft;amount=.32f;}
        }
        if(id!=shown){
            if(!preset){WriteFace(previous,0);previous=shown;previousStrength=strength;strength=0;}
            if(faceLayer>=0 && id.Length!=0){
                // A neutral expression is zero layer weight. Never blend an empty/default
                // state over the body while entering or leaving a facial expression.
                if(strength<=0)rig.Play("Face."+id,faceLayer,0);
                else rig.CrossFadeInFixedTime("Face."+id,.18f,faceLayer);
            }
            shown=id;
        }
        float step=Time.unscaledDeltaTime*5f;
        strength=Mathf.MoveTowards(strength,shown.Length!=0?amount:0,step);
        if(faceLayer>=0)rig.SetLayerWeight(faceLayer,strength);
        else if(!preset){
            if(previous.Length!=0){previousStrength=Mathf.MoveTowards(previousStrength,0,step);WriteFace(previous,previousStrength);if(previousStrength==0)previous="";}
            if(shown.Length!=0)WriteFace(shown,strength);
        }
    }
    void StopAction(bool returnToIdle=true){
        if(graph.IsValid())graph.Destroy();
        if(preset && rig){if(returnToIdle && playing.Length!=0)rig.CrossFadeInFixedTime("Base Layer.idle",.22f,BodyLayer);}
        else if(Menu.vrchat){var o=Menu.actions.FirstOrDefault(x=>x.id==playing);if(o!=null)Parameter(o,false);}
        else if(legacy){legacy.Stop();var idle=clips.Keys.FirstOrDefault(k=>k.IndexOf("idle",StringComparison.OrdinalIgnoreCase)>=0);if(idle!=null){legacy[idle].wrapMode=WrapMode.Loop;legacy.Play(idle);}}
        playing="";until=0;
    }
    public void Play(string id,bool loop){
        ownsTravel=false;
        var option=Menu.actions.FirstOrDefault(x=>x.id==id);
        if(option==null){StopAction();return;}
        // Cross-fade directly from the current action. Requesting idle and the new
        // gesture in the same animator tick interrupts the first transition.
        StopAction(false);
        float duration=2.2f;
        if(id==BounceId && bounce!=null){bounce();duration=.58f;nextBounce=Time.time+1.0f;}
        else if(id==Cheer)duration=cheerSeconds;
        else if(preset){rig.CrossFadeInFixedTime("Base Layer."+id,.18f,BodyLayer);if(id=="sit")duration=3;}
        else if(!string.IsNullOrEmpty(option.parameter))Parameter(option,true);
        else if(clips.TryGetValue(id,out var clip)){
            duration=Mathf.Max(.25f,clip.length);
            if(legacy && legacy.GetClip(id)){legacy[id].wrapMode=loop?WrapMode.Loop:WrapMode.Once;legacy.CrossFade(id,.12f);}
            else if(rig){graph=PlayableGraph.Create("Imported action");var output=AnimationPlayableOutput.Create(graph,"Model",rig);output.SetSourcePlayable(AnimationClipPlayable.Create(graph,clip));graph.Play();}
        }
        playing=id;actionAt=Time.time;until=loop?float.PositiveInfinity:Time.time+duration;
    }
    public bool Tap(){
        if(Menu.actions.Count==0)return false;
        var candidates=Menu.actions.Where(x=>x.id!="sit" && x.id!=BounceId).ToArray();if(candidates.Length==0)candidates=Menu.actions.ToArray();
        Play(candidates[cycle++%candidates.Length].id,false);
        return true;
    }
    void RunDrivers(){
        if(!rig)return;
        foreach(var layer in drivers){
            int i=layer.Key;
            int hash=rig.IsInTransition(i)?rig.GetNextAnimatorStateInfo(i).fullPathHash:rig.GetCurrentAnimatorStateInfo(i).fullPathHash;
            if(entered.TryGetValue(i,out var last)&&last==hash)continue;
            entered[i]=hash;if(!layer.Value.TryGetValue(hash,out var operations))continue;
            foreach(var op in operations){
                if(!parameters.TryGetValue(op.name,out var dst))continue;
                float v=op.value;
                if(op.type=="Add")v+=ReadParameter(op.name);
                else if(op.type=="Copy"){v=ReadParameter(op.source);if(op.convertRange)v=Mathf.Lerp(op.destMin,op.destMax,Mathf.InverseLerp(op.sourceMin,op.sourceMax,v));}
                else if(op.type=="Random")v=dst.type==AnimatorControllerParameterType.Bool?(UnityEngine.Random.value<op.chance?1:0):dst.type==AnimatorControllerParameterType.Int?UnityEngine.Random.Range(Mathf.RoundToInt(op.valueMin),Mathf.RoundToInt(op.valueMax)+1):UnityEngine.Random.Range(op.valueMin,op.valueMax);
                SetParameter(op.name,v);
            }
        }
    }
    void LateUpdate(){
        RunDrivers();
        if(playing==BounceId && float.IsPositiveInfinity(until) && Time.time>=nextBounce){bounce?.Invoke();nextBounce=Time.time+1;}
        if(until>0 && Time.time>=until)StopAction();
    }
    public bool HasAuthoredIdle=>preset||Menu.vrchat;
    public bool TryAmbientWave(){
        if(!string.IsNullOrEmpty(playing))return false;
        if(preset && Menu.actions.Any(x=>x.id=="wave")){Play("wave",false);return true;}
        if(cheerSeconds>0){Play(Cheer,false);return true;}
        return false;
    }
    void OnDestroy(){if(graph.IsValid())graph.Destroy();}
}
