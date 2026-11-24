using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPet;
using ShapePath=System.Windows.Shapes.Path;

static class LiveWheelChecks {
    static readonly string[] actual={"faces","outfit","actions","idle","settings","characters"};
    static readonly string[] captions={"表情","衣装与网格","动作","待机方式","桌宠设置","切换角色"};
    static readonly List<string> log=new();
    static void Ok(bool condition,string s){if(!condition)throw new InvalidOperationException(s);log.Add("PASS "+s);}
    static List<ShapePath> Targets(Canvas c)=>c.Children.OfType<ShapePath>().Where(x=>AutomationProperties.GetName(x)!="").ToList();
    static void Button(UIElement hit,RoutedEvent evt)=>hit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=evt});
    static void Select(Canvas c,string label){
        c.UpdateLayout();
        var all=Targets(c);int ix=all.FindIndex(x=>AutomationProperties.GetName(x)==label);
        if(ix<0)throw new InvalidOperationException("missing '"+label+"' in ["+string.Join(", ",all.Select(AutomationProperties.GetName))+"]");
        double a=(-90+ix*360.0/all.Count)*Math.PI/180;
        foreach(var radius in new[]{65d,130d,191d}){
            var h=c.InputHitTest(new Point(205+Math.Cos(a)*radius,205+Math.Sin(a)*radius));
            if(!ReferenceEquals(h,all[ix]))throw new InvalidOperationException("native sector hit test failed for "+label+" at "+radius);
        }
        Button(all[ix],UIElement.MouseLeftButtonDownEvent);Button(all[ix],UIElement.MouseLeftButtonUpEvent);
    }
    static void Save(BitmapSource b,string f){
        var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var stream=File.Create(f);e.Save(stream);
    }
    static void Picture(Canvas c,string dir,string name, string compare="",bool changed=false){
        c.UpdateLayout();var r=new RenderTargetBitmap(820,820,192,192,PixelFormats.Pbgra32);r.Render(c);
        string f=Path.Combine(dir,name+".png");Save(r,f);
        if(compare.Length!=0){
            string before=Path.Combine(compare,name+".png");
            Ok(File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(f))!=changed,
                (changed?"restyled native window: ":"pixel-identical real native view: ")+name);
        }
    }
    public static int Run(string dir,bool baseline,string compare){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var lib=new Library(Path.Combine(dir,"live-profile-"+Guid.NewGuid().ToString("N")));
        lib.Data.Detail=0;lib.Data.Roam=false;lib.Data.Pinned=false;var pet=new PetWindow(lib,Array.Empty<string>());Exception? failure=null;
        pet.Loaded+=async(_,_)=>{
            try {
                var t=Stopwatch.StartNew();while((pet.Active==null||pet.Busy) && t.ElapsedMilliseconds<55000)await Task.Delay(85);
                Ok(pet.Active?.Name=="云团","bundled generic sample loaded from actual Unity renderer");
                await pet.Import(GlbFaceFixture.Create(dir));
                Ok(pet.Active?.Name=="验收云团","procedural copy with real glTF morph targets loaded");
                Ok(pet.MenuOptions.faces.Any(),"Unity discovered actual expression targets");
                string other=baseline?"":compare;
                var w=pet.Wheel;w.Present();await Task.Delay(100);var c=(Canvas)((Viewbox)w.Content).Child;
                void Capture(string name,bool changed=false)=>Picture(c,dir,name,other,changed);
                void Back(){
                    c.UpdateLayout();DependencyObject? hit=c.InputHitTest(new Point(205,205)) as DependencyObject;
                    while(hit!=null && hit!=c && !(hit is Border border && border.Cursor==Cursors.Hand))
                        hit=VisualTreeHelper.GetParent(hit);
                    if(hit is not Border hub || hub.Cursor!=Cursors.Hand)throw new InvalidOperationException("center hit area lost");
                    Button(hub,UIElement.MouseLeftButtonUpEvent);
                }
                void KeyBack(Key key)=>w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w),Environment.TickCount,key){RoutedEvent=UIElement.PreviewKeyDownEvent});
                Capture("home");
                Select(c,"表情");Capture("faces",true);var face=pet.MenuOptions.faces[0];
                Select(c,face.label);Ok(pet.Active!.Face==face.id,"real expression selection sends same model ID");Select(c,face.label);Ok(pet.Active.Face=="","original expression deselects");Back();
                Select(c,"衣装");Capture("outfit",true);var clothing=pet.MenuOptions.clothes[0];
                Select(c,clothing.label);Ok(pet.Active.Wear[clothing.id]!=clothing.initial,"real model mesh visibility toggles same model ID");
                Select(c,clothing.label);Select(c,"下一页");Capture("outfit-paged",true);Select(c,"上一页");Back();
                Select(c,"动作");Capture("actions",true);var action=pet.MenuOptions.actions.First(x=>x.label=="Wave");
                Select(c,action.label);Ok(pet.Active.Action==action.id,"real action uses original imported clip");
                Select(c,"持续播放");Ok(pet.Active.Loop,"real action loop toggles");Select(c,"回到待机");Ok(!pet.Active.Loop&&pet.Active.Action=="","real action and loop reset");
                Select(c,"待机方式");Capture("idle",true);Select(c,"原地待机");Ok(pet.Active.IdlePose==6&&!w.IsVisible,"real floating idle choice keeps original state and dismisses");
                w.Present();Select(c,"桌宠设置");Capture("settings",true);
                Select(c,"窗口置顶");Ok(pet.Store.Data.Pinned,"real options pin toggles");Select(c,"窗口置顶");
                Select(c,"半身近景");Ok(pet.Store.Data.CloseUp,"real settings close-up toggles");Select(c,"显示全身");
                Select(c,"切换角色");Capture("characters",true);KeyBack(Key.Escape);Select(c,"清晰度 / 大小");
                pet.Panel.UpdateLayout();
                Ok(!w.IsVisible&&Tree(pet.Panel).OfType<TabControl>().Single().SelectedItem is TabItem tab && (string)tab.Header=="显示","real Display link goes directly to the old selected page");
                Ok(Tree(pet.Panel).OfType<TextBlock>().Any(x=>x.Text=="画质"),"previous scoped Display cards present in actual build");
                w.Present();Select(c,"角色菜单");Capture("protected-role");
                Select(c,"模型表情");Capture("protected-shared-faces");Back();
                Select(c,"动作与姿势");Capture("protected-shared-actions");Select(c,"待机方式");Capture("protected-shared-idle");Back();Back();
                if(Targets(c).Any(x=>AutomationProperties.GetName(x)=="衣装与配件")){Select(c,"衣装与配件");Capture("protected-garments");Back();}
                Select(c,"其他网格");Capture("protected-parts");Back();Back();
                Select(c,"聊天");Ok(!w.IsVisible&&pet.Panel.ReadingChat,"original chat opens directly without any message or service");
                w.Present();Select(c,"桌宠设置");Select(c,"切换角色");Select(c,"云团");
                t.Restart();while((pet.Busy||pet.Active?.Name!="云团")&&t.ElapsedMilliseconds<20000)await Task.Delay(75);
                Ok(pet.Active?.Name=="云团"&&!pet.Busy,"real character switch opens the selected character; no other data used");
                w.Present();Select(c,"表情");Ok(Targets(c).Count==6,"original model without expression support remains at home");
                KeyBack(Key.Back);Ok(!w.IsVisible,"Backspace dismisses floating root");
                pet.Panel.Go("轮盘");pet.Panel.UpdateLayout();var selected=(TabItem)Tree(pet.Panel).OfType<TabControl>().Single().SelectedItem;
                var embedded=(Canvas)((Viewbox)selected.Content).Child;Select(embedded,"桌宠设置");
                Ok(Targets(embedded).Any(x=>AutomationProperties.GetName(x)=="切换角色"),"real contained Studio sub-wheel opens the same stock settings");
            }catch(Exception e){failure=e;}
            finally{pet.Exit();}
        };
        try{app.Run(pet);}finally{typeof(PetWindow).GetMethod("ReleaseRenderer",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,null);}
        try{if(failure==null&&!baseline)Montage(dir);}catch(Exception e){failure=e;}
        if(failure!=null)log.Add("FAIL "+failure);File.WriteAllLines(Path.Combine(dir,"display-smoke.txt"),log);
        return failure==null?0:1;
    }
    static IEnumerable<DependencyObject> Tree(DependencyObject root){
        yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var n in Tree(VisualTreeHelper.GetChild(root,i)))yield return n;
    }
    static void Montage(string dir){
        const int width=1480,height=1128;var visual=new DrawingVisual();using(var g=visual.RenderOpen()){
            SolidColorBrush Paint(string v)=>new((Color)ColorConverter.ConvertFromString(v));
            void Write(string s,int font,double x,double y,string color,bool bold){
                var t=new FormattedText(s,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),
                    font,Paint(color),1.0);g.DrawText(t,new Point(x,y));
            }
            g.DrawRectangle(new LinearGradientBrush(Color.FromRgb(236,244,255),Color.FromRgb(252,240,248),40),null,new Rect(0,0,width,height));
            Write("桌边伙伴 · A 款子轮盘",36,39,25,"#322740",true);
            Write("Windows 实际界面 · 桌边伙伴自带的六个入口（示例云团）",21,40,79,"#625772",false);
            for(int i=0;i<actual.Length;i++){
                double x=36+i%3*474,y=126+i/3*485;
                var box=new Rect(x,y,460,470);
                g.DrawRoundedRectangle(Paint("#FCFAFF"),new Pen(Paint("#DFD4ED"),1),box,21,21);
                Write(captions[i],25,x+21,y+11,"#4B3A63",true);
                var b=new BitmapImage(new Uri(Path.Combine(dir,actual[i]+".png")));g.DrawImage(b,new Rect(x+27,y+49,406,406));
            }
            Write("只换桌边伙伴自带的外观；从「角色菜单」进入的模型自带页面保留原样。",19,39,1096,"#625772",false);
        }
        var r=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);r.Render(visual);
        Save(r,Path.Combine(dir,"桌边伙伴-A款子轮盘-Windows实拍.png"));
    }
}
