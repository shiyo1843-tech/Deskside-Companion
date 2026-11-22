using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DeskPet;

internal sealed record PackageAsset(string Guid,string Path){
    public string Extension=>System.IO.Path.GetExtension(Path).ToLowerInvariant();
    public bool Ready=>Extension is ".vrm" or ".glb" or ".petmodel";
    public override string ToString()=>Path;
}

// Unity exports asset, asset.meta and pathname as separate records in arbitrary order.
internal sealed class UnityPackage:IDisposable {
    readonly FileStream input;
    readonly Dictionary<string,PackageAsset> assets=new(StringComparer.OrdinalIgnoreCase);
    public string Hash{get;private set;}="";
    public List<PackageAsset> Choices{get;}=new();
    UnityPackage(string path)=>input=File.OpenRead(path);
    public static UnityPackage Open(string path,CancellationToken ct){
        var pack=new UnityPackage(path);
        try{pack.Index(ct);return pack;}catch{pack.Dispose();throw;}
    }
    void Records(Action<string,string,TarEntry> visit,CancellationToken ct){
        input.Position=0;
        using var gz=new GZipStream(input,CompressionMode.Decompress,true);
        using var tar=new TarReader(gz);
        TarEntry? item;
        while((item=tar.GetNextEntry())!=null){
            ct.ThrowIfCancellationRequested();
            if(item.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))continue;
            string name=item.Name.StartsWith("./",StringComparison.Ordinal)?item.Name[2..]:item.Name;
            var parts=name.Split('/');
            if(parts.Length==2 && parts[0].Length==32 && parts[0].All(Uri.IsHexDigit))visit(parts[0],parts[1],item);
        }
    }
    static bool AssetPath(string path){
        string[] parts=path.Split('/');
        return parts.Length>=2 && parts[0]=="Assets" && parts.All(p=>p.Length!=0 && p!="." && p!=".." &&
            p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())<0 && !p.EndsWith('.') && !p.EndsWith(' ') &&
            !System.Text.RegularExpressions.Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
    void Index(CancellationToken ct){
        Hash=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var bodies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Records((id,kind,item)=>{
            if(kind=="asset"){if(!bodies.Add(id))throw new InvalidDataException("包内资源重复。");return;}
            if(kind!="pathname" || item.DataStream==null)return;
            if(item.Length>32768)throw new InvalidDataException("包内资源路径过长。");
            using var reader=new StreamReader(item.DataStream,Encoding.UTF8,true,1024,true);
            string path=reader.ReadToEnd().TrimEnd('\0','\r','\n');
            if(!path.StartsWith("Assets/",StringComparison.Ordinal))return;
            if(!AssetPath(path)||!paths.Add(path)||!assets.TryAdd(id,new(id,path)))throw new InvalidDataException("包里有重复或无效的资源路径："+path);
        },ct);
        Choices.AddRange(assets.Values.Where(a=>bodies.Contains(a.Guid)&&(a.Ready||a.Extension is ".prefab" or ".fbx"))
            .OrderBy(a=>a.Ready?0:a.Extension==".prefab"?1:2).ThenBy(a=>a.Path,StringComparer.OrdinalIgnoreCase));
        if(Choices.Count==0)throw new InvalidDataException("包里没有可选的角色 Prefab、FBX、VRM、GLB 或 petmodel。只有贴图、动作、场景或编辑器工具的包请先在原工程里组装角色。");
    }
    static readonly HashSet<string> ModelAssets=new((
        ".prefab .fbx .obj .dae .asset .mat .anim .controller .overridecontroller .mask .avatar .shader .cginc .hlsl .glslinc .compute .shadergraph .shadersubgraph "+
        ".png .jpg .jpeg .tga .bmp .psd .tif .tiff .exr .hdr .dds .cubemap .rendertexture .txt .json .bytes .ttf .otf .wav .ogg .mp3").Split(' '),StringComparer.OrdinalIgnoreCase);
    public void Extract(PackageAsset chosen,string target,bool project,CancellationToken ct){
        var root=System.IO.Path.GetFullPath(target)+System.IO.Path.DirectorySeparatorChar;
        var wrote=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Records((id,kind,item)=>{
            if(kind is not ("asset" or "asset.meta") || item.DataStream==null || !assets.TryGetValue(id,out var a))return;
            if(project?!ModelAssets.Contains(a.Extension):a.Guid!=chosen.Guid || kind!="asset")return;
            string filename=project?a.Path+(kind=="asset.meta"?".meta":""):System.IO.Path.GetFileName(a.Path);
            string dest=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,filename));
            if(!dest.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!wrote.Add(dest))throw new InvalidDataException("包内资源路径冲突："+filename);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            using var f=File.Create(dest);
            var buf=new byte[81920];int n;
            while((n=item.DataStream.Read(buf,0,buf.Length))!=0){ct.ThrowIfCancellationRequested();f.Write(buf,0,n);}
        },ct);
    }
    public void Dispose()=>input.Dispose();
}