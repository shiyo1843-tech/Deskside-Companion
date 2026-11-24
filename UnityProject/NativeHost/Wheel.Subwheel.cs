using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShapePath=System.Windows.Shapes.Path;

namespace DeskPet;

public sealed partial class Wheel {
    enum Subwheel { None,Faces,Wear,Moves,Options,Idle,Characters }

    // Treat navigation provenance as part of the route. A model can also reach faces,
    // moves or rest, or use a folder with the same label; none of those get this skin.
    Subwheel AppSubwheelScope(){
        if(history.Count==1 && history.Peek().route=="home"){
            return route switch{"faces"=>Subwheel.Faces,"wear"=>Subwheel.Wear,"moves"=>Subwheel.Moves,"options"=>Subwheel.Options,_=>Subwheel.None};
        }
        if(history.Count==2 && history.Last().route=="home"){
            return (history.Peek().route,route) switch{("moves","rest")=>Subwheel.Idle,("options","characters")=>Subwheel.Characters,_=>Subwheel.None};
        }
        return Subwheel.None;
    }

    static readonly Brush SelectedCap=Glaze("#F7E9F5","#E8D2ED"), SelectedRim=Ink("#9A67A6"),
        BadgeInk=Ink("#76559D"), BluePlate=Ink("#F9F6FF"), DetailInk=Ink("#5B477D");
    static readonly Lazy<IReadOnlyDictionary<string,ImageSource>> SubGlyphs=new(MakeSubGlyphs);
    static IReadOnlyDictionary<string,ImageSource> MakeSubGlyphs(){
        var paths=new Dictionary<string,string>{
            ["characters"]="M4,2 L16,2 16,15 4,15 Z M8,6 A2,2 0 1 0 12,6 A2,2 0 1 0 8,6 M7,12 Q10,9 13,12 M7,18 L19,18 19,5",
            ["import"]="M10,2 L10,12 M6,8 L10,12 14,8 M3,11 L3,17 17,17 17,11",
            ["display"]="M2,3 L18,3 18,14 2,14 Z M10,14 L10,18 M6,18 L14,18",
            ["framing"]="M6,3 L3,3 3,6 M14,3 L17,3 17,6 M3,14 L3,17 6,17 M17,14 L17,17 14,17 M7,9 A3,3 0 1 0 13,9 A3,3 0 1 0 7,9 M6,15 Q10,12 14,15",
            ["pin"]="M6,3 L14,3 12,7 15,11 5,11 8,7 Z M10,11 L10,18",
            ["leave"]="M11,3 L3,3 3,17 11,17 M8,10 L18,10 M14,6 L18,10 14,14",
            ["repeat"]="M3,8 Q5,4 8,4 L16,4 M13,1 L16,4 13,7 M17,12 Q15,16 12,16 L4,16 M7,13 L4,16 7,19",
            ["reset"]="M4,8 A6.5,6.5 0 1 1 5,15 M3,3 L3,8 8,8",
            ["quiet"]="M13,2 A8,8 0 1 0 17,15 Q9,15 9,8 Q9,4 13,2 Z",
            ["previous"]="M12,5 L7,10 12,15",
            ["next"]="M8,5 L13,10 8,15",
            ["back"]="M8,5 L3,10 8,15 M3,10 L16,10"
        };
        var pen=Ice(new Pen(DetailInk,1.45){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round});
        return paths.ToDictionary(k=>k.Key,k=>{
            var group=new DrawingGroup();using(var dc=group.Open()){
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,26,26));
                dc.DrawRoundedRectangle(BluePlate,new Pen(Ink("#D8CCE9"),.8),new Rect(.75,.75,24.5,24.5),8,8);
                dc.PushTransform(new TranslateTransform(3,3));dc.DrawGeometry(null,pen,Geometry.Parse(k.Value));dc.Pop();
            }
            return (ImageSource)Ice(new DrawingImage(Ice(group)));
        });
    }
    ImageSource SubArt(Subwheel kind,string stockIcon,int navigation){
        if(navigation!=0)return SubGlyphs.Value[navigation<0?"previous":"next"];
        string? symbol=(kind,stockIcon) switch{
            (Subwheel.Options,"Back")=>"leave",
            (Subwheel.Options,"Gear")=>"display",
            (_,"Clones")=>"characters",
            (_,"Avatar_Height")=>"import",
            (_,"HeadHands")=>"framing",
            (_,"Toggle")=>"pin",
            (_,"Radial")=>"repeat",
            (_,"Reset")=>"reset",
            (Subwheel.Idle,"Tool_Pose")=>"quiet",
            (_,"Back")=>"back",
            _=>null
        };
        return symbol==null?HomeArt(kind==Subwheel.Options && stockIcon=="Option"?"Emojis":stockIcon):SubGlyphs.Value[symbol];
    }
    void DrawEnamelSubwheel(IReadOnlyList<Slice> slices,Subwheel kind,bool paging){
        At(new Image{Source=HomeRim.Value,Width=410,Height=410,IsHitTestVisible=false},0,0);
        double span=360d/slices.Count;
        for(int i=0;i<slices.Count;i++){
            int direction=paging && i>=slices.Count-2?(i==slices.Count-2?-1:1):0;
            EnamelPiece(slices[i],-90-span/2+i*span,-90+span/2+i*span,HomeCaps[(i+(int)kind-1)%HomeCaps.Length],kind,direction,slices.Count);
        }
        EnamelCenter();
    }
    void EnamelPiece(Slice item,double a,double b,Caps palette,Subwheel kind,int navigation,int count){
        double mid=(a+b)/2;
        var face=new ShapePath{Data=KeyCap(a+.8,b-.8),IsHitTestVisible=false,StrokeLineJoin=PenLineJoin.Round};
        canvas.Children.Add(face);
        // Local clip belongs to the decoration, not the original hit path.
        var decor=new Canvas{Width=410,Height=410,Clip=face.Data,IsHitTestVisible=false};At(decor,0,0);
        void Place(UIElement e,double x,double y){Canvas.SetLeft(e,x);Canvas.SetTop(e,y);decor.Children.Add(e);}
        bool dense=count>=7;
        var p=P(mid,dense?133:131);double size=dense?35:41, artTop=p.Y-(dense?31:34), labelTop=p.Y+(dense?8:10);
        var art=new Image{Width=size,Height=size,Source=SubArt(kind,item.Icon,navigation),Stretch=Stretch.Uniform,Opacity=item.Enabled?1:.5};
        RenderOptions.SetBitmapScalingMode(art,BitmapScalingMode.HighQuality);Place(art,p.X-size/2,artTop);
        var caption=new TextBlock{Text=item.Text,Foreground=item.Enabled?HomeText:RestText,FontSize=dense?12.5:13.5,FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,
            LineHeight=dense?14.5:16,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,MaxHeight=dense?29:32};
        // Fit the horizontal caption inside the painted sector. Two compact lines
        // keep more of a long authored label visible; the exact text remains in the tooltip.
        double CaptionWidth(){
            var safe=KeyCap(a+1.5,b-1.5);
            for(int width=dense?96:112;width>=60;width-=4){
                caption.Width=width;caption.Measure(new Size(width,caption.MaxHeight));
                double x=p.X-width/2,y=labelTop+Math.Min(caption.DesiredSize.Height+1,caption.MaxHeight);
                if(safe.FillContains(new Point(x,labelTop))&&safe.FillContains(new Point(x+width,labelTop))
                   &&safe.FillContains(new Point(x,y))&&safe.FillContains(new Point(x+width,y)))return width;
            }
            return 60;
        }
        caption.Width=CaptionWidth();Place(caption,p.X-caption.Width/2,labelTop);
        if(item.On){
            var mark=new Border{Width=15,Height=15,CornerRadius=new CornerRadius(7.5),Background=BadgeInk,
                BorderBrush=HomeEdge,BorderThickness=new Thickness(.75),
                Child=new ShapePath{Data=Geometry.Parse("M0,3 L2.7,5.7 6.7,1.1"),Width=7,Height=6,Stroke=Brushes.White,
                    StrokeThickness=1.3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,
                    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            Place(mark,p.X+size/2-10,artTop-1);
        }
        // Preserve v1.0.0 sector radii, seam stroke, hit order and dispatch exactly.
        var hit=new ShapePath{Data=Sector(a,b),Fill=Brushes.Transparent,Stroke=Brushes.Transparent,StrokeThickness=1.25,
            Cursor=item.Enabled?Cursors.Hand:Cursors.Arrow,ToolTip=item.Enabled?item.Text:item.Text+" · 这个参数未连接到桌宠可用的动画层"};
        AutomationProperties.SetName(hit,item.Text);
        bool over=false,pressed=false;
        void Style(){
            face.Fill=!item.Enabled?OffGlaze:pressed?palette.Down:over?palette.Hot:item.On?SelectedCap:palette.Rest;
            face.Stroke=!item.Enabled?OffEdge:pressed?DownEdge:over?HotEdge:item.On?SelectedRim:HomeEdge;
            face.StrokeThickness=item.Enabled && (over||pressed||item.On)?1.7:1.15;
            Canvas.SetTop(art,artTop+(pressed?1:0));Canvas.SetTop(caption,labelTop+(pressed?.5:0));
        }
        hit.MouseEnter+=(_,_)=>{if(item.Enabled){over=true;Style();}};hit.MouseLeave+=(_,_)=>{over=pressed=false;Style();};
        hit.MouseLeftButtonDown+=(_,_)=>{if(item.Enabled){pressed=true;Style();}};
        hit.MouseLeftButtonUp+=(_,e)=>{pressed=false;Style();if(item.Enabled)item.Run();e.Handled=true;};
        Style();canvas.Children.Add(hit);
    }
    void EnamelCenter(){
        var hub=new Border{Width=116,Height=116,CornerRadius=new CornerRadius(58),BorderThickness=new Thickness(1.25),
            BorderBrush=Ink("#CFC2E1"),Background=HubGlaze,Cursor=Cursors.Hand};
        var content=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=92};
        content.Children.Add(new Image{Source=SubGlyphs.Value["back"],Width=22,Height=22,Margin=new Thickness(0,0,0,4)});
        content.Children.Add(new TextBlock{Text=title,Foreground=HomeText,FontSize=14.5,FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxHeight=38});
        content.Children.Add(new TextBlock{Text="‹  返回",Foreground=HintText,FontSize=10.5,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,2,0,0)});
        hub.Child=new Border{Margin=new Thickness(5.5),CornerRadius=new CornerRadius(52),BorderThickness=new Thickness(.8),BorderBrush=Ink("#E9DEF1"),Child=content,IsHitTestVisible=false};
        AutomationProperties.SetName(hub,"返回");
        hub.MouseEnter+=(_,_)=>{hub.Background=HubWarm;hub.BorderBrush=HotEdge;};
        hub.MouseLeave+=(_,_)=>{hub.Background=HubGlaze;hub.BorderBrush=Ink("#CFC2E1");};
        hub.MouseLeftButtonDown+=(_,_)=>hub.Background=HubDown;
        hub.MouseLeftButtonUp+=(_,e)=>{hub.Background=HubGlaze;e.Handled=true;Back();};At(hub,147,147);
    }
}
