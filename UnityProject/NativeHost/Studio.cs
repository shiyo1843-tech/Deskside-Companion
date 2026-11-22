using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation;
using Microsoft.Win32;

namespace DeskPet;
public sealed partial class Studio:Window {
    readonly PetWindow pet;
    readonly TabControl tabs=new(){Margin=new Thickness(20,0,20,0)};
    readonly ListBox list=new(){MinHeight=110,MaxHeight=250};
    readonly TextBlock status=Text("",12),heading=Text("",20),chatTitle=Text("",17);
    readonly StackPanel timeline=new();
    readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBox composer=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=48,MaxHeight=88,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly ComboBox voice=new();
    readonly ComboBox lighting=new();
    static readonly int[] LightLevels={50,65,80,90,100,110,125,150};
    readonly Button send,import,resume;
    readonly Dictionary<string,string> drafts=new();
    readonly HashSet<string> inflight=new();
    readonly CancellationTokenSource lifetime=new();
    readonly Wheel inlineWheel;
    string? wheelCharacter;
    string? displayed;
    bool ending,refreshing;
    static readonly Brush Ink=Brush("#3A2D44"),Accent=Brush("#795D91");
    static SolidColorBrush Brush(string c)=>new((Color)ColorConverter.ConvertFromString(c));
    static TextBlock Text(string s,double size)=>new(){Text=s,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#42394A"),Margin=new Thickness(0,4,0,6)};
    static Button Btn(string s,Action action){var b=new Button{Content=s,Margin=new Thickness(0,4,7,4)};b.Click+=(_,_)=>action();return b;}
    static void Id(DependencyObject o,string id)=>AutomationProperties.SetAutomationId(o,id);
    static string Voice(string v)=>v=="gpt"?"云端阿璃":v=="gemini"?"云端哈基米":"自定义 API";
    string ThreadKey(Character c)=>c.Id+"/"+Library.ChatService(c,pet.Store.Settings);
    public Studio(PetWindow owner){
        pet=owner;BuildShell(owner);
        var library=new StackPanel{Margin=new Thickness(4)};Tab("角色",new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=library});
        library.Children.Add(Text("我的角色",16));Id(list,"characters");library.Children.Add(list);
        list.SelectionChanged+=async(_,_)=>{if(!refreshing && list.SelectedItem is ListBoxItem {Tag:Character c} && c.Id!=pet.Active?.Id)await pet.Select(c);};
        var row=new WrapPanel();library.Children.Add(row);
        import=Btn("导入模型…",async()=>{var f=new OpenFileDialog{Title="导入角色模型或 Unity 资源包",Filter="角色模型和 Unity 包|*.vrm;*.glb;*.petmodel;*.unitypackage|Unity 资源包|*.unitypackage|所有文件|*.*"};if(f.ShowDialog(this)==true)await pet.Import(f.FileName);});
        Id(import,"import");row.Children.Add(import);row.Children.Add(Btn("旋转角色",pet.Turn));
        import.Background=Brush("#624577");import.Foreground=Brushes.White;import.BorderBrush=import.Background;
        library.Children.Add(abilities);
        var connect=Btn("接入自己的 AI",()=>Go("服务"));Id(connect,"connect-ai");library.Children.Add(connect);
        library.Children.Add(Text("左键触碰 · 右键按住移动 · 右键轻点打开轮盘",12));

        var chat=new DockPanel{Margin=new Thickness(12)};Tab("聊天",chat);
        DockPanel.SetDock(chatTitle,Dock.Top);Id(chatTitle,"chat-title");chat.Children.Add(chatTitle);
        var compose=new StackPanel();DockPanel.SetDock(compose,Dock.Bottom);
        compose.Children.Add(composer);Id(composer,"composer");
        composer.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;_=Send();}};
        var actions=new WrapPanel();send=Btn("发送",()=>_=Send());Id(send,"send");actions.Children.Add(send);
        actions.Children.Add(Btn("AI 设置",()=>Go("服务")));
        resume=Btn("继续接收",()=>_=Send(true));Id(resume,"resume");actions.Children.Add(resume);
        send.Background=Brush("#624577");send.Foreground=Brushes.White;send.BorderBrush=send.Background;compose.Children.Add(actions);chat.Children.Add(compose);
        scroll.Content=timeline;chat.Children.Add(scroll);

        BuildServicePage();
        var display=new StackPanel{Margin=new Thickness(5)};Tab("显示",new ScrollViewer{Content=display,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        display.Children.Add(Text("渲染分辨率",17));
        var quality=new ComboBox{Padding=new Thickness(8,5,8,5)}; Id(quality,"quality");
        foreach(int w in Renderer.Widths)quality.Items.Add(w+" × "+(w*11/9)+" 像素");
        quality.SelectedIndex=Math.Clamp(pet.Store.Data.Detail,0,3);quality.SelectionChanged+=(_,_)=>{pet.SetDetail(quality.SelectedIndex);Status("清晰度已保存 · "+quality.SelectedItem);};display.Children.Add(quality);
        void Check(string name,bool enabled,Action<bool> change){
            var box=new CheckBox{Content=name,IsChecked=enabled,Margin=new Thickness(0,6,0,3)};
            box.Click+=(_,_)=>{change(box.IsChecked==true);pet.Store.Save();};display.Children.Add(box);
        }
        Check("按桌面大小优化渲染",pet.Store.Data.Adaptive,on=>{pet.Store.Data.Adaptive=on;pet.UpdateViewport();});
        display.Children.Add(frameRate);
        display.Children.Add(Text("当前角色的灯光亮度",16));
        Id(lighting,"character-light");foreach(int value in LightLevels)lighting.Items.Add(value==100?"100% · 模型默认":value+"%");
        lighting.SelectionChanged+=(_,_)=>{if(!refreshing && lighting.SelectedIndex>=0)pet.SetLighting(LightLevels[lighting.SelectedIndex]);};display.Children.Add(lighting);
        display.Children.Add(Text("桌面互动",16));
        Check("允许角色在桌面上走动",pet.Store.Data.Roam,on=>{pet.Store.Data.Roam=on;pet.PauseActivity();});
        display.Children.Add(Text("自主动作间隔",14));
        var frequency=new ComboBox{ItemsSource=new[]{"安静 · 45–80 秒","适中 · 22–40 秒","活泼 · 10–20 秒"},SelectedIndex=Math.Clamp(pet.Store.Data.ActivityFrequency,0,2)};
        frequency.SelectionChanged+=(_,_)=>{pet.Store.Data.ActivityFrequency=frequency.SelectedIndex;pet.ScheduleRest();pet.Store.Save();};
        display.Children.Add(frequency);
        Check("在角色旁显示聊天回复",pet.Store.Data.Bubbles,on=>{pet.Store.Data.Bubbles=on;if(!on)pet.ClearBubble();});
        Check("让 AI 随聊天做动作和表情",pet.Store.Data.ChatActions,on=>{pet.Store.Data.ChatActions=on;if(!on)pet.CancelChatReaction();});
        display.Children.Add(Text("桌面大小",16));var scale=new WrapPanel();scale.Children.Add(Btn("缩小",()=>pet.ResizePet(-1)));scale.Children.Add(Btn("放大",()=>pet.ResizePet(1)));display.Children.Add(scale);
        display.Children.Add(Text("鼠标放在角色上滚动，也可以调整大小。",12));
        display.Children.Add(Text("按住鼠标中键左右拖动可旋转一整圈。角色获得焦点时，← / → 每次微调 1°。",12));
        var angles=new WrapPanel();angles.Children.Add(Btn("左转 1°",()=>pet.TurnBy(-1)));angles.Children.Add(Btn("右转 1°",()=>pet.TurnBy(1)));angles.Children.Add(Btn("面向正前",()=>{pet.PauseActivity();pet.SetYaw(0);}));display.Children.Add(angles);
        display.Children.Add(Btn("切换桌面全身 / 半身",()=>pet.SetClose(!pet.Store.Data.CloseUp)));
        display.Children.Add(Text("Ctrl + Alt + P   呼出或收起角色轮盘",12));
        inlineWheel=new Wheel(pet);tabs.Items.Insert(1,new TabItem{Header="轮盘",Content=inlineWheel.Embed()});
        Closing+=(_,e)=>{if(!ending){e.Cancel=true;Hide();}};
        Refresh();
    }
    static void Field(Panel p,string name,TextBox control,string initial){var label=Text(name,12);p.Children.Add(label);control.Text=initial;control.Padding=new Thickness(5);p.Children.Add(control);}
    void Tab(string title,UIElement content){tabs.Items.Add(new TabItem{Header=title,Content=content,Padding=new Thickness(13,6,13,6)});}
    public void Go(string title){tabs.SelectedItem=tabs.Items.Cast<TabItem>().FirstOrDefault(t=>t.Header as string==title)??tabs.Items[0];Refresh();if(title=="聊天")composer.Focus();}
    public void Status(string s)=>status.Text=s;
    public void End(){ending=true;lifetime.Cancel();}
    readonly TextBlock frameRate=Text("",12);
    public bool ReadingChat=>IsVisible && (tabs.SelectedItem as TabItem)?.Header as string=="聊天";
    public void FrameRate(double produced,double displayed){
        if(IsVisible && (tabs.SelectedItem as TabItem)?.Header as string=="显示")
            frameRate.Text=$"Unity {produced:F0} 帧/秒　·　桌面提交 {displayed:F0} 帧/秒";
    }
    static string Explain(Exception e)=>e switch {
        HttpRequestException=>"无法连接聊天服务，请检查网络、服务器地址及证书。",
        OperationCanceledException=>"等待回复超时。已提交的云端消息可用“继续接收”查看。",
        _=>e.GetBaseException().Message
    };
    readonly TextBlock abilities=Text("",12);
    public void Refresh(){
        refreshing=true;
        try{
            var active=pet.Active;string? now=active==null?null:ThreadKey(active);
            var menu=pet.MenuOptions;
            lighting.IsEnabled=active!=null && !pet.Busy;lighting.SelectedIndex=Array.IndexOf(LightLevels,active?.LightPercent??100);
            int actionCount=menu.actions.Count+(!menu.humanoidMotion && !menu.actions.Any(o=>o.id==ReactionMenu.BounceId)?1:0);
            int sourceCount=menu.nodes?.Count??0;
            string source=sourceCount>0?$"原角色菜单 {sourceCount} 项":"按模型能力生成菜单";
            abilities.Text=active==null?"":(menu.humanoidMotion?"人形步态":"通用跳步")+" · "+source+"　"+actionCount+" 个可选动作 · "+menu.faces.Count+" 项表情";
            if(displayed!=now){if(displayed!=null)drafts[displayed]=composer.Text;composer.Text=now!=null?drafts.GetValueOrDefault(now,""):"";displayed=now;}
            heading.Text=active?.Name??"桌边伙伴";onStage.Text=active==null?"":Voice(active.Voice);list.Items.Clear();
            foreach(var c in pet.Store.Data.Characters){var item=new ListBoxItem{Content=CharacterTile(c),Tag=c};AutomationProperties.SetName(item,c.Name);list.Items.Add(item);if(c.Id==active?.Id)list.SelectedItem=item;}
            list.IsEnabled=import.IsEnabled=!pet.Busy;voice.IsEnabled=active!=null && !inflight.Any(x=>x.StartsWith(active.Id+"/"));
            UpdateServiceChoices(active);
            chatTitle.Text=active==null?"先导入角色":active.Name+"  ·  "+Voice(active.Voice);
            var log=active!=null?pet.Store.Chat(active):new ThreadLog();
            timeline.Children.Clear();
            foreach(var line in log.Lines)Bubble(line.Role=="user"?"你":Voice(active!.Voice),line.Text,line.Role=="user");
            if(log.Pending is {} p)Bubble("你 · 等待回复",p.Text,true);
            bool working=now!=null && inflight.Contains(now);
            send.IsEnabled=active!=null && !working && log.Pending==null;
            resume.Visibility=log.Pending!=null && !working?Visibility.Visible:Visibility.Collapsed;
            if(working)timeline.Children.Add(Text("正在等回复…",12));
            string wheelKey=$"{active?.Id}/{active?.File}/{menu.actions.Count}/{menu.clothes.Count}/{menu.faces.Count}/{menu.nodes?.Count}";
            if(!pet.Busy && wheelCharacter!=wheelKey){inlineWheel.RefreshModel();wheelCharacter=wheelKey;}
            scroll.Dispatcher.BeginInvoke(new Action(scroll.ScrollToEnd));
        }finally{refreshing=false;}
    }
    void Bubble(string author,string message,bool own){
        var panel=new StackPanel();var name=Text(author,11);name.Foreground=Accent;panel.Children.Add(name);
        var text=Text(message,14);panel.Children.Add(text);
        timeline.Children.Add(new Border{Background=Brush(own?"#EBE1F4":"#F2EEF1"),Padding=new Thickness(11,3,11,4),Margin=new Thickness(own?24:0,3,own?0:24,3),CornerRadius=new CornerRadius(10),Child=panel});
    }
    async Task Send(bool again=false){
        if(pet.Active is not {} original)return;
        var c=original with {}; string k=ThreadKey(c);
        if(!inflight.Add(k))return;
        var settings=pet.Store.Settings;
        var log=pet.Store.Chat(c,settings);string input=composer.Text.Trim();
        try {
            if(!again && (input.Length==0 || log.Pending!=null))return;
            if((c.Voice=="api" && string.IsNullOrWhiteSpace(settings.Url)) || (c.Voice!="api" && !pet.Store.HasCompanionPair)){Go("服务");Status("先接入自己的 AI，写好的消息会留在聊天页。");return;}
            if(input.Length>4000)throw new InvalidDataException("单条消息最多 4000 字。");
            var generation=pet.AvatarGeneration;
            var menu=pet.Store.Data.ChatActions?ReactionMenu.From(pet.MenuOptions,ReactionMenu.AllowsBody(c),c.Face.Length==0):null;
            if(menu?.Entries.Count==0)menu=null;
            ChatReply reply;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);deadline.CancelAfter(TimeSpan.FromMinutes(5));
            if(c.Voice=="api"){
                Refresh();reply=await Compatible.Reply(settings,c,log,input,menu,deadline.Token);
            }else {
                if(log.Pending==null){
                    using var check=Cloud.Connected(pet.Store.Root);
                    log.Pending=new Pending(Guid.NewGuid().ToString(),input,c.Voice){DesktopMenu=menu};pet.Store.SaveChat(c,log);
                    if(displayed==k)composer.Clear();
                }
                input=log.Pending.Text;Refresh();
                reply=await Cloud.Reply(pet.Store.Root,log.Pending,()=>pet.Store.SaveChat(c,log),deadline.Token);
            }
            log.Lines.Add(new Line("user",input));log.Lines.Add(new Line("assistant",reply.Text));
            log.Pending=null;pet.Store.SaveChat(c,log,settings);drafts.Remove(k);
            if(!ending && generation==pet.AvatarGeneration && ThreadKey(c)==k)pet.ShowChatReply(c,reply,Voice(c.Voice));
            if(displayed==k){composer.Clear();Status("已收到回复");}
        }catch(CloudTurnFailed e){log.Pending=null;pet.Store.SaveChat(c,log);if(displayed==k){composer.Text=input;Status(e.Message);}}
        catch(Exception e){if(displayed==k)Status(Explain(e));}
        finally{inflight.Remove(k);if(!ending)Refresh();}
    }
}
