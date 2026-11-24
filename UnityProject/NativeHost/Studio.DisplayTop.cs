using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Glyph=System.Windows.Shapes.Path;

namespace DeskPet;

public sealed partial class Studio {
    // Only explicitly assigning one of these keyed styles opts into the Display trial.
    // The global Theme.xaml and everything below 桌面大小 still use their original styles.
    ResourceDictionary? displayTopStyles;
    Style TopStyle(string key)=>(Style)(displayTopStyles??=new ResourceDictionary{
        Source=new Uri("/桌边伙伴;component/DisplayTop.xaml",UriKind.Relative)
    })[key];
    static Glyph DisplayGlyph(string data,string ink,double size)=>new(){
        Data=Geometry.Parse(data),Stroke=Brush(ink),StrokeThickness=1.35,
        StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,
        Width=size,Height=size,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false
    };
    static StackPanel DisplayCard(Panel parent,string title,bool quality){
        var items=new StackPanel();
        var card=new Border{
            Background=new LinearGradientBrush(
                Brush(quality?"#EFF4FD":"#F9F1F8").Color,
                Brush(quality?"#F6F0FA":"#F3EDFA").Color,110),
            BorderBrush=Brush(quality?"#DFD8ED":"#E6D9ED"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(13),Padding=new Thickness(11,8,11,9),Margin=new Thickness(0,0,0,8),Child=items,
            UseLayoutRounding=true
        };
        var heading=new DockPanel{Margin=new Thickness(0,0,0,2)};
        var badge=new Border{Width=22,Height=22,CornerRadius=new CornerRadius(7),Margin=new Thickness(0,0,8,0),
            Background=Brush(quality?"#E4E9FA":"#EFDEF0"),Child=DisplayGlyph(
                quality?"M1,2 L13,2 13,10 1,10 Z M4,13 L10,13 M7,10 L7,13":
                    "M7,1 Q8,5.5 13,7 Q8,8.5 7,13 Q6,8.5 1,7 Q6,5.5 7,1 Z",
                quality?"#63548E":"#805885",13)};
        DockPanel.SetDock(badge,Dock.Left);heading.Children.Add(badge);
        heading.Children.Add(new TextBlock{Text=title,Foreground=Brush("#392B51"),FontWeight=FontWeights.SemiBold,FontSize=15,
            VerticalAlignment=VerticalAlignment.Center});
        items.Children.Add(heading);parent.Children.Add(card);return items;
    }
    void DisplayField(Panel group,string name,ComboBox combo){
        combo.Style=TopStyle("DisplaySelect");
        var row=new Grid{Margin=new Thickness(0,2,0,2)};
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.18,GridUnitType.Star)});
        var label=new TextBlock{Text=name,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,
            FontSize=12.5,FontWeight=FontWeights.SemiBold,Foreground=Brush("#443451"),Margin=new Thickness(0,0,9,0)};
        AutomationProperties.SetLabeledBy(combo,label);
        row.Children.Add(label);Grid.SetColumn(combo,1);row.Children.Add(combo);group.Children.Add(row);
    }
    CheckBox DisplayCheck(string name,bool enabled)=>new(){Content=name,IsChecked=enabled,Style=TopStyle("DisplayCheck")};
    static Border DisplayMeter(TextBlock metric){
        metric.Margin=new Thickness(0);metric.Foreground=Brush("#5A4775");metric.FontSize=11.5;metric.VerticalAlignment=VerticalAlignment.Center;
        var row=new DockPanel();
        var wave=DisplayGlyph("M0,7 L3,7 5,2 8,11 10,7 14,7","#775C9A",13);
        wave.Margin=new Thickness(0,0,8,0);
        DockPanel.SetDock(wave,Dock.Left);row.Children.Add(wave);row.Children.Add(metric);
        return new Border{Child=row,Background=Brush("#ECE7F7"),BorderBrush=Brush("#E3DBF3"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(7),Padding=new Thickness(7,2,7,2),Margin=new Thickness(0,3,0,3)};
    }
}
