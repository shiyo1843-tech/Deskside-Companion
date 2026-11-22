using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

// Sample actual Mecanim clips on the avatar's own Humanoid rig. A bones-only copy lets
// wardrobe, fingers, expressions and the original Animator continue to run underneath.
public sealed class DesktopGait : IDisposable {
    readonly Transform stage;
    readonly Transform[] target=new Transform[(int)HumanBodyBones.LastBone],sample=new Transform[(int)HumanBodyBones.LastBone];
    readonly Quaternion[] saved=new Quaternion[(int)HumanBodyBones.LastBone];
    readonly Quaternion[] blendFrom=new Quaternion[(int)HumanBodyBones.LastBone];
    readonly Quaternion[] blendTarget=new Quaternion[(int)HumanBodyBones.LastBone];
    readonly Quaternion[] lastPose=new Quaternion[(int)HumanBodyBones.LastBone];
    readonly Dictionary<string,AnimationClip> library=new Dictionary<string,AnimationClip>();
    GameObject skeleton;
    SkirtContact skirt;
    PlayableGraph graph;
    AnimationPlayableOutput output;
    AnimationClipPlayable playback;
    AnimationClip selected,walk,crouch,prone,settle,carried,turnLeft,turnRight,turnClip;
    float turnStart=-1,turnFrom,turnArc;
    float bounceAt=-10;
    bool genericOffset,usedTravelClip;
    bool turnFeetReady;
    Vector3 turnLeftStart,turnRightStart,turnLeftEnd,turnRightEnd;
    Quaternion turnLeftRotation,turnRightRotation,turnLeftEndRotation,turnRightEndRotation;
    Quaternion stageRotation;
    Vector3 stagePosition,hipPosition,lastHip,blendHip;
    Vector3 previousLeft,previousRight;
    float clock,modeAt,weight,heading,lookAt,crouchHeading,proneHeading,walkStart,settleAt,contactPhase,blendAt=-1;
    int request,exiting;
    float headingSpeed,blendDuration=.22f;
    bool wrote,haveFeet,havePose,fading;
    public bool Available=>walk && skeleton;
    public AnimationClip Celebration=>Get("Celebration");
    public float Distance {get;private set;}
    public string ClipName=>selected?selected.name:"standing";
    public float LeftShoeY{get;private set;}
    public float RightShoeY{get;private set;}
    public float HeadY{get;private set;}
    public float LookAngle{get;private set;}
    public float Heading=>heading;
    public void ManualHeading(){heading=headingSpeed=crouchHeading=proneHeading=0;}
    public void Bounce(){bounceAt=clock;}
    public string ShoeTrace(){
        if(!Available)return "";
        var l=Shoe(true);var r=Shoe(false);
        return FormattableString.Invariant($",{l.x:F4},{l.y:F4},{l.z:F4},{r.x:F4},{r.y:F4},{r.z:F4}");
    }
    public string FootTrace(int serial,int mode){
        if(!Available)return "";
        var basis=Quaternion.Inverse(Bone(HumanBodyBones.Hips).rotation);
        string Rotation(HumanBodyBones b){var joint=Bone(b);var q=joint?basis*joint.rotation:Quaternion.identity;
            return FormattableString.Invariant($"{q.x:F5},{q.y:F5},{q.z:F5},{q.w:F5}");}
        var world=Bone(HumanBodyBones.LeftLowerLeg).rotation;
        return FormattableString.Invariant($"{serial},{Time.frameCount},{Time.unscaledTime:F4},{mode},{ClipName},{Rotation(HumanBodyBones.LeftLowerLeg)},{Rotation(HumanBodyBones.RightLowerLeg)},{Rotation(HumanBodyBones.LeftFoot)},{Rotation(HumanBodyBones.RightFoot)},{Rotation(HumanBodyBones.LeftToes)},{Rotation(HumanBodyBones.RightToes)},{world.x:F5},{world.y:F5},{world.z:F5},{world.w:F5}");
    }
    public float LowView{get;private set;}
    public float CenterX{get;private set;}
    public bool FinishedStep{get;private set;}
    static bool BodyBone(int i)=>i<=(int)HumanBodyBones.RightToes || i==(int)HumanBodyBones.UpperChest;
    static Transform CopyHierarchy(Transform original,Transform parent,HashSet<Transform> needed){
        var copy=new GameObject(original.name).transform;copy.SetParent(parent,false);
        copy.localPosition=original.localPosition;copy.localRotation=original.localRotation;copy.localScale=original.localScale;
        for(int i=0;i<original.childCount;i++){var child=original.GetChild(i);if(needed.Contains(child))CopyHierarchy(child,copy,needed);}
        return copy;
    }
    public DesktopGait(GameObject model,Transform pivot,PetMenu menu){
        stage=pivot;var rig=model.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.avatar && a.avatar.isValid && a.isHuman);
        if(!rig)return;
        foreach(var clip in Resources.LoadAll<AnimationClip>("DeskPetMotions"))library[clip.name]=clip;
        var originals=(rig.runtimeAnimatorController?rig.runtimeAnimatorController.animationClips:Array.Empty<AnimationClip>()).Where(c=>c && c.humanMotion).Distinct().ToArray();
        bool Named(AnimationClip c,string s)=>c.name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0;
        walk=originals.FirstOrDefault(c=>c.length>.3f && !Named(c,"proxy") && !Named(c,"crouch") && !Named(c,"back") && (Named(c,"walk_forward")||Named(c,"walk_fwd")||c.name.Equals("walk",StringComparison.OrdinalIgnoreCase)))??Get("TravelWalk");
        AnimationClip Pose(string tag,string fallback)=>originals.Where(c=>Named(c,tag)).OrderBy(c=>Named(c,"proxy")?1:0).FirstOrDefault()??Get(fallback);
        crouch=Pose("crouch_still","Crouch_Idle_Loop");prone=Pose("low_crawl_still","Crawl_Idle_Loop");
        carried=originals.FirstOrDefault(c=>c.length>.15f && (Named(c,"held_idle")||Named(c,"picked_up_idle")||Named(c,"dangling")));
        settle=Get("Settle");turnLeft=Get("TurnLeft");turnRight=Get("TurnRight");if(!walk)return;
        skirt=new SkirtContact(model,rig,menu);
        var needed=new HashSet<Transform>();
        for(int i=0;i<target.Length;i++){
            target[i]=rig.GetBoneTransform((HumanBodyBones)i);
            for(var t=target[i];t && t!=rig.transform;t=t.parent)needed.Add(t);
        }
        skeleton=CopyHierarchy(rig.transform,rig.transform.parent,needed).gameObject;skeleton.name="Desktop motion sampler";skeleton.hideFlags=HideFlags.HideAndDontSave;
        var copy=skeleton.AddComponent<Animator>();copy.avatar=rig.avatar;copy.applyRootMotion=false;copy.cullingMode=AnimatorCullingMode.AlwaysAnimate;copy.Rebind();
        for(int i=0;i<sample.Length;i++)sample[i]=copy.GetBoneTransform((HumanBodyBones)i);
        graph=PlayableGraph.Create("Desktop motion clips");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        output=AnimationPlayableOutput.Create(graph,"Body",copy);graph.Play();
        // Match the first stop pose to this rig's walking phase before switching clips.
        var contact=new Quaternion[4];float best=float.PositiveInfinity;
        if(settle){Sample(settle,0,false);for(int k=0;k<4;k++)contact[k]=sample[1+k].localRotation;}
        for(int i=0;i<=48;i++){
            Sample(walk,walk.length*i/48f,true);
            if(settle){
                float error=0;for(int k=0;k<4;k++)error+=Quaternion.Angle(contact[k],sample[1+k].localRotation);
                if(error<best){best=error;contactPhase=walk.length*i/48f;}
            }
        }
        Debug.Log("Desktop motion clips: "+walk.name+" / "+crouch?.name+" / "+prone?.name+"; skirt panels="+skirt.Count);
    }
    AnimationClip Get(string name)=>library.TryGetValue(name,out var clip)?clip:null;
    void Sample(AnimationClip clip,float t,bool loop){
        if(clip!=selected){if(playback.IsValid())graph.DestroyPlayable(playback);
            playback=AnimationClipPlayable.Create(graph,clip);playback.SetApplyFootIK(false);playback.SetSpeed(0);
            output.SetSourcePlayable(playback);selected=clip;
        }
        playback.SetTime(clip.length<.001f?0:loop?Mathf.Repeat(t,clip.length):Mathf.Min(t,clip.length));graph.Evaluate(0);
    }
    Transform Bone(HumanBodyBones bone)=>target[(int)bone];
    Quaternion TransitionRotation(int i,Quaternion pose,float t){
        // q and -q describe the same rotation. Keep the incoming clip on the
        // same arc for the whole transition when it crosses 180 degrees.
        if(Quaternion.Dot(blendTarget[i],pose)<0)pose=new Quaternion(-pose.x,-pose.y,-pose.z,-pose.w);
        blendTarget[i]=pose;
        var from=blendFrom[i];
        return new Quaternion(Mathf.Lerp(from.x,pose.x,t),Mathf.Lerp(from.y,pose.y,t),
            Mathf.Lerp(from.z,pose.z,t),Mathf.Lerp(from.w,pose.w,t)).normalized;
    }
    void HangingPose(float age){
        var across=Vector3.ProjectOnPlane(Bone(HumanBodyBones.RightUpperLeg).position-Bone(HumanBodyBones.LeftUpperLeg).position,Vector3.up).normalized;
        var ahead=Vector3.Cross(across,Vector3.up);
        float blend=Mathf.SmoothStep(0,1,age/.24f);
        float beat=Mathf.Sin(age*8.3f);
        float effort=.38f+.62f*Mathf.Exp(-age*.58f);
        void Aim(HumanBodyBones parent,HumanBodyBones child,Vector3 desired){
            var p=Bone(parent);var c=Bone(child);
            if(p && c)p.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(c.position-p.position,desired),blend)*p.rotation;
        }
        void Side(bool left){
            float sign=left?-1:1,kick=(1+beat*sign)*.5f*effort;
            var upper=left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg;
            var shin=left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg;
            var foot=left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot;
            var arm=left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;
            var elbow=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;
            var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
            var footRotation=Bone(foot).rotation;
            Aim(upper,shin,Vector3.down+ahead*(.13f+.33f*kick)+across*(sign*.07f));
            Aim(shin,foot,Vector3.down-ahead*(.26f+.69f*kick));
            Bone(foot).rotation=Quaternion.AngleAxis(-9*blend,across)*footRotation;
            Aim(arm,elbow,Vector3.down*.72f+across*(sign*(.55f+.29f*kick))+ahead*.23f);
            Aim(elbow,hand,Vector3.down*.28f+across*(sign*.24f)+ahead*(.58f+.28f*kick));
        }
        Side(true);Side(false);
    }
    Vector3 Shoe(bool left){
        var foot=Bone(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
        var toe=Bone(left?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
        return toe && toe.position.y<foot.position.y?toe.position:foot.position;
    }
    Vector3 FootGoal(bool left,float ground,Vector3 middle,Vector3 forward,float shorten,out float pelvisDrop){
        // Keep the clip's horizontal step, shoe roll and knee direction. Only adjust
        // excess vertical clearance after retargeting to the actual leg length.
        var thigh=Bone(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
        var shin=Bone(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
        var shoe=Bone(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
        float clearance=Shoe(left).y-ground;
        float upper=Vector3.Distance(thigh.position,shin.position),lower=Vector3.Distance(shin.position,shoe.position);
        float cap=(upper+lower)*.038f, desired=cap*(1-Mathf.Exp(-clearance/Mathf.Max(cap,.001f)));
        Vector3 goal=shoe.position-Vector3.up*(clearance-desired);
        // Compact the stride along the heading, keeping the natural stance width and timing.
        goal-=forward*(Vector3.Dot(shoe.position-middle,forward)*shorten);
        pelvisDrop=ReachDrop(thigh.position,goal,upper+lower);
        return goal;
    }
    static float ReachDrop(Vector3 thigh,Vector3 goal,float length){
        float reach=length*.998f,planar=Vector3.ProjectOnPlane(goal-thigh,Vector3.up).sqrMagnitude;
        return Mathf.Clamp(thigh.y-goal.y-Mathf.Sqrt(Mathf.Max(0,reach*reach-planar)),0,length*.11f);
    }
    void PlaceFoot(bool left,Vector3 goal,Vector3 forward){
        var thigh=Bone(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
        var shin=Bone(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
        var shoe=Bone(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
        if((goal-shoe.position).sqrMagnitude<.0000001f)return;
        float upper=Vector3.Distance(thigh.position,shin.position),lower=Vector3.Distance(shin.position,shoe.position);
        Vector3 line=goal-thigh.position;float length=Mathf.Clamp(line.magnitude,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
        Vector3 axis=line.normalized;
        // Find the authored bend in the source leg's plane. Projecting the knee on
        // the shortened target line made it cross that line as the leg straightened.
        var authored=Vector3.ProjectOnPlane(shin.position-thigh.position,(shoe.position-thigh.position).normalized);
        var facing=Vector3.ProjectOnPlane(forward,axis).normalized;
        var bend=Vector3.ProjectOnPlane(authored,axis);
        float flex=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.22f,bend.magnitude/upper));
        Vector3 pole=Vector3.Slerp(facing,bend.normalized,flex*Mathf.Clamp01(Vector3.Dot(facing,bend.normalized)));
        float along=(upper*upper-lower*lower+length*length)/(2*length);
        var knee=thigh.position+axis*along+pole.normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        var roll=shoe.rotation;
        thigh.rotation=Quaternion.FromToRotation(shin.position-thigh.position,knee-thigh.position)*thigh.rotation;
        shin.rotation=Quaternion.FromToRotation(shoe.position-shin.position,goal-shin.position)*shin.rotation;
        shoe.rotation=roll;
    }
    void PlaceTurnFeet(float progress,Vector3 forward){
        var left=Bone(HumanBodyBones.LeftFoot);var right=Bone(HumanBodyBones.RightFoot);
        if(!turnFeetReady){
            turnFeetReady=true;turnLeftStart=left.position;turnRightStart=right.position;
            turnLeftRotation=left.rotation;turnRightRotation=right.rotation;
        }
        float leftStep=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,.91f,progress));
        float rightStep=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.45f,progress));
        Vector3 Goal(bool isLeft,float step){
            var origin=isLeft?turnLeftStart:turnRightStart;var end=isLeft?turnLeftEnd:turnRightEnd;
            var knee=Bone(isLeft?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
            float shin=Vector3.Distance(knee.position,(isLeft?left:right).position);
            float rise=Mathf.Max(0,Shoe(isLeft).y-Shoe(!isLeft).y);
            return Vector3.Lerp(origin,end,step)+Vector3.up*(Mathf.Min(rise,shin*.14f)*4*step*(1-step));
        }
        var leftGoal=Goal(true,leftStep);var rightGoal=Goal(false,rightStep);
        float Drop(bool isLeft,Vector3 goal){
            var thigh=Bone(isLeft?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
            var knee=Bone(isLeft?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
            var foot=isLeft?left:right;
            return ReachDrop(thigh.position,goal,Vector3.Distance(thigh.position,knee.position)+Vector3.Distance(knee.position,foot.position));
        }
        Bone(HumanBodyBones.Hips).position-=Vector3.up*Mathf.Max(Drop(true,leftGoal),Drop(false,rightGoal));
        PlaceFoot(true,leftGoal,forward);PlaceFoot(false,rightGoal,forward);
        left.rotation=Quaternion.Slerp(turnLeftRotation,turnLeftEndRotation,leftStep);
        right.rotation=Quaternion.Slerp(turnRightRotation,turnRightEndRotation,rightStep);
    }
    void MoveWithoutRig(int mode,float viewWidth,float dt,bool useTravelClip){
        const float period=.67f;
        bool walk=Math.Abs(mode)==1,stop=Math.Abs(mode)==7;
        if(mode!=request){
            if(walk){walkStart=clock;FinishedStep=false;}
            if(stop){
                float phase=Mathf.Repeat(clock-walkStart,period);
                settleAt=clock+(usedTravelClip || phase<.02f?0:period-phase);FinishedStep=false;
            }
            request=mode;
        }
        if(walk)usedTravelClip=useTravelClip;
        bool traveling=walk || (stop && clock<settleAt);
        float air=0;
        if(traveling){
            float phase=Mathf.Repeat((clock-walkStart)/period,1);
            air=usedTravelClip?0:Mathf.Sin(Mathf.PI*Mathf.InverseLerp(.08f,.9f,phase));
            Distance=dt*(usedTravelClip?.33f:.56f*air)/Mathf.Max(.01f,viewWidth);
        }
        if(traveling || stop){
            float target=Mathf.DeltaAngle(stage.eulerAngles.y,traveling?-Mathf.Sign(mode)*48:0);
            heading=Mathf.SmoothDampAngle(heading,target,ref headingSpeed,.16f,240,dt);
            if(stop && !traveling && Mathf.Abs(Mathf.DeltaAngle(heading,target))<.2f){heading=target;headingSpeed=0;FinishedStep=true;}
        }
        float t=Mathf.Clamp01((clock-bounceAt)/.58f);
        float pop=t<1?Mathf.Sin(Mathf.PI*t):0;
        stageRotation=stage.rotation;stagePosition=stage.position;genericOffset=true;
        stage.rotation=Quaternion.AngleAxis(-4f*air*Mathf.Sign(mode),Vector3.forward)*Quaternion.AngleAxis(heading,Vector3.up)*stageRotation;
        stage.position+=Vector3.up*(.095f*air+.12f*pop);
    }
    public void Apply(int mode,float viewWidth,bool useTravelClip=false){
        Distance=0;LookAngle=0;
        float dt=Mathf.Min(Time.unscaledDeltaTime,.08f);clock+=dt;
        if(!Available){MoveWithoutRig(mode,viewWidth,dt,useTravelClip);return;}
        LowView=Mathf.MoveTowards(LowView,mode==5?1:0,dt*(mode==9?4.5f:.85f));
        if(mode!=request){
            turnStart=-1;turnFeetReady=false;
            exiting=mode==0 && (request==4 || request==5)?request:0;
            if(Math.Abs(mode)==1){walkStart=clock;settleAt=-1;FinishedStep=false;}
            if(Math.Abs(mode)==7){
                float phase=Mathf.Repeat((clock-walkStart)*.90f,walk.length);
                settleAt=clock+Mathf.Repeat(contactPhase-phase,walk.length)/.90f;
                FinishedStep=false;
            }
            if(mode==4)crouchHeading=Mathf.DeltaAngle(stage.eulerAngles.y,0);
            if(mode==5)proneHeading=Mathf.DeltaAngle(stage.eulerAngles.y,42);
            request=mode;modeAt=clock;haveFeet=false;
            if(mode==5)lookAt=clock+2;
        }
        bool landing=Math.Abs(mode)==7;
        bool walking=Math.Abs(mode)==1 || landing;
        bool turning=false;float turnProgress=0;
        float age=clock-modeAt;
        AnimationClip clip=null;bool loop=true;float time=age;
        if(walking){
            clip=walk;time=(clock-walkStart)*.90f;
            if(landing && clock>=settleAt){
                clip=settle;loop=false;time=clock-settleAt;
                if(!settle || time>=settle.length){
                    if(turnStart<0){
                        turnStart=clock;turnFrom=heading;
                        turnArc=Mathf.DeltaAngle(stage.eulerAngles.y+heading,0);
                        turnClip=turnArc>=0?turnRight:turnLeft;
                        var front=Quaternion.AngleAxis(turnFrom+turnArc,Vector3.up);
                        var left=Bone(HumanBodyBones.LeftFoot);var right=Bone(HumanBodyBones.RightFoot);
                        turnLeftEnd=stage.position+front*(left.position-stage.position);turnRightEnd=stage.position+front*(right.position-stage.position);
                        turnLeftEndRotation=front*left.rotation;turnRightEndRotation=front*right.rotation;
                    }
                    turning=true;walking=false;clip=turnClip?turnClip:settle;
                    float duration=turnClip?turnClip.length:.7f;
                    turnProgress=Mathf.Clamp01((clock-turnStart)/Mathf.Max(.01f,duration));
                    time=turnClip?turnProgress*duration:settle?settle.length:0;
                    heading=turnFrom+turnArc*Mathf.SmoothStep(0,1,turnProgress);headingSpeed=0;
                    FinishedStep=turnProgress>=1;
                }
            }
        }
        else if(mode==4 || mode==5){
            var enter=Get(mode==4?"Crouch_Enter":"Crawl_Enter");
            if(enter && age<enter.length){clip=enter;loop=false;}
            else{clip=mode==4?crouch:prone;time-=enter?enter.length:0;}
        }else if(exiting!=0){
            clip=Get(exiting==4?"Crouch_Exit":"Crawl_Exit");loop=false;
            if(!clip || age>=clip.length){exiting=0;clip=null;}
        }else if(mode==2)clip=Get("Idle_LookAround_Loop");
        else if(mode==8)clip=Celebration;
        else if(mode==9){clip=carried?carried:Get("Idle_LookAround_Loop");time=age;}
        weight=Mathf.MoveTowards(weight,clip?1:0,dt*(clip?3.7f:2.15f));
        if(!clip && weight<=0){
            havePose=false;fading=false;
            haveFeet=false;
            if(Mathf.Abs(Mathf.DeltaAngle(0,heading))>.001f){stageRotation=stage.rotation;stage.rotation=Quaternion.AngleAxis(heading,Vector3.up)*stageRotation;stageOnly=true;}
            return;
        }
        if(havePose && ((clip && (clip!=selected || fading)) || (!clip && !fading))){
            Array.Copy(lastPose,blendFrom,lastPose.Length);blendHip=lastHip;blendAt=clock;
            Array.Copy(lastPose,blendTarget,lastPose.Length);
            blendDuration=(mode==4 || mode==5 || exiting!=0 || (!clip && !walking))?.46f:.24f;
        }
        fading=!clip;
        if(clip)Sample(clip,time,loop);
        if(!selected || weight<=0)return;
        var hips=Bone(HumanBodyBones.Hips);
        // Keep a single visible heading across clips authored in different FBX coordinate systems.
        float desired=walking?Mathf.DeltaAngle(stage.eulerAngles.y,Mathf.Sign(mode)*-71):mode==5?proneHeading:mode==4?crouchHeading:heading;
        if(!turning)heading=Mathf.SmoothDampAngle(heading,desired,ref headingSpeed,.16f,260,dt);
        wrote=true;stagePosition=stage.position;stageRotation=stage.rotation;hipPosition=hips.localPosition;
        Vector3 HipAxis()=>Vector3.ProjectOnPlane(Bone(HumanBodyBones.RightUpperLeg).position-Bone(HumanBodyBones.LeftUpperLeg).position,Vector3.up);
        var neutralAxis=HipAxis();
        var neck=Bone(HumanBodyBones.Neck)??Bone(HumanBodyBones.Head);
        float collarY=neck.position.y;
        for(int i=0;i<target.Length;i++)if(BodyBone(i) && target[i] && sample[i])saved[i]=target[i].localRotation;
        float floor=Mathf.Min(Shoe(true).y,Shoe(false).y);
        stage.rotation=Quaternion.AngleAxis(heading,Vector3.up)*stageRotation;
        float transition=Mathf.SmoothStep(0,1,Mathf.Clamp01((clock-blendAt)/blendDuration));
        for(int i=0;i<target.Length;i++)if(BodyBone(i) && target[i] && sample[i]){
            bool leg=i>=(int)HumanBodyBones.LeftUpperLeg && i<=(int)HumanBodyBones.RightFoot;
            float strideWeight=walking && leg?(clip==walk?.79f:Mathf.Lerp(.79f,1,Mathf.Clamp01(time/.28f))):1;
            if(mode==9)strideWeight=leg?.38f:.53f;
            var pose=clip?Quaternion.Slerp(saved[i],sample[i].localRotation,weight*strideWeight):saved[i];
            target[i].localRotation=havePose && transition<1?TransitionRotation(i,pose,transition):pose;
        }
        var hip=clip?Vector3.Lerp(hipPosition,sample[(int)HumanBodyBones.Hips].localPosition,weight):hipPosition;
        hips.localPosition=havePose && transition<1?Vector3.Lerp(blendHip,hip,transition):hip;
        var posedAxis=HipAxis();
        if(posedAxis.sqrMagnitude>.0001f && neutralAxis.sqrMagnitude>.0001f){
            float align=Vector3.SignedAngle(posedAxis,Quaternion.AngleAxis(heading,Vector3.up)*neutralAxis,Vector3.up);
            stage.rotation=Quaternion.AngleAxis(align,Vector3.up)*stage.rotation;
        }
        if(walking){
            float stance=Mathf.Min(Shoe(true).y,Shoe(false).y);
            var middle=(Bone(HumanBodyBones.LeftFoot).position+Bone(HumanBodyBones.RightFoot).position)*.5f;
            var travelAxis=Vector3.ProjectOnPlane(Quaternion.AngleAxis(heading,Vector3.up)*stageRotation*Vector3.back,Vector3.up).normalized;
            float shorten=.32f*weight;
            if(landing && clip==settle)shorten*=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(time/settle.length));
            var leftGoal=FootGoal(true,stance,middle,travelAxis,shorten,out var leftDrop);
            var rightGoal=FootGoal(false,stance,middle,travelAxis,shorten,out var rightDrop);
            hips.position-=Vector3.up*Mathf.Max(leftDrop,rightDrop);
            PlaceFoot(true,leftGoal,travelAxis);PlaceFoot(false,rightGoal,travelAxis);
        }
        if(mode==9 && !carried)HangingPose(age);
        var l=Shoe(true);var r=Shoe(false);
        float lowest=Mathf.Min(l.y,r.y);
        if(mode==5 || exiting==5){
            foreach(var b in GroundBones){var t=Bone(b);if(t)lowest=Mathf.Min(lowest,t.position.y);}
        }
        stage.position+=Vector3.up*(mode==9?Mathf.Lerp(floor-lowest,collarY-neck.position.y,Mathf.SmoothStep(0,1,age/.24f)):floor-lowest);
        if(turning){
            // Both source clips step with the right foot first. Anchor the support
            // foot in world space so adding character yaw cannot drag it sideways.
            PlaceTurnFeet(turnProgress,Quaternion.AngleAxis(heading,Vector3.up)*stageRotation*Vector3.back);
        }
        if(mode==5 && clock>lookAt){
            // Turn around the upright world axis; a prone neck's local yaw axis points at the floor.
            float cycle=Mathf.Repeat(clock-lookAt,13f);
            LookAngle=cycle<5?27*Mathf.Sin(cycle*Mathf.PI/2.5f)*Mathf.Min(1,cycle/.55f,(5-cycle)/.55f):0;
            var head=Bone(HumanBodyBones.Head);
            if(neck!=head)neck.rotation=Quaternion.AngleAxis(LookAngle*.32f,Vector3.up)*neck.rotation;
            if(head)head.rotation=Quaternion.AngleAxis(LookAngle*(neck!=head?.68f:1f),Vector3.up)*head.rotation;
        }
        skirt.Apply(floor);
        for(int i=0;i<target.Length;i++)if(BodyBone(i) && target[i])lastPose[i]=target[i].localRotation;
        lastHip=hips.localPosition;havePose=true;
        LeftShoeY=Shoe(true).y;RightShoeY=Shoe(false).y;HeadY=Bone(HumanBodyBones.Head).position.y;
        CenterX=(Bone(HumanBodyBones.Head).position.x+hips.position.x+(l.x+r.x)*.5f)/3;
        if(walking && Mathf.Abs(Mathf.DeltaAngle(heading,desired))<8 && weight>.8f){
            // Drive the window from the stance foot's observed backwards travel, in screen widths.
            if(haveFeet){
                float direction=Mathf.Sign(mode);
                float dxL=(previousLeft.x-l.x)*direction,dxR=(previousRight.x-r.x)*direction;
                float back=Mathf.Abs(l.y-r.y)<.025f?Mathf.Max(dxL,dxR):l.y<r.y?dxL:dxR;
                Distance=Mathf.Clamp(back,0,dt*2.5f)/Mathf.Max(.01f,viewWidth);
            }
            haveFeet=true;previousLeft=l;previousRight=r;
        }else haveFeet=false;
    }
    static readonly HumanBodyBones[] GroundBones={HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg};
    bool stageOnly;
    public void Restore(){
        skirt?.Restore();
        if(genericOffset){stage.SetPositionAndRotation(stagePosition,stageRotation);genericOffset=false;}
        if(stageOnly){stage.rotation=stageRotation;stageOnly=false;}
        if(!wrote)return;
        for(int i=0;i<target.Length;i++)if(BodyBone(i) && target[i] && sample[i])target[i].localRotation=saved[i];
        Bone(HumanBodyBones.Hips).localPosition=hipPosition;stage.rotation=stageRotation;stage.position=stagePosition;wrote=false;
    }
    public void Dispose(){if(graph.IsValid())graph.Destroy();if(skeleton)Object.Destroy(skeleton);}
}
