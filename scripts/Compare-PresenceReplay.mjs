import fs from "node:fs";
const [beforePath,afterPath,outputPath]=process.argv.slice(2);
const read=p=>JSON.parse(fs.readFileSync(p,"utf8").replace(/^\uFEFF/,""));
const before=read(beforePath),after=read(afterPath);
if(before.CaptureSha256!==after.CaptureSha256)throw Error("Capture SHA mismatch");
const index=new Map(after.FocusRows.map(r=>[r.PacketNumber,r]));
const changes=[];let coordinateChanges=0,locationTimestampChanges=0;
for(const old of before.FocusRows){
 const next=index.get(old.PacketNumber);
 if(!next)throw Error("Missing replay row "+old.PacketNumber);
 if(JSON.stringify(old.AfterRecovery?.Location)!==JSON.stringify(next.AfterRecovery?.Location))coordinateChanges++;
 if(old.AfterRecovery?.LocationObservedAt!==next.AfterRecovery?.LocationObservedAt)locationTimestampChanges++;
 if(old.AfterDirect?.ObservedAt!==next.AfterDirect?.ObservedAt){
  changes.push({packetNumber:old.PacketNumber,packetSequence:old.PacketSequence,at:old.At,
   before:old.AfterDirect?.ObservedAt,after:next.AfterDirect?.ObservedAt,
   nonOwnerBatch:next.Batches.some(b=>b.NetRefHandle===after.ActorHandle&&!b.HasOwnerData),
   beforeAgeSeconds:(Date.parse(old.At)-Date.parse(old.AfterDirect?.ObservedAt))/1000,
   afterAgeSeconds:(Date.parse(next.At)-Date.parse(next.AfterDirect?.ObservedAt))/1000});
 }
}
const pass=changes.length>0&&coordinateChanges===0&&locationTimestampChanges===0
 &&changes.every(c=>c.nonOwnerBatch&&c.afterAgeSeconds===0);
const result={status:pass?"PASS":"FAIL",scope:"Presence-only replay regression, not full live acceptance",
 actor:after.ActorHandle,captureSha256:after.CaptureSha256,packets:after.PacketCount,
 changedPresenceRows:changes.length,coordinateChanges,locationTimestampChanges,
 beforeMaxChangedRowPresenceAgeSeconds:Math.max(...changes.map(c=>c.beforeAgeSeconds)),changes};
fs.writeFileSync(outputPath,JSON.stringify(result,null,2),{flag:"wx"});
console.log(JSON.stringify({...result,changes:changes.slice(0,2)},null,2));
if(!pass)process.exitCode=1;
