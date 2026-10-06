import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
const root=path.resolve(process.argv[2]);
const counters={}, tracks=new Map(), candidates=new Map(), jumps=[];let latestSummary;
for await(const line of readline.createInterface({input:fs.createReadStream(path.join(root,'live-monitor-events.jsonl')),crlfDelay:Infinity})){
 let e;try{e=JSON.parse(line);}catch{continue;}
 counters[e.event]=(counters[e.event]||0)+1;if(e.event==='Summary')latestSummary=e;
 if(e.event==='CandidateEvidence'){const c=e.candidate;const k=String(c.actorHandle);if(!candidates.has(k))candidates.set(k,{handle:k,decisions:{},species:c.species});const v=candidates.get(k);v.decisions[c.decision]=(v.decisions[c.decision]||0)+1;}
 if(!e.key?.startsWith('player:'))continue;
 if(!tracks.has(e.key))tracks.set(e.key,{key:e.key,first:e.observedByMonitorAt,last:e.observedByMonitorAt,admissions:0,omissions:0,appearances:0,disappearances:0,matchingDestroyOmissions:0,renderWithoutAgentProof:0,staleTransitions:0,freshTransitions:0,reasons:{},segments:[],open:null});
 const t=tracks.get(e.key);t.last=e.observedByMonitorAt;
 if(e.event==='AgentAdmission'){t.admissions++;t.species=e.entity.species;t.lastAdmissionLocationAge=e.entity.locationAgeMs;t.lastPacket=e.packet;}
 if(e.event==='AgentOmission'){t.omissions++;t.lastOmission=e;const age=Date.parse(e.observedByMonitorAt)-Date.parse(e.packet?.destroyedAt);if(age>=0&&age<2000)t.matchingDestroyOmissions++;}
 if(e.event==='LocationStateChange'){if(e.to==='stale')t.staleTransitions++;else t.freshTransitions++;}
 if(e.event==='RenderAppeared'){t.appearances++;if(!e.entity)t.renderWithoutAgentProof++;t.open=e.observedByMonitorAt;}
 if(e.event==='RenderDisappeared'){t.disappearances++;t.reasons[e.reason]=(t.reasons[e.reason]||0)+1;if(t.open)t.segments.push({start:t.open,end:e.observedByMonitorAt,seconds:(Date.parse(e.observedByMonitorAt)-Date.parse(t.open))/1000,reason:e.reason});t.open=null;t.lastDisappearance=e;}
 if(e.event==='SuspiciousPositionChange')jumps.push(e);
}
console.log(JSON.stringify({scope:'Live sampled correlation, not an independent verdict on dinosaur existence',latestSummary,counters,tracks:[...tracks.values()].map(({lastOmission,lastDisappearance,...t})=>({...t,lastOmission:lastOmission?{at:lastOmission.observedByMonitorAt,presence:lastOmission.lastEntity?.observedAt,locationAt:lastOmission.lastEntity?.locationObservedAt,packet:lastOmission.packet}:null,lastDisappearance:lastDisappearance?{at:lastDisappearance.observedByMonitorAt,reason:lastDisappearance.reason,entityStillInAgent:!!lastDisappearance.entity,locationAgeMs:lastDisappearance.entity?.locationAgeMs}:null})),candidates:[...candidates.values()],playerPositionWarnings:jumps},null,2));
