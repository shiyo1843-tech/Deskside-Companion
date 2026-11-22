using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace DeskPet;
public record Option(string id,string label,bool initial=false,bool advanced=false);
public record WheelNode(string id,string parent,string label,string kind,string parameter,string icon,string[] axes,string[] axisLabels,float value,float initial,bool available);
public sealed class Options {public List<Option> actions{get;set;}=new();public List<Option> faces{get;set;}=new();public List<Option> clothes{get;set;}=new();public List<WheelNode> nodes{get;set;}=new(); public bool vrchat{get;set;} public bool humanoidMotion{get;set;}}
public sealed class Renderer : IDisposable {
    public static readonly int[] Widths={720,1080,1440,2160};
    readonly string session;
    readonly Process process;
    readonly MemoryMappedFile map;
    readonly MemoryMappedViewAccessor memory;
    byte[] raw=Array.Empty<byte>();
    const int DisplayCounter=64+2160*2640*4;
    int submitted;
    readonly Stopwatch health=Stopwatch.StartNew(),sample=Stopwatch.StartNew();
    int sampleFrame,sampleDisplay;
    public double ProducedFps{get;private set;}
    public double DisplayFps{get;private set;}
    readonly DispatcherTimer timer;
    WriteableBitmap bitmap=new(1,1,96,96,PixelFormats.Bgra32,null);
    public ImageSource Image=>bitmap;
    public ImageBrush FaceBrush {get;}=new(){Stretch=Stretch.Uniform,ViewboxUnits=BrushMappingMode.RelativeToBoundingBox};
    public Rect PreviewRegion {get;private set;}=new(0,0,1,1);
    readonly CancellationTokenSource lifetime=new();
    bool stopping,disposed;
    int lastRegion=-1;
    int serial,frame,tap;
    public bool HasFrame=>frame!=0;
    public int Travel {get;private set;}
    public bool Landed {get;private set;}
    public int? DisplayYaw {get;private set;}
    public event Action<string>? Failed;
    public event Action? ImageChanged;
    public int FrameSerial {get;private set;}
    public Options Menu{get;private set;}=new();
    int controlSeq,pulse,yawCommand;
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int cmd);
    public Renderer(string root,int quality,bool close) {
        string id=Guid.NewGuid().ToString("N");
        session=Path.Combine(Path.GetTempPath(),"DeskPet",id);
        Directory.CreateDirectory(session);
        map=MemoryMappedFile.CreateNew("DeskPet-"+id,DisplayCounter+16); memory=map.CreateViewAccessor();Quality(quality);CloseUp(close);
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Renderer","Portrait.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(var arg in new[]{"-batchmode","-force-d3d11","-screen-fullscreen","0","-logFile",Path.Combine(root,"renderer.log"),"-petSession",session,"-petOwner",Environment.ProcessId.ToString()})start.ArgumentList.Add(arg);
        process=Process.Start(start)??throw new IOException("Unity 渲染器未启动。");
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>Tick(); timer.Start();
        CompositionTarget.Rendering+=OnRender;
    }
    void OnRender(object? sender,EventArgs args)=>Tick();
    void Tick(){
        if(stopping)return;
        if(health.ElapsedMilliseconds>=500){health.Restart();if(process.HasExited){timer.Stop();CompositionTarget.Rendering-=OnRender;Failed?.Invoke("Unity 渲染器已退出。请重新打开应用，详见数据目录的 renderer.log。");return;}}
        if(frame==0 && process.MainWindowHandle!=IntPtr.Zero) ShowWindow(process.MainWindowHandle,0);
        var seq=memory.ReadInt32(0);
        if(sample.ElapsedMilliseconds>=1000){
            ProducedFps=(seq-sampleFrame)*.5/sample.Elapsed.TotalSeconds;DisplayFps=(submitted-sampleDisplay)/sample.Elapsed.TotalSeconds;
            sampleFrame=seq;sampleDisplay=submitted;sample.Restart();
        }
        if(seq==frame || (seq&1)!=0)return;
        int w=memory.ReadInt32(20),h=memory.ReadInt32(24),serial=memory.ReadInt32(32);
        if(w<9 || w>Widths[^1] || h!=w*11/9)return;
        bool resized=bitmap.PixelWidth!=w;
        if(raw.Length!=w*h*4)raw=new byte[w*h*4];
        memory.ReadArray(64,raw,0,raw.Length);
        int distance=memory.ReadInt32(DisplayCounter+4),stance=memory.ReadInt32(DisplayCounter+8);
        int region=memory.ReadInt32(DisplayCounter+12);
        Thread.MemoryBarrier(); if(seq!=memory.ReadInt32(0))return;
        Travel=distance;Landed=(stance&1)!=0;DisplayYaw=(stance&0x400)!=0?(stance>>1)&511:null;
        if(resized)bitmap=new WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null);
        bitmap.WritePixels(new Int32Rect(0,0,w,h),raw,w*4,0);frame=seq;FrameSerial=serial;
        memory.Write(DisplayCounter,++submitted);
        if(resized || lastRegion!=region){
            lastRegion=region;
            FaceBrush.ImageSource=bitmap;
            FaceBrush.Viewbox=Crop(region,w,h,1,.47);
            PreviewRegion=Crop(region,w,h,1.85,.27);
            ImageChanged?.Invoke();
        }
    }
    static Rect Crop(int region,int w,int h,double zoomOut,double eyeHeight){
        if(region==0)return new Rect(0,0,1,1);
        double x=(region&1023)/1023d,y=((region>>10)&1023)/1023d;
        double width=Math.Clamp(((region>>20)&1023)/1023d*zoomOut,1d/w,1);
        double height=width*w/h;
        return new Rect(Math.Clamp(x-width/2,0,1-width),Math.Clamp(y-height*eyeHeight,0,1-height),width,height);
    }
    public async Task Load(string path,int yaw,int lightPercent=100){
        var token=lifetime.Token;token.ThrowIfCancellationRequested();
        int n=++serial;pulse=0;var file=Path.Combine(session,"load.json");
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(new {serial=n,path,lightPercent}));File.Move(file+".tmp",file,true);
        var reply=Path.Combine(session,"reply-"+n+".json");
        while(!File.Exists(reply)) { if(process.HasExited)throw new IOException("Unity 无法载入角色，请检查 renderer.log。"); await Task.Delay(80,token); }
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(reply,token));
        token.ThrowIfCancellationRequested();
        var r=doc.RootElement; if(!r.GetProperty("ok").GetBoolean())throw new InvalidDataException(r.GetProperty("error").GetString());
        var nextMenu=r.TryGetProperty("menu",out var m)?m.Deserialize<Options>()??new():new();
        Turn(yaw);
        var deadline=Stopwatch.StartNew();
        while(FrameSerial!=n && deadline.Elapsed<TimeSpan.FromSeconds(20))await Task.Delay(40,token);
        token.ThrowIfCancellationRequested();
        if(FrameSerial!=n)throw new IOException("Unity 已读取模型，但没有返回画面，请检查显卡支持。");
        Menu=nextMenu;
    }
    public void Thumbnail(string path){
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())context.DrawRectangle(FaceBrush,null,new Rect(0,0,144,144));
        var small=new RenderTargetBitmap(144,144,96,96,PixelFormats.Pbgra32);
        small.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(small));
        string temp=path+".tmp";
        try{using(var file=File.Create(temp))encoder.Save(file);File.Move(temp,path,true);}
        finally{File.Delete(temp);}
    }
    void Write(int offset,int value){if(!stopping)memory.Write(offset,value);}
    public void Turn(int degrees)=>Write(8,((++yawCommand&0x1fffff)<<9)|((degrees%360+360)%360));
    public void Activity(int mode)=>Write(12,mode);
    public void Viewport(double pixels,bool adaptive){
        int width=Math.Clamp((int)Math.Ceiling(pixels/9)*9,9,Widths[^1]);
        Write(40,width|0xe0000|(adaptive?0x10000:0));
    }
    public void Controls(Character c,bool play=false){
        if(stopping)return;
        if(play)++pulse;
        var cmd=new{serial,seq=++controlSeq,pulse,lightPercent=c.LightPercent,action=c.Action,loop=c.Loop,face=c.Face,wear=c.Wear.Select(k=>new{id=k.Key,on=k.Value}).ToArray(),parameters=c.Parameters.Select(k=>new{name=k.Key,value=k.Value}).ToArray()};
        string file=Path.Combine(session,"controls.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(cmd));File.Move(file+".tmp",file,true);memory.Write(36,controlSeq);
    }
    public void Quality(int level)=>Write(16,Math.Clamp(level,0,Widths.Length-1));
    public void CloseUp(bool close)=>Write(28,close?1:0);
    public void Carry(double horizontalWidths){if(stopping)return;memory.Write(48,(int)Math.Clamp(horizontalWidths*4096,int.MinValue,int.MaxValue));memory.Write(52,4);}
    public void Pointer(Point position,bool pressed,bool over,bool begin=false){
        if(stopping)return;
        int x=(short)Math.Clamp(Math.Round(position.X*8192),short.MinValue,short.MaxValue);
        int y=(short)Math.Clamp(Math.Round((1-position.Y)*8192),short.MinValue,short.MaxValue);
        int packed=(x&0xffff)|(y<<16);
        memory.Write(48,packed);memory.Write(52,(pressed?1:0)|(over?2:0));
        if(begin){memory.Write(44,packed);Thread.MemoryBarrier();memory.Write(4,++tap);}
    }
    public void BeginShutdown(){
        if(stopping)return;
        stopping=true;lifetime.Cancel();
        timer.Stop();CompositionTarget.Rendering-=OnRender;
    }
    public void Dispose(){
        if(disposed)return;
        BeginShutdown();disposed=true;
        try {File.WriteAllText(Path.Combine(session,"quit"),"");if(!process.HasExited&&!process.WaitForExit(2000))process.Kill(true);}catch(InvalidOperationException){}
        process.Dispose();memory.Dispose();map.Dispose();lifetime.Dispose();
        try{Directory.Delete(session,true);}catch(IOException){}
    }
}
