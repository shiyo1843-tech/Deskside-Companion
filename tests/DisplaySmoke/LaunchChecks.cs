using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

static class LaunchChecks {
    delegate bool WindowVisit(nint h,nint v);
    [DllImport("user32.dll")]static extern bool EnumDesktopWindows(nint desk,WindowVisit visit,nint data);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(nint h,StringBuilder text,int size);
    [DllImport("user32.dll")]static extern int GetWindowThreadProcessId(nint h,out int pid);
    [DllImport("user32.dll")]static extern bool PostMessage(nint h,uint msg,nint w,nint l);
    public static int Run(string result,string entry,nint desk,string profileName="DisplayTrialProfile"){
        var notes=new List<string>();bool passed=false,started=false;Process? app=null;nint pet=0;
        string root=Path.GetFullPath(Path.GetDirectoryName(entry)!);
        string profile=Path.GetFullPath(Path.Combine(root,profileName));
        void Pass(string s)=>notes.Add("PASS "+s);
        bool Until(Func<bool> condition,int ms){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<ms){if(condition())return true;Thread.Sleep(100);}return condition();}
        nint Window(string title){
            nint found=0;EnumDesktopWindows(desk,(h,_)=>{if(!IsWindowVisible(h))return true;
                var b=new StringBuilder(200);GetWindowText(h,b,b.Capacity);if(b.ToString()!=title)return true;found=h;return false;},0);return found;
        }
        try{
            if(Directory.Exists(profile))throw new InvalidOperationException("intended fresh trial profile already exists; refuse to modify");
            string cmd=Path.Combine(Environment.SystemDirectory,"cmd.exe");
            var info=new ProcessStartInfo(cmd){Arguments=$"/d /c \"\"{entry}\"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            using var shell=Process.Start(info)??throw new InvalidOperationException("launch CMD did not start");started=true;
            if(!shell.WaitForExit(8000) || shell.ExitCode!=0)throw new InvalidOperationException("actual launch CMD failed");
            Pass("exact delivered "+Path.GetFileName(entry)+" exited successfully, on private desktop");
            if(!Until(()=>(pet=Window("桌边伙伴 · 角色"))!=0,45000))throw new InvalidOperationException("No app HWND from delivered launcher");
            GetWindowThreadProcessId(pet,out int pid);app=Process.GetProcessById(pid);
            var loaded=Path.Combine(profile,"library.json");
            if(!Until(()=>{try{using var json=JsonDocument.Parse(File.ReadAllText(loaded));return json.RootElement.GetProperty("Active").ValueKind==JsonValueKind.String;}catch(IOException){return false;}catch(JsonException){return false;}},50000))
                throw new InvalidOperationException("fresh delivered profile did not activate rendered model");
            Pass("self-contained EXE and its Unity renderer loaded the bundled sample in "+profileName);
            if(!Until(()=>Window("桌边伙伴 · 控制台")!=0,4000))throw new InvalidOperationException("Console not open");
            var control=AutomationElement.FromHandle(Window("桌边伙伴 · 控制台"));
            var display=control.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.NameProperty,"显示"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem)));
            ((SelectionItemPattern)(display??throw new InvalidOperationException("actual Display tab not found")).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            bool Has(string text)=>control.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,text))!=null;
            if(!Until(()=>Has("画质")&&Has("桌面互动"),6500))throw new InvalidOperationException("published executable missing trial cards");
            Pass("opened actual Display tab: both new cards are present");
            if(profileName=="SubwheelsTrialProfile"){
                PostMessage(pet,0x0312,3741,0);
                if(!Until(()=>Window("桌边伙伴 · 轮盘")!=0,5000))throw new InvalidOperationException("Published wheel shortcut did not open");
                var wheel=AutomationElement.FromHandle(Window("桌边伙伴 · 轮盘"));
                if(!new[]{"角色菜单","表情","衣装","聊天","动作","桌宠设置"}.All(name=>wheel.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,name))!=null))
                    throw new InvalidOperationException("Published wheel home labels changed");
                Pass("original global shortcut handler and all six home entries in published EXE");
            }
            PostMessage(pet,0x0010,0,0);
            if(!app.WaitForExit(12000))throw new InvalidOperationException("published app did not shut down normally");
            Pass("published app shut down normally");
            passed=true;
        }catch(Exception e){notes.Add("FAIL "+e);}
        finally{
            if(started && pet==0)pet=Window("桌边伙伴 · 角色");
            if(pet!=0 && app is {HasExited:false}){PostMessage(pet,0x0010,0,0);if(!app.WaitForExit(4000))app.Kill(true);}
            // The exact launcher created this fresh test profile. Archive it within the
            // same folder tree only after the private app has shut down, leaving a clean first run.
            if(started && (app?.HasExited??false) && Directory.Exists(profile) &&
               Directory.GetParent(profile)?.FullName==root &&
               (File.GetAttributes(profile)&FileAttributes.ReparsePoint)==0 &&
               result.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {
                Directory.Move(profile,Path.Combine(result,"verified-launch-profile-"+DateTime.UtcNow.ToString("HHmmss")));
            }
            File.WriteAllLines(Path.Combine(result,"display-smoke.txt"),notes);
        }
        return passed?0:1;
    }
}
