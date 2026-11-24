using System.IO;
using System.Text;
using System.Text.Json.Nodes;

// Add two morph targets to the procedural sample only for native preview/tests.
// Never write to the distributed samples, or read any privately supplied avatar.
static class GlbFaceFixture {
    public static string Create(string dir) {
        string p=Path.Combine(AppContext.BaseDirectory,"示例模型","云团.glb");
        using var input=new BinaryReader(File.OpenRead(p));if(input.ReadUInt32()!=0x46546C67 || input.ReadInt32()!=2)throw new InvalidDataException("expected GLB 2");
        input.ReadInt32();int n=input.ReadInt32();if(input.ReadUInt32()!=0x4E4F534A)throw new InvalidDataException("expected GLB JSON");
        var json=(JsonObject)JsonNode.Parse(input.ReadBytes(n))!;n=input.ReadInt32();if(input.ReadUInt32()!=0x004E4942)throw new InvalidDataException("expected GLB BIN");
        using var data=new MemoryStream();using var bin=new BinaryWriter(data);bin.Write(input.ReadBytes(n));
        var head=json["nodes"]!.AsArray().Single(v=>v!["name"]!.GetValue<string>()=="head")!;
        var meshes=json["meshes"]!.AsArray();
        JsonObject mesh=meshes[head["mesh"]!.GetValue<int>()]!.DeepClone().AsObject();
        head["mesh"]=meshes.Count;head["name"]="脸部";
        mesh["extras"]=new JsonObject{["targetNames"]=new JsonArray((JsonNode?)"微笑",(JsonNode?)"眨眼")};mesh["weights"]=new JsonArray((JsonNode?)0,(JsonNode?)0);
        var views=json["bufferViews"]!.AsArray();var access=json["accessors"]!.AsArray();
        var primitive=mesh["primitives"]![0]!;int count=access[primitive["attributes"]!["POSITION"]!.GetValue<int>()]!["count"]!.GetValue<int>();
        var targets=new JsonArray();
        foreach(float delta in new[]{.015f,-.015f}){
            int view=views.Count;views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=(int)data.Position,["byteLength"]=count*12});
            for(int i=0;i<count;i++){bin.Write(0f);bin.Write(delta);bin.Write(0f);}
            int ix=access.Count;access.Add(new JsonObject{["bufferView"]=view,["componentType"]=5126,["count"]=count,["type"]="VEC3",
                ["min"]=new JsonArray((JsonNode?)0,(JsonNode?)delta,(JsonNode?)0),["max"]=new JsonArray((JsonNode?)0,(JsonNode?)delta,(JsonNode?)0)});
            targets.Add(new JsonObject{["POSITION"]=ix});
        }
        primitive["targets"]=targets;meshes.Add(mesh);json["buffers"]![0]!["byteLength"]=(int)data.Length;
        byte[] text=Encoding.UTF8.GetBytes(json.ToJsonString());int pad=(4-text.Length%4)%4;
        var file=Path.Combine(dir,"验收云团.glb");using var w=new BinaryWriter(File.Create(file));
        w.Write(0x46546C67u);w.Write(2);w.Write(28+text.Length+pad+(int)data.Length);
        w.Write(text.Length+pad);w.Write(0x4E4F534Au);w.Write(text);for(int i=0;i<pad;i++)w.Write((byte)32);
        w.Write((int)data.Length);w.Write(0x004E4942u);w.Write(data.ToArray());return file;
    }
}
