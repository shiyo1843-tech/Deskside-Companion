// The only substituted boundary is the model/host. No network, files or user authorization to run an action.
using System.Windows;
namespace DeskPet;
public sealed record Character(string Id,string Name) {
    public Dictionary<string,float> Parameters {get;}=new(); public Dictionary<string,bool> Wear {get;}=new();
    public string Face="",Action=""; public int IdlePose; public bool Loop;
}
public record Option(string id,string label,bool initial=false,bool advanced=false);
public record WheelNode(string id,string parent,string label,string kind,string parameter,string icon,string[] axes,string[] axisLabels,float value,float initial,bool available);
public sealed class Options {
    public List<Option> faces=new(),clothes=new(),actions=new(); public List<WheelNode> nodes=new(); public bool humanoidMotion=true;
}
static class ReactionMenu {public const string BounceId="bounce";}
public sealed class StoreData {public List<Character> Characters=new();public bool CloseUp;}
public sealed class Library {public readonly StoreData Data=new();}
public sealed class Studio:Window {public void Status(string _){}}
public sealed class PetWindow:Window {
    public readonly Library Store=new();public readonly Studio Panel=new();
    public Character? Active {get;set;} public readonly Options MenuOptions=new();
    public bool IsExiting=>false; public string? LastTab,LastAction;public int Applied;
    public void Open(string tab)=>LastTab=tab;public void ApplyControls(bool play=false)=>Applied++;
    public void SetClose(bool value)=>Store.Data.CloseUp=value;public void TogglePin()=>Topmost=!Topmost;
    public void Exit()=>LastAction="exit";public Task Select(Character c){Active=c;return Task.CompletedTask;}
    public void ChooseIdle(int p){Active!.IdlePose=p;}public int SupportedIdle(int p)=>p;public void PlayMenuAction(string id)=>LastAction=id;
}
