import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import crypto from 'node:crypto';
const root = path.resolve(process.argv[2]);
const output = path.join(root, 'recall-audit.json');
if (fs.existsSync(output)) throw new Error('Refusing to overwrite audit');
const read = async (name, visit) => {
  const input = fs.createReadStream(path.join(root, name));
  for await (const line of readline.createInterface({input, crlfDelay: Infinity})) if(line.trim()) visit(JSON.parse(line));
};
const percentile = (a,p) => a.length ? [...a].sort((a,b)=>a-b)[Math.min(a.length-1, Math.floor((a.length-1)*p))] : null;
const summary = a => ({min: percentile(a,0), p50:percentile(a,.5),p95:percentile(a,.95),max:percentile(a,1)});
const inspect = JSON.parse(fs.readFileSync(path.join(root,'inspection-inbound.json'),'utf8').replace(/^\uFEFF/,''))[0];
const creations = new Set(inspect.ActorCreations.map(x=>String(x.ActorHandle)));
const updates = new Map();
for(const b of inspect.BatchEvents) updates.set(String(b.NetRefHandle),(updates.get(String(b.NetRefHandle))||0)+1);
const tracks = new Map(), frames=[], markers = new Map(), renderCounts=[];
let pipeline, agentFrames=0, first,last,unknownKinds=[];
await read('raw-capture/agent-live-compare.jsonl', f=>{
 if(f.stage!=='agent-post-fusion') return;
 agentFrames++; const at=Date.parse(f.frameObservedAt);first??=at;last=at;pipeline=f.pipeline;
 const entities=[...(f.mapOutputPlayers||[]),...(f.mapOutputAi||[])];
 frames.push({players:(f.mapOutputPlayers||[]).length,ai:(f.mapOutputAi||[]).length,total:entities.length,provisional:entities.filter(e=>e.provisional).length,verifiedPosition:entities.filter(e=>e.hasVerifiedPosition).length,fresh:entities.filter(e=>e.locationAgeMs<=2000).length});
 for(const e of entities){
   const key=e.kind+':'+e.trackId;
   if(!tracks.has(key)) tracks.set(key,{key,handle:String(e.actorNetRefHandle),kind:e.kind,species:e.speciesId,first:at,last:at,observations:0,provisional:0,verifiedPosition:0,locationAges:[],locationChanges:0,maxJumpMeters:0,maxGapSeconds:0,previous:null});
   const t=tracks.get(key); t.maxGapSeconds=Math.max(t.maxGapSeconds,(at-t.last)/1000);t.last=at;t.observations++;t.provisional+=Number(!!e.provisional);t.verifiedPosition+=Number(!!e.hasVerifiedPosition);t.locationAges.push(e.locationAgeMs);
   if(t.previous&&e.location){let dist=Math.hypot(e.location.x-t.previous.x,e.location.y-t.previous.y,(e.location.z||0)-(t.previous.z||0))/100;t.maxJumpMeters=Math.max(t.maxJumpMeters,dist);if(dist>0)t.locationChanges++;} t.previous=e.location;
 }
});
let renderRows=0;
await read('raw-capture/map-diagnostics.jsonl',r=>{
 if(r.stage!=='render-end')return;renderRows++; const at=Date.parse(r.ReceivedAt);const list=(r.RenderedMarkers||[]).filter(m=>m.Key?.includes('pro-entity:'));
 renderCounts.push({total:list.length,player:list.filter(m=>m.Key.includes('pro-entity:player:')).length,ai:list.filter(m=>m.Key.includes('pro-entity:ai:')).length,provisional:list.filter(m=>m.IsProvisional).length,stale:list.filter(m=>m.IsStale).length});
 for(const m of list){const key=m.Key.match(/pro-entity:(player|ai):(\d+)/)?.slice(1).join(':');if(!key)continue;
 if(!markers.has(key))markers.set(key,{key,first:at,last:at,samples:0,stale:0,provisional:0,maxGapSeconds:0});
 const v=markers.get(key);v.maxGapSeconds=Math.max(v.maxGapSeconds,(at-v.last)/1000);v.last=at;v.samples++;v.stale+=Number(!!m.IsStale);v.provisional+=Number(!!m.IsProvisional);
 }
});
const trackRows=[...tracks.values()].map(t=>{const {previous,locationAges,...rest}=t;return {...rest,spanSeconds:(t.last-t.first)/1000,locationAgeMs:summary(locationAges),creationInCapture:creations.has(t.handle),batchEvents:updates.get(t.handle)||0,render:markers.get(t.key.toLowerCase())||null};});
const hash = async file => {const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(path.join(root,file)))h.update(b);return h.digest('hex');};
const hashes={};for(const file of ['raw-capture/udp-inbound.bin','raw-capture/udp-outbound.bin','raw-capture/udp-capture.bin']) hashes[file]=await hash(file);
const result={status:'NEED_STAGE_EVIDENCE',scope:'Packet and runtime diagnostic; neither actor count nor heuristically decoded positions are independent ground truth.',hashes,agentFrames,renderRows,spanSeconds:(last-first)/1000,agentCounts:Object.fromEntries(Object.keys(frames[0]).map(k=>[k,summary(frames.map(f=>f[k]))])),renderCounts:Object.fromEntries(Object.keys(renderCounts[0]).map(k=>[k,summary(renderCounts.map(f=>f[k]))])),pipeline,raw:{packets:inspect.PacketCount,parsed:inspect.ParsedPacketCount,creations:inspect.ActorCreationCount,distinctCreationActors:creations.size,distinctBatchHandles:updates.size,updateOnlyHandles:[...updates.keys()].filter(k=>!creations.has(k)).length,identityDiscoveries:inspect.DiscoveredPlayerIdentityCount,structuralPairCandidates:inspect.StructuralReferencePairs.length,provenActors:inspect.ProvenPlayerActorCount,signals:inspect.CreatureSignalHistory},tracks:trackRows,renderTracks:[...markers.values()].map(m=>({...m,spanSeconds:(m.last-m.first)/1000}))};
fs.writeFileSync(output,JSON.stringify(result,null,2),{flag:'wx'});
console.log(JSON.stringify({...result,tracks:trackRows.filter(t=>t.kind==='Player'),renderTracks:undefined},null,2));
console.log('AI tracks:',trackRows.filter(t=>t.kind==='Ai').length,'Output:',output);
