[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SessionPath,

    [string[]] $ActorName = @('A Penguin', 'hai985235'),

    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$session = [IO.Path]::GetFullPath($SessionPath)
if (-not (Test-Path -LiteralPath $session -PathType Container)) {
    throw "Session path does not exist: $session"
}

function Read-JsonLines([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadLines($path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $rows.Add(($line | ConvertFrom-Json -Depth 32)) } catch { }
    }
    return $rows.ToArray()
}

function Get-Value($object, [string[]] $names) {
    if ($null -eq $object) { return $null }
    foreach ($name in $names) {
        $property = $object.PSObject.Properties[$name]
        if ($null -ne $property) { return $property.Value }
    }
    return $null
}

function Get-ActorName($entity) {
    return [string](Get-Value $entity @('ingameName', 'IngameName', 'playerProofName', 'PlayerProofName', 'name', 'Name'))
}

function Get-TrackId($entity) {
    $value = Get-Value $entity @('trackId', 'TrackId', 'actorNetRefHandle', 'ActorNetRefHandle')
    if ($null -eq $value) { return $null }
    return [long]$value
}

function Get-NameEvidence($entity) {
    [ordered]@{
        ExactName = Get-ActorName $entity
        TrackId = Get-TrackId $entity
        ActorNetRefHandle = Get-Value $entity @('actorNetRefHandle', 'ActorNetRefHandle')
        PlayerStateNetRefHandle = Get-Value $entity @('playerStateNetRefHandle', 'PlayerStateNetRefHandle')
        PawnNetRefHandle = Get-Value $entity @('pawnNetRefHandle', 'PawnNetRefHandle')
        SpeciesId = Get-Value $entity @('speciesId', 'SpeciesId')
        SpeciesShortName = Get-Value $entity @('speciesShortName', 'SpeciesShortName')
        LocationObservedAt = Get-Value $entity @('locationObservedAt', 'LocationObservedAt')
        ObservedAt = Get-Value $entity @('observedAt', 'ObservedAt', 'frameObservedAt', 'FrameObservedAt')
        IsProvisional = Get-Value $entity @('isProvisional', 'IsProvisional')
    }
}

function Add-Hit($bucket, $stage, $row, $entity) {
    $bucket[$stage]++
    $track = Get-TrackId $entity
    if ($null -ne $track) { [void]$bucket.TrackIds.Add([string]$track) }
    if ($row.ServerEndpoint) { [void]$bucket.Endpoints.Add([string]$row.ServerEndpoint) }
    $observed = Get-Value $entity @('observedAt', 'ObservedAt', 'frameObservedAt', 'FrameObservedAt')
    if ($observed) { $bucket.LastObservedAt = [string]$observed }
}

$agentPath = Join-Path $session 'agent-live-compare.jsonl'
if (-not (Test-Path -LiteralPath $agentPath)) { $agentPath = Join-Path $session 'raw-capture\agent-live-compare.jsonl' }
$mapPath = Join-Path $session 'map-diagnostics.jsonl'
if (-not (Test-Path -LiteralPath $mapPath)) { $mapPath = Join-Path $session 'raw-capture\map-diagnostics.jsonl' }
$agentRows = @(Read-JsonLines $agentPath)
$mapRows = @(Read-JsonLines $mapPath)

$reports = [ordered]@{}
foreach ($name in $ActorName) {
    $reports[$name] = [ordered]@{
        ExactName = $name
        AgentFrames = 0
        IdentityCandidateFrames = 0
        InboundFrames = 0
        RemoteEntityFrames = 0
        MapOutputFrames = 0
        RenderDiagnosticFrames = 0
        NoFrameRows = 0
        TrackIds = [Collections.Generic.HashSet[string]]::new()
        Endpoints = [Collections.Generic.HashSet[string]]::new()
        LastObservedAt = $null
        RejectionReasons = [Collections.Generic.HashSet[string]]::new()
        Evidence = [Collections.Generic.List[object]]::new()
    }
}

foreach ($row in $agentRows) {
    foreach ($name in $ActorName) {
        $bucket = $reports[$name]
        $matched = $false
        foreach ($stage in @(
            @{ Name = 'identityCandidates'; Values = @($row.identityCandidates) },
            @{ Name = 'identityScanCandidates'; Values = @($row.identityScanCandidates) },
            @{ Name = 'inbound'; Values = @($row.inboundPlayers) + @($row.inboundAi) },
            @{ Name = 'remoteEntities'; Values = @($row.RemoteEntities) },
            @{ Name = 'mapOutput'; Values = @($row.mapOutputPlayers) + @($row.mapOutputAi)
        })) {
            foreach ($entity in $stage.Values) {
                if ($null -eq $entity) { continue }
                if ((Get-ActorName $entity) -ne $name) { continue }
                $matched = $true
                [void]$bucket.Evidence.Add([ordered]@{
                    Stage = $stage.Name
                    ServerEndpoint = [string]$row.ServerEndpoint
                    FrameSequence = Get-Value $row @('Sequence', 'sequence')
                    Data = Get-NameEvidence $entity
                })
                switch ($stage.Name) {
                    'identityCandidates' { Add-Hit $bucket 'IdentityCandidateFrames' $row $entity }
                    'identityScanCandidates' { Add-Hit $bucket 'IdentityCandidateFrames' $row $entity }
                    'inbound' { Add-Hit $bucket 'InboundFrames' $row $entity }
                    'remoteEntities' { Add-Hit $bucket 'RemoteEntityFrames' $row $entity }
                    'mapOutput' { Add-Hit $bucket 'MapOutputFrames' $row $entity }
                }
            }
        }
        if ($matched) { $bucket.AgentFrames++ }
    }
}

foreach ($row in $mapRows) {
    $noFrame = [string]$row.ProTrackingDiagnostics.FrameState -eq 'no-frame'
    foreach ($name in $ActorName) {
        $bucket = $reports[$name]
        if ($noFrame) { $bucket.NoFrameRows++ }
        foreach ($marker in @($row.RenderedMarkers)) {
            $key = [string](Get-Value $marker @('Key', 'key'))
            foreach ($track in $bucket.TrackIds) {
                if ($key -match "pro-entity:[^:]+:$([regex]::Escape($track))(#|$)") {
                    $bucket.RenderDiagnosticFrames++
                }
            }
        }
        foreach ($rejection in @($row.ProTrackingDiagnostics.Rejections.PSObject.Properties)) {
            if ([int]$rejection.Value -gt 0) { [void]$bucket.RejectionReasons.Add([string]$rejection.Name) }
        }
    }
}

$result = [ordered]@{
    GeneratedAt = [DateTimeOffset]::UtcNow
    SessionPath = $session
    AgentPath = $agentPath
    MapPath = $mapPath
    AgentRows = $agentRows.Count
    MapRows = $mapRows.Count
    Actors = @($reports.Values | ForEach-Object {
        [ordered]@{
            ExactName = $_.ExactName
            AgentFrames = $_.AgentFrames
            IdentityCandidateFrames = $_.IdentityCandidateFrames
            InboundFrames = $_.InboundFrames
            RemoteEntityFrames = $_.RemoteEntityFrames
            MapOutputFrames = $_.MapOutputFrames
            RenderDiagnosticFrames = $_.RenderDiagnosticFrames
            NoFrameRows = $_.NoFrameRows
            TrackIds = @($_.TrackIds)
            Endpoints = @($_.Endpoints)
            LastObservedAt = $_.LastObservedAt
            # ProTrackingDiagnostics.Rejections is frame-level aggregate data,
            # not an actor-keyed rejection map. Never attribute those reasons
            # to an exact actor that was not observed in the capture.
            RejectionReasons = if ($_.AgentFrames -gt 0) { @($_.RejectionReasons) } else { @() }
            Evidence = @($_.Evidence | Select-Object -First 20)
            Status = if ($_.AgentFrames -eq 0) { 'NOT_OBSERVED' } elseif ($_.RenderDiagnosticFrames -gt 0 -or $_.MapOutputFrames -gt 0) { 'RENDERED_OR_OUTPUT' } else { 'OBSERVED_BEFORE_RENDER' }
        }
    })
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $session 'reference-actors-analysis.json' }
$result | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json -Depth 16
