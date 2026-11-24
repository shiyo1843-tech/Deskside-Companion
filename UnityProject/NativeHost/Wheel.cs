using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Automation;
using ShapePath=System.Windows.Shapes.Path;

namespace DeskPet;
public sealed partial class Wheel:Window {
    record Slice(string Text,string Icon,Action Run,bool On=false,bool Enabled=true);
    readonly PetWindow pet;
    bool embedded;
    readonly Canvas canvas=new(){Width=410,Height=410};
    readonly Stack<(string route,int page,string? reset)> history=new();
    readonly Dictionary<string,ImageSource> images=new();
    string route="home",title="桌边伙伴";int page;
    static SolidColorBrush Paint(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    public Wheel(PetWindow owner){
        pet=owner;Title="桌边伙伴 · 轮盘";WindowStyle=WindowStyle.None;AllowsTransparency=true;
        ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;Background=Brushes.Transparent;Topmost=true;Width=420;Height=420;
        FontFamily=new FontFamily("Microsoft YaHei UI");Content=new Viewbox{Child=canvas,Margin=new Thickness(4)};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape || e.Key==Key.Back){Back();e.Handled=true;}};
        Closing+=(_,e)=>{if(!Application.Current.Dispatcher.HasShutdownStarted && !pet.IsExiting){e.Cancel=true;Dismiss();}};
    }
    public void Present(){
        Dismiss();Owner=pet.Panel.IsVisible?pet.Panel:pet;route="home";page=0;
        var point=pet.PointToScreen(Mouse.GetPosition(pet));var dpi=VisualTreeHelper.GetDpi(pet);
        var work=SystemParameters.WorkArea;Left=Math.Clamp(point.X/dpi.DpiScaleX-Width/2,work.Left,Math.Max(work.Left,work.Right-Width));Top=Math.Clamp(point.Y/dpi.DpiScaleY-Height/2,work.Top,Math.Max(work.Top,work.Bottom-Height));
        Draw();Show();Activate();Focus();
    }
    public UIElement Embed(){embedded=true;var visual=(UIElement)Content;Content=null;return visual;}
    public void RefreshModel(){history.Clear();route="home";page=0;Draw();}
    public void Dismiss(){while(history.Count>0){var s=history.Pop();if(s.reset!=null)Param(s.reset,0);}if(IsVisible)Hide();}
    void Next(string next,string? reset=null){history.Push((route,page,reset));route=next;page=0;Draw();}
    void Back(){if(history.Count==0){if(embedded)Hub("角色");else Dismiss();return;}var prior=history.Pop();if(prior.reset!=null)Param(prior.reset,0);route=prior.route;page=prior.page;Draw();}
    void Hub(string tab){Dismiss();pet.Open(tab);}
    static Point P(double a,double r)=>new(205+Math.Cos(a*Math.PI/180)*r,205+Math.Sin(a*Math.PI/180)*r);
    static StreamGeometry Sector(double a,double b){
        var g=new StreamGeometry();using(var c=g.Open()){
            c.BeginFigure(P(a,195),true,true);c.ArcTo(P(b,195),new Size(195,195),0,b-a>180,SweepDirection.Clockwise,true,false);
            c.LineTo(P(b,61),true,false);c.ArcTo(P(a,61),new Size(61,61),0,b-a>180,SweepDirection.Counterclockwise,true,false);
        }g.Freeze();return g;
    }
    ImageSource SliceImage(string name){
        if(images.TryGetValue(name,out var saved))return saved;
        try{
            using var stream=name.StartsWith("@")?new MemoryStream(Convert.FromBase64String(name[1..])):
                typeof(Wheel).Assembly.GetManifestResourceStream("WheelIcons."+name+".png")??throw new FileNotFoundException(name);
            var bmp=new BitmapImage();bmp.BeginInit();bmp.CacheOption=BitmapCacheOption.OnLoad;
            bmp.StreamSource=stream;bmp.EndInit();bmp.Freeze();
            if(images.Count>350)images.Clear();images[name]=bmp;return bmp;
        }catch(Exception e) when(e is IOException or FormatException or NotSupportedException or ArgumentException){
            if(name!="Default")return SliceImage("Default");
            return new DrawingImage(new GeometryDrawing(Brushes.Thistle,null,new EllipseGeometry(new Point(12,12),10,10)));
        }
    }
    void At(UIElement e,double x,double y){Canvas.SetLeft(e,x);Canvas.SetTop(e,y);canvas.Children.Add(e);}
    void Piece(Slice item,double a,double b){
        double mid=(a+b)/2;
        var normal=Paint(item.On?"#6C5076":"#42374D");var chosen=Paint("#775B81");
        var shape=new ShapePath{Data=Sector(a,b),Fill=normal,Stroke=Paint("#937C9F"),StrokeThickness=1.25,Cursor=item.Enabled?Cursors.Hand:Cursors.Arrow,Opacity=item.Enabled?1:.50};
        AutomationProperties.SetName(shape,item.Text);
        shape.ToolTip=item.Enabled?item.Text:item.Text+" · 这个参数未连接到桌宠可用的动画层";
        shape.MouseEnter+=(_,_)=>{if(item.Enabled)shape.Fill=chosen;};shape.MouseLeave+=(_,_)=>shape.Fill=normal;
        shape.MouseLeftButtonUp+=(_,e)=>{if(item.Enabled)item.Run();e.Handled=true;};canvas.Children.Add(shape);
        var center=P(mid,127);var art=new Image{Source=SliceImage(item.Icon),Width=44,Height=44,IsHitTestVisible=false,Opacity=item.Enabled?1:.55};
        At(art,center.X-22,center.Y-31);
        var label=new TextBlock{Text=(item.On?"✓ ":"")+item.Text,Foreground=Paint(item.On?"#F6DDB2":"#FFF7FF"),FontSize=14,FontWeight=FontWeights.Medium,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Width=96,IsHitTestVisible=false};At(label,center.X-48,center.Y+15);
    }
    void Center(){
        var center=new Border{Width=116,Height=116,CornerRadius=new CornerRadius(58),BorderThickness=new Thickness(2),BorderBrush=Paint("#AE95BC"),Background=Paint("#302837"),Cursor=Cursors.Hand};
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=96};
        text.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=Paint("#FEF2FC"),FontSize=15,FontWeight=FontWeights.SemiBold,MaxHeight=44});
        text.Children.Add(new TextBlock{Text=history.Count==0?(embedded?"角色列表":"收起"):"‹  返回",TextAlignment=TextAlignment.Center,Foreground=Paint("#D0B5D5"),FontSize=10,Margin=new Thickness(0,4,0,0)});
        center.Child=text;center.MouseLeftButtonUp+=(_,e)=>{e.Handled=true;Back();};At(center,147,147);
    }
    void Param(string? name,float value){if(pet.Active is not {} c || string.IsNullOrWhiteSpace(name))return;c.Parameters[name]=value;pet.ApplyControls();}
    public void Draw(){
        canvas.Children.Clear();var list=new List<Slice>();var menu=pet.MenuOptions;var c=pet.Active;
        void Add(string s,string i,Action f,bool on=false,bool enabled=true)=>list.Add(new(s,i,f,on,enabled));
        switch(route){
        case "home":title=c?.Name??"桌边伙伴";
            Add("角色菜单","Option",()=>Next(menu.nodes?.Count>0?"source":"model-controls"),enabled:c!=null);
            Add("表情","Expressions",()=>Next("faces"),enabled:menu.faces.Count>0);
            Add("衣装","Looks",()=>Next("wear"),enabled:menu.clothes.Count>0);
            Add("聊天","Emojis",()=>Hub("聊天"),enabled:c!=null);
            Add("动作","Tool_Pose",()=>Next("moves"),enabled:c!=null);
            Add("桌宠设置","Tools",()=>Next("options"));break;
        case "model-controls":title="模型可用项目";
            if(menu.faces.Count>0)Add("模型表情","Expressions",()=>Next("faces"));
            if(c!=null)Add("动作与姿势","Tool_Pose",()=>Next("moves"));
            if(menu.clothes.Any(o=>!o.advanced))Add("衣装与配件","Looks",()=>Next("garments"));
            if(menu.clothes.Any(o=>o.advanced))Add("其他网格","Clones",()=>Next("parts"));
            break;
        case "options":title="桌宠设置";
            Add("切换角色","Clones",()=>Next("characters"));Add("导入模型","Avatar_Height",()=>Hub("角色"));Add("清晰度 / 大小","Gear",()=>Hub("显示"));
            Add("聊天服务","Option",()=>Hub("服务"));Add(pet.Store.Data.CloseUp?"显示全身":"半身近景","HeadHands",()=>{pet.SetClose(!pet.Store.Data.CloseUp);Draw();});
            Add("窗口置顶","Toggle",()=>{pet.TogglePin();Draw();},pet.Topmost);Add("退出桌宠","Back",()=>pet.Exit());break;
        case "characters":title="切换角色";foreach(var ch in pet.Store.Data.Characters)Add(ch.Name,"Clones",async()=>{Dismiss();await pet.Select(ch);},ch.Id==c?.Id);break;
        case "rest":title=menu.humanoidMotion?"待机姿势":"待机方式";if(c!=null){
            var choices=menu.humanoidMotion?new[]{(0,"自动变换"),(6,"站着"),(4,"蹲下"),(5,"趴着看四周")}:new[]{(0,"自动活动"),(6,"原地待机")};
            foreach(var (pose,label) in choices)
                Add(label,"Tool_Pose",()=>{pet.ChooseIdle(pose);pet.Panel.Status("待机 · "+label);if(embedded)Draw();else Dismiss();},pet.SupportedIdle(c.IdlePose)==pose);
            }break;
        case "moves":title="动作";if(c!=null){
            Add(menu.humanoidMotion?"待机姿势":"待机方式","Tool_Pose",()=>Next("rest"));
            var bounce=menu.actions.FirstOrDefault(o=>o.id==ReactionMenu.BounceId);
            if(bounce!=null || !menu.humanoidMotion){
                string label=bounce?.label??"轻跳";
                Add(label,"Tool_Pose",()=>{pet.PlayMenuAction(ReactionMenu.BounceId);StatusAndDraw(label);});
            }
            foreach(var o in menu.actions.Where(o=>o.id!=ReactionMenu.BounceId))Add(o.label,"Tool_Pose",()=>{pet.PlayMenuAction(o.id);StatusAndDraw(o.label);});
            if(menu.actions.Count>0)Add("持续播放","Radial",()=>{c.Loop=!c.Loop;pet.ApplyControls(true);Draw();},c.Loop);
            Add("回到待机","Reset",()=>{c.Action="";c.Loop=false;pet.ApplyControls(true);Draw();});}break;
        case "faces":title="表情";if(c!=null){Add("跟随动作","Reset",()=>{c.Face="";pet.ApplyControls();Draw();},c.Face=="");
            foreach(var o in menu.faces)Add(o.label,"Expressions",()=>{c.Face=c.Face==o.id?"":o.id;pet.ApplyControls();Draw();},c.Face==o.id);}break;
        case "wear":case "garments":case "parts":
            title=route=="parts"?"其他网格":route=="garments"?"衣装与配件":"衣装与网格";
            if(c!=null)foreach(var o in menu.clothes.Where(x=>route=="wear" || x.advanced==(route=="parts")))
                Add(o.label,"Looks",()=>{c.Wear[o.id]=!c.Wear.GetValueOrDefault(o.id,o.initial);pet.ApplyControls();Draw();},c.Wear.GetValueOrDefault(o.id,o.initial));
            break;
        default:
            var nodes=menu.nodes??new();var current=nodes.FirstOrDefault(x=>x.id==route);
            title=current?.label??"角色菜单";string parent=current?.id??"";
            if(current!=null && current.kind.EndsWith("Puppet")){Puppet(current);Center();return;}
            foreach(var n in nodes.Where(x=>x.parent==parent)){
                string icon=string.IsNullOrEmpty(n.icon)?n.kind switch{"SubMenu"=>"Option","Toggle"=>"Toggle","Button"=>"Tool_Pose",_=>"Radial"}:"@"+n.icon;
                bool on=n.kind=="Toggle" && Math.Abs((c?.Parameters.GetValueOrDefault(n.parameter,n.initial)??n.initial)-n.value)<.001f;
                Add(n.label,icon,async()=>{
                    if(n.kind=="SubMenu" || n.kind.EndsWith("Puppet")){Param(n.parameter,n.value);Next(n.id,n.parameter);return;}
                    if(n.kind=="Toggle"){Param(n.parameter,on?0:n.value);Draw();return;}
                    if(n.kind=="Button"){Param(n.parameter,n.value);await Task.Delay(220);if(pet.Active==c)Param(n.parameter,0);Draw();}
                },on,n.available);
            }
            if(route=="source" && c!=null)Add("动作与姿势","Tool_Pose",()=>Next("moves"));
            break;
        }
        int cap=list.Count>8?6:8, pages=Math.Max(1,(list.Count+cap-1)/cap);page=Math.Clamp(page,0,pages-1);
        var visible=list.Skip(page*cap).Take(cap).ToList();
        if(pages>1){visible.Add(new("上一页","Back",()=>{page=(page+pages-1)%pages;Draw();}));visible.Add(new("下一页","Option",()=>{page=(page+1)%pages;Draw();}));}
        if(visible.Count==0)visible.Add(new("返回","Back",Back));
        // Two half-arcs avoid the degeneracy of an SVG-style full-circle arc.
        if(visible.Count==1)visible.Add(new("返回","Back",Back));
        if(route=="home"){DrawStarHome(visible);return;}
        double span=360.0/visible.Count;for(int i=0;i<visible.Count;i++)Piece(visible[i],-90-span/2+i*span,-90+span/2+i*span);
        Center();
    }
    void StatusAndDraw(string message){pet.Panel.Status("播放 · "+message);Draw();}
    void Puppet(WheelNode n){
        var plate=new Ellipse{Width=390,Height=390,Fill=Paint("#42374D"),Stroke=Paint("#937C9F"),StrokeThickness=2};At(plate,10,10);
        var axes=n.axes??Array.Empty<string>();for(int i=0;i<axes.Length;i++){
            string key=axes[i];if(string.IsNullOrEmpty(key))continue;
            double y=i<2?59+i*39:282+(i-2)*36;
            var label=new TextBlock{Text=(n.axisLabels?.ElementAtOrDefault(i) is {Length:>0} l?l:key),Foreground=Brushes.White,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Width=184};At(label,114,y);
            var slider=new Slider{Width=180,Minimum=n.kind=="TwoAxisPuppet"?-1:0,Maximum=1,Value=pet.Active?.Parameters.GetValueOrDefault(key)??0,IsMoveToPointEnabled=true};
            slider.ValueChanged+=(_,e)=>Param(key,(float)e.NewValue);At(slider,114,y+14);
        }
    }
}
