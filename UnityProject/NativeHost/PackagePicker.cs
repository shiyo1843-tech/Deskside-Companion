using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace DeskPet;

internal sealed class PackagePicker:Window {
    readonly ListBox choices=new(){MinHeight=85,MaxHeight=230};
    readonly TextBlock project=new(){TextWrapping=TextWrapping.Wrap},editor=new(){TextWrapping=TextWrapping.Wrap};
    public PackageAsset? Asset=>choices.SelectedItem as PackageAsset;
    public string ProjectFolder{get;private set;}
    public string EditorPath{get;private set;}
    public PackagePicker(Studio owner,string name,UnityPackage pack,Catalog library,PackageAsset? previous,string error){
        Owner=owner;Icon=owner.Icon;Title="从 Unity 资源包导入";Width=585;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=owner.Background;FontFamily=owner.FontFamily;
        ProjectFolder=Directory.Exists(library.PackageProject)?library.PackageProject:"";
        EditorPath=PackageConversion.EditorFor(ProjectFolder,library.PackageEditor);
        var panel=new StackPanel{Margin=new Thickness(20)};
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void Label(string s){panel.Children.Add(new TextBlock{Text=s,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,7,0,5),TextWrapping=TextWrapping.Wrap});}
        static Button Button(string label,Action action){var b=new Button{Content=label,Padding=new Thickness(11,4,11,4),Margin=new Thickness(0,4,7,4)};b.Click+=(_,_)=>action();return b;}
        Label("选择要导入的角色 · "+name);
        choices.ItemsSource=pack.Choices;choices.SelectedItem=previous??pack.Choices[0];AutomationProperties.SetAutomationId(choices,"package-assets");panel.Children.Add(choices);
        var setup=new StackPanel();panel.Children.Add(setup);
        void Update(){
            var a=Asset;setup.Visibility=a!=null&&!a.Ready&&!library.PackageImports.ContainsKey(pack.Hash+"/"+a.Guid)?Visibility.Visible:Visibility.Collapsed;
            project.Text=ProjectFolder.Length>0?ProjectFolder:"使用空工程（素材自带全部依赖）";
            editor.Text=EditorPath.Length>0?EditorPath:"未找到 Unity 编辑器";
        }
        setup.Children.Add(new TextBlock{Text="要保留原菜单，请选角色根 Prefab。单独选服装或 FBX 只导入该物件。首次转换需要本机 Unity；使用 VRChat、第三方着色器或插件的角色请选择已装好依赖的工程。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,11,0,8)});
        setup.Children.Add(project);var row=new WrapPanel();setup.Children.Add(row);
        row.Children.Add(Button("选择依赖工程…",()=>{var f=new OpenFolderDialog{Title="选择原角色工程或配好依赖的模板工程"};if(f.ShowDialog(this)==true){ProjectFolder=f.FolderName;var found=PackageConversion.EditorFor(ProjectFolder,"");if(found.Length>0)EditorPath=found;Update();}}));
        row.Children.Add(Button("使用空工程",()=>{ProjectFolder="";Update();}));
        setup.Children.Add(editor);var choose=Button("选择 Unity.exe…",()=>{var f=new OpenFileDialog{Filter="Unity 编辑器|Unity.exe"};if(f.ShowDialog(this)==true){EditorPath=f.FileName;Update();}});choose.HorizontalAlignment=HorizontalAlignment.Left;setup.Children.Add(choose);
        if(error.Length>0)panel.Children.Add(new TextBox{Text=error,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=115,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Foreground=Brushes.DarkRed,Margin=new Thickness(0,7,0,0)});
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        buttons.Children.Add(Button("取消",()=>DialogResult=false));var accept=Button("导入所选角色",()=>DialogResult=true);accept.IsDefault=true;AutomationProperties.SetAutomationId(accept,"package-confirm");buttons.Children.Add(accept);
        choices.SelectionChanged+=(_,_)=>Update();Update();
    }
}
