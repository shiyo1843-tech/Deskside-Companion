using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace DeskPet;
public sealed record CloudConfig(string endpoint,string certificate_sha256);
public static class Cloud {
    static string Local=>Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly byte[] NativePurpose=Encoding.UTF8.GetBytes("AliCompanion.native.v1");
    static readonly byte[] PetPurpose=Encoding.UTF8.GetBytes("DeskPet.cloud.v1");
    public static CloudConfig Config(){
        var dir=Path.Combine(Local,"Programs","AliCompanion","versions");
        string? path=Environment.GetEnvironmentVariable("DESKPET_CLOUD_CONFIG");
        if(string.IsNullOrEmpty(path) && Directory.Exists(dir))path=Directory.GetDirectories(dir).Select(d=>Path.Combine(d,"build-config.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if(path==null || !File.Exists(path))throw new InvalidOperationException("未找到璃伴服务器配置。请安装你的璃伴 Windows 客户端，或设置 DESKPET_CLOUD_CONFIG。");
        var c=JsonSerializer.Deserialize<CloudConfig>(File.ReadAllText(path));
        if(c==null || !Uri.TryCreate(c.endpoint,UriKind.Absolute,out var u) || u.Scheme!="https" || u.UserInfo!="" || u.AbsolutePath!="/" || u.Query!="" || u.Fragment!="" || !System.Text.RegularExpressions.Regex.IsMatch(c.certificate_sha256??"","^[a-fA-F0-9]{64}$"))throw new InvalidDataException("璃伴服务器配置无效。");
        return c;
    }
    static string? Token(string root){
        string own=Path.Combine(root,"device.bin");
        if(File.Exists(own)){
            var plain=ProtectedData.Unprotect(File.ReadAllBytes(own),PetPurpose,DataProtectionScope.CurrentUser);
            try{return Encoding.UTF8.GetString(plain);}finally{CryptographicOperations.ZeroMemory(plain);}
        }
        string existing=Path.Combine(Local,"AliCompanion","protected-state.bin");
        if(!File.Exists(existing))return null;
        var data=ProtectedData.Unprotect(File.ReadAllBytes(existing),NativePurpose,DataProtectionScope.CurrentUser);
        try{using var document=JsonDocument.Parse(data);if(document.RootElement.TryGetProperty("device",out var device)&&device.ValueKind==JsonValueKind.Object)return device.GetProperty("token").GetString();return null;}
        finally{CryptographicOperations.ZeroMemory(data);}
    }
    static HttpClient Client(CloudConfig c,string? token){
        var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false};
        handler.ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>cert!=null && (errors&(SslPolicyErrors.RemoteCertificateNameMismatch|SslPolicyErrors.RemoteCertificateNotAvailable))==0 && DateTime.UtcNow>=cert.NotBefore.ToUniversalTime() && DateTime.UtcNow<=cert.NotAfter.ToUniversalTime() && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData),Convert.FromHexString(c.certificate_sha256));
        var h=new HttpClient(handler){BaseAddress=new Uri(c.endpoint),Timeout=TimeSpan.FromSeconds(35)};
        if(token!=null)h.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return h;
    }
    public static async Task Pair(string root,string path){
        var c=Config(); if(new FileInfo(path).Length>32768)throw new InvalidDataException("配对文件太大。");
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path)); var p=doc.RootElement;
        if(new Uri(p.GetProperty("endpoint").GetString()!)!=new Uri(c.endpoint) || !string.Equals(p.GetProperty("certificate_sha256").GetString(),c.certificate_sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("配对文件与已安装璃伴的服务器不匹配。");
        using var h=Client(c,null);
        var paired=await Fetch(h,"/v1/pair",new{pair_code=p.GetProperty("pair_code").GetString(),platform="windows",label="桌边伙伴"});
        var raw=Encoding.UTF8.GetBytes(paired.GetProperty("token").GetString()!);
        try{string dest=Path.Combine(root,"device.bin");File.WriteAllBytes(dest+".tmp",ProtectedData.Protect(raw,PetPurpose,DataProtectionScope.CurrentUser));File.Move(dest+".tmp",dest,true);}finally{CryptographicOperations.ZeroMemory(raw);}
    }
    public static HttpClient Connected(string root){
        var c=Config(); string? token=Token(root);
        if(string.IsNullOrWhiteSpace(token))throw new InvalidOperationException("本机还没有璃伴配对。请在服务设置里导入新的 .alilink 配对文件，或先在璃伴完成配对。");
        return Client(c,token);
    }
    public static async Task<JsonElement> Fetch(HttpClient h,string path,object? body=null,CancellationToken ct=default){
        using var req=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,path);
        if(path=="/v1/health")req.Headers.Add("X-DeskPet-Capabilities","1");
        if(body!=null)req.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var res=await h.SendAsync(req,ct);
        if(!res.IsSuccessStatusCode)throw new IOException(res.StatusCode switch {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"服务拒绝认证，请检查密钥或重新配对。",
            HttpStatusCode.TooManyRequests=>"服务繁忙或额度用尽，请稍后再试。",
            _=>"服务返回 HTTP "+(int)res.StatusCode+"，请检查配置或稍后重试。"});
        using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));return doc.RootElement.Clone();
    }
    public static async Task<ChatReply> Reply(string root,Pending request,Action checkpoint,CancellationToken ct) {
        using var h=Connected(root);
        var basePath="/v1/conversations/"+request.Voice;
        if(request.EventId==null){
            var payload=new Dictionary<string,object>{{"client_message_id",request.Id},{"text",request.Text},{"attachments",Array.Empty<string>()}};
            if(request.DesktopMenu?.Entries.Count>0){
                if(!request.SendDesktopMenu.HasValue){
                    var health=await Fetch(h,"/v1/health",null,ct);
                    request.SendDesktopMenu=health.TryGetProperty("desktop_reactions",out var supported)&&supported.ValueKind==JsonValueKind.True;
                    checkpoint();
                }
                if(request.SendDesktopMenu==true)payload["desktop_menu"]=request.DesktopMenu.Wire();
            }
            var response=await Fetch(h,basePath+"/messages",payload,ct);
            var e=response.GetProperty("event");request.EventId=e.GetProperty("event_id").GetString();
            request.Cursor=Math.Max(0,e.GetProperty("created_seq").GetInt64()-1);checkpoint();
        }
        // Re-scan from the accepted event on reconnect. Only correlated replies from this persona qualify.
        long cursor=request.Cursor;
        var parts=new Dictionary<string,string>();
        bool complete=false;JsonElement cue=default;
        while(!ct.IsCancellationRequested) {
            var page=await Fetch(h,$"{basePath}/events?limit=50&after={cursor}&wait=15",null,ct);
            foreach(var e in page.GetProperty("items").EnumerateArray()){
                cursor=Math.Max(cursor,e.GetProperty("seq").GetInt64());
                if(e.GetProperty("persona").GetString()!=request.Voice)continue;
                string? id=e.GetProperty("event_id").GetString(), status=e.GetProperty("status").GetString();
                if(id==request.EventId && status=="failed")throw new CloudTurnFailed("云端未完成这次回复："+e.GetProperty("error_code").GetString());
                if(id==request.EventId && status=="completed")complete=true;
                if(e.GetProperty("author_type").GetString()=="assistant" && e.GetProperty("reply_to_event_id").GetString()==request.EventId) {
                    var lines=e.GetProperty("content_parts").EnumerateArray().Select(p=>p.GetProperty("type").GetString()=="text"?p.GetProperty("text").GetString():"[附件，可在璃伴查看]");
                    parts[id!]=string.Join("\n",lines);
                    if(e.TryGetProperty("desktop_reaction",out var reaction))cue=reaction.Clone();
                }
            }
            if(complete && parts.Count>0 && !page.GetProperty("has_more").GetBoolean()){
                var text=string.Join("\n",parts.Values);
                return request.DesktopMenu?.Resolve(text,cue)??new ChatReply(text);
            }
            if(complete && !page.GetProperty("has_more").GetBoolean() && parts.Count==0)throw new CloudTurnFailed("云端已结束此轮，没有返回可显示的消息。");
        }
        throw new OperationCanceledException();
    }
}
public sealed class CloudTurnFailed(string message):Exception(message);
public static class Compatible {
    internal static Uri Endpoint(string url){
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || string.IsNullOrEmpty(uri.Host) || uri.UserInfo!="" || uri.Fragment!="" || uri.Query!="" || (uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback)))throw new InvalidDataException("请填写完整的 HTTPS 聊天接口。本机服务也可使用 HTTP。");
        if(uri.AbsolutePath.TrimEnd('/').EndsWith("/v1",StringComparison.OrdinalIgnoreCase))
            return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/chat/completions");
        if(uri.AbsolutePath=="/")throw new InvalidDataException("请填写服务商提供的完整聊天地址，或以 /v1 结尾的基础地址。");
        return uri;
    }
    static byte[] Purpose(Uri endpoint)=>Encoding.UTF8.GetBytes("DeskPet.api-key.v1\n"+endpoint.AbsoluteUri);
    internal static ServiceSettings Configure(ServiceSettings previous,string url,string model,string variable,string key,bool forget){
        var endpoint=Endpoint(url.Trim());
        if(string.IsNullOrWhiteSpace(model))throw new InvalidDataException("请填写 AI 服务提供的模型名。");
        var settings=new ServiceSettings{Url=endpoint.AbsoluteUri,Model=model.Trim(),KeyVariable=variable.Trim(),HistoryVersion=1};
        if(!string.IsNullOrWhiteSpace(key)){
            byte[] bytes=Encoding.UTF8.GetBytes(key.Trim());
            try{settings.ProtectedKey=Convert.ToBase64String(ProtectedData.Protect(bytes,Purpose(endpoint),DataProtectionScope.CurrentUser));}
            finally{CryptographicOperations.ZeroMemory(bytes);}
        }else if(!forget && Uri.TryCreate(previous.Url,UriKind.Absolute,out var old) && old.AbsoluteUri==endpoint.AbsoluteUri){
            settings.ProtectedKey=previous.ProtectedKey;
        }
        return settings;
    }
    static string? ApiKey(ServiceSettings s,Uri endpoint){
        if(!string.IsNullOrEmpty(s.ProtectedKey)){
            try{
                var bytes=ProtectedData.Unprotect(Convert.FromBase64String(s.ProtectedKey),Purpose(endpoint),DataProtectionScope.CurrentUser);
                try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
            }catch(Exception e) when(e is CryptographicException or FormatException){throw new InvalidDataException("这台电脑无法读取保存的 API Key，请在服务页重新填写。");}
        }
        if(string.IsNullOrWhiteSpace(s.KeyVariable))return null;
        var key=Environment.GetEnvironmentVariable(s.KeyVariable)??Environment.GetEnvironmentVariable(s.KeyVariable,EnvironmentVariableTarget.User);
        if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("请在服务页填写 API Key，或设置环境变量 "+s.KeyVariable+"。");
        return key;
    }
    public static async Task<ChatReply> Reply(ServiceSettings s,Character ch,ThreadLog log,string message,ReactionMenu? menu,CancellationToken ct){
        var uri=Endpoint(s.Url);
        if(string.IsNullOrWhiteSpace(s.Model))throw new InvalidDataException("请先填写服务的模型名。");
        using var h=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)};
        var key=ApiKey(s,uri);
        if(key!=null)h.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",key);
        var messages=new List<object>{new {role="system",content="你是桌面伙伴「"+ch.Name+"」。请用中文自然简短地和用户聊天。"+menu?.Instruction()}};
        foreach(var l in log.Lines.TakeLast(32))messages.Add(new {role=l.Role,content=l.Text});
        messages.Add(new {role="user",content=message});
        var res=await Cloud.Fetch(h,uri.AbsoluteUri,new{model=s.Model,messages},ct);
        string? result=res.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if(string.IsNullOrWhiteSpace(result))throw new InvalidDataException("服务没有返回文字。");
        return menu?.ReadCompatible(result)??new ChatReply(result);
    }
}
