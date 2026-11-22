using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10;

// Apply local touch after the avatar animator and VRM spring bones, just before the portrait is drawn.
public sealed class PointerTouch : MonoBehaviour
{
    sealed class Joint
    {
        public Transform bone, tip;
        public Joint parent;
        public float radius, pull, spring;
        public bool grab, collide, wrote, isHair;
        public string parameter;
        public Vector3 endpoint;
        public Vector3 Tip=>tip?tip.position:bone.TransformPoint(endpoint);
        public Quaternion original, written;
        public Vector3 displacement, velocity, target;
    }
    sealed class Lid
    {
        public SkinnedMeshRenderer skin;
        public int index;
        public float weight, original, written;
        public bool wrote;
    }
    readonly List<Joint> joints=new List<Joint>();
    readonly List<Lid> lids=new List<Lid>();
    readonly Dictionary<Transform,Joint> indexed=new Dictionary<Transform,Joint>();
    readonly HumanoidTouch body=new HumanoidTouch();
    ModelControls controls;
    Transform leftEye,rightEye,head,neck;
    Joint grabbed;
    Vector2 origin,previous;
    bool previousOver,eyeHeld;
    float blinkUntil,closure,pressedAt;
    string grabParameter;
    int lastKind;
    public int Kind {get;private set;}
    public int Strength {get;private set;}
    public bool BodyPressed {get;private set;}
    static bool HairName(string n){
        n=n.ToLowerInvariant();
        return n.Contains("hair")||n.Contains("ahoge")||n.Contains("bang")||n.Contains("ponytail")||n.Contains("twintail")||n.Contains("髪")||n.Contains("头发")||n.Contains("머리");
    }
    public void Prepare(GameObject model,ModelControls owner){
        controls=owner;var menu=owner.Menu;
        var rig=model.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.isHuman&&a.avatar&&a.avatar.isValid);
        body.Prepare(model,rig,menu,owner);
        if(rig){leftEye=rig.GetBoneTransform(HumanBodyBones.LeftEye);rightEye=rig.GetBoneTransform(HumanBodyBones.RightEye);head=rig.GetBoneTransform(HumanBodyBones.Head);neck=rig.GetBoneTransform(HumanBodyBones.Neck);}
        var root=model.transform;
        if(!string.IsNullOrEmpty(menu.eyeMesh)&&!string.IsNullOrEmpty(menu.blinkShape)){
            var skin=root.Find(menu.eyeMesh)?.GetComponent<SkinnedMeshRenderer>();
            if(skin&&skin.sharedMesh)AddLid(skin,skin.sharedMesh.GetBlendShapeIndex(menu.blinkShape),100);
        }
        var vrm=model.GetComponentInChildren<Vrm10Instance>(true);
        if(lids.Count==0 && vrm && vrm.Vrm && vrm.Vrm.Expression.Blink){
            foreach(var bind in vrm.Vrm.Expression.Blink.MorphTargetBindings){
                var skin=vrm.transform.Find(bind.RelativePath)?.GetComponent<SkinnedMeshRenderer>();
                AddLid(skin,bind.Index,100*bind.Weight);
            }
        }
        if(lids.Count==0){
            string[] names={"vrc.blink","eye_closed_blink","eye_closed","eye_close","blink","まばたき","auto_blink"};
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                if(!skin.sharedMesh)continue;
                foreach(var name in names){int index=skin.sharedMesh.GetBlendShapeIndex(name);if(index>=0){AddLid(skin,index,100);break;}}
            }
        }
        if(menu.hair!=null || menu.parts!=null){
            foreach(var h in (menu.hair??new List<HairChain>()).Concat(menu.parts??new List<HairChain>())){
                var start=string.IsNullOrEmpty(h.root)?root:root.Find(h.root);if(!start||(!h.grab&&!h.collision))continue;
                var ignored=new HashSet<Transform>((h.ignore??Array.Empty<string>()).Select(root.Find).Where(t=>t));
                AddBranch(start,null,ignored,h,menu.hair!=null&&menu.hair.Contains(h));
            }
        }else{
            // Older bundles and VRMs can supply spring joints or named hair bones directly.
            var scope=head?head:root;
            foreach(var t in scope.GetComponentsInChildren<Transform>(true)){
                bool hair=HairName(t.name);
                if(indexed.ContainsKey(t))continue;
                var spring=t.GetComponent<VRM10SpringBoneJoint>();
                if(!hair&&!spring)continue;
                var h=new HairChain{radius=spring?spring.m_jointRadius:.015f,pull=.32f,spring=.30f,grab=true,collision=true};
                AddBranch(t,null,new HashSet<Transform>(),h,hair);
            }
            foreach(var spring in root.GetComponentsInChildren<VRM10SpringBoneJoint>(true)){
                if(indexed.ContainsKey(spring.transform)||spring.transform.IsChildOf(scope))continue;
                AddBranch(spring.transform,null,new HashSet<Transform>(),new HairChain{radius=spring.m_jointRadius,pull=.32f,spring=.3f,grab=true,collision=true},false);
            }
        }
    }
    void AddLid(SkinnedMeshRenderer skin,int index,float weight){
        if(skin&&skin.sharedMesh&&index>=0&&index<skin.sharedMesh.blendShapeCount)lids.Add(new Lid{skin=skin,index=index,weight=weight});
    }
    void AddBranch(Transform bone,Joint parent,HashSet<Transform> ignored,HairChain s,bool isHair){
        if(!bone||ignored.Contains(bone))return;
        Joint next=parent;
        if(bone.childCount==1 || (bone.childCount==0&&s.endpoint.sqrMagnitude>.000001f)){
            var child=bone.childCount>0?bone.GetChild(0):null;
            if(!child||(!ignored.Contains(child)&&Vector3.Distance(child.position,bone.position)>.0001f)){
                if(!indexed.TryGetValue(bone,out next)){
                    next=new Joint{bone=bone,tip=child,endpoint=s.endpoint,parent=parent,radius=s.radius,pull=s.pull,spring=s.spring,grab=s.grab,collide=s.collision,isHair=isHair,parameter=s.parameter};
                    indexed.Add(bone,next);joints.Add(next);
                }
            }
        }
        for(int i=0;i<bone.childCount;i++)AddBranch(bone.GetChild(i),next,ignored,s,isHair);
    }
    // Release only offsets written by this component so authored animation continues underneath.
    void Update(){
        body.Restore();
        foreach(var j in joints)if(j.wrote){if(Quaternion.Angle(j.bone.localRotation,j.written)<.015f)j.bone.localRotation=j.original;j.wrote=false;}
        foreach(var l in lids)if(l.wrote){if(Mathf.Abs(l.skin.GetBlendShapeWeight(l.index)-l.written)<.01f)l.skin.SetBlendShapeWeight(l.index,l.original);l.wrote=false;}
    }
    static Vector2 Flat(Vector3 p)=>new Vector2(p.x,p.y);
    static Vector2 WorldXY(Camera cam,Vector2 uv)=>Flat(cam.ViewportToWorldPoint(new Vector3(uv.x,uv.y,5)));
    bool HitEye(Vector2 p,Camera camera){
        if(lids.Count==0||!leftEye||!rightEye)return false;
        var mid=(leftEye.position+rightEye.position)*.5f;
        if(head){
            var front=mid-head.position;
            if(neck)front=Vector3.ProjectOnPlane(front,(head.position-neck.position).normalized);
            if(front.sqrMagnitude>.00001f&&Vector3.Dot(front.normalized,camera.transform.forward)>.28f)return false;
        }
        float r=Mathf.Clamp(Vector3.Distance(leftEye.position,rightEye.position)*.42f,.025f,.085f);
        foreach(var eye in new[]{leftEye,rightEye}){
            var d=(p-Flat(eye.position))/r;
            if(d.x*d.x+d.y*d.y*1.35f<1)return true;
        }
        return false;
    }
    static Vector3 Closest(Joint j,Vector2 p){
        var a=j.bone.position;var b=j.Tip;var ab=Flat(b-a);
        float ratio=ab.sqrMagnitude>.000001f?Mathf.Clamp01(Vector2.Dot(p-Flat(a),ab)/ab.sqrMagnitude):0;
        return Vector3.Lerp(a,b,ratio);
    }
    float Radius(Joint j)=>Mathf.Clamp(j.radius*Mathf.Abs(j.bone.lossyScale.x)+.032f,.035f,.12f);
    Joint PickJoint(Vector2 p,bool forGrab){
        Joint pick=null;float best=float.PositiveInfinity;
        foreach(var j in joints){
            if(!j.bone.gameObject.activeInHierarchy||(forGrab?!j.grab:!j.collide))continue;
            var hit=Closest(j,p);float distance=Vector2.Distance(p,Flat(hit));float radius=Radius(j);
            if(distance>radius)continue;
            if(leftEye&&rightEye){
                var face=(leftEye.position+rightEye.position)*.5f;
                float span=Vector3.Distance(leftEye.position,rightEye.position);
                var delta=p-Flat(face);
                if(Mathf.Abs(delta.x)<span*.84f&&Mathf.Abs(delta.y)<span*.78f&&hit.z>face.z+.02f)continue;
            }
            float score=distance/radius+hit.z*.22f;if(score<best){best=score;pick=j;}
        }
        return pick;
    }
    void Push(Joint j,Vector3 offset){
        for(int i=0;j!=null&&i<5;i++,j=j.parent){
            if(offset.sqrMagnitude>j.target.sqrMagnitude)j.target=offset;
            offset*=.62f;
        }
    }
    public void Simulate(Camera cam,Vector2 down,Vector2 current,bool began,bool pressed,bool over,float carriedBlink,float carrySway){
        BodyPressed=false;Kind=0;Strength=0;
        var p=WorldXY(cam,current);float dt=Mathf.Clamp(Time.unscaledDeltaTime,.001f,.065f);
        bool native=body.Contact(began?WorldXY(cam,down):p,over||pressed||began);
        if(began){
            pressedAt=Time.unscaledTime;origin=WorldXY(cam,down);grabbed=null;eyeHeld=HitEye(origin,cam);
            if(eyeHeld){lastKind=2;blinkUntil=Time.unscaledTime+.23f;}
            else if((grabbed=PickJoint(origin,true))!=null){lastKind=grabbed.isHair?3:6;grabbed.velocity+=new Vector3(.035f,0,-.06f);}
            else if(native)lastKind=8;
            else{lastKind=body.Press(origin);if(lastKind==0){lastKind=1;BodyPressed=true;}}
        }
        foreach(var j in joints)j.target=j.isHair?Vector3.left*(carrySway*.002f):Vector3.zero;
        bool grip=grabbed!=null&&pressed&&Time.unscaledTime-pressedAt>=.22f;
        string requested=grip?grabbed.parameter:null;
        if(requested!=grabParameter){
            if(!string.IsNullOrEmpty(grabParameter))controls.SetParameter(grabParameter+"_IsGrabbed",0);
            if(!string.IsNullOrEmpty(requested))controls.SetParameter(requested+"_IsGrabbed",1);
            grabParameter=requested;
        }
        if(grabbed!=null&&pressed){
            if(grip){
                var delta=Vector2.ClampMagnitude(p-origin,.32f);Push(grabbed,new Vector3(delta.x,delta.y,-Mathf.Min(.045f,delta.magnitude*.20f)));Kind=grabbed.isHair?3:6;
            }else Kind=5;
        }else{
            grabbed=null;
            if(over&&previousOver&&!pressed){
                // Compare both cursor positions through this frame's camera. A low
                // camera rising with the avatar must never become a hair stroke.
                var oldPoint=WorldXY(cam,previous);
                var movement=p-oldPoint;
                // Treat the moving cursor as a small finger, including a sweep between render frames.
                if(movement.sqrMagnitude>.000001f && movement.sqrMagnitude<.32f*.32f){
                    int steps=Mathf.Clamp(Mathf.CeilToInt(movement.magnitude/.028f),1,9);
                    for(int i=1;i<=steps;i++){
                        var at=Vector2.Lerp(oldPoint,p,(float)i/steps);var joint=PickJoint(at,false);
                        if(joint==null||HitEye(at,cam))continue;
                        var n=Flat(Closest(joint,at))-at;
                        var shove=Vector2.ClampMagnitude(movement*2.4f+n.normalized*.035f,.18f);
                        Push(joint,new Vector3(shove.x,shove.y,.016f));joint.velocity+=new Vector3(shove.x,shove.y,0)*.32f;Kind=4;
                    }
                }
            }
        }
        if(!pressed)eyeHeld=false;
        float target=eyeHeld||Time.unscaledTime<blinkUntil?1:carriedBlink;
        closure=Mathf.MoveTowards(closure,target,dt*(target>0?22f:6.2f));
        if(closure>.001f){
            Kind=2;Strength=Mathf.RoundToInt(closure*1000);
            foreach(var l in lids){
                l.original=l.skin.GetBlendShapeWeight(l.index);l.written=Mathf.Lerp(l.original,l.weight,closure);
                l.skin.SetBlendShapeWeight(l.index,l.written);l.wrote=true;
            }
        }
        foreach(var j in joints){
            if(j.displacement.sqrMagnitude+j.velocity.sqrMagnitude+j.target.sqrMagnitude<.0000001f)continue;
            float k=105+Mathf.Clamp01(j.pull)*165,damping=2*Mathf.Sqrt(k)*Mathf.Lerp(.88f,.39f,Mathf.Clamp01(j.spring));
            int count=Mathf.CeilToInt(dt*120);float step=dt/count;
            for(int i=0;i<count;i++){j.velocity+=((j.target-j.displacement)*k-j.velocity*damping)*step;j.displacement+=j.velocity*step;}
            var rest=j.Tip-j.bone.position;var desired=rest+j.displacement;
            if(rest.sqrMagnitude<.0000001f||desired.sqrMagnitude<.0000001f)continue;
            j.original=j.bone.localRotation;
            var rotation=Quaternion.RotateTowards(Quaternion.identity,Quaternion.FromToRotation(rest,desired),42);
            j.bone.rotation=rotation*j.bone.rotation;j.written=j.bone.localRotation;j.wrote=true;
            Strength=Mathf.Max(Strength,Mathf.RoundToInt(1000*Quaternion.Angle(Quaternion.identity,rotation)/42));
        }
        int bodyKind=body.Apply(pressed && !native && grabbed==null && !eyeHeld);
        if(Kind==0)Kind=native?8:bodyKind;
        if(began&&Kind==0)Kind=lastKind;
        previous=current;previousOver=over;
    }
}
