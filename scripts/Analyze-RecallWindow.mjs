import fs from "node:fs";
import path from "node:path";
import readline from "node:readline";
const root=path.resolve(process.argv[2]);
const manifest=JSON.parse(fs.readFileSync(path.join(root,"session-manifest.json"),"utf8").replace(/^\uFEFF/,""));
if(!manifest.CompletedAt)throw Error("Writers not closed");
const start=Date.parse(manifest.RawCaptureStartedAt),end=Date.parse(manifest.RawCaptureCompletedAt);
const stats={},ids={},tracks=new Map(),rejections={};let firstPipe,lastPipe;
function sample(k,v){(stats[k]??=[]).push(v);}
function identity(k,id){(ids[k]??=new Set()).add(id);}
for(const file of ["agent-live-compare.jsonl","map-diagnostics.jsonl"]){
 for await(const line of readline.createInterface({input:fs.createReadStream(path.join(root,"raw-capture",file)),crlfDelay:Infinity})){
  const r=JSON.parse(line),at=Date.parse(r.recordedAt??r.ReceivedAt);if(at<start||at>end)continue;
  if(r.stage==="agent-post-fusion"){
   firstPipe??=r.pipeline;lastPipe=r.pipeline;
   const ps=r.mapOutputPlayers??[],ai=r.mapOutputAi??[];
   sample("agentPlayers",ps.length);sample("agentPredictedPlayers",ps.filter(e=>e.provisional).length);
   sample("agentVerifiedPlayers",ps.filter(e=>!e.provisional).length);sample("agentAiCandidates",ai.filter(e=>e.provisional).length);
   sample("agentQueueDepth",(r.pipeline.realtimeQueueDepth??0)+(r.pipeline.proofQueueDepth??0));
   sample("agentAnchoredMovement",ps.filter(e=>e.locationEvidenceSource==="AnchoredOwnerMovement").length);
   for(const e of ps){identity(e.provisional?"predictedPlayers":"verifiedPlayers",e.trackId);if(e.locationEvidenceSource==="SerializedActorCreation")identity("serializedProvenancePlayers",e.trackId);if(e.locationEvidenceSource==="AnchoredOwnerMovement")identity("anchoredMovementPlayers",e.trackId);}
  }
  if(r.stage==="render-end"){
   const ms=r.RenderedMarkers??[],ps=ms.filter(m=>m.Key?.includes("pro-entity:player:"));
   sample("renderPlayers",ps.length);sample("renderPredictedPlayers",ps.filter(m=>m.IsProvisional).length);
   sample("renderFreshPlayers",ps.filter(m=>!m.IsStale).length);sample("renderStalePlayers",ps.filter(m=>m.IsStale).length);
   for(const [k,v]of Object.entries(r.ProTrackingDiagnostics?.Rejections??{}))rejections[k]=(rejections[k]??0)+v;
   const seen=new Set(ps.map(m=>m.Key));
   for(const m of ps){identity(m.IsProvisional?"renderPredictedPlayers":"renderVerifiedPlayers",m.Key);let t=tracks.get(m.Key);if(!t){t={key:m.Key,segments:[],open:at,first:at,last:at,provisional:m.IsProvisional};tracks.set(m.Key,t);}if(t.open===null)t.open=at;t.last=at;}
   for(const t of tracks.values())if(t.open!==null&&!seen.has(t.key)){t.segments.push((at-t.open)/1000);t.open=null;}
  }
 }
}
const distributions={};for(const[k,a]of Object.entries(stats)){a.sort((x,y)=>x-y);distributions[k]={samples:a.length,min:a[0],p50:a[Math.floor((a.length-1)*.5)],p95:a[Math.floor((a.length-1)*.95)],max:a.at(-1)};}
const counters={};for(const k of ["capturedPackets","processedPackets","queueDroppedPackets","npcapDroppedPackets","sequenceGapPackets","duplicatePackets","reorderedPackets"]){counters[k]={start:firstPipe?.[k],end:lastPipe?.[k],delta:(lastPipe?.[k]??0)-(firstPipe?.[k]??0)};}
const result={status:"NEED_STAGE_EVIDENCE",scope:"Frozen Agent/render logs filtered to raw capture window; not independent game truth. AI ? is legacy discovery, not new player recall.",start:new Date(start).toISOString(),end:new Date(end).toISOString(),durationSeconds:(end-start)/1000,distributions,identities:Object.fromEntries(Object.entries(ids).map(([k,v])=>[k,[...v]])),counters,rejectionObservations:rejections,tracks:[...tracks.values()].map(t=>({...t,maxContinuousSeconds:Math.max(0,...t.segments,t.open===null?0:(end-t.open)/1000)})),limitations:["No outbound raw capture or packet-event stream in this session.","Initial/prewarm packets before capture start cannot be replayed from this raw file.","No matched baseline run; marker count alone does not measure recall improvement."]};
result.limitations=[
 fs.existsSync(path.join(root,"raw-capture/udp-outbound.bin"))?"Outbound raw exists; verify time coverage before correlating.":"No outbound raw capture in this session.",
 fs.existsSync(path.join(root,"raw-capture/packet-events.jsonl"))?"Packet-event stream exists; latest-event matching alone is not causality.":"No packet-event stream in this session.",
 "Check capture start versus launcher/prewarm before claiming initial packet coverage.",
 "No independent game ground truth; marker count alone does not measure recall improvement."
];
fs.writeFileSync(path.join(root,process.argv[3]??"recall-window-analysis.json"),JSON.stringify(result,null,2),{flag:"wx"});
console.log(JSON.stringify({...result,tracks:result.tracks.map(t=>({key:t.key,maxContinuousSeconds:t.maxContinuousSeconds,segments:t.segments.length}))},null,2));
