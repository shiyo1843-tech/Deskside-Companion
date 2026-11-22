using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DeskPet;

internal static class PackageConversion {
    public static string EditorFor(string project,string preferred){
        string version="2022.3.22f1";
        if(project.Length!=0){
            var file=Path.Combine(project,"ProjectSettings","ProjectVersion.txt");
            if(!File.Exists(file))return "";
            var m=Regex.Match(File.ReadAllText(file),@"(?m)^m_EditorVersion:\s*([\w.]+)");
            if(!m.Success)return "";
            version=m.Groups[1].Value;
        }
        string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Unity","Hub","Editor",version,"Editor","Unity.exe");
        return File.Exists(preferred)?preferred:File.Exists(standard)?standard:"";
    }
    static void CopyTree(string from,string to,CancellationToken ct){
        Directory.CreateDirectory(to);
        foreach(string path in Directory.EnumerateFileSystemEntries(from)){
            ct.ThrowIfCancellationRequested();
            var attributes=File.GetAttributes(path);
            // Source links must never become write-through links in the temporary project.
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("工程包含链接目录或文件，请使用依赖已放在工程内的副本："+path);
            var name=Path.GetFileName(path);
            if(name is ".git" or ".vs" or ".svn")continue;
            var target=Path.Combine(to,name);
            if((attributes&FileAttributes.Directory)!=0)CopyTree(path,target,ct);
            else{File.Copy(path,target);File.SetAttributes(target,FileAttributes.Normal);}
        }
    }
    static void Prepare(string job,string source,string version,CancellationToken ct){
        Directory.CreateDirectory(Path.Combine(job,"Assets"));
        Directory.CreateDirectory(Path.Combine(job,"Packages"));
        Directory.CreateDirectory(Path.Combine(job,"ProjectSettings"));
        string manifest=Path.Combine(job,"Packages","manifest.json");
        if(source.Length!=0){
            source=Path.GetFullPath(source);
            if(!File.Exists(Path.Combine(source,"ProjectSettings","ProjectVersion.txt")) || !Directory.Exists(Path.Combine(source,"Assets")) || !File.Exists(Path.Combine(source,"Packages","manifest.json")))
                throw new InvalidDataException("请选择含 Assets、Packages、ProjectSettings 的 Unity 工程根目录。");
            foreach(string part in new[]{"Assets","Packages","ProjectSettings"})CopyTree(Path.Combine(source,part),Path.Combine(job,part),ct);
            var json=JsonNode.Parse(File.ReadAllText(manifest)) ?? throw new InvalidDataException("工程的 Packages/manifest.json 无法读取。");
            if(json["dependencies"] is JsonObject deps){
                foreach(var dep in deps.ToList()){
                    string? v=(string?)dep.Value;
                    if(v!=null && v.StartsWith("file:",StringComparison.OrdinalIgnoreCase)){
                        string pkg=Path.GetFullPath(v[5..],Path.Combine(source,"Packages"));
                        string rel=Path.GetRelativePath(source,pkg);
                        bool copied=new[]{"Assets","Packages","ProjectSettings"}.Any(p=>rel.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                        string local=Path.Combine(job,rel);
                        if(!copied){
                            var cache=Path.Combine(job,"LocalPackages");Directory.CreateDirectory(cache);
                            local=Path.Combine(cache,Guid.NewGuid().ToString("N")+(File.Exists(pkg)?Path.GetExtension(pkg):""));
                            if(Directory.Exists(pkg) && File.Exists(Path.Combine(pkg,"package.json")))CopyTree(pkg,local,ct);
                            else if(File.Exists(pkg))File.Copy(pkg,local);
                            else throw new IOException("找不到工程的本地依赖："+dep.Key+" · "+pkg);
                        }
                        deps[dep.Key]="file:"+Path.GetRelativePath(Path.Combine(job,"Packages"),local).Replace('\\','/');
                    }
                }
                File.WriteAllText(manifest,json.ToJsonString());
            }
        }else{
            File.WriteAllText(Path.Combine(job,"ProjectSettings","ProjectVersion.txt"),"m_EditorVersion: "+version+"\n");
            File.WriteAllText(manifest,"{\"dependencies\":{\"com.unity.modules.animation\":\"1.0.0\",\"com.unity.modules.assetbundle\":\"1.0.0\",\"com.unity.modules.physics\":\"1.0.0\",\"com.unity.modules.audio\":\"1.0.0\",\"com.unity.modules.cloth\":\"1.0.0\"}}");
        }
    }
    public static async Task<string> Run(UnityPackage package,PackageAsset asset,string scratch,string source,string editor,Action<string> status,CancellationToken ct,string? logDirectory=null){
        if(asset.Ready){
            await Task.Run(()=>package.Extract(asset,scratch,false,ct),ct);
            return Path.Combine(scratch,Path.GetFileName(asset.Path));
        }
        if(!File.Exists(editor)||!Path.GetFileName(editor).Equals("Unity.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("首次转换 Prefab / FBX 需要本机 Unity 编辑器。在导入窗口选择 Unity.exe；通用版按 Unity 2022.3.22f1 验证。");
        string job=Path.Combine(scratch,"Project");string output=Path.Combine(scratch,Path.GetFileNameWithoutExtension(asset.Path)+".petmodel");
        string version=Directory.GetParent(editor)?.Parent?.Name??"2022.3.22f1";
        if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+[abfp]\d+$"))version="2022.3.22f1";
        status(source.Length==0?"正在准备临时 Unity 工程…":"正在复制所选工程的素材和依赖…");
        await Task.Run(()=>{
            Prepare(job,source,version,ct);
            package.Extract(asset,job,true,ct);
            string scripts=Path.Combine(job,"Assets","DeskPetImport_"+Guid.NewGuid().ToString("N"),"Editor");Directory.CreateDirectory(scripts);
            foreach(string n in new[]{"DeskPetExporter.cs","DeskPetVRChat.cs","DeskPetPackageBuild.cs"}){
                using var res=typeof(PackageConversion).Assembly.GetManifestResourceStream("PackageTools."+n)??throw new IOException("缺少内置转换器："+n);
                using var reader=new StreamReader(res);
                File.WriteAllText(Path.Combine(scripts,n),"namespace DesktopPetPackageConversion {\n"+reader.ReadToEnd()+"\n}\n");
            }
        },ct);
        ct.ThrowIfCancellationRequested();
        status("Unity 正在导入材质和动画并生成角色，首次转换可能需要几分钟…");
        string result=Path.Combine(scratch,"result.json");
        string logDir=logDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPet","Logs");
        Directory.CreateDirectory(logDir);
        string log=Path.Combine(logDir,"UnityImport-"+Path.GetFileName(scratch)+".log");
        var info=new ProcessStartInfo(editor){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(var arg in new[]{"-batchmode","-quit","-buildTarget","StandaloneWindows64","-projectPath",job,"-logFile",log,"-executeMethod","DesktopPetPackageConversion.DeskPetPackageBuild.ConvertSelected","-deskPetAsset",asset.Path,"-deskPetOutput",output,"-deskPetResult",result})info.ArgumentList.Add(arg);
        using var child=Process.Start(info)??throw new IOException("无法启动 Unity 编辑器。");
        using var stop=ct.Register(()=>{try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}});
        await child.WaitForExitAsync();ct.ThrowIfCancellationRequested();
        if(File.Exists(result)){
            using var json=JsonDocument.Parse(await File.ReadAllTextAsync(result,ct));
            if(json.RootElement.TryGetProperty("error",out var err) && !string.IsNullOrWhiteSpace(err.GetString()))throw new InvalidDataException(err.GetString());
        }
        if(child.ExitCode!=0 || !File.Exists(result)||!File.Exists(output)){
            var detail=File.Exists(log)?File.ReadLines(log).FirstOrDefault(l=>l.Contains("error CS")||l.Contains("No valid Unity Editor license")||l.Contains("Failed to resolve packages")):null;
            throw new InvalidDataException("Unity 未完成转换。"+(detail??"请核对编辑器版本、许可证和原工程插件。")+"\n日志："+log);
        }
        return output;
    }
}
