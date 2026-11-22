using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeskPet;
public sealed record Character(string Id,string Name,string File) {
    public string Voice {get;set;}="api";
    public int Yaw {get;set;}
    public int LightPercent {get;set;}=100;
    public string Face {get;set;}="";
    public bool Loop {get;set;}
    public string Action {get;set;}="";
    public int IdlePose {get;set;}
    public Dictionary<string,bool> Wear {get;set;}=new();
    public Dictionary<string,float> Parameters {get;set;}=new();
}
public sealed class Catalog {
    public Dictionary<string,string> PackageImports {get;set;}=new();
    public string PackageProject {get;set;}="";
    public string PackageEditor {get;set;}="";
    public List<Character> Characters {get;set;}=new();
    public string? Active {get;set;}
    public double? X {get;set;}
    public double? Y {get;set;}
    public bool Pinned {get;set;}=true;
    public int Detail {get;set;}=2;
    public double Size {get;set;}=420;
    public bool CloseUp {get;set;}
    public bool Roam {get;set;}=true;
    public int ActivityFrequency {get;set;}=0;
    public bool Bubbles {get;set;}=true;
    public bool ChatActions {get;set;}=true;
    public bool Adaptive {get;set;}=true;
}
public sealed record Line(string Role,string Text);
public sealed class ThreadLog {
    public List<Line> Lines {get;set;}=new();
    public Pending? Pending {get;set;}
}
public sealed record Pending(string Id,string Text,string Voice) {
    public string? EventId {get;set;}
    public long Cursor {get;set;}
    public ReactionMenu? DesktopMenu {get;set;}
    public bool? SendDesktopMenu {get;set;}
}
public sealed class ServiceSettings {
    public string Url {get;set;}="";
    public string Model {get;set;}="";
    public string KeyVariable {get;set;}="DESKPET_API_KEY";
    public string ProtectedKey {get;set;}="";
    public int HistoryVersion {get;set;}
}
public sealed class Library {
    public string Root {get;}
    public Catalog Data {get;}
    public ServiceSettings Settings {get;set;}
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public Library(string root) {
        Root=Path.GetFullPath(root); Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root,"Models")); Directory.CreateDirectory(Path.Combine(Root,"Chats"));
        Data=Read<Catalog>("library.json"); Settings=Read<ServiceSettings>("service.json");
        if(Settings.HistoryVersion==0 && Uri.TryCreate(Settings.Url,UriKind.Absolute,out _)){
            foreach(var character in Data.Characters){
                var c=character with {Voice="api"};
                string old=Path.Combine(Root,"Chats",c.Id+"-api.json"),current=Path.Combine(Root,ChatFile(c,Settings));
                if(old!=current && File.Exists(old) && !File.Exists(current))File.Move(old,current);
                if(old!=current && File.Exists(old+".bak") && !File.Exists(current+".bak"))File.Move(old+".bak",current+".bak");
            }
            Settings.HistoryVersion=1;SaveSettings(Settings);
        }
    }
    T Read<T>(string file) where T:new() {
        string path=Path.Combine(Root,file);
        if(!File.Exists(path))return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path))??throw new InvalidDataException(file); }
        catch(JsonException) { if(File.Exists(path+".bak"))return JsonSerializer.Deserialize<T>(File.ReadAllText(path+".bak"))??throw new InvalidDataException(file); throw; }
    }
    void Write<T>(string file,T obj,bool backup=true) {
        var path=Path.Combine(Root,file); var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                JsonSerializer.Serialize(f,obj,Json); f.Flush(true);
            }
            if(File.Exists(path)) File.Replace(temp,path,backup?path+".bak":null); else File.Move(temp,path);
        } finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public void Save()=>Write("library.json",Data);
    public void SaveSettings(ServiceSettings settings){
        Write("service.json",settings,false);Settings=settings;
        File.Delete(Path.Combine(Root,"service.json.bak"));
    }
    public bool HasCompanionPair=>File.Exists(Path.Combine(Root,"device.bin")) ||
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AliCompanion","protected-state.bin"));
    public static string ChatService(Character c,ServiceSettings settings){
        if(c.Voice!="api" || !Uri.TryCreate(settings.Url,UriKind.Absolute,out var endpoint))return c.Voice;
        var identity=Encoding.UTF8.GetBytes(endpoint.AbsoluteUri+"\n"+settings.Model.Trim());
        return "api-"+Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
    }
    static string ChatFile(Character c,ServiceSettings settings)=>Path.Combine("Chats",c.Id+"-"+ChatService(c,settings)+".json");
    public ThreadLog Chat(Character c,ServiceSettings? settings=null)=>Read<ThreadLog>(ChatFile(c,settings??Settings));
    public void SaveChat(Character c,ThreadLog log,ServiceSettings? settings=null)=>Write(ChatFile(c,settings??Settings),log);
    public string Model(Character c)=>Path.Combine(Root,"Models",c.File);
    public async Task UpgradeBundles(string dir){
        string index=Path.Combine(dir,"upgrades.json");if(!File.Exists(index))return;
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(index));
        foreach(var item in doc.RootElement.EnumerateArray()){
            string? prior=item.GetProperty("previous").GetString(),name=item.GetProperty("name").GetString(),hash=item.GetProperty("current").GetString();
            int i=Data.Characters.FindIndex(c=>c.File==prior+".petmodel");if(i<0 || name!=Path.GetFileName(name))continue;
            var staged=await Stage(Path.Combine(dir,name+".petmodel"));if(staged.File!=hash+".petmodel")throw new InvalidDataException("内置角色包校验失败。");
            Data.Characters[i]=Data.Characters[i] with {File=staged.File};Save();
        }
    }
    public async Task<Character> Stage(string path) {
        string ext=Path.GetExtension(path).ToLowerInvariant();
        if(ext is not (".vrm" or ".glb" or ".petmodel"))throw new InvalidDataException(
            ext==".prefab" ? "单个 Prefab 引用了工程内其他文件。请导入含相关素材的 .unitypackage 或在原工程导出 .petmodel。" :
            "暂不支持 "+ext+"。请选择 .vrm、内嵌资源的 .glb、Windows .petmodel 或 .unitypackage。");
        string temp=Path.Combine(Root,"Models",Guid.NewGuid().ToString("N")+".tmp");
        try {
            await using(var src=File.OpenRead(path)) await using(var dst=File.Create(temp)) await src.CopyToAsync(dst);
            using var content=File.OpenRead(temp);
            string hash=Convert.ToHexString(await SHA256.HashDataAsync(content)).ToLowerInvariant(); content.Close();
            string filename=hash+ext;
            var existing=Data.Characters.FirstOrDefault(c=>c.File==filename);
            string dest=Path.Combine(Root,"Models",filename);
            if(!File.Exists(dest))File.Move(temp,dest);
            return existing??new Character(hash,Path.GetFileNameWithoutExtension(path),filename) {
                Voice=HasCompanionPair
                    ? (Data.Characters.Count(c=>c.Voice=="gpt")>Data.Characters.Count(c=>c.Voice=="gemini")?"gemini":"gpt") : "api"
            };
        } finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
