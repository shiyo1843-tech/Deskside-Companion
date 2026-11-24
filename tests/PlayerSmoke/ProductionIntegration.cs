using System.Diagnostics;
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

static class ProductionIntegration {
    static Canvas CanvasOf(Wheel w)=>(Canvas)((Viewbox)w.Content).Child;
    static List<ShapePath> Hits(Canvas c)=>c.Children.OfType<ShapePath>().Where(s=>AutomationProperties.GetName(s)!="").ToList();
    static void Dispatch(ShapePath s){
        foreach(var evt in new[]{UIElement.MouseLeftButtonDownEvent,UIElement.MouseLeftButtonUpEvent})
            s.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=evt});
    }
    static ShapePath ByName(Canvas c,string name)=>Hits(c).Single(p=>AutomationProperties.GetName(p)==name);
    static void Key(Wheel w,Key key)=>w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w),Environment.TickCount,key){RoutedEvent=UIElement.PreviewKeyDownEvent});
    static void View(Canvas c,string file){
        c.UpdateLayout();var r=new RenderTargetBitmap(820,820,192,192,PixelFormats.Pbgra32);r.Render(c);
        var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(r));using var f=File.Create(file);e.Save(f);
    }
    public static void Run(string run,Action<bool,string> ok){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var lib=new Library(Path.Combine(run,"integration-profile"));lib.Data.Detail=0;lib.Data.Roam=false;lib.Data.Pinned=false;
        var pet=new PetWindow(lib,Array.Empty<string>());Exception? failure=null;
        pet.Loaded+=async(_,_)=>{
            try {
                var t=Stopwatch.StartNew();while((pet.Active==null || pet.Busy) && t.ElapsedMilliseconds<65000)await Task.Delay(80);
                ok(pet.Active?.Name=="云团" && !pet.Busy,"production WPF types connected to the built Unity renderer and its actual generic model menu");
                Wheel w=pet.Wheel;w.Present();var c=CanvasOf(w);await Task.Delay(100);c.UpdateLayout();
                View(c,Path.Combine(run,"actual-floating-home.png"));
                void HomeClick(int i,string name){
                    c.UpdateLayout(); // A preceding Back() synchronously replaces the visual tree.
                    double a=(-90+i*60)*Math.PI/180;var pos=new Point(205+Math.Cos(a)*130,205+Math.Sin(a)*130);
                    var hit=c.InputHitTest(pos) as ShapePath;ok(hit!=null && AutomationProperties.GetName(hit)==name,"real WPF input hit-test: "+name);Dispatch(hit!);
                }
                HomeClick(5,"桌宠设置");ok(Hits(c).Any(p=>AutomationProperties.GetName(p)=="切换角色"),"original settings opened");
                Key(w,System.Windows.Input.Key.Back);ok(Hits(c).Any(p=>AutomationProperties.GetName(p)=="角色菜单"),"Backspace returns to real root");
                HomeClick(0,"角色菜单");var model=Hits(c).Select(AutomationProperties.GetName).ToArray();
                ok(model.Contains("动作与姿势"),"actual model menu fallback follows renderer capabilities");
                View(c,Path.Combine(run,"actual-model-menu.png"));Dispatch(ByName(c,"动作与姿势"));
                var wave=pet.MenuOptions.actions.First();Dispatch(ByName(c,wave.label));
                ok(pet.Active!.Action==wave.id,"actual model motion sets the renderer's action ID: "+wave.id);
                Key(w,System.Windows.Input.Key.Escape);ok(Hits(c).Any(p=>AutomationProperties.GetName(p)=="动作与姿势"),"Escape returns from action page to the real model menu");
                Key(w,System.Windows.Input.Key.Escape);HomeClick(3,"聊天");ok(!w.IsVisible && pet.Panel.ReadingChat,"chat opens original Studio chat tab directly, without sending a message");
                w.Present();Key(w,System.Windows.Input.Key.Escape);ok(!w.IsVisible,"Escape on floating root closes it");
                pet.Panel.Go("轮盘");var tabs=Tree(pet.Panel).OfType<TabControl>().First();
                var tab=(TabItem)tabs.SelectedItem;var embed=(Canvas)((Viewbox)tab.Content).Child;embed.UpdateLayout();var goBack=embed.Children.OfType<Border>().Single(b=>b.Cursor==Cursors.Hand);
                goBack.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
                ok((tabs.SelectedItem as TabItem)?.Header?.ToString()=="角色","embedded center returns to original character list");
            }catch(Exception e){failure=e;}finally{pet.Exit();}
        };
        try {app.Run(pet);}finally{
            typeof(PetWindow).GetMethod("ReleaseRenderer",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,null);
        }
        if(failure!=null)throw new Exception("actual host/renderer integration failed",failure);
    }
    static IEnumerable<DependencyObject> Tree(DependencyObject root){
        yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var n in Tree(VisualTreeHelper.GetChild(root,i)))yield return n;
    }
}
