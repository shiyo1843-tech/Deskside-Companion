using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(10000)]
public class PetRenderer : MonoBehaviour
{
    // Same fixed layout as the native host. Only the renderer writes the frame counter.
    const int Header = 64;
    static readonly int[] Widths={720,1080,1440,2160};
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint hi, uint lo, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr ptr);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [Serializable] class Command { public int serial,lightPercent; public string path; }
    [Serializable] class Reply { public int serial; public bool ok; public string error;public PetMenu menu; }
    Camera cam;
    Light keyLight,fillLight;
    bool toonLighting;
    int lightPercent;
    RenderTexture rt,resolved;
    Texture2D capture;
    byte[] pixels;
    IntPtr map, view;
    int frame, tap, angle, lastYawCommand=int.MinValue, lastSerial, loadedSerial, quality=-1;
    int pendingReads, submitted, published, viewport=-1;
    DesktopGait gait;
    int lastActivity;
    bool waving;
    const int Metrics=64+2160*2640*4;
    int travel;
    string traceFile;
    StreamWriter footTrace;
    float traceAt;
    float portraitY=.47f;
    string channel;
    string loadedPath;
    bool loading;
    float nextPoll;
    Process parent;
    GameObject actor;
    PetMotion motion;
    ModelControls controls;
    PointerTouch touch;
    Vrm10Instance vrmActor;
    int lastChoice;
    AssetBundle bundle;
    Transform pivot;
    Transform framedHead,leftEye,rightEye;
    float headClearance,portraitSpan;
    bool carrying,offsetForCarry;
    float lift,liftSpeed,swing,swingSpeed,lastCarryX,carryTime;
    Vector3 beforeLiftPosition;
    Quaternion beforeLiftRotation;
    static string Arg(string key) { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length?args[i+1]:null; }

    void Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        channel = Arg("-petSession");
        traceFile=Arg("-petMotionTrace");
        string footLog=Arg("-petFootTrace");if(footLog!=null)footTrace=new StreamWriter(footLog);
        if (channel != null)
        {
            map = OpenFileMapping(0x0006, false, "DeskPet-"+Path.GetFileName(channel));
            view = MapViewOfFile(map, 0x0006, 0, 0, UIntPtr.Zero);
            if (view==IntPtr.Zero) { Debug.LogError("共享画布无法连接"); Application.Quit(2); return; }
            parent=Process.GetProcessById(int.Parse(Arg("-petOwner")));
        }
        cam = new GameObject("Portrait camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0,0,-5);
        cam.orthographic = true; cam.orthographicSize = 1.18f;
        cam.aspect=360f/440;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0,0,0,0);
        cam.allowHDR=false; cam.allowMSAA=false; cam.enabled = channel==null;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        keyLight=LightAt(new Vector3(24,-25,0),new Color(1,.96f,.92f));
        fillLight=LightAt(new Vector3(25,150,0),new Color(.83f,.89f,1));
        SetLighting(100);
        pivot = new GameObject("Portrait").transform;
        if (channel==null)
        {
            var demo=Path.Combine(Application.streamingAssetsPath,"云团.glb");
            if(File.Exists(demo)) Load(new Command {path=demo});
            return;
        }
        StartCoroutine(CaptureFrames());
    }

    Light LightAt(Vector3 rotation,Color color)
    {
        var light = new GameObject("Studio light").AddComponent<Light>();
        light.type=LightType.Directional;light.color=color;
        light.transform.rotation=Quaternion.Euler(rotation);return light;
    }
    void SetLighting(int value){
        lightPercent=value>0?Mathf.Clamp(value,50,150):100;
        float scale=lightPercent*.01f;
        // MToon adds ambient and direct light to an already painted albedo.
        // Leave highlight headroom before the transparent target clamps it.
        keyLight.intensity=(toonLighting?.63f:.76f)*scale;
        fillLight.intensity=(toonLighting?.10f:.28f)*scale;
        RenderSettings.ambientLight=(toonLighting?new Color(.19f,.19f,.20f):new Color(.52f,.52f,.56f))*scale;
    }

    void Update()
    {
        if(channel==null || view==IntPtr.Zero) return;
        // Unity updates skinned mesh buffers after LateUpdate. Leave the pose on the skeleton
        // for that pass and restore it before this frame's normal Animator evaluation.
        if(offsetForCarry){pivot.SetPositionAndRotation(beforeLiftPosition,beforeLiftRotation);offsetForCarry=false;}
        gait?.Restore();
        int choiceId=Marshal.ReadInt32(view,36);
        if(controls && choiceId!=lastChoice){try{var ch=JsonUtility.FromJson<PetChoice>(File.ReadAllText(Path.Combine(channel,"controls.json")));if(ch.serial==loadedSerial && ch.seq==choiceId){controls.Apply(ch);if((ch.lightPercent>0?ch.lightPercent:100)!=lightPercent)SetLighting(ch.lightPercent);lastChoice=choiceId;}}catch(IOException){}}
        int yaw=Marshal.ReadInt32(view,8);
        if(lastYawCommand!=yaw){
            lastYawCommand=yaw;angle=yaw<0?yaw:yaw&511;gait?.ManualHeading();
            pivot.rotation=Quaternion.Euler(0,angle,0);
        }
        if (Time.unscaledTime<nextPoll) return;
        nextPoll=Time.unscaledTime+.12f;
        if (parent.HasExited || File.Exists(Path.Combine(channel,"quit"))) { Application.Quit(); return; }
        var path=Path.Combine(channel,"load.json");
        if (!loading && File.Exists(path))
        {
            try { var cmd=JsonUtility.FromJson<Command>(File.ReadAllText(path)); if(cmd.serial!=lastSerial) Load(cmd); }
            catch(IOException) { /* An atomic host replacement may be in progress. */ }
        }
    }

    async void Load(Command cmd)
    {
        lastSerial=cmd.serial; loading=true;
        GameObject next=null,container=null;
        AssetBundle nextBundle=null;
        var reply=new Reply {serial=cmd.serial};
        try
        {
            if(actor && string.Equals(cmd.path,loadedPath,StringComparison.OrdinalIgnoreCase)){SetLighting(cmd.lightPercent);reply.ok=true;reply.menu=controls.Menu;loadedSerial=cmd.serial;return;}
            string ext=Path.GetExtension(cmd.path).ToLowerInvariant();
            if(ext==".vrm") {
                var vrm=await Vrm10.LoadPathAsync(cmd.path,canLoadVrm0X:true,controlRigGenerationOption:ControlRigGenerationOption.None);
                if(vrm!=null){vrm.UpdateType=Vrm10Instance.UpdateTypes.None;next=vrm.gameObject;}
            } else if(ext==".glb") {
                var gltf=await GltfUtility.LoadAsync(cmd.path,new RuntimeOnlyAwaitCaller());
                gltf.ShowMeshes(); next=gltf.gameObject;
            } else if(ext==".petmodel") {
                nextBundle=AssetBundle.LoadFromFile(cmd.path);
                if(!nextBundle) throw new InvalidDataException("此角色包损坏，或目标平台不是 Windows。");
                var names=nextBundle.GetAllAssetNames().Where(n=>n.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(names.Length!=1) throw new InvalidDataException("角色包必须包含一个主 Prefab。");
                var prefab=nextBundle.LoadAsset<GameObject>(names[0]); next=Instantiate(prefab);
            } else throw new InvalidDataException("请选择 VRM、GLB 或导出的 .petmodel。");
            if(!next) throw new InvalidDataException("没有找到可显示的模型。");
            if(ext==".glb")ImportedHumanoid.TryBind(next);
            foreach(var c in next.GetComponentsInChildren<Camera>(true)) c.enabled=false;
            foreach(var l in next.GetComponentsInChildren<Light>(true)) l.enabled=false;
            // The desktop camera renders offscreen. Scene-view visibility must not
            // suspend the character's animator or its skinning for a single frame.
            foreach(var rig in next.GetComponentsInChildren<Animator>(true))rig.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var skin in next.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.updateWhenOffscreen=true;
            var meshes=next.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            if(meshes.Length==0) throw new InvalidDataException("模型中没有启用的网格。");
            if(ext==".petmodel")next.transform.Rotate(0,180,0,Space.World);
            var humanoid=next.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.avatar && a.avatar.isValid && a.isHuman);
            if(humanoid && ext!=".petmodel"){
                var lateral=humanoid.GetBoneTransform(HumanBodyBones.RightUpperLeg).position-humanoid.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                lateral=Vector3.ProjectOnPlane(lateral,Vector3.up);
                if(lateral.sqrMagnitude>.00000001f)next.transform.Rotate(0,Vector3.SignedAngle(lateral,Vector3.left,Vector3.up),0,Space.World);
            }
            PetMotion.RelaxArms(next);
            Bounds bounds=VisibleBounds(meshes[0]);
            foreach(var r in meshes.Skip(1)) bounds.Encapsulate(VisibleBounds(r));
            if(humanoid){
                var head=humanoid.GetBoneTransform(HumanBodyBones.Head).position;
                var feet=(humanoid.GetBoneTransform(HumanBodyBones.LeftFoot).position+humanoid.GetBoneTransform(HumanBodyBones.RightFoot).position)*.5f;
                var stature=head-feet;
                if(traceFile!=null)Debug.Log("Framing "+ext+" mesh height="+bounds.size.y+" skeleton height="+stature.y);
                bounds.Encapsulate(head+stature*.12f);bounds.Encapsulate(feet-stature*.04f);
            }
            float size=Mathf.Max(bounds.size.y,bounds.size.x/cam.aspect,bounds.size.z*.35f);
            if(float.IsNaN(size)||float.IsInfinity(size)||size<.00001f) throw new InvalidDataException("模型尺寸无效。");
            var nextHead=humanoid?humanoid.GetBoneTransform(HumanBodyBones.Head):null;
            float nextClearance=nextHead?Mathf.Max(.16f,(bounds.max.y-nextHead.position.y)*1.98f/size)+.055f:0;
            container=new GameObject("Active character");
            next.transform.SetParent(container.transform,true);
            next.transform.position-=bounds.center;
            container.transform.localScale=Vector3.one*(1.98f/size);
            container.transform.SetParent(pivot,false);
            portraitY=Mathf.Max(.06f,bounds.extents.y*container.transform.localScale.y-.46f);
            var nextControls=container.AddComponent<ModelControls>();
            string menuAsset=nextBundle?nextBundle.GetAllAssetNames().FirstOrDefault(n=>n.EndsWith("/desktop-controls.json")):null;
            nextControls.Prepare(next,menuAsset!=null?nextBundle.LoadAsset<TextAsset>(menuAsset):null);
            var newMotion=container.AddComponent<PetMotion>();newMotion.Prepare(next);
            var nextTouch=container.AddComponent<PointerTouch>();nextTouch.Prepare(next,nextControls);
            if(nextControls.HasAuthoredIdle)newMotion.enabled=false;
            var nextGait=new DesktopGait(next,pivot,nextControls.Menu);
            nextControls.Menu.humanoidMotion=nextGait.Available;
            if(nextGait.Available)nextControls.AddSharedGesture(nextGait.Celebration);
            else nextControls.AddGenericMovement(nextGait.Bounce);
            gait?.Dispose();
            if(actor) { actor.SetActive(false); Destroy(actor); }
            if(bundle) bundle.Unload(true);
            actor=container; motion=newMotion; bundle=nextBundle;controls=nextControls;touch=nextTouch;reply.menu=nextControls.Menu;
            toonLighting=meshes.SelectMany(r=>r.sharedMaterials).Any(m=>m && m.shader && m.shader.name.IndexOf("MToon",StringComparison.OrdinalIgnoreCase)>=0);
            SetLighting(cmd.lightPercent);
            vrmActor=next.GetComponent<Vrm10Instance>();
            framedHead=nextHead;headClearance=nextClearance;
            leftEye=humanoid?humanoid.GetBoneTransform(HumanBodyBones.LeftEye):null;
            rightEye=humanoid?humanoid.GetBoneTransform(HumanBodyBones.RightEye):null;
            if(nextHead){
                var feet=(humanoid.GetBoneTransform(HumanBodyBones.LeftFoot).position+
                    humanoid.GetBoneTransform(HumanBodyBones.RightFoot).position)*.5f;
                portraitSpan=Mathf.Clamp(Vector3.Distance(nextHead.position,feet)*.40f,.16f,.96f);
            }else portraitSpan=0;
            gait=nextGait;lastActivity=0;waving=false;carrying=false;lift=swing=liftSpeed=swingSpeed=carryTime=0;
            if(view!=IntPtr.Zero)tap=Marshal.ReadInt32(view,4);
            loadedPath=cmd.path;
            loadedSerial=cmd.serial;
            reply.ok=true;
        }
        catch(Exception e)
        {
            if(container) Destroy(container); else if(next) Destroy(next);
            if(nextBundle) nextBundle.Unload(true);
            reply.error="读取失败，请检查模型版本、文件完整性及依赖。"+e.GetBaseException().Message;
            Debug.LogWarning(reply.error);
        }
        finally
        {
            loading=false;
            if(channel!=null)
            {
                var target=Path.Combine(channel,"reply-"+cmd.serial+".json");
                File.WriteAllText(target+".new",JsonUtility.ToJson(reply)); File.Move(target+".new",target);
            }
        }
    }

    static Bounds VisibleBounds(Renderer renderer)
    {
        if(!(renderer is SkinnedMeshRenderer skin) || !skin.sharedMesh) return renderer.bounds;
        var mesh=new Mesh();
        try {
            skin.BakeMesh(mesh); mesh.RecalculateBounds();
            var b=mesh.bounds; var t=skin.transform;
            var world=new Bounds(t.TransformPoint(b.center),Vector3.zero);
            for(int i=0;i<8;i++)world.Encapsulate(t.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return world;
        } finally {Destroy(mesh);}
    }

    void Resize(int level,int display)
    {
        cam.targetTexture=null;RenderTexture.active=null;
        if(rt){rt.Release();Destroy(rt);}if(resolved){resolved.Release();Destroy(resolved);}
        if(capture){Destroy(capture);capture=null;}
        int limit=Widths[level];
        int output=Mathf.Clamp(display&0xffff,0,limit);
        if(output==0)output=limit;
        int w=(display&0x10000)!=0?Mathf.Min(limit,output*2):limit,h=w*11/9;
        rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        rt.antiAliasing=w>=output*2?1:4;rt.filterMode=FilterMode.Bilinear;
        rt.Create();cam.allowMSAA=rt.antiAliasing>1;cam.targetTexture=rt;
        resolved=new RenderTexture(output,output*11/9,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);resolved.Create();
        pixels=new byte[resolved.width*resolved.height*4];quality=level;viewport=display;
        if(!SystemInfo.supportsAsyncGPUReadback)capture=new Texture2D(resolved.width,resolved.height,TextureFormat.BGRA32,false);
    }
    int FaceRegion()
    {
        if(!framedHead)return 0;
        Vector3 focus=leftEye && rightEye?(leftEye.position+rightEye.position)*.5f:
            framedHead.position+framedHead.up*(portraitSpan*.15f);
        var point=cam.WorldToViewportPoint(focus);
        float side=portraitSpan/(2*cam.orthographicSize*cam.aspect);
        int Unit(float v)=>Mathf.RoundToInt(Mathf.Clamp01(v)*1023f);
        return Unit(point.x)|(Unit(1-point.y)<<10)|(Mathf.Max(1,Unit(side))<<20);
    }
    void Publish(int serial,int distance,int stopped,int faceRegion)
    {
        if(view==IntPtr.Zero)return;
        Marshal.WriteInt32(view,0,++frame); Thread.MemoryBarrier();
        Marshal.WriteInt32(view,20,resolved.width);Marshal.WriteInt32(view,24,resolved.height);Marshal.WriteInt32(view,32,serial);
        Marshal.Copy(pixels,0,IntPtr.Add(view,Header),pixels.Length);
        if((viewport&0x20000)!=0){Marshal.WriteInt32(view,Metrics+4,distance);Marshal.WriteInt32(view,Metrics+8,stopped);}
        if((viewport&0x40000)!=0)Marshal.WriteInt32(view,Metrics+12,faceRegion);
        Thread.MemoryBarrier(); Marshal.WriteInt32(view,0,++frame);
    }
    void LateUpdate()
    {
        if(view==IntPtr.Zero || !actor)return;
        bool close=Marshal.ReadInt32(view,28)!=0;
        cam.orthographicSize=close?.57f:1.13f;
        cam.transform.position=new Vector3(0,close?portraitY:0,-5);
        int clicks=Marshal.ReadInt32(view,4),flags=Marshal.ReadInt32(view,52);
        Vector2 PointAt(int offset){
            int packed=Marshal.ReadInt32(view,offset);
            return new Vector2((short)(packed&0xffff)/8192f,(short)(packed>>16)/8192f);
        }
        int activity=Marshal.ReadInt32(view,12);
        if(activity==10 && lastActivity!=10){if(!gait.Available)gait.Bounce();else if(motion && motion.enabled)motion.Tap();}
        if((flags&1)!=0 && activity!=4 && activity!=5)activity=0;
        if(activity==3){if(lastActivity!=3)waving=controls.TryAmbientWave();if(!waving)activity=2;}
        if(controls.SharedGesture)activity=8;
        bool held=(flags&4)!=0;
        if(held)activity=9;
        gait.Apply(activity,2*cam.orthographicSize*cam.aspect,controls.ModelTravel(activity));lastActivity=Marshal.ReadInt32(view,12);
        if(gait.LowView>0){
            float low=gait.LowView;
            cam.orthographicSize+=low*(close?.12f:.19f);
            cam.transform.position=new Vector3(gait.CenterX*low,close?Mathf.Lerp(portraitY,gait.HeadY,low):-.48f*low,-5);
        }
        travel=unchecked(travel+Mathf.RoundToInt(gait.Distance*1000000));
        controls.UpdateExpression(activity,gait.LookAngle);
        if(vrmActor)vrmActor.Runtime.Process();
        float dx=Marshal.ReadInt32(view,48)/4096f;
        if(held && !carrying){lastCarryX=dx;carryTime=0;}
        if(held)carryTime+=Time.unscaledDeltaTime;
        float velocity=held && carrying?Mathf.Clamp((dx-lastCarryX)/Mathf.Max(.008f,Time.unscaledDeltaTime),-3,3):0;
        lastCarryX=dx;carrying=held;
        lift=Mathf.SmoothDamp(lift,held?1:0,ref liftSpeed,held?.13f:.19f);
        swing=Mathf.SmoothDamp(swing,held?velocity*4+1.2f*Mathf.Sin(carryTime*3):0,ref swingSpeed,.16f);
        float startled=held && controls.NaturalFace?Mathf.SmoothStep(0,.88f,Mathf.Clamp01(carryTime/.12f))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.38f,.68f,carryTime))):0;
        touch.Simulate(cam,PointAt(44),PointAt(48),tap!=clicks,(flags&1)!=0,(flags&2)!=0,startled,swing*lift);tap=clicks;
        if(touch.BodyPressed && !controls.Tap())motion.Tap();
        if(lift>.001f){
            beforeLiftPosition=pivot.position;beforeLiftRotation=pivot.rotation;offsetForCarry=true;
            pivot.RotateAround(pivot.position+Vector3.up*.54f,Vector3.forward,swing*lift);
            pivot.position+=Vector3.up*(.14f*lift);
            if(!close && framedHead){
                // The low camera can still be rising as a prone character unfolds.
                var center=cam.transform.position;
                center.y=Mathf.Max(center.y,framedHead.position.y+headClearance-cam.orthographicSize);
                cam.transform.position=center;
            }
        }
        if(traceFile!=null && Time.unscaledTime>=traceAt){traceAt=Time.unscaledTime+.08f;
            File.AppendAllText(traceFile,FormattableString.Invariant($"{loadedSerial},{Time.unscaledTime:F3},{activity},{gait.ClipName},{gait.LeftShoeY:F4},{gait.RightShoeY:F4},{gait.HeadY:F4},{gait.LookAngle:F2},{travel},{controls.ShownFace},{controls.FaceWeight:F3},{controls.MeasuredFace():F2},{angle+gait.Heading:F2}")+gait.ShoeTrace()+","+controls.PlayingAction+"\n");}
        Marshal.WriteInt32(view,56,touch.Kind);Marshal.WriteInt32(view,60,touch.Strength);
        if(footTrace!=null)footTrace.WriteLine(gait.FootTrace(loadedSerial,activity));
    }
    System.Collections.IEnumerator CaptureFrames()
    {
        var end=new WaitForEndOfFrame();
        while(true){
            yield return end;
            if(view!=IntPtr.Zero && actor)CaptureFrame();
        }
    }
    void CaptureFrame()
    {
        // Capture after Unity has skinned the meshes for this pose. A busy GPU may
        // skip a picture, but the skeleton and secondary motion must keep advancing.
        int desired=Mathf.Clamp(Marshal.ReadInt32(view,16),0,Widths.Length-1);
        int display=Marshal.ReadInt32(view,40);
        if(quality!=desired || viewport!=display){
            if(pendingReads!=0)return;
            Resize(desired,display);
        }
        if(pendingReads>=3)return;
        int distance=travel,stopped=gait.FinishedStep?1:0;
        if((display&0x80000)!=0){
            int visibleYaw=(Mathf.RoundToInt(angle+gait.Heading)%360+360)%360;
            stopped|=0x400|(visibleYaw<<1);
        }
        cam.Render();
        // A host with a viewport consumes top-down pixels directly, sized for the desktop.
        if(display!=0)Graphics.Blit(rt,resolved,new Vector2(1,-1),new Vector2(0,1));
        else Graphics.Blit(rt,resolved);
        int serial=loadedSerial,faceRegion=(display&0x40000)!=0?FaceRegion():0;
        int ticket=++submitted;
        if(!capture){
            ++pendingReads;
            AsyncGPUReadback.Request(resolved,0,TextureFormat.BGRA32,request=>{
                --pendingReads;if(view==IntPtr.Zero || ticket<=published || serial!=loadedSerial)return;
                if(request.hasError){if(!capture)capture=new Texture2D(resolved.width,resolved.height,TextureFormat.BGRA32,false);return;}
                published=ticket;
                request.GetData<byte>().CopyTo(pixels);Publish(serial,distance,stopped,faceRegion);
            });
        } else {
            var previous=RenderTexture.active;
            try{RenderTexture.active=resolved;capture.ReadPixels(new Rect(0,0,resolved.width,resolved.height),0,0,false);capture.GetRawTextureData<byte>().CopyTo(pixels);published=ticket;Publish(serial,distance,stopped,faceRegion);}
            finally{RenderTexture.active=previous;}
        }
    }
    void OnDestroy() { if(pendingReads!=0)AsyncGPUReadback.WaitAllRequests();footTrace?.Dispose();gait?.Dispose();if(view!=IntPtr.Zero){UnmapViewOfFile(view);view=IntPtr.Zero;} if(map!=IntPtr.Zero) CloseHandle(map); if(rt) rt.Release();if(resolved)resolved.Release(); }
}
