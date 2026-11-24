using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

// Exercise the actual signed-by-neither-party local build, not a lookalike. Use a new
// non-interactive Win32 desktop: no screenshots/input on the user's real desktop.
internal static class PlayerSmoke {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int cb;public string? reserved,desktop,title;public int x,y,cx,cy,bufx,bufy,fill,flags;
        public short show,bytes;public nint res,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public nint process,thread;public int pid,tid;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateDesktop(string name,nint device,nint mode,uint flags,uint access,nint security);
    [DllImport("user32.dll")]static extern nint GetThreadDesktop(int tid);
    [DllImport("kernel32.dll")]static extern int GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(nint h,int index,StringBuilder info,int length,out int count);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder cmd,nint ps,nint ts,bool inherit,uint flags,nint env,string dir,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(nint h,out uint code);
    delegate bool EnumWindow(nint hwnd,nint p);
    [DllImport("user32.dll")]static extern bool EnumDesktopWindows(nint desktop,EnumWindow each,nint data);
    [DllImport("user32.dll")]static extern int GetWindowThreadProcessId(nint w,out int pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(nint w,StringBuilder b,int max);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint w);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(nint w,uint msg,nint wp,nint lp);
    static nint desktop;static Process? process;static readonly List<string> log=new();
    static void Note(string s){log.Add(s);Console.WriteLine(s);}
    static void Ok(bool b,string s){if(!b)throw new Exception(s);Note("PASS "+s);}
    static bool Until(Func<bool> predicate,int ms){var time=Stopwatch.StartNew();while(time.ElapsedMilliseconds<ms){if(predicate())return true;if(process?.HasExited==true)return false;Thread.Sleep(90);}return predicate();}
    static nint Window(string expected,bool visible=true){
        nint value=0;EnumDesktopWindows(desktop,(w,_)=>{GetWindowThreadProcessId(w,out int pid);if(pid!=process!.Id || visible && !IsWindowVisible(w))return true;
            var b=new StringBuilder(256);GetWindowText(w,b,b.Capacity);if(b.ToString()==expected){value=w;return false;}return true;},0);return value;
    }
    static HashSet<string> Words(nint hwnd){var root=AutomationElement.FromHandle(hwnd);
        return root.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Select(a=>a.Current.Name).Where(s=>s.Length>0).ToHashSet();}
    [STAThread]static int Main(string[] args){
        string exe=Path.GetFullPath(args[0]),outRoot=Path.GetFullPath(args[1]),run=args.Length==4?Path.GetFullPath(args[2]):Path.Combine(outRoot,"player-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Environment.ProcessId);
        if(args.Length!=4){
            // STA/COM can create a hidden OLE window before Main, making SetThreadDesktop fail
            // with ERROR_BUSY. Start the verifier on its target desktop from the beginning.
            Directory.CreateDirectory(run);string testDesktop="StarWheelQA-"+Guid.NewGuid().ToString("N");
            nint desk=CreateDesktop(testDesktop,0,0,0,0x01ff,0);if(desk==0){Console.Error.WriteLine(new Win32Exception());return 1;}
            string helper=Environment.ProcessPath!;var si=new Startup{cb=Marshal.SizeOf<Startup>(),desktop=testDesktop,flags=1,show=0};
            var cmd=new StringBuilder($"\"{helper}\" \"{exe}\" \"{outRoot}\" \"{run}\" {testDesktop}");
            if(!CreateProcess(helper,cmd,0,0,false,0x08000000,0,AppContext.BaseDirectory,ref si,out var child)){Console.Error.WriteLine(new Win32Exception());return 1;}
            using var cp=Process.GetProcessById(child.pid);CloseHandle(child.thread);
            if(!cp.WaitForExit(145000)){cp.Kill(true);Console.Error.WriteLine("private test process timed out");return 1;}
            string result=Path.Combine(run,"result.txt");if(File.Exists(result))Console.Write(File.ReadAllText(result));Console.WriteLine("Evidence: "+run);
            bool got=GetExitCodeProcess(child.process,out var code);CloseHandle(child.process);return got?(int)code:1;
        }
        Directory.CreateDirectory(run);try{
            var profile=Path.Combine(run,"profile");Directory.CreateDirectory(profile);File.WriteAllText(Path.Combine(profile,"library.json"),"""{"Detail":0,"Pinned":false,"Roam":false,"Adaptive":true}""");
            Environment.SetEnvironmentVariable("DESKPET_API_KEY",null);Environment.SetEnvironmentVariable("DESKPET_CLOUD_CONFIG",null);Environment.SetEnvironmentVariable("OPENAI_API_KEY",null);
            string name=args[3];desktop=GetThreadDesktop(GetCurrentThreadId());var actual=new StringBuilder(256);
            if(!GetUserObjectInformation(desktop,2,actual,actual.Capacity*2,out _) || actual.ToString()!=name)
                throw new InvalidOperationException("Verifier not attached to the intended private desktop.");
            var startup=new Startup{cb=Marshal.SizeOf<Startup>(),desktop=name,flags=1,show=0};
            if(!CreateProcess(exe,new StringBuilder($"\"{exe}\" --data-dir \"{profile}\""),0,0,false,0,0,Path.GetDirectoryName(exe)!,ref startup,out var pi))throw new Win32Exception();
            process=Process.GetProcessById(pi.pid);CloseHandle(pi.thread);Note("Published EXE launched on a new non-active desktop, PID "+process.Id+"; isolated empty profile.");
            bool ModelLoaded(){try{using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(profile,"library.json")));var json=doc.RootElement;
                return json.TryGetProperty("Active",out var id) && id.ValueKind==JsonValueKind.String && json.GetProperty("Characters").EnumerateArray().Any(c=>c.GetProperty("Id").GetString()==id.GetString() && c.GetProperty("Name").GetString()=="云团");
            }catch(IOException){return false;}catch(JsonException){return false;}}
            Ok(Until(ModelLoaded,75000),"Unity returned a real rendered frame and the host activated the original generic sample 云团.");
            nint pet=Window("桌边伙伴 · 角色");Ok(pet!=0,"real pet HWND exists");
            const string wheelTitle="桌边伙伴 · 轮盘";void Hot(){if(!PostMessage(pet,0x0312,3741,0))throw new Win32Exception();}
            Hot();Ok(Until(()=>Window(wheelTitle)!=0,5000),"Ctrl+Alt+P's process-targeted WM_HOTKEY handler opened the floating wheel");
            nint wheel=Window(wheelTitle);var home=new[]{"角色菜单","表情","衣装","聊天","动作","桌宠设置"};
            Ok(Until(()=>home.All(Words(wheel).Contains),5000),"real WPF accessibility tree contains the six original labels");
            Hot();Ok(Until(()=>Window(wheelTitle)==0,4000),"original shortcut handler dismisses");Hot();Ok(Until(()=>Window(wheelTitle)!=0,4000),"original shortcut handler reopens");
            PostMessage(Window(wheelTitle),0x0010,0,0);Ok(Until(()=>Window(wheelTitle)==0,4000),"normal close request dismisses popup without closing the app");
            PostMessage(pet,0x0010,0,0);Ok(process.WaitForExit(12000),"published app exited cleanly");
            Ok(GetExitCodeProcess(pi.process,out var appCode) && appCode==0,"published app exit code 0");CloseHandle(pi.process);
            // Do not move a real hardware pointer on a non-input desktop. Test mouse routing
            // using WPF's actual input hit test/routed events, with the production host/player.
            ProductionIntegration.Run(run,Ok);return 0;
        }catch(Exception e){Note("FAIL "+e);return 1;}
        finally{if(process is {HasExited:false}){nint p=Window("桌边伙伴 · 角色",false);if(p!=0)PostMessage(p,0x10,0,0);if(!process.WaitForExit(4000))process.Kill(true);}
            File.WriteAllLines(Path.Combine(run,"result.txt"),log);Console.WriteLine("Evidence: "+run);}
    }
}
