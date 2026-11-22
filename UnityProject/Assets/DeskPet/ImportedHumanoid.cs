using System;
using System.Collections.Generic;
using System.Linq;
using UniHumanoid;
using UnityEngine;

// A plain glTF skin has joint indices but no standard humanoid mapping.
// Only build an Avatar when the named, skinned hierarchy is unambiguous.
public sealed class ImportedHumanoid : MonoBehaviour {
    Avatar owned;
    static readonly Dictionary<string,HumanBodyBones> Names=NamesForCommonRigs();

    static Dictionary<string,HumanBodyBones> NamesForCommonRigs(){
        var names=new Dictionary<string,HumanBodyBones>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<(int)HumanBodyBones.LastBone;i++){
            var bone=(HumanBodyBones)i;
            var human=bone.ToString();
            string side=human.StartsWith("Left")?"left":human.StartsWith("Right")?"right":"";
            string part=human.Substring(side.Length).ToLowerInvariant();
            string aliases=part;
            switch(part){
                case "hips": aliases+=" pelvis";break;
                case "spine": aliases+=" spine01";break;
                case "chest": aliases+=" spine1 spine02";break;
                case "upperchest": aliases+=" spine2 spine03";break;
                case "neck": aliases+=" neck01";break;
                case "shoulder": aliases+=" clavicle collar";break;
                case "upperarm": aliases+=" arm";break;
                case "lowerarm": aliases+=" forearm elbow";break;
                case "hand": aliases+=" wrist";break;
                case "upperleg": aliases+=" upleg thigh";break;
                case "lowerleg": aliases+=" leg calf shin knee";break;
                case "foot": aliases+=" ankle";break;
                case "toes": aliases+=" toe toebase ball";break;
            }
            foreach(string alias in aliases.Split(' ')){
                if(side.Length==0){names[alias]=bone;continue;}
                names[side+alias]=bone;names[alias+side]=bone;
                names[side[0]+alias]=bone;names[alias+side[0]]=bone;
            }
        }
        return names;
    }

    static string Key(string name){
        string tail=name.Substring(name.LastIndexOf(':')+1);
        string key=new string(tail.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if(key.StartsWith("mixamorig"))return key.Substring(9).TrimStart('0','1','2','3','4','5','6','7','8','9');
        if(key.StartsWith("jbipc"))return key.Substring(5);
        return key.StartsWith("jbip")?key.Substring(4):key;
    }

    public static bool TryBind(GameObject model){
        var animators=model.GetComponentsInChildren<Animator>(true);
        if(animators.Any(a=>a.avatar && a.avatar.isValid && a.isHuman))return true;
        if(animators.Any(a=>a.runtimeAnimatorController))return false;
        var used=new HashSet<Transform>();
        foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach(var joint in skin.bones)
                for(var t=joint;t && t!=model.transform;t=t.parent)used.Add(t);
        if(used.Count==0)return false;
        var map=new Dictionary<HumanBodyBones,Transform>();
        foreach(var group in used.Where(t=>Names.ContainsKey(Key(t.name))).GroupBy(t=>Names[Key(t.name)])){
            var joints=group.ToArray();var bone=group.Key;
            if(joints.Length>1)joints=joints.Where(t=>string.Equals(Key(t.name),bone.ToString(),StringComparison.OrdinalIgnoreCase)).ToArray();
            if(joints.Length==1)map.Add(bone,joints[0]);
        }
        for(int i=0;i<(int)HumanBodyBones.LastBone;i++){
            var bone=(HumanBodyBones)i;
            if(!map.TryGetValue(bone,out var joint)){if(HumanTrait.RequiredBone(i)){Debug.Log("Missing humanoid joint: "+bone);return false;}continue;}
            int parent=HumanTrait.GetParentBone(i);
            while(parent>=0 && !map.ContainsKey((HumanBodyBones)parent))parent=HumanTrait.GetParentBone(parent);
            if(parent>=0 && !joint.IsChildOf(map[(HumanBodyBones)parent])){Debug.Log("Incompatible humanoid hierarchy: "+bone);return false;}
        }
        var description=ScriptableObject.CreateInstance<AvatarDescription>();
        Avatar avatar=null;
        var original=new Dictionary<Transform,Quaternion>();
        try{
            // AvatarBuilder takes these transforms as its reference T pose. GLBs can
            // be saved in a relaxed or animated pose, even with correct joint names.
            var side=(map[HumanBodyBones.RightUpperArm].position-map[HumanBodyBones.LeftUpperArm].position).normalized;
            void Aim(HumanBodyBones start,HumanBodyBones end,Vector3 direction){
                var t=map[start];var delta=map[end].position-t.position;
                if(delta.sqrMagnitude<.00000001f)return;
                original[t]=t.localRotation;t.rotation=Quaternion.FromToRotation(delta,direction)*t.rotation;
            }
            Aim(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,-side);Aim(HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,-side);
            Aim(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,side);Aim(HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,side);
            Aim(HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,Vector3.down);Aim(HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,Vector3.down);
            Aim(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,Vector3.down);Aim(HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,Vector3.down);
            description.SetHumanBones(map);
            avatar=AvatarBuilder.BuildHumanAvatar(model,description.ToHumanDescription(model.transform));
            if(!avatar || !avatar.isValid || !avatar.isHuman)return false;
            var animator=model.GetComponent<Animator>()??model.AddComponent<Animator>();
            animator.avatar=avatar;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            model.AddComponent<ImportedHumanoid>().owned=avatar;avatar=null;
            return true;
        }catch(Exception error){
            Debug.LogWarning("Humanoid mapping unavailable: "+error.Message);return false;
        }finally{
            foreach(var joint in original)joint.Key.localRotation=joint.Value;
            if(avatar)Destroy(avatar);
            Destroy(description);
        }
    }
    void OnDestroy(){if(owned)Destroy(owned);}
}
