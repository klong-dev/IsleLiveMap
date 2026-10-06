import fs from 'node:fs';
import path from 'node:path';
const root=path.resolve(process.argv[2]);
// Five-minute debug cycle by default; longer runs require an explicit duration.
const seconds=Number(process.argv[3]||300);
if(!Number.isFinite(seconds)||seconds<1||seconds>1800)throw Error('Invalid duration');
const eventFile=fs.openSync(path.join(root,'live-monitor-events.jsonl'),'wx');
const tails=new Map(),owners=new Map(),entities=new Map(),visible=new Map();
const counts={packets:0,parseFailures:0,initial:0,destroy:0,appeared:0,disappeared:0,agentFrames:0,renderFrames:0,errors:0};
let stageSession=null,lastAgentAt=0,lastRenderAt=0,lastHostAt=0,lastSummary=0;
const emit=(event,data)=>fs.writeSync(eventFile,JSON.stringify({observedByMonitorAt:new Date().toISOString(),event,...data})+'\n');
function tail(file,visit){
 const p=path.join(root,file);if(!fs.existsSync(p))return;
 if(!tails.has(file))tails.set(file,{offset:0,partial:''});const t=tails.get(file);
 const size=fs.statSync(p).size;if(size<t.offset)throw Error('Input truncated: '+file);
 const fd=fs.openSync(p,'r');try{while(t.offset<size){const b=Buffer.alloc(Math.min(1024*1024,size-t.offset));const n=fs.readSync(fd,b,0,b.length,t.offset);if(!n)break;t.offset+=n;const lines=(t.partial+b.subarray(0,n).toString('utf8')).split('\n');t.partial=lines.pop();for(const l of lines){if(!l.trim())continue;try{visit(JSON.parse(l));}catch(e){counts.errors++;emit('ParseOrMonitorError',{file,message:String(e)});}}}}finally{fs.closeSync(fd);}
}
function packet(r){counts.packets++;if(!r.parsed){counts.parseFailures++;return;}
 for(const evidence of r.endReplicationEvidence||[]) emit('EndReplicationWireEvidence',{
     endpoint:r.sourceAddress+':'+r.sourcePort,sequence:r.sequence,rawOffset:r.rawOffset,
     capturedAt:r.at,complete:r.complete,evidence,semanticClassification:'unknown'});
 for(const b of r.batches||[]){const k=r.sourceAddress+':'+r.sourcePort+':'+b.owner;const old=owners.get(k)||{batches:0,initialCount:0};owners.set(k,{...old,lastAt:r.at,sequence:r.sequence,rawOffset:r.rawOffset,bitOffset:b.offset,bits:b.bits,batches:old.batches+1,initialCount:old.initialCount+Number(b.initial),ownerData:b.ownerData});if(b.initial){counts.initial++;emit('InitialPacket',{owner:k,packet:owners.get(k)});}}
 for(const id of r.destroyed||[]){counts.destroy++;const k=r.sourceAddress+':'+r.sourcePort+':'+id;const p=owners.get(k)||{};owners.set(k,{...p,destroyedAt:r.at,destroySequence:r.sequence});emit('DestroyOrScopeExitPacket',{owner:k,sequence:r.sequence,at:r.at});}}
function proof(e,endpoint){
 return owners.get(endpoint+':'+(e.actorNetRefHandle||e.trackId))||null;
}
function agent(f){if(f.stage!=='agent-post-fusion')return;counts.agentFrames++;lastAgentAt=Date.parse(f.recordedAt);
 if(stageSession!==f.sessionId){emit('SessionChange',{from:stageSession,to:f.sessionId});entities.clear();stageSession=f.sessionId;}
 const next=new Map();for(const e of [...(f.mapOutputPlayers||[]),...(f.mapOutputAi||[])]){const key=e.kind.toLowerCase()+':'+e.trackId;const previous=entities.get(key);const state=e.locationAgeMs>2000?'stale':'fresh';const value={...e,endpoint:f.serverEndpoint,frameSequence:f.sequence,frameAt:f.recordedAt,state};next.set(key,value);
 if(!previous)emit('AgentAdmission',{key,entity:value,packet:proof(e,f.serverEndpoint),reason:e.provisional?'CandidateAdmissionNotIndependentProof':'AgentClassifiedPlayerOrFauna'});
 else if(previous.state!==state)emit('LocationStateChange',{key,from:previous.state,to:state,locationObservedAt:e.locationObservedAt,presenceAt:e.observedAt,packet:proof(e,f.serverEndpoint)});
 if(previous?.location&&e.location){const jump=Math.hypot(e.location.x-previous.location.x,e.location.y-previous.location.y,(e.location.z||0)-(previous.location.z||0))/100;if(jump>200)emit('SuspiciousPositionChange',{key,meters:jump,from:previous.location,to:e.location,packet:proof(e,f.serverEndpoint)});}}
 for(const [key,e]of entities)if(!next.has(key))emit('AgentOmission',{key,lastEntity:e,packet:proof(e,e.endpoint),reason:'AbsentFromAgentOutput; inspect destroy/presence and validation evidence'});entities.clear();for(const [k,v]of next)entities.set(k,v);
 for(const e of f.validationRejected||[])emit('ValidationRejected',{sequence:f.sequence,entity:e});
 for(const e of f.ongoingCandidates||[])emit('CandidateEvidence',{sequence:f.sequence,candidate:e});
}
function render(r){if(r.stage!=='render-end')return;counts.renderFrames++;lastRenderAt=Date.parse(r.ReceivedAt);const next=new Map();
 for(const m of r.RenderedMarkers||[]){const found=m.Key?.match(/pro-entity:(player|ai):(\d+)/);if(!found)continue;const key=found[1]+':'+found[2];next.set(key,m);if(!visible.has(key)){counts.appeared++;const e=entities.get(key);emit('RenderAppeared',{key,marker:m,sequence:r.ProPlayerSequence,entity:e,packet:e?proof(e,r.ProPlayerServerEndpoint):null,reason:e?'MatchingAgentIdentity; coordinate proof still separate':'AwaitingAgentEvidence'});}}
 for(const [key,m]of visible)if(!next.has(key)){counts.disappeared++;const e=entities.get(key);const life=(r.ProTrackingDiagnostics?.Lifecycle||[]).find(x=>x.TrackId===Number(key.split(':')[1])&&x.Kind===(key.startsWith('player:')?1:2));emit('RenderDisappeared',{key,previous:m,sequence:r.ProPlayerSequence,entity:e,packet:e?proof(e,r.ProPlayerServerEndpoint):null,lifecycle:life,aggregateRejections:r.ProTrackingDiagnostics?.Rejections,reason:life?.RemovalReason||(e?'StillInLatestAgentOutput; merger/projection evidence required':'AbsentFromLatestAgentOutput; correlation not proof')});}
 visible.clear();for(const [k,v]of next)visible.set(k,v);}
const started=Date.now();emit('MonitorStarted',{seconds,mode:'Incremental live capture callback + Agent/IPC/render tails; not screen or independent game truth'});
const timer=setInterval(()=>{try{tail('raw-capture/packet-events.jsonl',packet);tail('agent-live-compare.jsonl',agent);tail('host-ipc-compare.jsonl',r=>{lastHostAt=Date.now();});tail('map-diagnostics.jsonl',render);
 const now=Date.now();if(now-lastSummary>30000){lastSummary=now;const s={elapsedSeconds:Math.round((now-started)/1000),...counts,owners:owners.size,agentPlayers:[...entities.keys()].filter(k=>k.startsWith('player:')).length,renderPlayers:[...visible.keys()].filter(k=>k.startsWith('player:')).length,renderTotal:visible.size,stale:[...visible.values()].filter(m=>m.IsStale).length,agentAgeSeconds:(now-lastAgentAt)/1000,renderAgeSeconds:(now-lastRenderAt)/1000};emit('Summary',s);console.log(JSON.stringify(s));}
 if(now-started>=seconds*1000){clearInterval(timer);emit('MonitorCompleted',{...counts});fs.closeSync(eventFile);console.log('LIVE_MONITOR_COMPLETED');}
}catch(e){clearInterval(timer);emit('MonitorFailed',{message:String(e)});fs.closeSync(eventFile);console.error(e);process.exitCode=1;}},250);
