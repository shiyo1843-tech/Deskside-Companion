using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeskPet;
public sealed partial class PetWindow {
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public NativeRect Bounds,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint fallback);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    readonly Stopwatch clock=Stopwatch.StartNew();
    ReplyBubble? bubble;
    double destination,edge;
    int lastTravel,restIndex;
    double start,duration,next,statsAt;
    int mode;
    public long AvatarGeneration {get;private set;}
    double reactionUntil;
    bool reactionClip,reactionPose;
    public bool RotatingPreview{get;set;}
    bool nextWalk=true;
    double Now=>clock.Elapsed.TotalSeconds;
    public void UpdateViewport(){
        if(closing)return;
        double desktop=Width*VisualTreeHelper.GetDpi(this).DpiScaleX;
        // The control panel also crops this texture for its larger portrait.
        double preview=Panel.IsVisible?Math.Max(280,Panel.ActualWidth*.42)*2*VisualTreeHelper.GetDpi(Panel).DpiScaleX:0;
        renderer.Viewport(Math.Max(desktop,preview),Store.Data.Adaptive);
    }
    void InitDesktopLife(){
        ScheduleRest();
        CompositionTarget.Rendering+=OnDesktopFrame;
        LocationChanged+=(_,_)=>{if(bubble?.IsVisible==true)bubble.Follow(this,WorkArea());};
    }
    internal Rect WorkArea(){
        var handle=new WindowInteropHelper(this).Handle;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(handle==IntPtr.Zero || !GetWindowRect(handle,out var window) || !GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return SystemParameters.WorkArea;
        if((info.Flags&1)!=0)return SystemParameters.WorkArea;
        var scale=VisualTreeHelper.GetDpi(this);var w=info.Work;
        return new Rect(Left+(w.Left-window.Left)/scale.DpiScaleX,Top+(w.Top-window.Top)/scale.DpiScaleY,(w.Right-w.Left)/scale.DpiScaleX,(w.Bottom-w.Top)/scale.DpiScaleY);
    }
    static double Between(double low,double high)=>low+(high-low)*Random.Shared.NextDouble();
    public void ScheduleRest(){
        double wait=Store.Data.ActivityFrequency switch {1=>Between(22,40),2=>Between(10,20),_=>Between(45,80)};
        next=Now+wait;
    }
    void SetActivity(int value){mode=value;renderer.Activity(value);}
    public void PauseActivity(){
        CancelChatReaction();
        bool moved=Math.Abs(mode) is 1 or 7;
        if(mode!=0)SetActivity(0);
        if(moved && !closing && renderer.DisplayYaw is int visibleYaw)SetYaw(visibleYaw,false);
        ScheduleRest();
        if(moved && !closing)SavePosition();
    }
    void BeginWalk(double now){
        var area=WorkArea();
        // A pet larger than the work area can still move on the other axis.
        double left=Math.Min(area.Left,Left),right=Math.Max(left,Math.Max(Left,area.Right-Width));
        if(area.Width>=Width){left=area.Left;right=area.Right-Width;}
        double sign=Random.Shared.Next(2)==0?-1:1;
        if(Left-left<Width*.85)sign=1;
        if(right-Left<Width*.85)sign=-1;
        edge=sign>0?right:left;
        // Leave room for the character's closing step, which continues to move the window.
        double space=Math.Abs(edge-Left);
        double run=Math.Min(Between(.55,1.05)*Width,space-Width*.58);
        if(run<20){BeginGesture(now);return;}
        double x=Left+sign*run;
        if(Math.Abs(x-Left)<16){BeginGesture(now);return;}
        destination=x;lastTravel=renderer.Travel;start=now;duration=7;
        SetActivity(x>Left?1:-1);
    }
    void BeginGesture(double now){
        if(!MenuOptions.humanoidMotion){start=now;duration=.9;SetActivity(10);return;}
        int[] choices={4,5,2,3};
        int pose=choices[(restIndex++)%choices.Length];
        start=now;duration=pose==5?Between(10,15):pose==4?Between(5,9):2.8;SetActivity(pose);
    }
    internal int SupportedIdle(int pose)=>!MenuOptions.humanoidMotion && pose is 4 or 5?6:pose;
    public void ChooseIdle(int pose){
        if(Active is not {} c)return;
        pose=SupportedIdle(pose);
        PauseActivity();c.IdlePose=pose;c.Action="";c.Loop=false;renderer.Controls(c,true);Store.Save();
        if(pose==4 || pose==5)SetActivity(pose);
        if(pose==0){nextWalk=false;next=Now+.45;}
    }
    internal void PlayMenuAction(string id){
        if(Active is not {} c)return;
        if(id==ReactionMenu.BounceId && !MenuOptions.actions.Any(o=>o.id==id)){
            PauseActivity();reactionPose=true;reactionUntil=Now+.9;SetActivity(10);return;
        }
        c.Action=id;
        if(SupportedIdle(c.IdlePose)==6 && !c.Loop){PauseActivity();renderer.Controls(c,true);Store.Save();}
        else ApplyControls(true);
    }
    void OnDesktopFrame(object? sender,EventArgs e){
        if(closing)return;
        double now=Now;
        if(now>=statsAt){Panel.FrameRate(renderer.ProducedFps,renderer.DisplayFps);statsAt=now+1;}
        bubble?.Advance(now);
        if(reactionUntil>0){
            if(now<reactionUntil)return;
            CancelChatReaction();
        }
        if(!IsLoaded || !IsVisible || WindowState==WindowState.Minimized || !renderer.HasFrame || Busy || Active==null ||
           (Active.Loop && Active.Action.Length!=0)){
            if(mode!=0)PauseActivity();else next=Math.Max(next,now+1.8);
            return;
        }
        int idle=SupportedIdle(Active.IdlePose);
        if(idle is 4 or 5){if(mode!=idle)SetActivity(idle);return;}
        bool interacting=touching || relocating || rotating || RotatingPreview || Wheel.IsVisible;
        bool stayHere=IsMouseOver || Panel.ReadingChat || bubble?.IsVisible==true;
        if(idle==6 || interacting){
            if(mode!=0)PauseActivity();else next=Math.Max(next,now+1.8);return;
        }
        if(mode==0){
            if(now<next)return;
            if(nextWalk && Store.Data.Roam && !stayHere)BeginWalk(now);else BeginGesture(now);
            nextWalk=!nextWalk;return;
        }
        if(Math.Abs(mode) is 1 or 7){
            int ticks=unchecked(renderer.Travel-lastTravel);lastTravel=renderer.Travel;
            double step=Math.Max(0,ticks)/1000000.0*Width;
            int direction=Math.Sign(mode);
            // Complete the stance foot's travel during a hover-initiated stop.
            if(Store.Data.Roam)Left+=direction*Math.Min(step,Math.Abs(edge-Left));
            if(Math.Abs(mode)==1 && (stayHere || !Store.Data.Roam || direction*(destination-Left)<=0 || now-start>duration)){
                SetActivity(direction*7);return;
            }
            // The closing step and turn run on the renderer's animation clock. A slow
            // frame must not make the host commit an unfinished, sideways pose.
            if(Math.Abs(mode)==1 || !renderer.Landed || renderer.DisplayYaw!=0)return;
        }
        if(now-start>=duration || Math.Abs(mode)==7){
            PauseActivity();
        }
    }
    public void CancelChatReaction(){
        if(reactionUntil<=0)return;
        reactionUntil=0;
        if(Active is {} c){
            var restored=reactionClip && !c.Loop?c with {Action=""}:c;
            renderer.Controls(restored,reactionClip);
            if(reactionPose)SetActivity(SupportedIdle(c.IdlePose) is 4 or 5?c.IdlePose:0);
        }
        reactionClip=reactionPose=false;ScheduleRest();
    }
    public void ShowChatReply(Character character,ChatReply reply,string speaker){
        if(closing || Busy || Active is not {} c || c.Id!=character.Id || c.Voice!=character.Voice)return;
        if(Store.Data.Bubbles){bubble??=new ReplyBubble(this);bubble.Present(character,speaker,reply.Text,Now,WorkArea());}
        if(!Store.Data.ChatActions || touching || relocating || rotating || RotatingPreview || Wheel.IsVisible)return;
        var action=ReactionMenu.AllowsBody(c)?reply.Action:null;
        var face=c.Face.Length==0 && MenuOptions.faces.Any(o=>o.id==reply.Face?.Id)?reply.Face:null;
        var clip=action?.Kind=="clip" && MenuOptions.actions.Any(o=>o.id==action.Id)?action.Id:null;
        int pose=action?.Kind=="pose"?action.Id switch {"bounce"=>10,"look" when MenuOptions.humanoidMotion=>2,"crouch" when MenuOptions.humanoidMotion=>4,"prone" when MenuOptions.humanoidMotion=>5,_=>0}:0;
        if(clip==null && pose==0 && face==null)return;
        CancelChatReaction();
        if(clip!=null || pose!=0)PauseActivity();
        reactionClip=clip!=null;reactionPose=reactionClip||pose!=0;
        if(reactionPose)SetActivity(pose);
        var temporary=c with {Face=face?.Id??c.Face,Action=clip??c.Action,Loop=clip==null && c.Loop};
        renderer.Controls(temporary,reactionClip);
        reactionUntil=Now+(pose==5?5:pose==4?4:clip!=null?4:2.4);
    }
    public void ClearBubble(){bubble?.Dismiss();}
    void EndDesktopLife(){CompositionTarget.Rendering-=OnDesktopFrame;bubble?.Close();}
}
