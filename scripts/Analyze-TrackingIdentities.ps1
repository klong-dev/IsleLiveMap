[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SessionPath,

    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$session = [IO.Path]::GetFullPath($SessionPath)
if (-not (Test-Path -LiteralPath $session -PathType Container)) {
    throw "Session path does not exist: $session"
}

function Read-JsonLines([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $items = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadLines($path)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $items.Add(($line | ConvertFrom-Json -Depth 32)) } catch { }
    }
    return $items.ToArray()
}

function Value($object, [string[]] $names) {
    if ($null -eq $object) { return $null }
    foreach ($name in $names) {
        $property = $object.PSObject.Properties[$name]
        if ($null -ne $property) { return $property.Value }
    }
    return $null
}

function Text($object, [string[]] $names) {
    $value = Value $object $names
    if ($null -eq $value) { return '' }
    return ([string]$value).Trim()
}

function TrackId($entity) {
    $value = Value $entity @('trackId', 'TrackId', 'actorNetRefHandle', 'ActorNetRefHandle')
    if ($null -eq $value) { return $null }
    try { return [long]$value } catch { return $null }
}

function IdentityKey($entity) {
    $track = TrackId $entity
    if ($null -eq $track -or $track -le 0) { return $null }
    $kind = Text $entity @('entityKind', 'EntityKind', 'kind', 'Kind')
    if ([string]::IsNullOrWhiteSpace($kind)) { $kind = 'unknown' }
    return "$kind`:$track"
}

function AddSet($dictionary, [string] $key, [string] $value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return }
    if (-not $dictionary.ContainsKey($key)) {
        $dictionary[$key] = [Collections.Generic.HashSet[string]]::new()
    }
    [void]$dictionary[$key].Add($value)
}

function CountValue($dictionary, [string] $key) {
    if ($dictionary.ContainsKey($key)) { return [int]$dictionary[$key] }
    return 0
}

$agentPath = Join-Path $session 'agent-live-compare.jsonl'
if (-not (Test-Path -LiteralPath $agentPath)) {
    $agentPath = Join-Path $session 'raw-capture\agent-live-compare.jsonl'
}
$mapPath = Join-Path $session 'map-diagnostics.jsonl'
if (-not (Test-Path -LiteralPath $mapPath)) {
    $mapPath = Join-Path $session 'raw-capture\map-diagnostics.jsonl'
}
$agentRows = @(Read-JsonLines $agentPath)
$mapRows = @(Read-JsonLines $mapPath)
$actors = @{}

$stages = @(
    @{ Name = 'identityCandidates'; Property = 'identityCandidates' },
    @{ Name = 'identityScanCandidates'; Property = 'identityScanCandidates' },
    @{ Name = 'inboundPlayers'; Property = 'inboundPlayers' },
    @{ Name = 'inboundAi'; Property = 'inboundAi' },
    @{ Name = 'remoteEntities'; Property = 'RemoteEntities' },
    @{ Name = 'mapOutputPlayers'; Property = 'mapOutputPlayers' },
    @{ Name = 'mapOutputAi'; Property = 'mapOutputAi' }
)

foreach ($row in $agentRows) {
    foreach ($stage in $stages) {
        $entities = @($row.($stage.Property))
        foreach ($entity in $entities) {
            if ($null -eq $entity) { continue }
            $key = IdentityKey $entity
            if ($null -eq $key) { continue }
            if (-not $actors.ContainsKey($key)) {
                $actors[$key] = [ordered]@{
                    IdentityKey = $key
                    TrackId = TrackId $entity
                    ActorNetRefHandle = Text $entity @('actorNetRefHandle', 'ActorNetRefHandle')
                    PlayerStateNetRefHandle = Text $entity @('playerStateNetRefHandle', 'PlayerStateNetRefHandle')
                    PawnNetRefHandle = Text $entity @('pawnNetRefHandle', 'PawnNetRefHandle')
                    Names = @{}
                    Species = @{}
                    Stages = @{}
                    FirstObservedAt = [string](Value $row @('ObservedAt', 'observedAt'))
                    LastObservedAt = [string](Value $row @('ObservedAt', 'observedAt'))
                    LastLocationObservedAt = ''
                    ServerEndpoints = @{}
                    Evidence = [Collections.Generic.List[object]]::new()
                }
            }
            $actor = $actors[$key]
            $actor.Stages[$stage.Name] = 1 + (CountValue $actor.Stages $stage.Name)
            $actor.LastObservedAt = [string](Value $row @('ObservedAt', 'observedAt'))
            $name = Text $entity @('ingameName', 'IngameName', 'playerProofName', 'PlayerProofName', 'name', 'Name')
            $speciesId = Text $entity @('speciesId', 'SpeciesId')
            $speciesName = Text $entity @('speciesShortName', 'SpeciesShortName')
            AddSet $actor.Names 'value' $name
            AddSet $actor.Species 'id' $speciesId
            AddSet $actor.Species 'name' $speciesName
            $endpoint = Text $row @('ServerEndpoint', 'serverEndpoint')
            AddSet $actor.ServerEndpoints 'value' $endpoint
            $locationAt = Text $entity @('locationObservedAt', 'LocationObservedAt')
            if ($locationAt) { $actor.LastLocationObservedAt = $locationAt }
            if ($actor.Evidence.Count -lt 12) {
                $actor.Evidence.Add([ordered]@{
                    Stage = $stage.Name
                    Sequence = Value $row @('Sequence', 'sequence')
                    ObservedAt = Value $row @('ObservedAt', 'observedAt')
                    Name = $name
                    SpeciesId = $speciesId
                    SpeciesShortName = $speciesName
                    LocationObservedAt = $locationAt
                    IsProvisional = Value $entity @('isProvisional', 'IsProvisional')
                })
            }
        }
    }
}

$renderedTracks = @{}
$noFrameRows = 0
$frameRows = 0
$rejections = @{}
foreach ($row in $mapRows) {
    $diagnostics = $row.ProTrackingDiagnostics
    if ([string](Value $diagnostics @('FrameState', 'frameState')) -eq 'no-frame') {
        $noFrameRows++
    } else {
        $frameRows++
    }
    foreach ($marker in @($row.RenderedMarkers)) {
        $key = Text $marker @('Key', 'key')
        if ($key -match 'pro-entity:[^:]+:(\d+)(?:#|$)') {
            $renderedTracks[$matches[1]] = 1 + (CountValue $renderedTracks $matches[1])
        }
    }
    foreach ($property in @($diagnostics.Rejections.PSObject.Properties)) {
        if ([int]$property.Value -gt 0) {
            $rejections[$property.Name] = [int]$property.Value
        }
    }
}

$actorReports = @($actors.Values | ForEach-Object {
    $track = [string]$_.TrackId
    $stageNames = @($_.Stages.Keys)
    $renderCount = CountValue $renderedTracks $track
    [ordered]@{
        IdentityKey = $_.IdentityKey
        TrackId = $_.TrackId
        ActorNetRefHandle = $_.ActorNetRefHandle
        PlayerStateNetRefHandle = $_.PlayerStateNetRefHandle
        PawnNetRefHandle = $_.PawnNetRefHandle
        Names = @($_.Names.Values | ForEach-Object { $_.Keys })
        SpeciesIds = @($_.Species['id'].Keys)
        SpeciesNames = @($_.Species['name'].Keys)
        Stages = $stageNames
        RenderedMarkerCount = $renderCount
        FirstObservedAt = $_.FirstObservedAt
        LastObservedAt = $_.LastObservedAt
        LastLocationObservedAt = $_.LastLocationObservedAt
        ServerEndpoints = @($_.ServerEndpoints.Values | ForEach-Object { $_.Keys })
        Status = if ($renderCount -gt 0) { 'RENDERED' } elseif ($stageNames -contains 'remoteEntities' -or $stageNames -contains 'mapOutputPlayers' -or $stageNames -contains 'mapOutputAi') { 'OUTPUT_NOT_RENDERED' } elseif ($stageNames.Count -gt 0) { 'OBSERVED_BEFORE_OUTPUT' } else { 'IDENTITY_ONLY' }
        Evidence = @($_.Evidence)
    }
})

$result = [ordered]@{
    GeneratedAt = [DateTimeOffset]::UtcNow
    SessionPath = $session
    AgentPath = $agentPath
    MapPath = $mapPath
    AgentRows = $agentRows.Count
    MapRows = $mapRows.Count
    FrameRows = $frameRows
    NoFrameRows = $noFrameRows
    FrameRejections = $rejections
    ActorCount = $actorReports.Count
    Actors = $actorReports
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $session 'tracking-identities-analysis.json'
}
$result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json -Depth 20
