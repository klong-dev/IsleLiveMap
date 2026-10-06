import fs from 'node:fs';
import readline from 'node:readline';
const root=process.argv[2];let frames=0, first,last;const p=new Map(),signals=new Map(),sources={},protocols={};
for await(const line of readline.createInterface({input:fs.createReadStream(root+'/raw-capture/agent-live-compare.jsonl'),crlfDelay:Infinity})){
 const r=JSON.parse(line);if(r.stage!=='agent-post-fusion')continue;frames++;first??=r.recordedAt;last=r.recordedAt;
 for(const e of r.mapOutputPlayers??[]){let t=p.get(e.trackId)??{id:e.trackId,provisional:0,verified:0,first:r.recordedAt,last:r.recordedAt,sources:{},positions:new Set()};t[e.provisional?'provisional':'verified']++;t.last=r.recordedAt;t.sources[e.locationEvidenceSource??'none']=(t.sources[e.locationEvidenceSource??'none']??0)+1;t.positions.add(JSON.stringify(e.location));p.set(e.trackId,t);}
 for(const s of r.creatureSignals??[]){const k=s.ownerHandle??s.OwnerHandle;signals.set(k,s);}
}
for await(const line of readline.createInterface({input:fs.createReadStream(root+'/raw-capture/packet-events.jsonl'),crlfDelay:Infinity})) {const r=JSON.parse(line);for(const b of r.batches??[]){if(b.initial){const k=String(b.initial.ProtocolId??b.initial.protocolId??'unknown');protocols[k]=(protocols[k]??0)+1;}}}
console.log(JSON.stringify({frames,first,last,players:[...p.values()].map(t=>({...t,positions:t.positions.size})),signalOwners:signals.size,signalSample:[...signals.values()].slice(0,5),protocols},null,2));

