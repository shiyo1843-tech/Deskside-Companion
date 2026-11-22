using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskPet;
public sealed class ReplyBubble:Window {
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    static SolidColorBrush ColorOf(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    static LinearGradientBrush Gradient(string a,string b)=>new((Color)ColorConverter.ConvertFromString(a),(Color)ColorConverter.ConvertFromString(b),22);
    static readonly Brush Plum=ColorOf("#49354F"),Lilac=ColorOf("#DAC7F5");
    readonly TextBlock name=new(){FontSize=15,FontWeight=FontWeights.Bold,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock voice=new(){FontSize=10.5,Foreground=ColorOf("#F4DFF1"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(9,1,0,0)};
    readonly TextBlock counter=new(){FontFamily=new FontFamily("Consolas"),FontSize=11,Foreground=ColorOf("#F6E8FF"),VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock body=new(){FontSize=15,LineHeight=23,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,Foreground=ColorOf("#3F3048")};
    readonly Border portrait=new(){Width=44,Height=44,CornerRadius=new CornerRadius(11),Background=Lilac,BorderBrush=ColorOf("#FFEECA"),BorderThickness=new Thickness(2)};
    readonly Grid tail=new(){Width=23,Height=13};
    readonly PetWindow pet;
    readonly Queue<string> pages=new();
    int count,page;
    double until;
    public ReplyBubble(PetWindow pet){
        this.pet=pet;Owner=pet;Title="桌边伙伴 · 聊天气泡";Width=316;SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;ShowActivated=false;Focusable=false;IsHitTestVisible=false;FontFamily=new FontFamily("Microsoft YaHei UI");
        var layer=new Grid{Margin=new Thickness(3,3,5,0)};
        layer.Children.Add(new Border{CornerRadius=new CornerRadius(16),Background=ColorOf("#63557569"),Margin=new Thickness(3,4,-2,-3)});
        var rows=new Grid();rows.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rows.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid{Margin=new Thickness(55,0,12,0),MinHeight=31};
        header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var speaker=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};speaker.Children.Add(name);speaker.Children.Add(voice);header.Children.Add(speaker);
        Grid.SetColumn(counter,1);header.Children.Add(counter);rows.Children.Add(header);
        var pageField=new Border{Background=Gradient("#FFFCF6","#F4EAFB"),CornerRadius=new CornerRadius(4,4,12,12),Padding=new Thickness(15,12,17,12),Margin=new Thickness(3,0,3,3)};
        var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(4)});content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new Border{Width=3,CornerRadius=new CornerRadius(2),Background=Gradient("#BC9CCB","#E9D8EB"),VerticalAlignment=VerticalAlignment.Stretch,Margin=new Thickness(0,3,0,2)});
        body.Margin=new Thickness(9,0,0,0);Grid.SetColumn(body,1);content.Children.Add(body);pageField.Child=content;Grid.SetRow(pageField,1);rows.Children.Add(pageField);
        var card=new Border{CornerRadius=new CornerRadius(15),BorderBrush=Plum,BorderThickness=new Thickness(1.6),Background=Gradient("#654B75","#80618A"),Child=rows};layer.Children.Add(card);
        var crest=new Border{Width=50,Height=50,CornerRadius=new CornerRadius(14),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
            Margin=new Thickness(3,-9,0,0),Background=Gradient("#76558F","#C498A7"),BorderThickness=new Thickness(1.4),BorderBrush=Plum,Child=portrait};
        layer.Children.Add(crest);
        var jewel=new Rectangle{Width=7,Height=7,Fill=ColorOf("#F4D795"),Stroke=Plum,StrokeThickness=1,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(45),
            HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(48,27,0,0)};layer.Children.Add(jewel);
        tail.Children.Add(new Polygon{Points=new PointCollection{new(0,0),new(23,0),new(4,13)},Fill=Plum});
        tail.Children.Add(new Polygon{Points=new PointCollection{new(2,0),new(20,0),new(5,10)},Fill=ColorOf("#F4EAFB")});
        var layout=new StackPanel{Margin=new Thickness(2,8,2,4)};layout.Children.Add(layer);layout.Children.Add(tail);Content=layout;
        SourceInitialized+=(_,_)=>{var h=new WindowInteropHelper(this).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x20|0x08000000|0x80);};
    }
    static IEnumerable<string> Split(string message){
        var chunk=new StringBuilder();int length=0,lines=1;
        var run=StringInfo.GetTextElementEnumerator(message.Trim().Replace("\r\n","\n").Replace('\r','\n'));
        while(run.MoveNext()){
            string ch=run.GetTextElement();chunk.Append(ch);length++;if(ch=="\n")lines++;
            if(length>=80 || lines>=5 || (length>=50 && "。！？…!?".Contains(ch,StringComparison.Ordinal))){
                string part=chunk.ToString().Trim();if(part.Length!=0)yield return part;
                chunk.Clear();length=0;lines=1;
            }
        }
        if(chunk.Length>0 && !string.IsNullOrWhiteSpace(chunk.ToString()))yield return chunk.ToString().Trim();
    }
    public void Present(Character character,string speaker,string message,double now,Rect area){
        pages.Clear();foreach(string s in Split(message))pages.Enqueue(s);
        if(pages.Count==0)return;
        name.Text=character.Name;voice.Text=speaker;Topmost=pet.Topmost;
        portrait.Background=pet.FacePortrait;
        count=pages.Count;page=0;Next(now);
        if(!IsVisible){Left=pet.Left;Top=pet.Top;Show();}
        UpdateLayout();Follow(pet,area);
    }
    void Next(double now){
        body.Text=pages.Dequeue();counter.Text=count>1?$"{++page} / {count}":"✦";
        until=now+Math.Clamp(3.2+body.Text.Length*.145,5,16);
    }
    public void Advance(double now){
        if(!IsVisible || now<until)return;
        if(pages.Count==0){Dismiss();return;}
        Next(now);UpdateLayout();Follow(pet,pet.WorkArea());
    }
    public void Follow(PetWindow owner,Rect area){
        bool right=owner.Left+owner.Width*.66+Width<=area.Right;
        double x=right?owner.Left+owner.Width*.66:owner.Left+owner.Width*.34-Width;
        tail.HorizontalAlignment=right?HorizontalAlignment.Left:HorizontalAlignment.Right;
        tail.RenderTransformOrigin=new Point(.5,.5);tail.RenderTransform=new ScaleTransform(right?1:-1,1);
        tail.Margin=new Thickness(22,-2,22,0);
        Left=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-Width));
        Top=Math.Clamp(owner.Top+owner.Height*.21-ActualHeight,area.Top,Math.Max(area.Top,area.Bottom-ActualHeight));
    }
    public void Dismiss(){pages.Clear();if(IsVisible)Hide();}
}
