using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UniVRM10;

public class PetMotion : MonoBehaviour
{
    Vector3 scale;
    float tapped=-100, nextBlink, blinkStart=-100;
    Vrm10Instance vrm;
    Animation legacy;
    string idle, greet;
    Animator animator;
    AnimationClip touchClip;
    PlayableGraph graph;
    Animator restRig;
    SkinnedMeshRenderer face;
    int blinkIndex=-1;
    static bool Match(string value, params string[] keys) => keys.Any(k=>value.ToLowerInvariant().Contains(k));
    static bool IsGreet(string name) => Match(name,"wave","greet","hello","happy","nod","salute","挥手","招手","おじぎ");
    static bool IsIdle(string name) => Match(name,"idle","breath","standby","待机","待機");

    public void Prepare(GameObject model)
    {
        restRig=FindRig(model);
        scale=transform.localScale; nextBlink=Time.time+2;
        vrm=model.GetComponentInChildren<Vrm10Instance>();
        legacy=model.GetComponentInChildren<Animation>();
        if(legacy) {
            var clips=legacy.Cast<AnimationState>().ToArray();
            idle=clips.FirstOrDefault(c=>IsIdle(c.name))?.name;
            greet=clips.FirstOrDefault(c=>IsGreet(c.name))?.name;
            if(idle!=null) { legacy[idle].wrapMode=WrapMode.Loop; legacy.Play(idle); }
            if(greet!=null) legacy[greet].wrapMode=WrapMode.Once;
        }
        animator=model.GetComponentsInChildren<Animator>().FirstOrDefault(a=>a.runtimeAnimatorController);
        if(animator) {
            animator.applyRootMotion=false;
            touchClip=animator.runtimeAnimatorController.animationClips.FirstOrDefault(c=>IsGreet(c.name));
        }
        foreach(var mesh in model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            if(!mesh.sharedMesh) continue;
            for(int i=0;i<mesh.sharedMesh.blendShapeCount;i++) {
                string name=mesh.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();
                if(name=="blink" || name=="まばたき" || name=="eye_close" || name=="eyes_closed") { face=mesh; blinkIndex=i; break; }
            }
            if(face) break;
        }
    }
    public static void RelaxArms(GameObject model)
    {
        RelaxArms(FindRig(model));
    }
    static Animator FindRig(GameObject model)=>model.GetComponentsInChildren<Animator>().FirstOrDefault(a=>a.isHuman && a.avatar && a.avatar.isValid);
    static void RelaxArms(Animator rig){
        if(!rig)return;
        Relax(rig,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm);
        Relax(rig,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm);
    }
    static void Relax(Animator rig,HumanBodyBones upper,HumanBodyBones lower)
    {
        var u=rig.GetBoneTransform(upper);var l=rig.GetBoneTransform(lower);
        if(!u || !l)return;
        var arm=(l.position-u.position).normalized;
        if(Mathf.Abs(arm.y)>.40f)return;
        var aim=new Vector3(arm.x*.24f,-.95f,-.12f).normalized;
        u.rotation=Quaternion.FromToRotation(arm,aim)*u.rotation;
    }
    void LateUpdate() { if(idle==null && greet==null && !graph.IsValid())RelaxArms(restRig); }

    public void Tap()
    {
        tapped=Time.time;
        if(greet!=null) { legacy.CrossFade(greet,.1f); if(idle!=null) legacy.CrossFadeQueued(idle,.1f); }
        else if(touchClip) {
            if(graph.IsValid()) graph.Destroy();
            graph=PlayableGraph.Create("Tap clip"); var output=AnimationPlayableOutput.Create(graph,"Character",animator);
            output.SetSourcePlayable(AnimationClipPlayable.Create(graph,touchClip)); graph.Play();
        }
    }
    void Update()
    {
        float t=Time.time-tapped;
        if(graph.IsValid() && t>=touchClip.length) graph.Destroy();
        if(Time.time>=nextBlink) { blinkStart=Time.time; nextBlink=Time.time+Random.Range(3.2f,5.9f); }
        float blink=Mathf.Clamp01(1-Mathf.Abs((Time.time-blinkStart-.10f)/.10f));
        float reaction=t<.72f?Mathf.Sin(Mathf.PI*t/.72f):0;
        if(vrm && vrm.Runtime!=null) {
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Blink,blink);
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Happy,reaction*.8f);
        } else if(face && blinkIndex>=0) {
            face.SetBlendShapeWeight(blinkIndex,100*Mathf.Max(blink,reaction*.8f));
        }
        bool freeMotion=greet==null && !touchClip;
        float breath=Mathf.Sin(Time.time*1.8f);
        transform.localPosition=new Vector3(0,breath*.008f+(freeMotion?reaction*.12f:0),0);
        transform.localRotation=Quaternion.Euler(freeMotion?-reaction*6:0,0,breath*.5f);
        transform.localScale=Vector3.Scale(scale,new Vector3(1,1+breath*.004f,1));
    }
    void OnDestroy() { if(graph.IsValid()) graph.Destroy(); }
}
