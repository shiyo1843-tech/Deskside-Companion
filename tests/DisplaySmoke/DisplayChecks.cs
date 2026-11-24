using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskPet;

static class DisplayChecks {
    const string LowerTitle="桌面大小";
    static readonly List<string> results=new();
    static void Assert(bool condition,string label){if(!condition)throw new InvalidOperationException(label);results.Add("PASS "+label);}
    static IEnumerable<DependencyObject> Tree(DependencyObject root){
        yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var n in Tree(VisualTreeHelper.GetChild(root,i)))yield return n;
    }
    static void Snapshot(Studio window,string name,string dir,double? rasterScale=null,Rect? region=null){
        var visual=(FrameworkElement)window.Content;window.UpdateLayout();
        var area=new Rect(0,0,visual.ActualWidth,visual.ActualHeight);
        var scale=rasterScale??VisualTreeHelper.GetDpi(window).DpiScaleX;
        // The window owns its background, not its Content. Composite those two actual WPF
        // properties; render the whole visual before trimming a section to avoid scroll transforms.
        DrawingVisual copy=new();RenderOptions.SetClearTypeHint(copy,ClearTypeHint.Enabled);
        using(var dc=copy.RenderOpen()){
            dc.DrawRectangle(window.Background,null,area);dc.DrawRectangle(new VisualBrush(visual){Stretch=Stretch.Fill},null,area);
        }
        var b=new RenderTargetBitmap((int)Math.Ceiling(area.Width*scale),(int)Math.Ceiling(area.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);b.Render(copy);
        int Px(double value)=>(int)Math.Round(value*scale,MidpointRounding.AwayFromZero);
        BitmapSource output=region is Rect r?new CroppedBitmap(b,new Int32Rect(Px(r.X),Px(r.Y),Px(r.Width),Px(r.Height))):b;
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(output));using var f=File.Create(Path.Combine(dir,name+".png"));encoder.Save(f);
    }
    static IEnumerable<string> Values(ComboBox c)=>c.Items.Cast<object>().Select(x=>x.ToString()!);
    static T Peer<T>(UIElement e,PatternInterface pattern) where T:class =>
        (T)(UIElementAutomationPeer.CreatePeerForElement(e)?.GetPattern(pattern)??throw new InvalidOperationException("Missing automation pattern: "+pattern));
    static void Key(UIElement control,Key key){
        var source=PresentationSource.FromVisual(control)??throw new InvalidOperationException("Control is not in the real window");
        control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key){RoutedEvent=UIElement.KeyDownEvent});
    }
    static ComboBox Combo(DependencyObject p,string id)=>Tree(p).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)==id);
    static CheckBox Check(DependencyObject p,string name)=>Tree(p).OfType<CheckBox>().Single(x=>x.Content?.ToString()==name);
    static void Toggle(CheckBox c){
        _=Peer<IToggleProvider>(c,PatternInterface.Toggle);
        // Native mouse/space calls this on the real CheckBox, toggling AND raising Click.
        // The automation toggle provider alone flips IsChecked without raising Click.
        typeof(ToggleButton).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(c,null);
    }
    static async Task Pick(ComboBox c,int index){
        var pop=Peer<IExpandCollapseProvider>(c,PatternInterface.ExpandCollapse);pop.Expand();await Task.Delay(110);
        var popup=(Popup?)c.Template.FindName("PART_Popup",c);
        if(popup?.IsOpen!=true)throw new InvalidOperationException("real combo box popup did not open");
        var item=c.ItemContainerGenerator.ContainerFromIndex(index) as ComboBoxItem??throw new InvalidOperationException("Popup item not visible");
        var panel=ItemsControl.ItemsControlFromItemContainer(item);
        if(!ReferenceEquals(panel,c))throw new InvalidOperationException("Lost original item container");
        var listPeer=(ComboBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(c)!;
        var child=listPeer.GetChildren().Single(p=>p.GetName()==item.Content?.ToString());
        ((ISelectionItemProvider)child.GetPattern(PatternInterface.SelectionItem)!).Select();pop.Collapse();
        if(c.SelectedIndex!=index)throw new InvalidOperationException("Combo item not selected");
    }
    static Rect Lower(Studio window,Panel display,TextBlock start){
        var root=(FrameworkElement)window.Content;var last=(FrameworkElement)display.Children[^1];
        double top=start.TransformToAncestor(root).Transform(new Point(0,0)).Y-start.Margin.Top;
        double end=last.TransformToAncestor(root).Transform(new Point(0,last.ActualHeight)).Y+last.Margin.Bottom;
        double x=display.TransformToAncestor(root).Transform(new Point(0,0)).X;
        return new Rect(x,top,display.ActualWidth,end-top);
    }
    static void Persisted(Library lib,string key,int value){
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(lib.Root,"library.json")));
        Assert(doc.RootElement.GetProperty(key).GetInt32()==value,"persisted exact original "+key);
    }
    static void CheckToggle(CheckBox box,Func<bool> property){
        bool start=property();Assert(box.IsChecked==start,"initial "+box.Content);
        Toggle(box);Assert(box.IsChecked==!start && property()==!start,"toggle "+box.Content);
        Toggle(box);Assert(box.IsChecked==start && property()==start,"restore "+box.Content);
    }
    public static int Run(string outDir,bool baseline,string compare){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        string profile=Path.Combine(outDir,"profile-"+Guid.NewGuid().ToString("N"));var lib=new Library(profile);
        lib.Data.Detail=3;lib.Data.ActivityFrequency=2;lib.Data.Pinned=false;lib.Data.Roam=false;
        var pet=new PetWindow(lib,Array.Empty<string>());Exception? failure=null;
        pet.Loaded+=async(_,_)=>{
            try{
                var sw=Stopwatch.StartNew();while((pet.Active==null || pet.Busy)&&sw.ElapsedMilliseconds<55000)await Task.Delay(80);
                Assert(pet.Active?.Name=="云团" && !pet.Busy,"real Unity sample activated and rendered");
                var p=pet.Panel;p.Go("显示");await Task.Delay(800);p.UpdateLayout();
                var tabs=Tree(p).OfType<TabControl>().Single();var sc=(ScrollViewer)((TabItem)tabs.SelectedItem).Content;
                var display=(StackPanel)sc.Content;
                var lower=display.Children.OfType<TextBlock>().Single(t=>t.Text==LowerTitle);
                var originalTail=display.Children.Cast<FrameworkElement>().SkipWhile(c=>c!=lower).ToArray();
                Assert(originalTail.Length==7 && originalTail[1] is WrapPanel s && s.Children.Count==2 && originalTail[4] is WrapPanel a && a.Children.Count==3,"original lower widgets and order");
                var q=Combo(display,"quality");var light=Combo(display,"character-light");var cadence=Tree(display).OfType<ComboBox>().Single(c=>c.Items.Count==3);
                var adapt=Check(display,"按桌面大小优化渲染");var roam=Check(display,"允许角色在桌面上走动");
                var bubbles=Check(display,"在角色旁显示聊天回复");var acts=Check(display,"让 AI 随聊天做动作和表情");
                Assert(Values(q).SequenceEqual(new[]{"720 × 880 像素","1080 × 1320 像素","1440 × 1760 像素","2160 × 2640 像素"}),"original resolutions");
                Assert(Values(light).Contains("100% · 模型默认") && light.IsEnabled,"original brightness values on active model");
                Assert(Values(cadence).SequenceEqual(new[]{"安静 · 45–80 秒","适中 · 22–40 秒","活泼 · 10–20 秒"}),"original cadence values");
                TextBlock? fps=null;sw.Restart();
                while(sw.ElapsedMilliseconds<4500) {
                    fps=Tree(display).OfType<TextBlock>().FirstOrDefault(t=>t.Text.StartsWith("Unity ") && t.Text.Contains("桌面提交"));
                    if(fps!=null && !fps.Text.StartsWith("Unity 0 ") && !fps.Text.Contains("桌面提交 0 "))break;
                    await Task.Delay(95);
                }
                Assert(fps!=null && !fps.Text.StartsWith("Unity 0 ") && !fps.Text.Contains("桌面提交 0 "),"original live FPS has measured render and display output");
                var dpi=VisualTreeHelper.GetDpi(p);results.Add($"Real HWND layout/capture DPI {dpi.PixelsPerInchX}/{dpi.PixelsPerInchY}; additional 144 DPI narrow raster.");
                Snapshot(p,"display-760x510",outDir);
                sc.ScrollToEnd();await Task.Delay(75);p.UpdateLayout();
                Snapshot(p,"lower-760x510",outDir,region:Lower(p,display,lower));
                Snapshot(p,"display-scrolled-760x510",outDir);
                p.Width=p.MinWidth;p.Height=p.MinHeight;sc.ScrollToTop();await Task.Delay(90);p.UpdateLayout();
                Snapshot(p,"display-narrow-635x440",outDir);
                Snapshot(p,"display-narrow-150pct",outDir,1.5);
                Assert(q.ActualWidth >= 130 && cadence.ActualWidth >=130,"resolution and cadence fit at minimum window width");
                var cap=Tree(display).OfType<TextBlock>().Single(x=>x.Text=="当前角色的灯光亮度");
                Assert(cap.ActualWidth>75 && cap.ActualHeight>=cap.FontSize,"brightness label has room to wrap");
                sc.ScrollToEnd();await Task.Delay(60);p.UpdateLayout();Snapshot(p,"lower-635x440",outDir,region:Lower(p,display,lower));
                sc.ScrollToTop();p.Width=760;p.Height=510;await Task.Delay(70);p.UpdateLayout();
                if(!baseline) {
                    Assert(display.Children[0] is Border && display.Children[1] is Border && ReferenceEquals(display.Children[2],lower),"exactly two new cards above original size section");
                    Assert(Tree(display).OfType<TextBlock>().Any(t=>t.Text=="画质"),"quality heading visible");
                    Assert(q.Style!=null && q.Style==light.Style && q.Style==cadence.Style && adapt.Style!=null && adapt.Style==acts.Style,"scoped combo and checkbox templates");
                    var detailed=p.Resources.MergedDictionaries.Single(d=>d.Source?.OriginalString.Contains("Theme.xaml")==true);
                    Assert(!detailed.Contains(typeof(ComboBox)) && !detailed.Contains(typeof(CheckBox)),"no implicit global combo/checkbox styles");
                    var lowerEnd=lower.TransformToAncestor(sc).Transform(new Point(0,lower.ActualHeight)).Y;
                    Assert(lowerEnd<=sc.ViewportHeight+1,"size heading stays fully visible at default window height");
                    foreach(string name in new[]{"lower-760x510","lower-635x440"})
                        Assert(File.ReadAllBytes(Path.Combine(compare,name+".png")).SequenceEqual(File.ReadAllBytes(Path.Combine(outDir,name+".png"))),"original lower image pixel-identical: "+name);
                }
                // Runs the real native combo popup / ComboBoxItem selection and real settings handlers.
                await Pick(q,1);Assert(lib.Data.Detail==1,"resolution UI selection writes original index");Persisted(lib,"Detail",1);
                await Pick(light,2);float pct=pet.Active!.LightPercent;
                Assert(pct==80,"brightness selection writes original character property");
                await Pick(cadence,1);Assert(lib.Data.ActivityFrequency==1,"cadence UI selection sets same index");Persisted(lib,"ActivityFrequency",1);
                CheckToggle(adapt,()=>lib.Data.Adaptive);CheckToggle(roam,()=>lib.Data.Roam);
                CheckToggle(bubbles,()=>lib.Data.Bubbles);CheckToggle(acts,()=>lib.Data.ChatActions);
                q.IsDropDownOpen=true;await Task.Delay(95);Key(q,System.Windows.Input.Key.Escape);
                Assert(!q.IsDropDownOpen,"Escape closes actual drop-down");
                string? saved=lib.Data.Active;lib.Data.Active=null;p.Refresh();
                Assert(!light.IsEnabled && (string?)light.SelectedItem=="100% · 模型默认","brightness disabled at unchanged default without active model");
                lib.Data.Active=saved;p.Refresh();
                Assert(light.IsEnabled && light.SelectedIndex==2 && pet.Active?.LightPercent==pct,"brightness selection restored when model active again");
                var scale=(WrapPanel)originalTail[1];var size=lib.Data.Size;
                ((Button)scale.Children[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert(lib.Data.Size==size+48,"untouched original enlarge button still runs");
            }catch(Exception e){failure=e;}finally{pet.Exit();}
        };
        try{app.Run(pet);}finally{typeof(PetWindow).GetMethod("ReleaseRenderer",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(pet,null);}
        if(failure!=null)results.Add("FAIL "+failure);File.WriteAllLines(Path.Combine(outDir,"display-smoke.txt"),results);
        return failure==null?0:1;
    }
}
