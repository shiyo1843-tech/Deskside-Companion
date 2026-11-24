using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath=System.Windows.Shapes.Path;

namespace DeskPet;

// Presentation only. Draw() sends the real home slices here. No model menu, expression
// parameter, navigation route or action is defined by this skin.
public sealed partial class Wheel {
    static T Ice<T>(T value) where T:Freezable {value.Freeze();return value;}
    static Color Hue(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    static SolidColorBrush Ink(string hex)=>Ice(new SolidColorBrush(Hue(hex)));
    static LinearGradientBrush Glaze(string light,string dark)=>Ice(new LinearGradientBrush(Hue(light),Hue(dark),55));
    static readonly SolidColorBrush HomeText=Ink("#302A43"), RestText=Ink("#625C71"), HomeEdge=Ink("#FCFBFF"),
        HintText=Ink("#685D80"), HotEdge=Ink("#8C72B4"), DownEdge=Ink("#77548E"), OffEdge=Ink("#D7D4E0");
    static readonly LinearGradientBrush OffGlaze=Glaze("#F2F1F5","#E7E7EE"),
        HubGlaze=Glaze("#FFFDFF","#F6F1FB"), HubWarm=Glaze("#F9F3FF","#EDE2F7"), HubDown=Glaze("#E7DAF4","#F3E8F9");
    sealed record Caps(Brush Rest,Brush Hot,Brush Down);
    static Caps Cap(string top,string base_,string hot,string down)=>new(Glaze(top,base_),Glaze(top,hot),Glaze(hot,down));
    static readonly Caps[] HomeCaps={
        Cap("#F2ECFC","#E0D7F5","#D3C3EC","#BFA6DD"), Cap("#F5ECF9","#E9DAF1","#E0C5E9","#CAA8D9"),
        Cap("#FAEDF4","#EFDCE9","#E9C1DB","#DDA5C9"), Cap("#FAEEF4","#F0DFEB","#EDCADC","#DFAEC9"),
        Cap("#ECF6FC","#D7EAF8","#BDDEF5","#AACDE9"), Cap("#EDF3FD","#DCE7F9","#C9D8F4","#B3C2E6")};
    static readonly Lazy<ImageSource> HomeRim=new(MakeRim);

    // Freeze and share the decorative chassis instead of allocating 60+ WPF elements on every draw.
    static DrawingImage MakeRim(){
        var group=new DrawingGroup();using(var dc=group.Open()){
            var center=new Point(205,205);
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,410,410));
            var rim=new LinearGradientBrush{StartPoint=new Point(.1,.05),EndPoint=new Point(.92,.93)};
            rim.GradientStops.Add(new GradientStop(Hue("#A2D5EE"),0));rim.GradientStops.Add(new GradientStop(Hue("#CCD9F9"),.27));
            rim.GradientStops.Add(new GradientStop(Hue("#D4C6F1"),.62));rim.GradientStops.Add(new GradientStop(Hue("#DDB7D3"),1));
            dc.DrawEllipse(Ink("#3A776D8F"),null,new Point(205,207),200,200);
            dc.DrawEllipse(rim,new Pen(Ink("#FFFDFE"),1.8),center,200,200);
            dc.DrawEllipse(Glaze("#FCFDFF","#F5F0F9"),new Pen(Ink("#FAF7FF"),1),center,194.8,194.8);
            var mark=Ink("#CABFDB");for(int i=0;i<80;i++){
                double a=-90+i*4.5;var dot=P(a,191.7);
                if(i%5==0)dc.DrawLine(new Pen(mark,1.1),P(a,190.0),P(a,193.3));else dc.DrawEllipse(mark,null,dot,.57,.57);
            }
            for(int i=0;i<4;i++){var at=P(-90+i*90,197.5);dc.DrawEllipse(Ink("#AA8FBF"),new Pen(Ink("#FBF7FF"),.8),at,2.3,2.3);}
            dc.DrawEllipse(Glaze("#EBE5F5","#F8F3FA"),new Pen(Ink("#FDFBFF"),1.2),center,72.1,72.1);
            dc.DrawEllipse(null,new Pen(Ink("#DED5EB"),.85),center,65,65);
        }return Ice(new DrawingImage(Ice(group)));
    }

    static StreamGeometry KeyCap(double a,double b){
        const double outer=186.5,inner=73.5;
        var g=new StreamGeometry();using(var dc=g.Open()){
            dc.BeginFigure(P(a,outer),true,true);
            dc.ArcTo(P(b,outer),new Size(outer,outer),0,b-a>180,SweepDirection.Clockwise,true,false);
            dc.LineTo(P(b,inner),true,false);
            dc.ArcTo(P(a,inner),new Size(inner,inner),0,b-a>180,SweepDirection.Counterclockwise,true,false);
        }return Ice(g);
    }
    ImageSource HomeArt(string original){
        var file=original switch{"Option"=>"01-role-menu","Expressions"=>"02-expression","Looks"=>"03-outfit",
            "Emojis"=>"04-chat","Tool_Pose"=>"05-action","Tools"=>"06-settings",_=>""};
        if(file=="")return SliceImage(original);string key="StarWheel."+file+".png";
        if(images.TryGetValue(key,out var image))return image;
        try{
            using var s=typeof(Wheel).Assembly.GetManifestResourceStream(key);
            if(s!=null){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.StreamSource=s;bitmap.EndInit();bitmap.Freeze();return images[key]=bitmap;}
        }catch(Exception ex) when(ex is IOException or FormatException or NotSupportedException or ArgumentException){}
        // Unexpected missing/damaged decoration must not remove the original button.
        return SliceImage(original);
    }
    void DrawStarHome(IReadOnlyList<Slice> slices){
        At(new Image{Source=HomeRim.Value,Width=410,Height=410,IsHitTestVisible=false},0,0);
        double span=360.0/slices.Count;
        for(int i=0;i<slices.Count;i++){double mid=-90+i*span;HomePiece(slices[i],mid-span/2,mid+span/2,HomeCaps[i%HomeCaps.Length]);}
        HomeCenter();
    }
    void HomePiece(Slice item,double a,double b,Caps palette){
        double mid=(a+b)/2;
        var face=new ShapePath{Data=KeyCap(a+.8,b-.8),IsHitTestVisible=false,StrokeLineJoin=PenLineJoin.Round};
        canvas.Children.Add(face);
        var p=P(mid,130.5);
        var icon=new Image{Width=53,Height=53,Source=HomeArt(item.Icon),Stretch=Stretch.Uniform,
            Opacity=item.Enabled?1:.49,IsHitTestVisible=false};
        RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
        At(icon,p.X-26.5,p.Y-38);
        var label=new TextBlock{Text=item.Text,FontSize=14.5,FontWeight=FontWeights.SemiBold,
            Foreground=item.Enabled?HomeText:RestText,TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis,Width=100,IsHitTestVisible=false};
        At(label,p.X-50,p.Y+17);
        // The cap is visually inset. The transparent hit path deliberately uses the exact
        // v1.0.0 sector: a hit on the text, artwork or narrow gutter still activates its wedge.
        var hit=new ShapePath{Data=Sector(a,b),Fill=Brushes.Transparent,Stroke=Brushes.Transparent,StrokeThickness=1.25,
            Cursor=item.Enabled?Cursors.Hand:Cursors.Arrow,ToolTip=item.Enabled?item.Text:item.Text+" · 这个参数未连接到桌宠可用的动画层"};
        AutomationProperties.SetName(hit,item.Text);
        bool over=false,pressed=false;
        void Style(){
            face.Fill=!item.Enabled?OffGlaze:pressed?palette.Down:over?palette.Hot:palette.Rest;
            face.Stroke=!item.Enabled?OffEdge:pressed?DownEdge:over?HotEdge:HomeEdge;
            face.StrokeThickness=item.Enabled && (over||pressed)?1.8:1.15;
            Canvas.SetTop(icon,p.Y-38+(pressed?1.3:0));Canvas.SetTop(label,p.Y+17+(pressed?.7:0));
        }
        hit.MouseEnter+=(_,_)=>{if(item.Enabled){over=true;Style();}};
        hit.MouseLeave+=(_,_)=>{over=pressed=false;Style();};
        hit.MouseLeftButtonDown+=(_,_)=>{if(item.Enabled){pressed=true;Style();}};
        hit.MouseLeftButtonUp+=(_,e)=>{pressed=false;Style();if(item.Enabled)item.Run();e.Handled=true;};
        Style();canvas.Children.Add(hit);
    }
    void HomeCenter(){
        var center=new Border{Width=116,Height=116,CornerRadius=new CornerRadius(58),BorderThickness=new Thickness(1.25),
            BorderBrush=Ink("#CFC2E1"),Background=HubGlaze,Cursor=Cursors.Hand};
        var inner=new Border{Margin=new Thickness(5.5),CornerRadius=new CornerRadius(52),BorderThickness=new Thickness(.8),
            BorderBrush=Ink("#E9DEF1"),IsHitTestVisible=false};
        var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=90};
        var star=new ShapePath{Width=17,Height=17,Stretch=Stretch.Fill,Margin=new Thickness(0,0,0,3),
            Data=Geometry.Parse("M 10,0 Q 11.6,7.7 20,10 Q 11.9,12 10,20 Q 8.1,12 0,10 Q 8.1,8 10,0 Z"),
            Stroke=Ink("#8E70B1"),Fill=Ink("#F2E6FA"),StrokeThickness=1.3,StrokeLineJoin=PenLineJoin.Round};
        stack.Children.Add(star);
        stack.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=39,Foreground=HomeText,FontSize=15,FontWeight=FontWeights.SemiBold});
        string hint=embedded?"角色列表":"收起";
        stack.Children.Add(new TextBlock{Text=hint,TextAlignment=TextAlignment.Center,Foreground=HintText,FontSize=10,
            Margin=new Thickness(0,2,0,0)});inner.Child=stack;center.Child=inner;AutomationProperties.SetName(center,hint);
        center.MouseEnter+=(_,_)=>{center.Background=HubWarm;center.BorderBrush=HotEdge;};
        center.MouseLeave+=(_,_)=>{center.Background=HubGlaze;center.BorderBrush=Ink("#CFC2E1");};
        center.MouseLeftButtonDown+=(_,_)=>center.Background=HubDown;
        center.MouseLeftButtonUp+=(_,e)=>{center.Background=HubGlaze;e.Handled=true;Back();};At(center,147,147);
    }
}
