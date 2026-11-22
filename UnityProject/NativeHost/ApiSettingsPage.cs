using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DeskPet;

public sealed partial class Studio {
    readonly StackPanel companion=new();

    void UpdateServiceChoices(Character? active){
        bool paired=pet.Store.HasCompanionPair;
        companion.Visibility=paired?Visibility.Visible:Visibility.Collapsed;
        voice.SelectedValue=active?.Voice;
    }

    void BuildServicePage(){
        var panel=new StackPanel{Margin=new Thickness(12)};
        Tab("服务",new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=panel});
        var previous=pet.Store.Settings;
        panel.Children.Add(Text("接入自己的 AI",17));
        panel.Children.Add(Text("使用你选择的 Chat Completions 兼容服务，也可以连接本机模型。",12));
        var address=new TextBox();var modelName=new TextBox();var variable=new TextBox();
        Id(address,"api-url");Id(modelName,"api-model");Id(variable,"api-variable");
        Field(panel,"接口地址（完整聊天地址或以 /v1 结尾的基础地址）",address,previous.Url);
        Field(panel,"模型名",modelName,previous.Model);
        var password=new PasswordBox{Padding=new Thickness(5)};
        Id(password,"api-key");panel.Children.Add(Text("API Key",12));panel.Children.Add(password);
        var keyNote=Text("",12);panel.Children.Add(keyNote);
        var advanced=new StackPanel();
        Field(advanced,"从环境变量读取密钥（可选）",variable,string.IsNullOrWhiteSpace(previous.Url)?"":previous.KeyVariable);
        var forget=new CheckBox{Content="清除这个接口已保存的 Key",Margin=new Thickness(0,5,0,5)};
        advanced.Children.Add(forget);
        panel.Children.Add(new Expander{Header="更多密钥设置",Content=advanced,Margin=new Thickness(0,5,0,5)});
        void KeyHint(){
            bool saved=pet.Store.Settings.ProtectedKey.Length>0 && pet.Store.Settings.Url==address.Text.Trim();
            forget.Visibility=saved?Visibility.Visible:Visibility.Collapsed;
            keyNote.Text=saved?"本机已保存 Key。留空沿用，填写可替换。":"填入自己的 Key，由当前 Windows 账户加密保存。本机免密服务可留空。";
        }
        address.TextChanged+=(_,_)=>{password.Clear();variable.Clear();forget.IsChecked=false;KeyHint();};
        KeyHint();
        var save=Btn("保存并开始聊天",()=>{
            try{
                if(pet.Active is {} busy && inflight.Any(k=>k.StartsWith(busy.Id+"/"))){Status("请等当前角色回复后再切换服务。");return;}
                var configured=Compatible.Configure(pet.Store.Settings,address.Text,modelName.Text,variable.Text,password.Password,forget.IsChecked==true);
                string? firstDraft=string.IsNullOrWhiteSpace(pet.Store.Settings.Url)?composer.Text:null;
                pet.Store.SaveSettings(configured);
                if(pet.Active is {} c && c.Voice!="api"){pet.CancelChatReaction();pet.ClearBubble();c.Voice="api";pet.Store.Save();}
                password.Clear();forget.IsChecked=false;address.Text=configured.Url;variable.Text=configured.KeyVariable;KeyHint();Go("聊天");
                if(!string.IsNullOrEmpty(firstDraft))composer.Text=firstDraft;
                Status("已保存，可以聊天了。");
            }catch(Exception e){Status(Explain(e));}
        });
        Id(save,"api-save");panel.Children.Add(save);
        panel.Children.Add(companion);
        companion.Children.Add(Text("当前角色的聊天身份",15));Id(voice,"voice");
        voice.SelectedValuePath="Tag";
        foreach(string id in new[]{"gpt","gemini","api"})voice.Items.Add(new ComboBoxItem{Content=Voice(id),Tag=id});
        companion.Children.Add(voice);
        voice.SelectionChanged+=(_,_)=>{
            if(refreshing || pet.Active is not {} c || voice.SelectedValue is not string id || c.Voice==id)return;
            pet.CancelChatReaction();pet.ClearBubble();c.Voice=id;pet.Store.Save();Refresh();
        };
        if(pet.Store.HasCompanionPair || File.Exists(Environment.GetEnvironmentVariable("DESKPET_CLOUD_CONFIG")) ||
            Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","AliCompanion","versions"))){
            var pair=Btn("导入璃伴配对文件…",async()=>{
                try{var f=new OpenFileDialog{Filter="璃伴配对|*.alilink"};if(f.ShowDialog(this)!=true)return;
                    await Cloud.Pair(pet.Store.Root,f.FileName);Refresh();Status("桌宠已与云端配对");}
                catch(Exception e){Status(Explain(e));}
            });
            panel.Children.Add(new Expander{Header="已有璃伴客户端",Content=pair,Margin=new Thickness(0,7,0,5)});
        }
    }
}
