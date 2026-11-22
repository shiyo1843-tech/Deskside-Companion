using System.Collections.Generic;
using UnityEngine;

// Localized reactions for humanoid parts without an authored touch receiver.
public sealed class HumanoidTouch
{
    sealed class Zone {public Transform from,to,driven;public float radius;public int kind;}
    readonly List<Zone> zones=new List<Zone>();
    readonly List<Receiver> receivers=new List<Receiver>();
    readonly Dictionary<string,float> values=new Dictionary<string,float>(),previous=new Dictionary<string,float>();
    sealed class Receiver {public Transform anchor,owner;public PetContact source;public bool wasInside;}
    Transform head,leftEye,rightEye,driven;
    Quaternion rest,written;
    bool wrote;
    Vector3 tilt;
    float started,lastHeld;
    int activeKind;
    ModelControls controls;
    public void Prepare(GameObject root,Animator rig,PetMenu menu,ModelControls owner){
        controls=owner;
        if(menu.contacts!=null)foreach(var c in menu.contacts){
            var t=string.IsNullOrEmpty(c.root)?root.transform:root.transform.Find(c.root);
            var obj=string.IsNullOrEmpty(c.owner)?root.transform:root.transform.Find(c.owner);
            if(t&&obj)receivers.Add(new Receiver{anchor=t,owner=obj,source=c});
        }
        if(!rig)return;
        Transform B(HumanBodyBones b)=>rig.GetBoneTransform(b);
        head=B(HumanBodyBones.Head);leftEye=B(HumanBodyBones.LeftEye);rightEye=B(HumanBodyBones.RightEye);
        void Add(HumanBodyBones from,HumanBodyBones to,float radius,int kind){
            var a=B(from);if(a)zones.Add(new Zone{from=a,to=B(to),driven=a,radius=radius,kind=kind});
        }
        Add(HumanBodyBones.LeftHand,HumanBodyBones.LeftMiddleDistal,.065f,13);
        Add(HumanBodyBones.RightHand,HumanBodyBones.RightMiddleDistal,.065f,13);
        Add(HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,.067f,14);
        Add(HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,.067f,14);
        Add(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,.073f,14);
        Add(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,.073f,14);
        Add(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,.085f,15);
        Add(HumanBodyBones.RightFoot,HumanBodyBones.RightToes,.085f,15);
        Add(HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,.08f,17);
        Add(HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,.08f,17);
        Add(HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,.09f,17);
        Add(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,.09f,17);
        Add(HumanBodyBones.Spine,HumanBodyBones.Neck,.15f,16);
    }
    static Vector3 Closest(Vector2 p,Vector3 a,Vector3 b){
        Vector2 ab=(Vector2)(b-a);float k=ab.sqrMagnitude<.000001f?0:Mathf.Clamp01(Vector2.Dot(p-(Vector2)a,ab)/ab.sqrMagnitude);
        return Vector3.Lerp(a,b,k);
    }
    public bool Contact(Vector2 p,bool over){
        bool active=false;values.Clear();
        foreach(var receiver in receivers){
            var c=receiver.source;var t=receiver.anchor;float scale=Mathf.Abs(t.lossyScale.x);
            float radius=c.radius*scale;var center=t.TransformPoint(c.position);
            var axis=t.rotation*c.rotation*Vector3.up*Mathf.Max(0,c.height*scale*.5f-radius);
            var point=Closest(p,center-axis,center+axis);float distance=Vector2.Distance(p,point);
            float margin=receiver.wasInside?Mathf.Clamp(radius*.18f,.008f,.032f):0;
            bool inside=over&&receiver.owner.gameObject.activeInHierarchy&&distance<radius+.026f+margin;
            if(inside&&leftEye&&rightEye){
                // A rear receiver must not activate through the visible face.
                var face=(leftEye.position+rightEye.position)*.5f;
                if(Vector2.Distance(p,face)<Vector3.Distance(leftEye.position,rightEye.position)&&point.z-radius>face.z+.045f+margin)inside=false;
            }
            float value=0;
            if(inside){
                active=true;
                if(c.mode=="OnEnter")value=receiver.wasInside?0:1;
                else if(c.mode=="Proximity")value=Mathf.Clamp01(1-distance/(radius+.026f));
                else value=1;
            }
            receiver.wasInside=inside;
            values.TryGetValue(c.parameter,out var prior);values[c.parameter]=Mathf.Max(value,prior);
        }
        foreach(var entry in values){
            previous.TryGetValue(entry.Key,out var old);
            if(Mathf.Abs(old-entry.Value)>.0001f){controls.SetParameter(entry.Key,entry.Value);previous[entry.Key]=entry.Value;}
        }
        return active;
    }
    public int Press(Vector2 p){
        Transform bone=null;int kind=0;float side=1;
        if(head&&leftEye&&rightEye){
            var eyes=(leftEye.position+rightEye.position)*.5f;
            float span=Mathf.Clamp(Vector3.Distance(leftEye.position,rightEye.position),.05f,.21f);
            var delta=p-(Vector2)eyes;
            if(Mathf.Abs(delta.x)<span*1.05f && delta.y> -span*.95f && delta.y<span*1.63f){
                bone=head;kind=delta.y<span*.13f?12:11;side=delta.x>=0?1:-1;
            }
        }
        if(!bone){
            float best=float.PositiveInfinity;
            foreach(var zone in zones){
                var q=Closest(p,zone.from.position,zone.to?zone.to.position:zone.from.position);
                float d=Vector2.Distance(p,q)/zone.radius;if(d>1)continue;
                float score=d+q.z*.56f+(zone.kind==16?.18f:0);
                if(score>=best)continue;best=score;bone=zone.driven;kind=zone.kind;
                side=head&&q.x<head.position.x?-1:1;
            }
        }
        if(!bone)return 0;
        driven=bone;started=lastHeld=Time.unscaledTime;activeKind=kind;
        switch(kind){
            case 11:tilt=new Vector3(9,0,-side*3);break;
            case 12:tilt=new Vector3(-2,side*8,side*8);break;
            case 13:tilt=new Vector3(-13,side*6,side*10);break;
            case 14:tilt=new Vector3(-6,0,side*8);break;
            case 15:tilt=new Vector3(-12,side*5,0);break;
            case 17:tilt=new Vector3(-6,0,side*5);break;
            default:tilt=new Vector3(-3,side*3,side*4);break;
        }
        return kind;
    }
    public void Restore(){
        if(wrote&&driven&&Quaternion.Angle(driven.localRotation,written)<.015f)driven.localRotation=rest;
        wrote=false;
    }
    public int Apply(bool held){
        if(!driven)return 0;
        float t=Time.unscaledTime-started;
        float amount=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.12f));
        if(held)lastHeld=Time.unscaledTime;
        float holdingAmount=Mathf.Lerp(1,.55f,Mathf.Clamp01((lastHeld-started-.12f)/.30f));
        float releasedFor=Time.unscaledTime-Mathf.Max(lastHeld,started+.12f);
        amount*=holdingAmount*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01(releasedFor/.37f)));
        if(amount<=0&&t>.16f){driven=null;return 0;}
        rest=driven.localRotation;driven.rotation=Quaternion.Euler(tilt*amount)*driven.rotation;written=driven.localRotation;wrote=true;
        return activeKind;
    }
}
