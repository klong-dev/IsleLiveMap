[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SessionPath,
    [Parameter(Mandatory)][string]$InspectionPath
)
$ErrorActionPreference = 'Stop'
$sessionDirectory = (Resolve-Path -LiteralPath $SessionPath).Path
$reportPath = Join-Path $sessionDirectory 'creature-signal-analysis.json'
if (Test-Path -LiteralPath $reportPath) { throw 'Refusing to overwrite an existing analysis.' }

function Read-CaptureSummary([string]$Path) {
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -ne 'ISLEIN01') { throw 'Invalid raw capture header.' }
        $count = 0L; $bytes = 0L; $first = $null; $last = $null
        $flows = @{}
        while ($stream.Position -lt $stream.Length) {
            $at = [DateTimeOffset]::new($reader.ReadInt64(), [TimeSpan]::Zero)
            $source = $reader.ReadString(); $sourcePort = $reader.ReadUInt16()
            $destination = $reader.ReadString(); $destinationPort = $reader.ReadUInt16()
            $size = $reader.ReadInt32()
            if ($size -le 0 -or $size -gt 65535 -or $size -gt $stream.Length - $stream.Position) {
                throw "Truncated/invalid raw record at $($stream.Position) in $Path"
            }
            $null = $stream.Seek($size, [IO.SeekOrigin]::Current)
            if ($null -eq $first -or $at -lt $first) { $first = $at }
            if ($null -eq $last -or $at -gt $last) { $last = $at }
            $count++; $bytes += $size
            $key = "${source}:$sourcePort -> ${destination}:$destinationPort"
            $flows[$key] = [long]$flows[$key] + 1
        }
        $stream.Position = 0
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [Convert]::ToHexString($sha.ComputeHash($stream)) } finally { $sha.Dispose() }
        [pscustomobject]@{ File = [IO.Path]::GetFileName($Path); Sha256 = $hash
            Records = $count; PayloadBytes = $bytes; FirstPacketAt = $first; LastPacketAt = $last
            PacketSpanSeconds = if ($null -ne $first) { ($last - $first).TotalSeconds } else { 0 }
            Flows = $flows }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

$captures = @(Get-ChildItem -LiteralPath (Join-Path $sessionDirectory 'raw-capture') -Filter '*.bin' -File |
    Sort-Object Name | ForEach-Object { Read-CaptureSummary $_.FullName })
$inspection = @(Get-Content -LiteralPath $InspectionPath -Raw | ConvertFrom-Json)[0]
$creations = @{}
foreach ($creation in $inspection.ActorCreations) { $creations[[string]$creation.ActorHandle] = $creation }
$signals = @($inspection.CreatureSignalHistory)
$owners = @($signals | Group-Object OwnerHandle | ForEach-Object {
    $owner = $_.Name; $rows = @($_.Group)
    [pscustomobject]@{ OwnerHandle = $owner
        SpeciesHints = @($rows.SpeciesId | Sort-Object -Unique)
        SignalKinds = @($rows.Kind | Sort-Object -Unique)
        DistinctAssets = @($rows.AssetPath | Sort-Object -Unique).Count
        References = ($rows.ReferenceObservations | Measure-Object -Sum).Sum
        CreationInWindow = $creations.ContainsKey($owner)
        FirstSignalAt = ($rows.FirstSeenAt | Sort-Object | Select-Object -First 1)
        LastSignalAt = ($rows.LastSeenAt | Sort-Object | Select-Object -Last 1)
        EvidenceOnly = $true }
})
$structural = @($inspection.StructuralReferencePairs | ForEach-Object {
    [pscustomobject]@{ OwnerHandle = $_.OwnerHandle; PlayerStateHandle = $_.PlayerStateHandle
        PawnHandle = $_.PawnHandle; Observations = $_.HitCount
        CreationInWindow = $creations.ContainsKey([string]$_.OwnerHandle)
        CandidateOnly = $true }
})
$knownProtocols = @{ '1325431006' = 'tyrannosaurus'; '2090433249' = 'triceratops' }
$dinoCreations = @($inspection.ActorCreations | Where-Object { $knownProtocols.ContainsKey([string]$_.ProtocolId) } |
    ForEach-Object { $creation = $_
        [pscustomobject]@{ ActorHandle = $creation.ActorHandle; ProtocolId = $creation.ProtocolId
            SpeciesHint = $knownProtocols[[string]$creation.ProtocolId]; ObservedAt = $creation.ObservedAt
            StructuralCandidates = @($structural | Where-Object { $_.OwnerHandle -eq $creation.ActorHandle })
            BatchEventsInWindow = @($inspection.BatchEvents | Where-Object { $_.NetRefHandle -eq $creation.ActorHandle }).Count }
    })
$result = [pscustomobject]@{
    SchemaVersion = 1; GeneratedAt = [DateTimeOffset]::UtcNow
    Status = 'NEED_STAGE_EVIDENCE'; Scope = 'Packet-only mid-session analysis; not an independent player-count or position oracle.'
    CaptureFiles = $captures
    InspectorInputSha256 = (Get-FileHash -LiteralPath $InspectionPath -Algorithm SHA256).Hash
    ParsedIrisDataStreamRecords = $inspection.ParsedPacketCount
    TotalActorCreationRecords = $inspection.ActorCreationCount
    DiscoveredIdentityRecords = $inspection.DiscoveredPlayerIdentityCount
    SpeciesSignalOwners = $owners; Signals = $signals
    StructuralCandidates = $structural; KnownDinoCreationHints = $dinoCreations
    SignalKindLegend = @('Asset', 'Animation', 'Sound', 'Effect', 'Status', 'MovementAsset')
    Limitations = @('No same-window Agent/IPC/render evidence.',
        'Unknown object handles stay unresolved; absence of an exported path is not absence of that species.',
        'An asset reference does not prove the action was performed, player control, or position.',
        'Outbound payload is preserved, not yet validated as an action/GPS decoder.',
        'Protocol species mapping is an existing hypothesis, not a new independently verified mapping.')
}
$output = [IO.File]::Open($reportPath, 'CreateNew', 'Write', 'None')
try {
    $writer = [IO.StreamWriter]::new($output, [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($result | ConvertTo-Json -Depth 12)) } finally { $writer.Dispose() }
} finally { $output.Dispose() }
Write-Output $reportPath
