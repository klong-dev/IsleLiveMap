import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const root=path.resolve(process.argv[2]);
const out=path.join(root,'live-audit-summary.json');
if(fs.existsSync(out))throw Error('Refusing to overwrite report');
const read=p=>JSON.parse(fs.readFileSync(path.join(root,p),'utf8').replace(/^\uFEFF/,''));
const manifest=read('session-manifest.json');
if(!manifest.CompletedAt)throw Error('Capture writers have not been finalized');
const summary=JSON.parse(execFileSync(process.execPath,[path.join(path.dirname(fileURLToPath(import.meta.url)),'Summarize-TrackingLive.mjs'),root],{encoding:'utf8',maxBuffer:16*1024*1024}));
const hashes={};for(const file of ['raw-capture/udp-inbound.bin','raw-capture/udp-outbound.bin','raw-capture/udp-capture.bin','raw-capture/packet-events.jsonl','live-monitor-events.jsonl']){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(path.join(root,file)))h.update(b);hashes[file]=h.digest('hex');}
// Describe this session, not the current default: old captures may be longer.
const tracePolicy={requestedDurationMinutes:manifest.DurationMinutes??null,
 fiveMinuteWindowRequested:manifest.DurationMinutes===5,
 acceptancePassed:false};
const final={status:'NEED_STAGE_EVIDENCE',session:manifest.SessionId,tracePolicy,binaries:read('binary-manifest.json'),hashes,...summary,limitations:[
 'Agent diagnostics are sampled at approximately 500 ms; renderer telemetry is not a visual game observation.',
 'Packet-event and Agent streams share a parser implementation; destroy decoding is not independently validated.',
 'Latest-event correlation does not establish the exact causal packet for a position.',
 'Provisional actors and aggregate marker counts are not independent dinosaur ground truth.',
 'A jump warning across an observation gap is not by itself proof of a teleport; replay is required.',
 'Unknown removals and candidates remain unknown, not automatically non-dinosaurs.',
 'Live run uses a dirty checkout; binary hashes identify the build, not the version commit suffix.'
]};
fs.writeFileSync(out,JSON.stringify(final,null,2),{flag:'wx'});
console.log(out);
