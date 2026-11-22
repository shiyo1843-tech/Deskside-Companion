// Original tiny glTF portraits. No external textures or art are required.
const fs=require('fs'),path=require('path');
const out=process.argv[2]||path.join(__dirname,'../Assets/StreamingAssets');
fs.mkdirSync(out,{recursive:true});
function make(name,fox){
  const d={asset:{version:'2.0',generator:'DeskPet'},scene:0,scenes:[{nodes:[]}],nodes:[],meshes:[],materials:[],accessors:[],bufferViews:[],buffers:[],animations:[]};
  const chunks=[];let length=0;
  function accessor(list,Type,code,type,width,bounds=false){
    const a=new Type(list),bytes=Buffer.from(a.buffer);
    const view=d.bufferViews.push({buffer:0,byteOffset:length,byteLength:bytes.length})-1;
    chunks.push(bytes);length+=bytes.length;
    const pad=(4-length%4)%4;chunks.push(Buffer.alloc(pad));length+=pad;
    const acc={bufferView:view,componentType:code,type,count:list.length/width};
    if(bounds){acc.min=Array(width).fill(Infinity);acc.max=Array(width).fill(-Infinity);
      list.forEach((n,i)=>{const k=i%width;acc.min[k]=Math.min(acc.min[k],n);acc.max[k]=Math.max(acc.max[k],n);});}
    return d.accessors.push(acc)-1;
  }
  const sphere={p:[],n:[],i:[]};
  for(let y=0;y<=20;y++)for(let x=0;x<=32;x++){
    const lat=-Math.PI/2+y*Math.PI/20,theta=x*2*Math.PI/32;
    const v=[Math.cos(lat)*Math.cos(theta),Math.sin(lat),Math.cos(lat)*Math.sin(theta)];sphere.p.push(...v);sphere.n.push(...v);
    if(y<20&&x<32){let j=y*33+x;sphere.i.push(j,j+33,j+1,j+1,j+33,j+34);}
  }
  function geo(g){return {attributes:{POSITION:accessor(g.p,Float32Array,5126,'VEC3',3,true),NORMAL:accessor(g.n,Float32Array,5126,'VEC3',3)},indices:accessor(g.i,Uint16Array,5123,'SCALAR',1)};}
  const ball=geo(sphere),colors=fox?['#b75034','#ffe7bb','#341f2b','#ed9a73','#456a69','#f4c568','#784136']:['#d7e4df','#fff1d7','#354349','#e59296','#569f9e','#e9b849','#aebec5'];
  const srgb=n=>n<=.04045?n/12.92:((n+.055)/1.055)**2.4;
  colors.forEach((hex,i)=>{const rgb=[1,3,5].map(k=>srgb(parseInt(hex.slice(k,k+2),16)/255));
    d.materials.push({name:['Fur','Cream','Ink','Blush','Scarf','Bell','Shadow'][i],pbrMetallicRoughness:{baseColorFactor:[...rgb,1],metallicFactor:i===5?.35:0,roughnessFactor:.82}});
    d.meshes.push({primitives:[{...ball,material:i}]});});
  function oval(n,m,x,y,z,sx,sy,sz,angle=0){
    const idx=d.nodes.push({name:n,mesh:m,translation:[x,y,z],scale:[sx,sy,sz],rotation:[0,0,Math.sin(angle/2),Math.cos(angle/2)]})-1;
    d.scenes[0].nodes.push(idx);return idx;
  }
  oval('body',0,0,.69,0,.47,.56,.38);oval('bib',1,0,.67,.27,.32,.39,.15);
  for(let s of [-1,1]){
    oval('hind paw',fox?2:0,s*.27,.23,.19,.245,.14,.24);
    oval('ear',0,s*.42,1.74,-.01,.17,.34,.17,-s*.34);
    oval('inner ear',3,s*.445,1.78,.105,.105,.22,.074,-s*.34);
  }
  oval('head',0,0,1.43,.03,.61,.48,.43);
  for(let s of [-1,1]){
    oval('soft cheek',1,s*.285,1.31,.36,.27,.15,.105,s*.08);
    if(fox)oval('red panda eye mask',6,s*.265,1.49,.373,.18,.12,.055,s*.23);
    oval('eye',2,s*.237,1.49,.436,.076,.094,.040);
    oval('catchlight',1,s*.237-.023,1.522,.471,.023,.027,.011);
    oval('rouge',3,s*.40,1.35,.414,.068,.027,.017,-s*.15);
  }
  oval('nose',2,0,1.355,.497,.061,.041,.036);
  for(let s of [-1,1])oval('smile',2,s*.044,1.302,.467,.048,.013,.014,s*.20);
  oval('scarf collar',4,0,1.02,.025,.404,.115,.339);
  oval('ribbon',4,-.23,.866,.295,.12,.176,.045,-.37);
  oval('brass bell',5,.04,.967,.38,.081,.09,.061);
  oval('paw left',0,-.4,.69,.25,.13,.23,.17,-.3);
  const paw=oval('paw right',0,.40,.74,.25,.13,.23,.17,.35);
  for(let i=0;i<5;i++){let t=i/4;oval('curled tail',fox?(i%2===0?0:1):6,.41+.25*Math.sin(t*2.3),.40+.53*t,-.16,.165-.028*t,.18,.17);}
  const times=accessor([0,.25,.48,.70,1],Float32Array,5126,'SCALAR',1,true);
  const qs=[.35,-.53,.14,-.53,.35].flatMap(a=>[0,0,Math.sin(a/2),Math.cos(a/2)]);
  const rots=accessor(qs,Float32Array,5126,'VEC4',4);
  d.animations.push({name:'Wave',samplers:[{input:times,output:rots,interpolation:'LINEAR'}],channels:[{sampler:0,target:{node:paw,path:'rotation'}}]});
  d.buffers=[{byteLength:length}];let json=Buffer.from(JSON.stringify(d));json=Buffer.concat([json,Buffer.alloc((4-json.length%4)%4,32)]);
  const bin=Buffer.concat(chunks),header=Buffer.alloc(20),bh=Buffer.alloc(8);header.write('glTF');header.writeUInt32LE(2,4);header.writeUInt32LE(28+json.length+bin.length,8);
  header.writeUInt32LE(json.length,12);header.write('JSON',16);bh.writeUInt32LE(bin.length);bh.write('BIN\0',4);
  fs.writeFileSync(path.join(out,name+'.glb'),Buffer.concat([header,json,bh,bin]));
}
make('云团',false);make('栗栗',true);
