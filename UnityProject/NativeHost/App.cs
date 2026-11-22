using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace DeskPet;
public static class Program {
    [STAThread] public static void Main(string[] args) {
        var index=Array.IndexOf(args,"--data-dir");
        string root=Path.GetFullPath(index>=0 && index+1<args.Length?args[index+1]:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPet"));
        using var single=new Mutex(true,"DeskPet-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20],out bool first);
        if(!first){MessageBox.Show("桌边伙伴已经在运行。右键角色可以打开面板。","桌边伙伴");return;}
        try {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var shell=new PetWindow(new Library(root),args);
            try {app.Run(shell);} finally {shell.ReleaseRenderer();}
        }catch(Exception e){MessageBox.Show(e.GetBaseException().Message,"桌边伙伴");}
    }
}
public sealed partial class PetWindow:Window {
    public readonly Library Store;
    public Character? Active=>Store.Data.Characters.FirstOrDefault(c=>c.Id==Store.Data.Active);
    public bool Busy{get;private set;}
    readonly Renderer renderer;
    public ImageSource Portrait=>renderer.Image;
    public ImageBrush FacePortrait=>renderer.FaceBrush;
    public Rect PreviewRegion=>renderer.PreviewRegion;
    int activeFrameSerial;
    public Options MenuOptions=>renderer.Menu;
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint mods,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    HwndSource? source;
    public readonly Studio Panel;
    public readonly Wheel Wheel;
    public bool IsExiting=>closing;
    Point startCursor,startWindow;
    bool touching,relocating,moved,travelled,closing,rotating;
    double rotationStartX;int rotationStartYaw;
    readonly System.Diagnostics.Stopwatch rightHold=new();
    public PetWindow(Library store,string[] args){
        Store=store;Title="桌边伙伴 · 角色";Width=Math.Clamp(Store.Data.Size,210,760);Height=Width*11/9;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Brand/DeskPet.ico"));
        AllowsTransparency=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Background=Brushes.Transparent;
        ShowInTaskbar=true;Topmost=store.Data.Pinned;
        var work=SystemParameters.WorkArea;
        var displays=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
        Left=Math.Clamp(store.Data.X??work.Right-Width-24,displays.Left-Width+60,displays.Right-60);
        Top=Math.Clamp(store.Data.Y??work.Bottom-Height,displays.Top-Height+60,displays.Bottom-60);
        renderer=new Renderer(store.Root,store.Data.Detail,store.Data.CloseUp);
        var image=new Image{Source=renderer.Image};Content=image;AutomationProperties.SetName(image,"桌宠角色");
        Panel=new Studio(this);Panel.Topmost=Topmost;Wheel=new Wheel(this);
        InitDesktopLife();
        SizeChanged+=(_,_)=>UpdateViewport();
        DpiChanged+=(_,_)=>UpdateViewport();
        Panel.IsVisibleChanged+=(_,_)=>UpdateViewport();
        Panel.SizeChanged+=(_,_)=>UpdateViewport();
        UpdateViewport();
        renderer.ImageChanged+=()=>{image.Source=renderer.Image;Panel.UpdatePortrait();};
        MouseWheel+=(_,e)=>ResizePet(Math.Sign(e.Delta));
        renderer.Failed+=s=>{Panel.Status(s);Open("角色");};
        SourceInitialized+=(_,_)=>{source=(HwndSource)PresentationSource.FromVisual(this)!;source.AddHook(OnMessage);if(!RegisterHotKey(source.Handle,3741,0x4003,0x50))Panel.Status("Ctrl + Alt + P 已被其他程序占用，可右键角色打开面板。");};
        Point At(MouseEventArgs e){var p=e.GetPosition(this);return new Point(p.X/ActualWidth,p.Y/ActualHeight);}
        MouseDown+=(_,e)=>{if(e.ChangedButton!=MouseButton.Middle || touching || relocating || Active is not {} c)return;
            Focus();PauseActivity();rotating=true;rotationStartX=e.GetPosition(this).X;rotationStartYaw=c.Yaw;
            CaptureMouse();renderer.Pointer(At(e),false,false);e.Handled=true;};
        MouseUp+=(_,e)=>{if(e.ChangedButton==MouseButton.Middle && rotating){rotating=false;Store.Save();ReleaseMouseCapture();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Left || e.Key==Key.Right){TurnBy(e.Key==Key.Left?-1:1);e.Handled=true;}};
        MouseLeftButtonDown+=(_,e)=>{if(relocating || rotating)return;Focus();PauseActivity();touching=true;CaptureMouse();renderer.Pointer(At(e),true,true,true);e.Handled=true;};
        MouseRightButtonDown+=(_,e)=>{if(touching || rotating)return;PauseActivity();startCursor=PointToScreen(e.GetPosition(this));startWindow=new Point(Left,Top);relocating=true;moved=travelled=false;rightHold.Restart();CaptureMouse();renderer.Pointer(At(e),false,false);e.Handled=true;};
        MouseMove+=(_,e)=>{if(rotating){SetYaw(rotationStartYaw+(int)Math.Round((e.GetPosition(this).X-rotationStartX)*.8),false);return;}
            if(!relocating){renderer.Pointer(At(e),touching,IsMouseOver);return;}
            if(e.RightButton!=MouseButtonState.Pressed)return;
            var cursor=PointToScreen(e.GetPosition(this));var delta=cursor-startCursor;
            var dpi=VisualTreeHelper.GetDpi(this);delta.X/=dpi.DpiScaleX;delta.Y/=dpi.DpiScaleY;
            if(!moved && Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            travelled=true;if(rightHold.ElapsedMilliseconds<220)return;
            moved=true;renderer.Carry(delta.X/Math.Max(1,Width));Left=startWindow.X+delta.X;Top=startWindow.Y+delta.Y;};
        MouseLeftButtonUp+=(_,e)=>{if(!touching)return;touching=false;renderer.Pointer(At(e),false,IsMouseOver);ReleaseMouseCapture();e.Handled=true;};
        MouseLeave+=(_,e)=>{if(!touching && !relocating)renderer.Pointer(At(e),false,false);};
        LostMouseCapture+=(_,e)=>{if(rotating){rotating=false;Store.Save();}touching=false;relocating=false;renderer.Pointer(At(e),false,false);};
        MouseRightButtonUp+=(_,e)=>{if(!relocating)return;relocating=false;ReleaseMouseCapture();if(moved)SavePosition();else if(!travelled&&rightHold.ElapsedMilliseconds<220)Wheel.Present();e.Handled=true;};
        Loaded+=async(_,_)=>{
            try{await store.UpgradeBundles(Path.Combine(AppContext.BaseDirectory,"我的角色"));}catch(Exception e){Panel.Status(e.GetBaseException().Message);}
            if(closing)return;
            bool fresh=Active==null;
            if(fresh || args.Contains("--open-panel"))Open("角色");
            string sample=Path.Combine(AppContext.BaseDirectory,"示例模型","云团.glb");
            var bundled=Path.Combine(AppContext.BaseDirectory,"我的角色");
            if(fresh && File.Exists(Path.Combine(bundled,"Lime.petmodel")) && File.Exists(Path.Combine(bundled,"Hikarun.petmodel"))){
                await Import(Path.Combine(bundled,"Lime.petmodel"));
                if(closing)return;
                await Import(Path.Combine(bundled,"Hikarun.petmodel"));
                var lime=store.Data.Characters.FirstOrDefault(c=>c.Name=="Lime");
                if(lime!=null)await Select(lime);
            } else if(fresh)await Import(sample);else await Select(Active!);
            if(closing)return;
            int pair=Array.IndexOf(args,"--pair-file");
            if(pair>=0 && pair+1<args.Length){try{await Cloud.Pair(Store.Root,args[pair+1]);Panel.Status("桌宠已与云端配对");}catch(Exception e){Panel.Status("配对失败 · "+e.GetBaseException().Message);}Open("服务");}
            int import=Array.IndexOf(args,"--import");
            string? file=import>=0 && import+1<args.Length?args[import+1]:args.Length==1?args[0]:null;
            if(file!=null && File.Exists(file))await Import(file);
        };
        Closing+=(_,e)=>{if(!closing){e.Cancel=true;Dispatcher.BeginInvoke(new Action(Exit));}};
    }
    IntPtr OnMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled){
        if(msg==0x0312 && wp.ToInt32()==3741){handled=true;if(Wheel.IsVisible)Wheel.Dismiss();else Wheel.Present();}return IntPtr.Zero;
    }
    public void ApplyControls(bool play=false){if(Active is {} c){CancelChatReaction();if(play){PauseActivity();c.IdlePose=0;}renderer.Controls(c,play);Store.Save();}}
    public void TogglePin(){Topmost=!Topmost;Panel.Topmost=Topmost;Store.Data.Pinned=Topmost;Store.Save();}
    void SavePosition(){Store.Data.X=Left;Store.Data.Y=Top;Store.Save();}
    public void Open(string tab){if(closing)return;Panel.Go(tab);if(!Panel.IsVisible)Panel.Show();if(Panel.WindowState==WindowState.Minimized)Panel.WindowState=WindowState.Normal;Panel.Activate();}
    public async Task Import(string path){
        if(Busy || closing)return;
        Busy=true;Panel.Status("正在导入 "+Path.GetFileName(path)+"…");Panel.Refresh();
        Character? staged=null;
        try{
            if(Path.GetExtension(path).Equals(".unitypackage",StringComparison.OrdinalIgnoreCase)){packageTask=ImportPackage(path);await packageTask;return;}
            staged=await Store.Stage(path);if(closing)return;await ActivateModel(staged);
        }
        catch(OperationCanceledException) when(closing){}
        catch(Exception e){if(staged!=null&&!Store.Data.Characters.Any(c=>c.File==staged.File))File.Delete(Store.Model(staged));if(!closing){Panel.Status(e.GetBaseException().Message);if(!Panel.IsVisible)Open("角色");}}
        finally{Busy=false;packageTask=null;if(!closing)Panel.Refresh();}
    }
    readonly CancellationTokenSource packageLifetime=new();
    Task? packageTask;
    readonly List<Task> packageCleanup=new();
    async Task ImportPackage(string path){
        var ct=packageLifetime.Token;
        using var pack=await Task.Run(()=>UnityPackage.Open(path,ct),ct);
        PackageAsset? previous=null;
        string error="";
        while(!closing){
            if(!Panel.IsVisible)Open("角色");
            var picker=new PackagePicker(Panel,Path.GetFileName(path),pack,Store.Data,previous,error);
            if(picker.ShowDialog()!=true || picker.Asset is not {} asset){if(!closing)Panel.Status("已取消导入。");return;}
            string key=pack.Hash+"/"+asset.Guid;
            if(Store.Data.PackageImports.TryGetValue(key,out var id) && Store.Data.Characters.FirstOrDefault(c=>c.Id==id) is {} cached && File.Exists(Store.Model(cached))){
                await ActivateModel(cached);return;
            }
            Store.Data.PackageImports.Remove(key);
            Store.Data.PackageProject=picker.ProjectFolder;Store.Data.PackageEditor=picker.EditorPath;
            string scratch=Path.Combine(Path.GetTempPath(),"DeskPetPackage-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);Character? staged=null;
            try{
                string model=await PackageConversion.Run(pack,asset,scratch,picker.ProjectFolder,picker.EditorPath,Panel.Status,ct);
                ct.ThrowIfCancellationRequested();staged=await Store.Stage(model);ct.ThrowIfCancellationRequested();
                await ActivateModel(staged,key);
                string report=Path.Combine(scratch,"result.json");
                if(!closing && File.Exists(report)){
                    using var parsed=System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
                    if(parsed.RootElement.TryGetProperty("warning",out var note) && !string.IsNullOrWhiteSpace(note.GetString()))Panel.Status("已导入。"+note.GetString());
                }
                return;
            }catch(OperationCanceledException) when(ct.IsCancellationRequested){return;}
            catch(Exception e){
                error=e.GetBaseException().Message;previous=asset;Panel.Status(error);
                if(staged!=null&&!Store.Data.Characters.Any(c=>c.File==staged.File))File.Delete(Store.Model(staged));
            }finally{
                // Import is committed (or failed). Deleting Unity's Library can take
                // several seconds, during which the user can already use the pet.
                packageCleanup.RemoveAll(task=>task.IsCompleted);
                packageCleanup.Add(Task.Run(()=>{try{Directory.Delete(scratch,true);}catch(IOException){}catch(UnauthorizedAccessException){}}));
            }
        }
    }
    public async Task Select(Character c){
        if(Busy || closing)return;Busy=true;Panel.Status("正在切换到 "+c.Name+"…");Panel.Refresh();
        try{await ActivateModel(c);}catch(Exception e){Panel.Status(e.GetBaseException().Message);Open("角色");}
        finally{Busy=false;if(!closing)Panel.Refresh();}
    }
    async Task ActivateModel(Character c,string? packageKey=null){
        SavePortrait();ClearBubble();AvatarGeneration++;Wheel.Dismiss();PauseActivity();
        await renderer.Load(Store.Model(c),c.Yaw,c.LightPercent);
        if(closing)return;
        activeFrameSerial=renderer.FrameSerial;
        if(!Store.Data.Characters.Any(x=>x.Id==c.Id))Store.Data.Characters.Add(c);
        if(packageKey!=null)Store.Data.PackageImports[packageKey]=c.Id;
        ClearBubble();Store.Data.Active=c.Id;Store.Save();Panel.Status("当前角色 · "+c.Name);
        renderer.Controls(c,c.Loop && c.Action.Length!=0);
    }
    internal string PortraitPath(Character c)=>Path.Combine(Store.Root,"Models",c.Id+".portrait-v2.png");
    void SavePortrait(){
        if(activeFrameSerial==0 || activeFrameSerial!=renderer.FrameSerial || Active is not {} c)return;
        try{renderer.Thumbnail(PortraitPath(c));}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void SetYaw(int angle,bool save=true){if(Active is {} c){c.Yaw=(angle%360+360)%360;renderer.Turn(c.Yaw);if(save)Store.Save();}}
    public void TurnBy(int degrees){if(Active is {} c){PauseActivity();SetYaw(c.Yaw+degrees);}}
    public void Turn()=>TurnBy(15);
    public void SetDetail(int level){Store.Data.Detail=Math.Clamp(level,0,3);renderer.Quality(Store.Data.Detail);Store.Save();}
    public void SetLighting(int percent){if(Active is {} c){c.LightPercent=Math.Clamp(percent,50,150);renderer.Controls(c);Store.Save();}}
    public void SetClose(bool close){Store.Data.CloseUp=close;renderer.CloseUp(close);Store.Save();Panel.UpdatePortrait();}
    public void ResizePet(int direction){double width=Math.Clamp(Width+direction*48,210,760);Left+=(Width-width)/2;Top+=(Height-width*11/9)/2;Width=width;Height=width*11/9;Store.Data.Size=width;SavePosition();}
    public async void Exit(){if(closing)return;closing=true;packageLifetime.Cancel();SavePortrait();renderer.BeginShutdown();EndDesktopLife();if(source!=null)UnregisterHotKey(source.Handle,3741);Panel.End();SavePosition();
        try{
            foreach(var picker in Application.Current.Windows.OfType<PackagePicker>().ToArray())picker.Close();
            if(Wheel.IsVisible)Wheel.Hide();
            if(Panel.IsVisible)Panel.Hide();
            Hide();
            if(packageTask!=null)await packageTask;
            await Task.WhenAll(packageCleanup);
        }catch(OperationCanceledException){}catch(Exception e){System.Diagnostics.Debug.WriteLine(e);}
        finally{Application.Current.Shutdown();}
    }
    internal void ReleaseRenderer()=>renderer.Dispose();
}

