using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Shell;
namespace DeskPet;

public sealed partial class Studio {
    readonly TextBlock onStage=Text("",12);
    readonly ImageBrush portraitBrush=new(){Stretch=Stretch.Uniform,AlignmentY=AlignmentY.Center};
    bool portraitPreview=true;
    public void UpdatePortrait(){
        portraitBrush.ImageSource=pet.Portrait;
        portraitBrush.Viewbox=portraitPreview?pet.PreviewRegion:new Rect(0,0,1,1);
    }
    static Border Rounded(string fill,double radius=14)=>new(){Background=Brush(fill),CornerRadius=new CornerRadius(radius)};
    void BuildShell(PetWindow owner){
        Title="桌边伙伴 · 控制台";Icon=owner.Icon;Width=760;Height=510;MinWidth=635;MinHeight=440;
        MaxHeight=Math.Max(445,SystemParameters.WorkArea.Height);WindowStartupLocation=WindowStartupLocation.CenterScreen;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/桌边伙伴;component/Theme.xaml",UriKind.Relative)});
        FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;Background=Brush("#FCFAFC");Foreground=Ink;
        var shell=new DockPanel{Margin=new Thickness(1)};Content=new Border{Child=shell,BorderBrush=Brush("#C9C0D0"),BorderThickness=new Thickness(1)};
        var bar=new DockPanel{Height=52,Margin=new Thickness(17,0,10,0),Background=Brushes.Transparent};
        bar.MouseLeftButtonDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left && e.LeftButton==MouseButtonState.Pressed)DragMove();};
        var mark=new Image{Source=Icon,Width=32,Height=32,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center};
        RenderOptions.SetBitmapScalingMode(mark,BitmapScalingMode.HighQuality);
        DockPanel.SetDock(mark,Dock.Left);bar.Children.Add(mark);
        var hide=Btn("收起",Hide);hide.Padding=new Thickness(11,3,11,3);hide.FontSize=12;hide.VerticalAlignment=VerticalAlignment.Center;
        DockPanel.SetDock(hide,Dock.Right);bar.Children.Add(hide);
        bar.Children.Add(new TextBlock{Text="桌边伙伴",FontWeight=FontWeights.SemiBold,Foreground=Ink,FontSize=15,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)});
        DockPanel.SetDock(bar,Dock.Top);shell.Children.Add(bar);
        var cols=new Grid{Margin=new Thickness(14,0,14,14)};cols.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(.92,GridUnitType.Star)});cols.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.22,GridUnitType.Star)});shell.Children.Add(cols);
        var gradient=new LinearGradientBrush(new GradientStopCollection{
            new GradientStop(Color.FromRgb(219,209,239),0),new GradientStop(Color.FromRgb(241,220,225),.62),new GradientStop(Color.FromRgb(248,230,205),1)},new Point(0,0),new Point(1,1));
        var stage=new Grid{ClipToBounds=true};var frame=new Border{Background=gradient,CornerRadius=new CornerRadius(16),Child=stage,Margin=new Thickness(0,0,16,0)};cols.Children.Add(frame);
        var halo=new System.Windows.Shapes.Ellipse{Width=197,Height=197,Stroke=Brush("#80FFFFFF"),StrokeThickness=1,Fill=Brush("#33FFFFFF"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,-52,0,0)};stage.Children.Add(halo);
        var halo2=new System.Windows.Shapes.Ellipse{Width=161,Height=161,Stroke=Brush("#80FFFFFF"),StrokeThickness=1,Margin=halo.Margin,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};stage.Children.Add(halo2);
        var stageGrid=new Grid();stageGrid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});stageGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});stage.Children.Add(stageGrid);
        UpdatePortrait();
        var preview=new Border{Background=portraitBrush,Margin=new Thickness(5,12,5,0),Cursor=Cursors.SizeWE,ToolTip="按住左键左右拖动，旋转当前角色"};
        stageGrid.Children.Add(preview);double fromX=0;int fromYaw=0;
        preview.MouseLeftButtonDown+=(_,e)=>{if(pet.Active is not {} c)return;pet.PauseActivity();fromX=e.GetPosition(preview).X;fromYaw=c.Yaw;
            pet.RotatingPreview=true;preview.CaptureMouse();e.Handled=true;};
        preview.MouseMove+=(_,e)=>{if(preview.IsMouseCaptured && e.LeftButton==MouseButtonState.Pressed)
            pet.SetYaw(fromYaw+(int)Math.Round((e.GetPosition(preview).X-fromX)*.8),false);};
        preview.MouseLeftButtonUp+=(_,e)=>{if(preview.IsMouseCaptured){preview.ReleaseMouseCapture();e.Handled=true;}};
        preview.LostMouseCapture+=(_,_)=>{pet.RotatingPreview=false;pet.Store.Save();};
        var switchView=Btn("看全身",()=>{});switchView.Padding=new Thickness(8,3,8,3);switchView.FontSize=11;
        switchView.HorizontalAlignment=HorizontalAlignment.Right;switchView.VerticalAlignment=VerticalAlignment.Top;switchView.Margin=new Thickness(0,10,10,0);
        switchView.Click+=(_,_)=>{portraitPreview=!portraitPreview;switchView.Content=portraitPreview?"看全身":"看近景";UpdatePortrait();};stageGrid.Children.Add(switchView);
        var label=new StackPanel{Margin=new Thickness(20,0,14,19)};
        heading.FontSize=30;heading.FontFamily=new FontFamily("Georgia, Microsoft YaHei UI");heading.FontWeight=FontWeights.SemiBold;heading.Foreground=Brush("#362A41");
        onStage.Foreground=Brush("#655668");label.Children.Add(heading);label.Children.Add(onStage);
        Grid.SetRow(label,1);stageGrid.Children.Add(label);
        var right=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,3,0)};Grid.SetColumn(right,1);cols.Children.Add(right);
        status.Margin=new Thickness(2,6,2,0);status.Foreground=Brush("#705E76");status.MaxHeight=58;Id(status,"status");DockPanel.SetDock(status,Dock.Bottom);right.Children.Add(status);
        tabs.Margin=new Thickness(0);right.Children.Add(tabs);
    }
    object CharacterTile(Character c){
        var row=new DockPanel{LastChildFill=true};
        var slim=Rounded(c.Voice=="gpt"?"#E3D5E9":c.Voice=="gemini"?"#D7E7E4":"#E8DFCF",8);slim.Width=39;slim.Height=39;slim.Margin=new Thickness(0,0,10,0);
        if(c.Id==pet.Active?.Id && !pet.Busy)slim.Background=pet.FacePortrait;
        else{
            string thumb=pet.PortraitPath(c);
            if(System.IO.File.Exists(thumb)){
                try{
                    using var input=System.IO.File.OpenRead(thumb);
                    var img=new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();img.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;img.StreamSource=input;img.EndInit();img.Freeze();
                    slim.Child=new Image{Source=img,Stretch=Stretch.Uniform};
                }catch(System.IO.IOException){}catch(System.IO.FileFormatException){}
            }
            if(slim.Child==null)slim.Child=new TextBlock{
                Text=string.IsNullOrWhiteSpace(c.Name)?"✧":System.Globalization.StringInfo.GetNextTextElement(c.Name),
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Ink
            };
        }
        DockPanel.SetDock(slim,Dock.Left);row.Children.Add(slim);
        var info=new StackPanel();var name=Text(c.Name,15);name.Margin=new Thickness(0);name.FontWeight=FontWeights.SemiBold;info.Children.Add(name);
        var kind=Text(Voice(c.Voice),11);kind.Foreground=Brush("#7B7182");kind.Margin=new Thickness(0,2,0,0);info.Children.Add(kind);row.Children.Add(info);
        return row;
    }
}
