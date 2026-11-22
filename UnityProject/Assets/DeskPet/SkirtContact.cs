using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Turn the authored skirt panels around their waist pivots and keep their segments
// outside the animated thighs and knees. All rotations are temporary pose offsets.
public sealed class SkirtContact {
    sealed class Section { public Transform bone,tip; public Quaternion original; public Vector3 clothLine; }
    sealed class Panel {
        public Section[] sections;
        public float side;
        public Vector3 radial;
    }
    readonly List<Panel> panels=new List<Panel>();
    readonly Transform hips,left,right,leftKnee,rightKnee,leftFoot,rightFoot;
    readonly Vector3 restL,restR,restDown;
    readonly float radius;
    public int Count=>panels.Count;
    bool wrote;
    static bool Skirt(string name){
        name=name.ToLowerInvariant();
        return name.Contains("skirt")||name.Contains("スカート")||name.Contains("裙摆")||name.Contains("hem_");
    }
    public SkirtContact(GameObject root,Animator rig,PetMenu menu){
        hips=rig.GetBoneTransform(HumanBodyBones.Hips);
        left=rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg);right=rig.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        leftKnee=rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg);rightKnee=rig.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        leftFoot=rig.GetBoneTransform(HumanBodyBones.LeftFoot);rightFoot=rig.GetBoneTransform(HumanBodyBones.RightFoot);
        restDown=hips.InverseTransformDirection(Vector3.down);
        restL=hips.InverseTransformVector(leftKnee.position-left.position);restR=hips.InverseTransformVector(rightKnee.position-right.position);
        radius=Mathf.Max(.01f,Vector3.Distance(left.position,leftKnee.position)*.24f);
        var rings=new HashSet<Transform>();
        if(menu.parts!=null)foreach(var item in menu.parts){
            var bone=root.transform.Find(item.root??"");if(bone && Skirt(bone.name) && bone.childCount>=3)rings.Add(bone);
        }
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
            if(t.childCount>=3 && t.Cast<Transform>().Count(b=>Skirt(b.name))>=3)rings.Add(t);
        foreach(var ring in rings){
            var center=Vector3.zero;
            var roots=ring.Cast<Transform>().Where(t=>t.childCount>0 && (Skirt(t.name)||Skirt(ring.name))).ToArray();
            if(roots.Length<3)continue;
            foreach(var r in roots)center+=r.position;
            center/=roots.Length;
            foreach(var r in roots){
                var sections=new List<Section>();var t=r;
                for(int depth=0;depth<7 && t.childCount>0;depth++){
                    var tip=t.Cast<Transform>().FirstOrDefault(b=>Skirt(b.name))??(t.childCount==1?t.GetChild(0):null);
                    if(!tip)break;
                    if((tip.position-t.position).sqrMagnitude>.000001f)sections.Add(new Section{bone=t,tip=tip});
                    t=tip;
                }
                if(sections.Count==0)continue;
                Vector3 radial=Vector3.ProjectOnPlane(t.position-center,Vector3.up);
                if(radial.sqrMagnitude<.0001f)radial=Vector3.ProjectOnPlane(r.position-center,Vector3.up);
                if(radial.sqrMagnitude<.00001f)continue;
                float side=Mathf.Clamp01(.5f+Vector3.Dot(t.position-center,right.position-left.position)/Mathf.Max(.001f,(right.position-left.position).sqrMagnitude));
                panels.Add(new Panel{sections=sections.ToArray(),side=side,radial=hips.InverseTransformDirection(radial.normalized)});
            }
        }
    }
    static Vector3 Collision(Vector3 p,Vector3 thigh,Vector3 knee,float radius,Vector3 outward){
        var axis=knee-thigh;float along=Mathf.Clamp01(Vector3.Dot(p-thigh,axis)/Mathf.Max(.00001f,axis.sqrMagnitude));
        var d=p-(thigh+axis*along);float r=Mathf.Lerp(radius*1.18f,radius*.92f,along);
        // A panel that already crossed the central axis must be pushed back to its own side.
        var dir=Vector3.ProjectOnPlane(outward,axis);if(dir.sqrMagnitude<.001f)dir=outward;dir.Normalize();
        if(d.sqrMagnitude>=r*r && (Vector3.Dot(d,dir)>=0 || d.sqrMagnitude>r*r*1.8f))return Vector3.zero;
        return dir*Mathf.Max(0,r-Vector3.Dot(d,dir));
    }
    public void Apply(float ground){
        if(panels.Count==0)return;
        wrote=true;
        Vector3 baseL=hips.TransformVector(restL),baseR=hips.TransformVector(restR);
        var l=leftKnee.position-left.position;var r=rightKnee.position-right.position;
        var swingL=Quaternion.FromToRotation(baseL,l);var swingR=Quaternion.FromToRotation(baseR,r);
        float tilt=Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,82,Vector3.Angle(hips.TransformDirection(restDown),Vector3.down)));
        float fold=Mathf.Clamp01(Mathf.Max(Vector3.Angle(l,leftFoot.position-leftKnee.position),Vector3.Angle(r,rightFoot.position-rightKnee.position))/100);
        float clearance=radius*Mathf.Lerp(1,1.34f,fold);
        foreach(var panel in panels){
            foreach(var seg in panel.sections)seg.original=seg.bone.localRotation;
            var outwards=hips.TransformDirection(panel.radial).normalized;
            // The forward panel follows a rising knee; the rear panel still hangs behind it.
            float wL=Mathf.Clamp01(Vector3.Dot(l-baseL,outwards)/(radius*1.25f));
            float wR=Mathf.Clamp01(Vector3.Dot(r-baseR,outwards)/(radius*1.25f));
            float wl=wL*(1-panel.side),wr=wR*panel.side;
            var root=panel.sections[0].bone;
            if(wl+wr>.001f){
                var swing=Quaternion.Slerp(swingL,swingR,wr/(wl+wr));
                var support=Quaternion.Slerp(Quaternion.identity,swing,Mathf.Clamp01((wl+wr)*1.16f));
                root.rotation=Quaternion.RotateTowards(Quaternion.identity,support,68)*root.rotation;
            }
            // Take all targets before moving the first link. Applying gravity to an
            // already bent child once per link folded the whole hem into the thighs.
            foreach(var seg in panel.sections)seg.clothLine=seg.tip.position-seg.bone.position;
            foreach(var seg in panel.sections){
                var line=seg.tip.position-seg.bone.position;
                float length=line.magnitude;if(length<.0001f)continue;
                // The waistband rotates with the pelvis. The loose part hangs in world gravity.
                var draped=Vector3.Slerp(seg.clothLine.normalized,Vector3.down,Mathf.Max(tilt*.42f,fold*.10f))*length;
                Vector3 Push(Vector3 p){
                    var a=Collision(p,left.position,leftKnee.position,clearance,outwards);
                    var b=Collision(p,right.position,rightKnee.position,clearance,outwards);
                    var c=Collision(p,leftKnee.position,leftFoot.position,clearance*.80f,outwards);
                    var d=Collision(p,rightKnee.position,rightFoot.position,clearance*.80f,outwards);
                    var thigh=a.sqrMagnitude>b.sqrMagnitude?a:b;var shin=c.sqrMagnitude>d.sqrMagnitude?c:d;
                    return thigh.sqrMagnitude>shin.sqrMagnitude?thigh:shin;
                }
                for(int iteration=0;iteration<2;iteration++){
                    var end=Push(seg.bone.position+draped);var middle=Push(seg.bone.position+draped*.55f)/.55f;
                    draped=(draped+(end.sqrMagnitude>middle.sqrMagnitude?end:middle)).normalized*length;
                }
                float minY=Mathf.Clamp(ground+radius*.10f-seg.bone.position.y,-length,length);
                // A deeply folded knee can support the front at waistband height.
                // It cannot flip the hem above the waist and into the upper body.
                float maxY=Mathf.Clamp(root.position.y+radius*.34f-seg.bone.position.y,minY,length);
                float height=Mathf.Clamp(draped.y,minY,maxY);
                if(Mathf.Abs(height-draped.y)>.00001f){
                    var lateral=Vector3.ProjectOnPlane(draped,Vector3.up);
                    if(lateral.sqrMagnitude<.000001f)lateral=Vector3.ProjectOnPlane(outwards,Vector3.up);
                    draped=lateral.normalized*Mathf.Sqrt(Mathf.Max(0,length*length-height*height))+Vector3.up*height;
                }
                seg.bone.rotation=Quaternion.FromToRotation(line,draped)*seg.bone.rotation;
            }
        }
    }
    public void Restore(){
        if(!wrote)return;
        foreach(var p in panels)foreach(var s in p.sections)s.bone.localRotation=s.original;
        wrote=false;
    }
}
