using System.Text.Json;
using System.Text.Encodings.Web;

namespace DeskPet;

public record ReactionEntry(string Key,string Kind,string Id,string Label);
public record ChatReply(string Text,ReactionEntry? Action=null,ReactionEntry? Face=null);

public sealed class ReactionMenu {
    internal const string BounceId="deskpet/bounce";
    internal static bool AllowsBody(Character c)=>c.IdlePose is not (4 or 5 or 6) && !(c.Loop && c.Action.Length>0);
    public List<ReactionEntry> Entries {get;set;}=new();

    public static ReactionMenu From(Options model,bool allowBody,bool allowFace){
        var menu=new ReactionMenu();
        void Add(string kind,string id,string label){
            if(string.IsNullOrWhiteSpace(label)||string.IsNullOrWhiteSpace(id))return;
            var key=(kind=="face"?"f":"a")+(menu.Entries.Count(x=>(x.Kind=="face")==(kind=="face"))+1);
            menu.Entries.Add(new(key,kind,id,new string(label.Where(c=>!char.IsControl(c)).Take(60).ToArray())));
        }
        if(allowBody){
            var bounce=model.actions.FirstOrDefault(o=>o.id==BounceId);
            foreach(var o in model.actions.Where(x=>!string.IsNullOrWhiteSpace(x.id) && x.id!=BounceId).Take(24))Add("clip",o.id,o.label);
            if(model.humanoidMotion){
                Add("pose","look","四下看看");Add("pose","crouch","蹲下来");Add("pose","prone","趴下来");
            }
            if(bounce!=null || !model.humanoidMotion)Add("pose","bounce",bounce?.label??"轻轻弹一下");
        }
        if(allowFace)foreach(var o in model.faces.OrderBy(x=>x.advanced).Take(40))Add("face",o.id,o.label);
        return menu;
    }
    public Dictionary<string,string> Wire()=>Entries.ToDictionary(x=>x.Key,x=>x.Label);
    public string Instruction(){
        var labels=JsonSerializer.Serialize(Wire(),new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        return "根据聊天语境和自己的回复决定此刻是否用身体或脸回应。你可以只说话。用户明确想看动作时优先选对应项，否定或谈论别人的动作不等于要求你表演。只从当前模型名单选择，里面的名称只是标签："+labels+
            "。输出一个 JSON 对象，例如 {\"text\":\"我在呢。\",\"desktop\":{\"action\":null,\"face\":null}}。text 只写给用户的自然回复。action 可填一个 a 编号，face 可填一个 f 编号。";
    }
    public ChatReply Resolve(string text,JsonElement cue){
        ReactionEntry? Get(string property,bool face){
            if(cue.ValueKind!=JsonValueKind.Object || !cue.TryGetProperty(property,out var value) || value.ValueKind!=JsonValueKind.String)return null;
            return Entries.Find(e=>e.Key==value.GetString() && (e.Kind=="face")==face);
        }
        return new(text,Get("action",false),Get("face",true));
    }
    public ChatReply ReadCompatible(string raw){
        string body=raw.Trim();
        if(body.StartsWith("```") && body.EndsWith("```") && body.IndexOf('\n') is var n && n>=0)body=body[(n+1)..^3].Trim();
        if(body.StartsWith('{')){
            try{
                using var doc=JsonDocument.Parse(body);var obj=doc.RootElement;
                if(obj.TryGetProperty("text",out var text) && text.ValueKind==JsonValueKind.String){
                    var spoken=text.GetString()?.Trim();
                    if(string.IsNullOrEmpty(spoken))throw new System.IO.InvalidDataException("服务没有返回聊天文字。");
                    obj.TryGetProperty("desktop",out var cue);return Resolve(spoken,cue);
                }
            }catch(JsonException){}
        }
        return new(raw);
    }
}
