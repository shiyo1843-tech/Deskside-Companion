using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Start the real WPF verifier in its private desktop from process creation;
// never switch the visible desktop or inject into the system keyboard/mouse.
static class Program {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Startup{
        public int cb;public string? reserved,desktop,title;public int x,y,cx,cy,bufx,bufy,fill,flags;
        public short show,bytes;public nint res,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public nint process,thread; public int pid,tid;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint CreateDesktop(string name,nint device,nint mode,uint flags,uint access,nint security);
    [DllImport("user32.dll")]static extern nint GetThreadDesktop(int tid);
    [DllImport("kernel32.dll")]static extern int GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(nint h,int index,StringBuilder info,int length,out int count);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string app,StringBuilder cmd,nint ps,nint ts,bool inherit,uint flags,nint env,string dir,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(nint h,out uint code);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(nint h);
    [DllImport("user32.dll")]static extern bool CloseDesktop(nint h);
    [STAThread]static int Main(string[] args){
        string outDir=Path.GetFullPath(args[0]);Directory.CreateDirectory(outDir);
        if(args.Length==4){
            var b=new StringBuilder(256);
            if(!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,b,b.Capacity*2,out _) || b.ToString()!=args[3])return 2;
            foreach(string name in new[]{"DESKPET_API_KEY","DESKPET_CLOUD_CONFIG","OPENAI_API_KEY"})Environment.SetEnvironmentVariable(name,null);
            if(args[1]=="launcher")return LaunchChecks.Run(outDir,args[2],GetThreadDesktop(GetCurrentThreadId()));
            if(args[1]=="wheel-launch")return LaunchChecks.Run(outDir,args[2],GetThreadDesktop(GetCurrentThreadId()),"SubwheelsTrialProfile");
            if(args[1] is "wheel-before" or "wheel-live")return LiveWheelChecks.Run(outDir,args[1]=="wheel-before",args[2]);
            return DisplayChecks.Run(outDir,args[1]=="baseline",args[2]);
        }
        string tag="DisplayQA-"+Guid.NewGuid().ToString("N");nint desk=CreateDesktop(tag,0,0,0,0x01ff,0);
        if(desk==0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create isolated test desktop");
        try{
            string exe=Environment.ProcessPath!;var si=new Startup{cb=Marshal.SizeOf<Startup>(),desktop=tag,flags=1,show=0};
            string compare=args.Length>2?Path.GetFullPath(args[2]):outDir;
            var command=new StringBuilder($"\"{exe}\" \"{outDir}\" {args[1]} \"{compare}\" {tag}");
            if(!CreateProcess(exe,command,0,0,false,0x08000000,0,AppContext.BaseDirectory,ref si,out var info))throw new Win32Exception();
            try{
                CloseHandle(info.thread);using var child=Process.GetProcessById(info.pid);
                if(!child.WaitForExit(90000)){child.Kill(true);Console.Error.WriteLine("Verifier timeout");return 2;}
                string log=Path.Combine(outDir,"display-smoke.txt");if(File.Exists(log))Console.WriteLine(File.ReadAllText(log));
                return GetExitCodeProcess(info.process,out uint code)?(int)code:2;
            }finally{CloseHandle(info.process);}
        }finally{CloseDesktop(desk);}
    }
}
