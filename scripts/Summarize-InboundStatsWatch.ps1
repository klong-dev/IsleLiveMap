param([Parameter(Mandatory)][string]$SessionPath)
$ErrorActionPreference='Stop'
$events=@(Get-Content (Join-Path $SessionPath 'stats-watch.jsonl') | ForEach-Object {ConvertFrom-Json -InputObject $_} | Where-Object Type -eq Change)
$transitions=@(); $prior=$null
foreach ($e in $events) {
 if ($prior -and (($null -eq $e.Values.Health) -ne ($null -eq $prior.Values.Health))) {
  $transitions += [pscustomobject]@{At=$e.At;Type=if($null -eq $e.Values.Health){'CurrentLost'}else{'CurrentReceived'};Owner=$e.Owner;HP=$e.Values.Health;MaxHP=$e.Values.MaxHealth;Stamina=$e.Values.Stamina;MaxStamina=$e.Values.MaxStamina}
 }; $prior=$e
}
$packetSummaries=@(); $sideTraffic=@()
foreach ($direction in @('inbound','outbound')) {
 $path=Join-Path $SessionPath ('udp-'+$direction+'.bin')
 # Deny concurrent writers: never hash/analyze a live raw file.
 $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 $reader=[IO.BinaryReader]::new($stream)
 $counts=@{}; $count=0; $first=$null; $last=$null
 try {
  if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -ne 'ISLEIN01') {throw 'Bad capture header'}
  while ($stream.Position -lt $stream.Length) {
   $offset=$stream.Position; $at=[DateTimeOffset]::new($reader.ReadInt64(),[TimeSpan]::Zero)
   $source=$reader.ReadString();$sp=$reader.ReadUInt16();$destination=$reader.ReadString();$dp=$reader.ReadUInt16();$length=$reader.ReadInt32()
   if ($length -le 0 -or $length -gt 65535 -or $length -gt $stream.Length-$stream.Position) {throw 'Invalid/truncated raw record'}
   $null=$stream.Seek($length,[IO.SeekOrigin]::Current);$count++;if($null -eq $first){$first=$at};$last=$at
   $endpoint=if($direction -eq 'outbound'){"${destination}:$dp"}else{"${source}:$sp"}
   if(-not $counts.ContainsKey($endpoint)){$counts[$endpoint]=0};$counts[$endpoint]++
   if($direction -eq 'outbound' -and $endpoint -ne '173.225.107.226:7777'){
    $sideTraffic += [pscustomobject]@{At=$at;Destination=$endpoint;SourcePort=$sp;RawOffset=$offset;Bytes=$length}
   }
  }
 } finally {$reader.Dispose();$stream.Dispose()}
 $packetSummaries += [pscustomobject]@{Direction=$direction;Packets=$count;First=$first;Last=$last;Endpoints=$counts;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
$correlations=@(foreach($t in $transitions | Where-Object Type -eq CurrentLost){
 $at=[DateTimeOffset]$t.At
 $near=@($sideTraffic | Where-Object {[Math]::Abs(($_.At-$at).TotalSeconds) -lt 4})
 [pscustomobject]@{Loss=$t;NearbySideTraffic=$near}
})
$result=[ordered]@{Watch=(Get-Content (Join-Path $SessionPath 'watch-summary.json') -Raw | ConvertFrom-Json);Transitions=$transitions;Correlations=$correlations;Packets=$packetSummaries;Limitations=@('UI samples sequential, not atomic','Watcher derived percentages rounded; raw current/max authoritative','Loss correlation supports endpoint-reset hypothesis; active endpoint transitions were not instrumented')}
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $SessionPath 'analysis.json') -Encoding utf8
$result | ConvertTo-Json -Depth 9
