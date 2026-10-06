import fs from "node:fs";
const [beforePath,afterPath,outputPath]=process.argv.slice(2);
const read=p=>JSON.parse(fs.readFileSync(p,"utf8").replace(/^\uFEFF/,""));
const before=read(beforePath),after=read(afterPath);
if(before.CaptureSha256!==after.CaptureSha256)throw Error("Capture mismatch");
const oldRows=new Map(before.FocusRows.map(r=>[r.PacketNumber,r]));
const eq=(a,b)=>a&&b&&a.X===b.X&&a.Y===b.Y&&a.Z===b.Z;
const distance=(a,b)=>Math.hypot(a.X-b.X,a.Y-b.Y,(a.Z||0)-(b.Z||0))/100;
const rows=after.FocusRows.flatMap(r=>{
 const c=r.Batches.find(b=>b.NetRefHandle===after.ActorHandle&&b.Creation)?.Creation;
 if(!c)return [];const old=oldRows.get(r.PacketNumber);
 return [{packetNumber:r.PacketNumber,packetSequence:r.PacketSequence,at:r.At,
 serialized:c.SpawnLocation,before:old?.AfterDirect?.Location,after:r.AfterDirect?.Location,
 matchesBefore:eq(old?.AfterDirect?.Location,c.SpawnLocation),matchesAfter:eq(r.AfterDirect?.Location,c.SpawnLocation),
 beforeErrorMeters:old?.AfterDirect?distance(old.AfterDirect.Location,c.SpawnLocation):null,
 afterTimestampMatches:r.AfterDirect?.LocationObservedAt===r.At,
 sameIdentity:old?.AfterDirect?.TrackId===r.AfterDirect?.TrackId&&old?.AfterDirect?.PlayerStateNetRefHandle===r.AfterDirect?.PlayerStateNetRefHandle&&old?.AfterDirect?.PawnNetRefHandle===r.AfterDirect?.PawnNetRefHandle}];
});
const passed=rows.length>1&&rows.some(r=>!r.matchesBefore)&&rows.every(r=>r.matchesAfter&&r.afterTimestampMatches&&r.sameIdentity);
const result={status:passed?"PASS":"FAIL",scope:"Serialized re-entry position regression, not general update decoder or live ground truth",captureSha256:after.CaptureSha256,actor:after.ActorHandle,packetCount:after.PacketCount,sameDiscoveredPlayerSet:JSON.stringify(before.Players)===JSON.stringify(after.Players),rows};
fs.writeFileSync(outputPath,JSON.stringify(result,null,2),{flag:"wx"});console.log(JSON.stringify(result,null,2));if(!passed)process.exitCode=1;
