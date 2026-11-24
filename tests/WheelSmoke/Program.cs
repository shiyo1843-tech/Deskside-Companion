using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPet;
using Target=System.Windows.Shapes.Path;

static class Program {
    static readonly string[] home={"角色菜单","表情","衣装","聊天","动作","桌宠设置"};
    static readonly List<string> checks=new();
    static readonly List<string> hitMaps=new();
    static string outDir="",before="";
    static void Ok(bool pass,string what){if(!pass)throw new Exception(what);checks.Add(what);}
    static void Layout(Canvas c){c.Measure(new Size(410,410));c.Arrange(new Rect(0,0,410,410));c.UpdateLayout();}
    // Window is deliberately not shown. InputHitTest rejects its whole invisible HWND;
    // use WPF's same geometry hit-test, skipping non-input decoration as WPF normally does.
    static Visual? Hit(Canvas c,Point point){
        Visual? hit=null;
        VisualTreeHelper.HitTest(c,o=>o is UIElement e && !e.IsHitTestVisible?HitTestFilterBehavior.ContinueSkipSelfAndChildren:HitTestFilterBehavior.Continue,
            r=>{hit=r.VisualHit as Visual;return HitTestResultBehavior.Stop;},new PointHitTestParameters(point));return hit;
    }
    static List<Target> Targets(Canvas c)=>c.Children.OfType<Target>().Where(x=>AutomationProperties.GetName(x)!="").ToList();
    static void Names(Canvas c,params string[] names)=>Ok(Targets(c).Select(AutomationProperties.GetName).SequenceEqual(names),"labels/order: "+string.Join(" / ",names));
    static Target Item(Canvas c,string name)=>Targets(c).Single(x=>AutomationProperties.GetName(x)==name);
    static void MouseButton(UIElement e,RoutedEvent evt)=>e.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount, System.Windows.Input.MouseButton.Left){RoutedEvent=evt});
    static void Click(Canvas c,string name)=>MouseButton(Item(c,name),UIElement.MouseLeftButtonUpEvent);
    static void Hover(UIElement e,bool on)=>e.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=on?Mouse.MouseEnterEvent:Mouse.MouseLeaveEvent});
    static void Center(Canvas c)=>MouseButton(c.Children.OfType<Border>().Single(x=>x.Cursor==Cursors.Hand),UIElement.MouseLeftButtonUpEvent);
    static RenderTargetBitmap Render(Canvas c){Layout(c);var r=new RenderTargetBitmap(820,820,192,192,PixelFormats.Pbgra32);r.Render(c);return r;}
    static byte[] Pixels(BitmapSource r){byte[] v=new byte[r.PixelWidth*r.PixelHeight*4];r.CopyPixels(v,4*r.PixelWidth,0);return v;}
    static string Fingerprint(Canvas c)=>Convert.ToHexString(SHA256.HashData(Pixels(Render(c))));
    static void Image(Canvas c,string name,bool unchanged=false,bool enamel=false){
        var r=Render(c);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(r));
        var f=Path.Combine(outDir,name+".png");using(var stream=File.Create(f))enc.Save(stream);
        // The deterministic encoded PNGs must match byte for byte (including alpha). Comparing a
        // freshly rendered premultiplied buffer to a decoded PNG would wrongly count rounding.
        if(unchanged && before.Length!=0)Ok(File.ReadAllBytes(Path.Combine(before,name+".png")).SequenceEqual(File.ReadAllBytes(f)),"unchanged pixels: "+name);
        if(enamel && before.Length!=0)Ok(!File.ReadAllBytes(Path.Combine(before,name+".png")).SequenceEqual(File.ReadAllBytes(f)),"enamel applied only to admitted path: "+name);
        hitMaps.Add(name);
        foreach(var s in Targets(c))hitMaps.Add(AutomationProperties.GetName(s)+"\t"+s.Data.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\t"+s.StrokeThickness+"\t"+s.Cursor);
    }
    static string OwnIcon(){var p=new byte[]{0x35,0xB1,0x6B,0xff,0x24,0x52,0x99,0xff};var b=BitmapSource.Create(2,1,96,96,PixelFormats.Bgra32,null,p,8);
        var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(b));using var m=new MemoryStream();enc.Save(m);return Convert.ToBase64String(m.ToArray());}
    static WheelNode Node(string id,string parent,string label,string kind,string param,string icon="",bool enabled=true,string[]? axes=null)=>new(id,parent,label,kind,param,icon,axes??Array.Empty<string>(),new[]{"横向","纵向","第三轴","第四轴"},1,0,enabled);
    static void WaitForPulse(){var fr=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(320)};t.Tick+=(_,_)=>{t.Stop();fr.Continue=false;};t.Start();Dispatcher.PushFrame(fr);}
    [STAThread]static int Main(string[] args){
        try {new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            outDir=Path.GetFullPath(args[0]);Directory.CreateDirectory(outDir);before=args.Length>1 && !args[1].StartsWith("--")?Path.GetFullPath(args[1]):"";
            bool baseline=args.Contains("--baseline");
            var p=new PetWindow();var c=new Character("fixture","桌边伙伴");p.Active=c;p.Store.Data.Characters.Add(c);var m=p.MenuOptions;
            m.actions.Add(new("wave","挥手"));m.clothes.Add(new("coat","测试外套",true));m.clothes.Add(new("extra","测试网格",true,true));
            for(int i=1;i<=10;i++)m.faces.Add(new("f"+i,"测试表情"+i));m.faces.Add(new("long","这是一个必须完整保留的模型原始表情标签名称"));
            p.Store.Data.Characters.Add(new("second","备用角色"));
            m.nodes.AddRange(new[]{Node("sub","","模型原生组","SubMenu","group",OwnIcon()),Node("sw","sub","作者开关","Toggle","switch"),Node("btn","sub","瞬时按钮","Button","button"),Node("pu","sub","二维控制","TwoAxisPuppet","axis-parent",axes:new[]{"x","y"}),Node("four","sub","四轴控制","FourAxisPuppet","four-parent",axes:new[]{"a","b","c","d"}),Node("locked","sub","不可用项","Toggle","blocked",enabled:false)});
            m.nodes.Add(Node("depth","sub","表情","SubMenu","depth",OwnIcon()));
            m.nodes.Add(Node("radial","depth","作者滑杆","RadialPuppet","radial-parent",axes:new[]{"axis"}));
            for(int i=1;i<=9;i++)m.nodes.Add(Node("dep"+i,"depth","作者编号"+i,"Toggle","model"+i,OwnIcon()));
            var w=new Wheel(p);var canvas=(Canvas)((Viewbox)w.Content).Child;w.Draw();
            Names(canvas,home);Image(canvas,"home",true);
            var pixels=Pixels(Render(canvas));int pix=(2*36*820+2*205)*4;
            bool light=pixels[pix]>175 && pixels[pix+1]>165 && pixels[pix+2]>170 && pixels[pix+3]>240;
            foreach(var (name,i) in home.Select((n,i)=>(n,i))){double a=(-90+i*60)*Math.PI/180;
                foreach(double radius in new[]{65d,132d,191d})Ok(ReferenceEquals(Hit(canvas,new Point(205+radius*Math.Cos(a),205+radius*Math.Sin(a))),Item(canvas,name)),"hit original sector "+name+" @"+radius);}
            var top=Item(canvas,home[0]);var neutral=Fingerprint(canvas);Hover(top,true);var hot=Fingerprint(canvas);
            Ok(hot!=neutral,"hover lights active home wedge");Image(canvas,"home-hover",true);
            MouseButton(top,UIElement.MouseLeftButtonDownEvent);var pressed=Fingerprint(canvas);Hover(top,false);
            Click(canvas,"聊天");Ok(p.LastTab=="聊天","chat opens original chat tab directly");Names(canvas,home);
            Click(canvas,"桌宠设置");Image(canvas,"settings",enamel:true);Names(canvas,"切换角色","导入模型","清晰度 / 大小","聊天服务","半身近景","窗口置顶","退出桌宠");
            Click(canvas,"半身近景");Ok(p.Store.Data.CloseUp && Targets(canvas).Any(x=>AutomationProperties.GetName(x)=="显示全身"),"same setting switches to full body");Click(canvas,"显示全身");
            Click(canvas,"窗口置顶");Ok(p.Topmost,"same pin callback");Image(canvas,"settings-checked",enamel:true);Click(canvas,"窗口置顶");
            Click(canvas,"清晰度 / 大小");Ok(p.LastTab=="显示","settings opens the same Display tab");
            w.RefreshModel();Click(canvas,"桌宠设置");Click(canvas,"切换角色");Names(canvas,"桌边伙伴","备用角色");Image(canvas,"characters",enamel:true);
            Click(canvas,"备用角色");Ok(p.Active?.Id=="second","character selection reaches original id");p.Active=c;w.RefreshModel();
            Click(canvas,"表情");Image(canvas,"faces-page1",enamel:true);Names(canvas,"跟随动作","测试表情1","测试表情2","测试表情3","测试表情4","测试表情5","上一页","下一页");
            Click(canvas,"下一页");Image(canvas,"faces-page2",enamel:true);Click(canvas,"测试表情9");Ok(c.Face=="f9","face selection writes real option ID");Image(canvas,"faces-on",enamel:true);
            Click(canvas,"测试表情9");Ok(c.Face=="","same face toggles off");Click(canvas,"这是一个必须完整保留的模型原始表情标签名称");Ok(c.Face=="long","long author label remains exact and selectable");
            Click(canvas,"这是一个必须完整保留的模型原始表情标签名称");Click(canvas,"上一页");Center(canvas);
            Click(canvas,"衣装");Image(canvas,"outfit",enamel:true);Click(canvas,"测试外套");Ok(c.Wear["coat"]==false,"clothing respects true initial value");Center(canvas);
            Click(canvas,"动作");Image(canvas,"actions",enamel:true);Click(canvas,"待机姿势");Image(canvas,"idle",enamel:true);Names(canvas,"自动变换","站着","蹲下","趴着看四周");
            Center(canvas);Click(canvas,"挥手");Ok(p.LastAction=="wave","action uses real id");Click(canvas,"持续播放");Ok(c.Loop,"loop checked");Click(canvas,"回到待机");Ok(!c.Loop && c.Action=="","reset to idle");Center(canvas);
            Click(canvas,"角色菜单");Image(canvas,"model-root",true);Ok(canvas.Children.OfType<System.Windows.Controls.Image>().Any(x=>(x.Source as BitmapSource)?.PixelWidth==2),"author custom image preserved");
            Click(canvas,"模型原生组");Image(canvas,"model-sub",true);Ok(c.Parameters["group"]==1,"sub-menu parent param set");
            Click(canvas,"表情");Image(canvas,"author-same-label-folder",true);Ok(c.Parameters["depth"]==1,"author folder with same label keeps real nesting");
            Click(canvas,"作者滑杆");Image(canvas,"puppet-radial",true);var axis=canvas.Children.OfType<Slider>().Single();axis.Value=.62;
            Ok(c.Parameters["axis"]==.62f,"radial puppet keeps exact axis");Center(canvas);Ok(c.Parameters["radial-parent"]==0,"radial return resets parent");
            Click(canvas,"下一页");Image(canvas,"author-second-page",true);Click(canvas,"作者编号8");Ok(c.Parameters["model8"]==1,"author pager keeps exact options");
            Center(canvas);Ok(c.Parameters["depth"]==0,"native nested group reset on Back");
            Click(canvas,"不可用项");Ok(!c.Parameters.ContainsKey("blocked"),"disabled model param never written");
            Click(canvas,"作者开关");Ok(c.Parameters["switch"]==1,"model toggle turns on");Image(canvas,"model-toggle",true);Click(canvas,"作者开关");Ok(c.Parameters["switch"]==0,"model toggle turns off");
            Click(canvas,"瞬时按钮");Ok(c.Parameters["button"]==1,"button pulse begins");WaitForPulse();Ok(c.Parameters["button"]==0,"button pulse ends");
            Click(canvas,"二维控制");Image(canvas,"puppet-two",true);var slider=canvas.Children.OfType<Slider>().First();Ok(slider.Minimum==-1 && slider.Maximum==1,"two-axis range unchanged");slider.Value=-.5;Ok(c.Parameters["x"]==-.5f,"two-axis real value");Center(canvas);Ok(c.Parameters["axis-parent"]==0,"leaving puppet resets parent");
            Click(canvas,"四轴控制");Image(canvas,"puppet-four",true);Ok(canvas.Children.OfType<Slider>().Count()==4 && canvas.Children.OfType<Slider>().All(s=>s.Minimum==0),"four-axis ranges unchanged");
            w.Dismiss();Ok(c.Parameters["four-parent"]==0 && c.Parameters["group"]==0,"dismiss unwinds all active parent params");
            w.RefreshModel();Click(canvas,"角色菜单");Click(canvas,"动作与姿势");Image(canvas,"shared-actions-via-author",true);
            Click(canvas,"待机姿势");Image(canvas,"shared-idle-via-author",true);Center(canvas);Center(canvas);Center(canvas);
            m.nodes.Clear();w.RefreshModel();Click(canvas,"角色菜单");Image(canvas,"model-fallback",true);Names(canvas,"模型表情","动作与姿势","衣装与配件","其他网格");
            Click(canvas,"模型表情");Image(canvas,"shared-faces-via-model",true);Center(canvas);
            Click(canvas,"衣装与配件");Image(canvas,"garments-via-model",true);Center(canvas);
            Click(canvas,"其他网格");Image(canvas,"parts-via-model",true);Center(canvas);
            Click(canvas,"动作与姿势");Image(canvas,"shared-actions-via-model",true);Click(canvas,"待机姿势");Image(canvas,"shared-idle-via-model",true);
            var emb=new Wheel(p);var ec=(Canvas)((Viewbox)emb.Embed()).Child;emb.Draw();Click(ec,"动作");Click(ec,"待机姿势");Click(ec,"蹲下");Ok(c.IdlePose==4,"embedded idle selects same original pose");Center(ec);Center(ec);Center(ec);Ok(p.LastTab=="角色","embedded home center opens character list");
            var no=new PetWindow();var empty=new Wheel(no);var en=(Canvas)((Viewbox)empty.Content).Child;empty.Draw();Names(en,home);Image(en,"home-disabled",true);string disabled=Fingerprint(en);Hover(Item(en,"表情"),true);
            Ok(Fingerprint(en)==disabled,"disabled button does not light on hover");Click(en,"表情");Click(en,"聊天");Ok(no.LastTab==null,"disabled chat never opens");Names(en,home);
            if(!baseline){Ok(light,"home top tile changed to approved ice/lilac palette");Ok(pressed!=hot,"pressed state differs from hover");}
            string hits=Path.Combine(outDir,"hit-maps.txt");File.WriteAllLines(hits,hitMaps);
            if(before.Length!=0)Ok(File.ReadAllBytes(hits).SequenceEqual(File.ReadAllBytes(Path.Combine(before,"hit-maps.txt"))),"all original hit paths, thickness, cursor and exact labels byte-identical");
            File.WriteAllLines(Path.Combine(outDir,"assertions.txt"),checks);Console.WriteLine("PASS "+checks.Count+" assertions, "+Directory.GetFiles(outDir,"*.png").Length+" app-only PNGs");return 0;
        }catch(Exception e){Console.Error.WriteLine("FAIL after "+checks.Count+" assertions: "+e);return 1;}
    }
}
