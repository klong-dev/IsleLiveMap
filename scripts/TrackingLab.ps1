[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start-session', 'capture', 'run-round', 'replay', 'analyze', 'report', 'fix-loop')]
    [string]$Command = 'start-session',

    [string]$SessionRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\tracking-lab'),
    [string]$SessionId,
    [int]$DurationMinutes = 5,
    [switch]$KeepPassedRaw,
    [switch]$LaunchInstalledApp,
    [switch]$RestartLauncher,
    [string]$LauncherPath
)

$ErrorActionPreference = 'Stop'

function Write-JsonFile([string]$Path, $Value) {
    $Value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Resolve-Session {
    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $latest = Get-ChildItem -LiteralPath $SessionRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $latest) { throw "Không tìm thấy session. Hãy chạy start-session trước." }
        return $latest.FullName
    }
    $path = Join-Path $SessionRoot $SessionId
    if (-not (Test-Path -LiteralPath $path)) { throw "Không tìm thấy session: $SessionId" }
    return $path
}

function Test-Preflight {
    $game = Get-Process -Name @(
        'TheIsle-Win64-Shipping',
        'TheIsleClient-Win64-Shipping'
    ) -ErrorAction SilentlyContinue | Select-Object -First 1
    $agent = Get-Process -Name 'IsleLiveMap.Pro.Agent' -ErrorAction SilentlyContinue
    $npcapDll = @(
        (Join-Path $env:SystemRoot 'System32\wpcap.dll'),
        (Join-Path $env:SystemRoot 'SysWOW64\wpcap.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $appRoot = Join-Path $env:LOCALAPPDATA 'KLongDev\IsleLiveMap'
    $islePilotCredential = Join-Path $appRoot 'islepilot-overlay.credential'
    $proCredential = Join-Path $appRoot 'pro-access.credential'
    $proRoot = Join-Path $appRoot 'Pro'
    $descriptorPath = Join-Path $proRoot 'current.json'
    $agentVersion = $null
    if (Test-Path -LiteralPath $descriptorPath) {
        try { $agentVersion = (Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json).version } catch { }
    }
    $agentExecutable = if ([string]::IsNullOrWhiteSpace([string]$agentVersion)) {
        Join-Path $proRoot 'current\IsleLiveMap.Pro.Agent.exe'
    } else {
        Join-Path $proRoot "versions\$agentVersion\IsleLiveMap.Pro.Agent.exe"
    }
    $ready = $true
    $ready = $ready -and ($null -ne $game)
    $ready = $ready -and ($null -ne $agent)
    $ready = $ready -and ($null -ne $npcapDll)
    $ready = $ready -and (Test-Path -LiteralPath $proCredential)
    $ready = $ready -and (Test-Path -LiteralPath $agentExecutable)
    [pscustomobject]@{
        CheckedAt = [DateTimeOffset]::UtcNow
        Ready = $ready
        GameProcessFound = $null -ne $game
        GamePid = if ($game) { $game.Id } else { $null }
        ProAgentFound = $null -ne $agent
        ProAgentPid = if ($agent) { $agent.Id } else { $null }
        NpcapLibraryFound = $null -ne $npcapDll
        NpcapLibrary = $npcapDll
        IslePilotCredentialFileFound = Test-Path -LiteralPath $islePilotCredential
        IslePilotCredentialPath = $islePilotCredential
        ProCredentialFileFound = Test-Path -LiteralPath $proCredential
        ProCredentialPath = $proCredential
        ProAgentExecutableFound = Test-Path -LiteralPath $agentExecutable
        ProAgentExecutablePath = $agentExecutable
        ProAgentVersion = $agentVersion
    }
}

function Test-BasePreflight {
    $full = Test-Preflight
    [pscustomobject]@{
        CheckedAt = $full.CheckedAt
        Ready = [bool]($full.NpcapLibraryFound -and
                 $full.ProCredentialFileFound -and
                 $full.ProAgentExecutableFound)
        GameProcessFound = $full.GameProcessFound
        GamePid = $full.GamePid
        ProAgentFound = $full.ProAgentFound
        ProAgentPid = $full.ProAgentPid
        NpcapLibraryFound = $full.NpcapLibraryFound
        NpcapLibrary = $full.NpcapLibrary
        IslePilotCredentialFileFound = $full.IslePilotCredentialFileFound
        IslePilotCredentialPath = $full.IslePilotCredentialPath
        ProCredentialFileFound = $full.ProCredentialFileFound
        ProCredentialPath = $full.ProCredentialPath
        ProAgentExecutableFound = $full.ProAgentExecutableFound
        ProAgentExecutablePath = $full.ProAgentExecutablePath
        ProAgentVersion = $full.ProAgentVersion
        LiveReady = $full.Ready
        PreflightScope = 'base'
    }
}

function New-Session {
    New-Item -ItemType Directory -Force -Path $SessionRoot | Out-Null
    $id = if ($SessionId) { $SessionId } else { "session-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), ([Guid]::NewGuid().ToString('N').Substring(0, 6)) }
    $path = Join-Path $SessionRoot $id
    New-Item -ItemType Directory -Force -Path $path, (Join-Path $path 'raw-capture'), (Join-Path $path 'screenshots') | Out-Null
    $preflight = Test-BasePreflight
    Write-JsonFile (Join-Path $path 'preflight.json') $preflight
    Write-JsonFile (Join-Path $path 'session-manifest.json') ([pscustomobject]@{
        SessionId = $id
        CreatedAt = [DateTimeOffset]::UtcNow
        DurationMinutes = $DurationMinutes
        RawCapturePolicy = 'Full local replay; passed raw artifacts are removable with -KeepPassedRaw.'
        GroundTruth = 'Chronological Agent/capture replay; no screenshot-based player count.'
        Preflight = $preflight
    })
    $env:ISLELIVEMAP_PRO_LIVE_COMPARE_PATH = Join-Path $path 'agent-live-compare.jsonl'
    $env:ISLE_MAP_DIAGNOSTICS_PATH = Join-Path $path 'map-diagnostics.jsonl'
    Write-Host "Session: $id"
    Write-Host "Artifacts: $path"
    if (-not $preflight.Ready) {
        Write-Warning 'Base preflight failed: Npcap, Pro credential, or Pro Agent executable is unavailable. IslePilot credential is optional for Pro tracking.'
    }
    if ($LaunchInstalledApp) {
        $exe = if ([string]::IsNullOrWhiteSpace($LauncherPath)) {
            Join-Path $env:LOCALAPPDATA 'IsleLiveMap\current\IsleLiveMap.exe'
        } else {
            [System.IO.Path]::GetFullPath($LauncherPath)
        }
        if (-not (Test-Path -LiteralPath $exe)) { throw "Không tìm thấy app đã cài: $exe" }
        if ($RestartLauncher) {
            Get-Process -Name 'IsleLiveMap.Pro.Agent' -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Get-Process -Name 'IsleLiveMap' -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        }
        Start-Process -FilePath $exe | Out-Null
        Write-Host 'Installed app launched. Enter the game/server and AFK.'
    }
    return $path
}

function Invoke-Capture([string]$Path) {
    $preflightPath = Join-Path $Path 'preflight.json'
    $preflight = Test-Preflight
    Write-JsonFile $preflightPath $preflight
    if (-not $preflight.Ready) {
        $manifestPath = Join-Path $Path 'session-manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_DEVELOPER' -Force
        $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue 'Preflight failed; no bypass.' -Force
        Write-JsonFile $manifestPath $manifest
        Write-Warning 'Capture stopped because live Pro preflight is not ready. Open Live Map to start the Agent, then retry capture.'
        return
    }
    foreach ($name in @('agent-live-compare.jsonl', 'map-diagnostics.jsonl')) {
        $target = Join-Path $Path $name
        if (-not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType File -Force -Path $target | Out-Null
        }
    }
    $seconds = [Math]::Max(60, $DurationMinutes * 60)
    Write-Host "Capturing for $DurationMinutes minutes. No game input is sent."
    Start-Sleep -Seconds $seconds
    $manifestPath = Join-Path $Path 'session-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName CompletedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
    $manifest | Add-Member -NotePropertyName CaptureFiles -NotePropertyValue @(
        'agent-live-compare.jsonl', 'map-diagnostics.jsonl'
    ) -Force
    foreach ($name in @('agent-live-compare.jsonl', 'map-diagnostics.jsonl')) {
        $source = Join-Path $Path $name
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $Path "raw-capture\$name") -Force
        }
    }
    Write-JsonFile $manifestPath $manifest
    Write-Host "Capture complete: $Path"
}

function Read-JsonLines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $items = [System.Collections.Generic.List[object]]::new()
    $contents = [System.IO.File]::ReadAllLines($Path)
    foreach ($line in $contents) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        # Preserve ISO timestamps as strings. PowerShell's default JSON date
        # conversion truncates fractional seconds to whole seconds, making
        # capture/decode and Agent/UI latency appear as exactly 0 ms.
        try {
            # Windows PowerShell 5.1 has no -DateKind parameter. Keep the
            # raw ISO strings there; the analyzer parses timestamps explicitly
            # and therefore does not need PowerShell's date conversion.
            if ($PSVersionTable.PSVersion.Major -ge 6) {
                $value = ConvertFrom-Json -InputObject $line -DateKind String
            } else {
                $value = ConvertFrom-Json -InputObject $line
            }
            $null = $items.Add($value)
        } catch { }
    }
    foreach ($item in $items) { Write-Output -NoEnumerate $item }
}

function Get-DateTimeOffsetOrNull($Value) {
    if ($null -eq $Value) { return $null }
    try { return [DateTimeOffset]::Parse($Value.ToString()) } catch { return $null }
}

function Get-EntityKey($Entity, [string]$Endpoint, [long]$Generation = 1) {
    $kind = $Entity.Kind.ToString().ToLowerInvariant()
    return "$Generation`:$Endpoint`:$kind`:$($Entity.TrackId)"
}

function Test-EligibleEntity($Entity, $Frame) {
    if ($null -eq $Entity -or [long]$Entity.TrackId -le 0) { return $false }
    $kind = $Entity.Kind.ToString().ToLowerInvariant()
    $location = $Entity.Location
    if ($null -eq $location -or $null -eq $location.X -or $null -eq $location.Y) { return $false }
    # Presence can refresh while a stationary actor keeps its last safe
    # coordinate. That is valid for a short stationary window, but an old
    # coordinate must not be treated as ground truth for marker accuracy.
    $locationAt = Get-DateTimeOffsetOrNull $Entity.LocationObservedAt
    $frameAt = Get-DateTimeOffsetOrNull $(if ($Entity.ObservedAt) { $Entity.ObservedAt } else { $Frame.ObservedAt })
    if ($null -ne $locationAt -and $null -ne $frameAt -and
        (($locationAt -gt $frameAt) -or
         (($frameAt - $locationAt).TotalSeconds -gt 90))) { return $false }
    if ($kind -eq 'ai') {
        return -not [string]::IsNullOrWhiteSpace([string]$Entity.SpeciesId) -and
            -not [string]::IsNullOrWhiteSpace([string]$Entity.SpeciesShortName)
    }
    if ($kind -eq 'player') {
        if ([bool]$Entity.IsProvisional) {
            return -not [string]::IsNullOrWhiteSpace([string]$Entity.SpeciesId) -and
                -not [string]::IsNullOrWhiteSpace([string]$Entity.SpeciesShortName)
        }
        return -not [string]::IsNullOrWhiteSpace([string]$Entity.PlayerProofName)
    }
    return $false
}

function Get-EntityRejectionReason($Entity, $Frame) {
    if ($null -eq $Entity -or [long]$Entity.TrackId -le 0) { return 'InvalidTrackId' }
    if ($null -eq $Entity.Location -or $null -eq $Entity.Location.X -or $null -eq $Entity.Location.Y) { return 'InvalidCoordinate' }
    $locationAt = Get-DateTimeOffsetOrNull $Entity.LocationObservedAt
    $frameAt = Get-DateTimeOffsetOrNull $(if ($Entity.ObservedAt) { $Entity.ObservedAt } else { $Frame.ObservedAt })
    if ($null -ne $locationAt -and $null -ne $frameAt -and ($frameAt - $locationAt).TotalSeconds -gt 90) { return 'StaleLocation' }
    $kind = $Entity.Kind.ToString().ToLowerInvariant()
    if ($kind -eq 'ai' -and ([string]::IsNullOrWhiteSpace([string]$Entity.SpeciesId) -or [string]::IsNullOrWhiteSpace([string]$Entity.SpeciesShortName))) { return 'MissingSpecies' }
    if ($kind -eq 'player' -and -not [bool]$Entity.IsProvisional -and [string]::IsNullOrWhiteSpace([string]$Entity.PlayerProofName)) { return 'MissingPlayerProof' }
    return 'ValidationOrProof'
}

function Get-RenderedKey($Marker) {
    if ($null -eq $Marker) { return $null }
    if (-not [string]::IsNullOrWhiteSpace([string]$Marker.Key)) { return [string]$Marker.Key }
    if (-not [string]::IsNullOrWhiteSpace([string]$Marker.SteamId)) { return [string]$Marker.SteamId }
    return $null
}

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($null -eq $Values -or $Values.Count -eq 0) { return $null }
    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($ordered.Count * $Percentile)) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, $index))
    return [Math]::Round([double]$ordered[$index], 2)
}

function Invoke-Analyze([string]$Path) {
    # Analyze the immutable capture snapshot when one exists.  The launcher
    # may remain open after `capture` returns, so reading the live files here
    # would mix the requested session with later frames and create false
    # lifecycle/missing-marker failures.
    $agentPath = Join-Path $Path 'raw-capture\agent-live-compare.jsonl'
    $mapPath = Join-Path $Path 'raw-capture\map-diagnostics.jsonl'
    if (-not (Test-Path -LiteralPath $agentPath)) { $agentPath = Join-Path $Path 'agent-live-compare.jsonl' }
    if (-not (Test-Path -LiteralPath $mapPath)) { $mapPath = Join-Path $Path 'map-diagnostics.jsonl' }
    $agent = @(Read-JsonLines $agentPath)
    $map = @(Read-JsonLines $mapPath)
    $renderRows = @($map | Where-Object { $_.stage -eq 'render-end' })
    $rendered = @($renderRows | ForEach-Object { @($_.RenderedMarkers) } | Where-Object { $_ })
    $renderIndex = @{}
    foreach ($row in $renderRows) {
        $rowAt = Get-DateTimeOffsetOrNull $row.ReceivedAt
        foreach ($marker in @($row.RenderedMarkers)) {
            $key = Get-RenderedKey $marker
            if ([string]::IsNullOrWhiteSpace($key) -or $null -eq $rowAt) { continue }
            if (-not $renderIndex.ContainsKey($key)) { $renderIndex[$key] = [System.Collections.Generic.List[object]]::new() }
            $renderIndex[$key].Add([pscustomobject]@{ At = $rowAt; Marker = $marker })
        }
    }
    $groundTruth = [System.Collections.Generic.List[object]]::new()
    $eligibleEntities = [System.Collections.Generic.List[object]]::new()
    $seenByEndpoint = @{}
    $renderLatencies = [System.Collections.Generic.List[double]]::new()
    $captureDecodeLatencies = [System.Collections.Generic.List[double]]::new()
    $agentUiLatencies = [System.Collections.Generic.List[double]]::new()
    $missing = [System.Collections.Generic.List[object]]::new()
    $entityStates = @{}
    $stageCounters = [ordered]@{
        CandidateActors = 0
        SpeciesEvidenceActors = 0
        LocatedActors = 0
        InboundPlayers = 0
        InboundAi = 0
        FusedPlayers = 0
        FusedAi = 0
        NearbyPlayers = 0
        UnmatchedInbound = 0
        UnmatchedPresence = 0
        BlockedNoProof = 0
        BlockedFusionOrValidation = 0
    }
    $hasStageEvidence = $false
    foreach ($frame in $agent) {
        $frameAt = Get-DateTimeOffsetOrNull $(if ($frame.ReceivedAt) { $frame.ReceivedAt } else { $frame.ObservedAt })
        $endpoint = if ([string]::IsNullOrWhiteSpace([string]$frame.ServerEndpoint)) { 'unknown' } else { [string]$frame.ServerEndpoint }
        if (-not $seenByEndpoint.ContainsKey($endpoint)) { $seenByEndpoint[$endpoint] = 1 }
        # The live recorder stores the post-fusion output in separate player
        # and AI lanes. Keep compatibility with older captures, but never
        # silently treat a new-schema frame as an empty roster.
        $entities = if ($null -ne $frame.mapOutputPlayers -or
                        $null -ne $frame.mapOutputAi) {
            @($frame.mapOutputPlayers) + @($frame.mapOutputAi)
        } else {
            @($frame.RemoteEntities)
        }
        if ($null -ne $frame.evidence -or $null -ne $frame.ongoingCandidates) {
            $hasStageEvidence = $true
            if ($frame.evidence) {
                $stageCounters.CandidateActors += [int]$frame.evidence.candidateActors
                $stageCounters.SpeciesEvidenceActors += [int]$frame.evidence.speciesEvidenceActors
                $stageCounters.LocatedActors += [int]$frame.evidence.locatedActors
            }
            $stageCounters.InboundPlayers += @($frame.inboundPlayers).Count
            $stageCounters.InboundAi += @($frame.inboundAi).Count
            $stageCounters.FusedPlayers += @($frame.mapOutputPlayers).Count
            $stageCounters.FusedAi += @($frame.mapOutputAi).Count
            $stageCounters.NearbyPlayers += @($frame.islePilot.nearbyPlayers).Count
            $stageCounters.UnmatchedInbound += @($frame.fusion.unmatchedInboundTrackIds).Count
            $stageCounters.UnmatchedPresence += @($frame.fusion.unmatchedPresenceTrackIds).Count
            foreach ($candidate in @($frame.ongoingCandidates)) {
                if ([string]$candidate.decision -eq 'blocked-no-exact-player-proof') { $stageCounters.BlockedNoProof++ }
                if ([string]$candidate.decision -eq 'blocked-by-fusion-or-validation') { $stageCounters.BlockedFusionOrValidation++ }
            }
        }
        foreach ($entity in $entities) {
            $eligible = Test-EligibleEntity $entity $frame
            $key = Get-EntityKey $entity $endpoint $seenByEndpoint[$endpoint]
            $render = $null
            $identityPrefix = "pro-entity:$($entity.Kind.ToString().ToLowerInvariant()):$($entity.TrackId)"
            if ($eligible) {
                # Rendered marker keys intentionally carry a visual namespace
                # (currently `steam:pro-entity:...#slot`).  Ground truth keys
                # must not depend on that namespace or a provisional slot: the
                # stable identity for replay matching is entity kind + TrackId.
                # The previous prefix check assumed the key started directly
                # with `pro-entity`, so every valid marker was falsely reported
                # as missing when the runtime added the `steam:` namespace.
                $kindName = $entity.Kind.ToString().ToLowerInvariant()
                $trackIdText = [regex]::Escape([string]$entity.TrackId)
                $renderKeyPattern = "(^|:)pro-entity:$kindName`:$trackIdText(#|$)"
                $candidates = @($renderIndex.Keys |
                    Where-Object { $_ -match $renderKeyPattern } |
                    ForEach-Object { $renderIndex[$_] })
                if ($null -ne $frameAt) {
                    if ($candidates.Count -gt 0) {
                        $render = @($candidates | ForEach-Object { $_ } |
                            Where-Object { $_.At -ge $frameAt } | Select-Object -First 1)
                        if ($render.Count -eq 0) { $render = @($candidates | Select-Object -Last 1) }
                        if ($render.Count -gt 0) { $render = $render[0] }
                    }
                }
            }
            $renderedNow = $null -ne $render
            $state = if (-not $eligible) { 'Rejected' } elseif ($renderedNow) { 'Visible' } else { 'TemporarilyMissing' }
            $line = [pscustomobject]@{
                ReceivedAt = $frameAt
                Sequence = $frame.Sequence
                Key = $key
                TrackId = $entity.TrackId
                Kind = $entity.Kind
                Eligible = $eligible
                Rendered = $renderedNow
                State = $state
                RenderedAt = if ($render) { $render.At } else { $null }
                LatencyMs = if ($render -and $frameAt) { [Math]::Round(($render.At - $frameAt).TotalMilliseconds, 2) } else { $null }
                RejectionReason = if ($eligible) { $null } else { Get-EntityRejectionReason $entity $frame }
            }
            $groundTruth.Add($line)
            if ($eligible) {
                $eligibleEntities.Add($line)
                if ($render) { $renderLatencies.Add([double]$line.LatencyMs) } else { $missing.Add($line) }
            }
            if ($render -and $frameAt) {
                $receivedAt = Get-DateTimeOffsetOrNull $frame.ReceivedAt
                $observedAt = Get-DateTimeOffsetOrNull $frame.ObservedAt
                if ($receivedAt -and $observedAt) { $captureDecodeLatencies.Add([Math]::Max(0, ($receivedAt - $observedAt).TotalMilliseconds)) }
                $agentUiLatencies.Add([Math]::Max(0, ($render.At - $frameAt).TotalMilliseconds))
            }
            $entityStates[$key] = $state
        }
    }
    $groundTruth | ForEach-Object { $_ | ConvertTo-Json -Depth 16 -Compress } |
        Set-Content -LiteralPath (Join-Path $Path 'replay-ground-truth.jsonl') -Encoding UTF8
    $sequences = @($agent | Where-Object { $null -ne $_.Sequence } | ForEach-Object { [long]$_.Sequence })
    $gaps = 0
    $duplicates = 0
    $reorders = 0
    for ($i = 1; $i -lt $sequences.Count; $i++) {
        if ($sequences[$i] -gt ($sequences[$i - 1] + 1)) { $gaps += $sequences[$i] - $sequences[$i - 1] - 1 }
        if ($sequences[$i] -eq $sequences[$i - 1]) { $duplicates++ }
        if ($sequences[$i] -lt $sequences[$i - 1]) { $reorders++ }
    }
    $diagnosticRejections = @{}
    foreach ($row in $renderRows) {
        $diagnostic = $row.ProTrackingDiagnostics
        if ($null -eq $diagnostic) { continue }
        foreach ($property in $diagnostic.Rejections.PSObject.Properties) {
            $current = if ($diagnosticRejections.ContainsKey($property.Name)) { [int]$diagnosticRejections[$property.Name] } else { 0 }
            $diagnosticRejections[$property.Name] = $current + [int]$property.Value
        }
    }
    $result = [pscustomobject]@{
        SessionId = (Split-Path $Path -Leaf)
        AnalyzedAt = [DateTimeOffset]::UtcNow
        AgentFrames = $agent.Count
        MapRenderRows = $renderRows.Count
        RenderedMarkerSamples = $rendered.Count
        DistinctRenderedMarkerKeys = @($rendered | ForEach-Object Key | Sort-Object -Unique).Count
        SequenceGapEstimate = $gaps
        DuplicateSequenceCount = $duplicates
        ReorderedSequenceCount = $reorders
        MaxUiQueueDelayMs = if ($renderRows) { ($renderRows | Measure-Object UiQueueDelayMs -Maximum).Maximum } else { $null }
        MaxSnapshotAgeMs = if ($renderRows) { ($renderRows | Measure-Object SnapshotAgeMs -Maximum).Maximum } else { $null }
        ProTrackingActiveRows = @($renderRows | Where-Object ProPlayerTrackingActive).Count
        CapturedPackets = $agent.Count
        EligibleEntityObservations = $eligibleEntities.Count
        RenderedEligibleObservations = @($eligibleEntities | Where-Object Rendered).Count
        MissingEligibleObservations = $missing.Count
        RejectedEntityObservations = @($groundTruth | Where-Object { -not $_.Eligible }).Count
        RejectionReasons = $diagnosticRejections
        P50MarkerLatencyMs = Get-Percentile $renderLatencies.ToArray() 0.50
        P95MarkerLatencyMs = Get-Percentile $renderLatencies.ToArray() 0.95
        P50CaptureDecodeLatencyMs = Get-Percentile $captureDecodeLatencies.ToArray() 0.50
        P95CaptureDecodeLatencyMs = Get-Percentile $captureDecodeLatencies.ToArray() 0.95
        P50AgentToUiLatencyMs = Get-Percentile $agentUiLatencies.ToArray() 0.50
        P95AgentToUiLatencyMs = Get-Percentile $agentUiLatencies.ToArray() 0.95
        MissingEntities = $missing
        GroundTruthPath = (Join-Path $Path 'replay-ground-truth.jsonl')
        StageEvidenceAvailable = $hasStageEvidence
        StageCounters = $stageCounters
        Status = if ($agent.Count -eq 0 -or $renderRows.Count -eq 0) { 'NEED_DEVELOPER' } elseif (-not $hasStageEvidence) { 'NEED_STAGE_EVIDENCE' } elseif ($missing.Count -gt 0) { 'FAIL' } else { 'PASS' }
        MissingMarkerProof = if (-not $hasStageEvidence) { 'Capture predates stage-level recorder schema; no claim about audio/candidate loss is allowed.' } elseif ($missing.Count -gt 0) { 'Eligible entity has no matching marker in render log.' } else { 'Every eligible replay entity has a marker in the observed render window.' }
    }
    Write-JsonFile (Join-Path $Path 'tracking-analysis.json') $result
    return $result
}

function Invoke-Replay([string]$Path) {
    $agentPath = Join-Path $Path 'agent-live-compare.jsonl'
    if (-not (Test-Path -LiteralPath $agentPath)) { $agentPath = Join-Path $Path 'raw-capture\agent-live-compare.jsonl' }
    if (-not (Test-Path -LiteralPath $agentPath)) {
        $analysis = Invoke-Analyze $Path
        $analysis = $analysis | Add-Member -NotePropertyName ReplayStatus -NotePropertyValue 'NEED_DEVELOPER' -PassThru
        $analysis = $analysis | Add-Member -NotePropertyName ReplayReason -NotePropertyValue 'Raw Agent capture is unavailable because preflight did not pass.' -PassThru
        Write-JsonFile (Join-Path $Path 'tracking-analysis.json') $analysis
        return $analysis
    }

    $analysis = Invoke-Analyze $Path
    $manifestPath = Join-Path $Path 'session-manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest | Add-Member -NotePropertyName ReplayedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
        $manifest | Add-Member -NotePropertyName ReplayGroundTruth -NotePropertyValue 'replay-ground-truth.jsonl' -Force
        Write-JsonFile $manifestPath $manifest
    }
    return $analysis
}

function Invoke-Report([string]$Path) {
    $analysisPath = Join-Path $Path 'tracking-analysis.json'
    if (-not (Test-Path -LiteralPath $analysisPath)) { $null = Invoke-Analyze $Path }
    $a = Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
    $lines = @(
        '# TRACKING LOOP RESULT',
        "- Session: $($a.SessionId)",
        "- Agent frames: $($a.AgentFrames)",
        "- Render row: $($a.MapRenderRows)",
        "- Marker sample: $($a.RenderedMarkerSamples)",
        "- Distinct marker keys: $($a.DistinctRenderedMarkerKeys)",
        "- Estimated sequence gaps: $($a.SequenceGapEstimate)",
        "- Max UI queue delay: $($a.MaxUiQueueDelayMs) ms",
        "- Max snapshot age: $($a.MaxSnapshotAgeMs) ms",
        "- Pro tracking active rows: $($a.ProTrackingActiveRows)",
        "- Eligible entity observations: $($a.EligibleEntityObservations)",
        "- Rendered eligible observations: $($a.RenderedEligibleObservations)",
        "- Missing marker observations: $($a.MissingEligibleObservations)",
        "- Rejected entity observations: $($a.RejectedEntityObservations)",
        "- Stage evidence available: $($a.StageEvidenceAvailable)",
        "- Stage counters: $(($a.StageCounters | ConvertTo-Json -Compress) -replace "`r?`n", '')",
        "- P50 latency marker: $($a.P50MarkerLatencyMs) ms",
        "- P95 latency marker: $($a.P95MarkerLatencyMs) ms",
        "- P50 capture -> decode: $($a.P50CaptureDecodeLatencyMs) ms",
        "- P95 capture -> decode: $($a.P95CaptureDecodeLatencyMs) ms",
        "- P50 Agent -> UI: $($a.P50AgentToUiLatencyMs) ms",
        "- P95 Agent -> UI: $($a.P95AgentToUiLatencyMs) ms",
        "- Ground truth: $($a.GroundTruthPath)",
        "- Status: $($a.Status)",
        '',
        $a.MissingMarkerProof
    )
    $reportPath = Join-Path $Path 'tracking-report.md'
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host ($lines -join [Environment]::NewLine)
}

function New-RegressionFixture([string]$Path, $Analysis) {
    $fixtureRoot = Join-Path $Path 'fixtures'
    New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
    $fixturePath = Join-Path $fixtureRoot 'tracking-regression.json'
    Write-JsonFile $fixturePath ([pscustomobject]@{
        CreatedAt = [DateTimeOffset]::UtcNow
        SourceSession = (Split-Path $Path -Leaf)
        RootCause = if ($Analysis.MissingEligibleObservations -gt 0) { 'eligible-marker-missing' } else { 'no-replay-divergence' }
        Acceptance = 'Every eligible ground-truth observation must resolve to a rendered marker or an explicit rejection/TTL reason.'
        MissingEntities = $Analysis.MissingEntities
        GroundTruthPath = $Analysis.GroundTruthPath
    })
    return $fixturePath
}

function Write-FixLoopState([string]$Path, $Analysis, [string]$FixturePath) {
    $rootCause = if ($Analysis.MissingEligibleObservations -gt 0) {
        'eligible-marker-missing'
    } elseif ($Analysis.SequenceGapEstimate -gt 0) {
        'sequence-gap'
    } elseif ($Analysis.P95MarkerLatencyMs -gt 2000) {
        'marker-latency'
    } else {
        'none-reproduced'
    }
    $state = [pscustomobject]@{
        UpdatedAt = [DateTimeOffset]::UtcNow
        Iteration = 1
        BaselineStatus = $Analysis.Status
        RootCause = $rootCause
        FixturePath = $FixturePath
        ReplayStatus = $Analysis.Status
        RequiredNextAction = if ($Analysis.Status -eq 'NEED_DEVELOPER') {
            'Developer must provide a live game/Pro/Npcap capture.'
        } elseif ($Analysis.Status -eq 'FAIL') {
            'Create one regression test for RootCause, patch one cause, replay the same raw capture, then run three live smoke sessions.'
        } else {
            'Run three live smoke sessions before committing a fix.'
        }
        CommitAllowed = $false
    }
    $statePath = Join-Path $Path 'loop-state.json'
    Write-JsonFile $statePath $state
    return $statePath
}

function Remove-PassedRawArtifacts {
    if ($KeepPassedRaw) { return }
    Get-ChildItem -LiteralPath $SessionRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $analysisPath = Join-Path $_.FullName 'tracking-analysis.json'
        if (-not (Test-Path -LiteralPath $analysisPath)) { return }
        $analysis = Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
        if ($analysis.Status -eq 'PASS') {
            Remove-Item -LiteralPath (Join-Path $_.FullName 'raw-capture') -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-Round {
    $round = "round-{0:yyyyMMdd-HHmmss}" -f (Get-Date)
    $roundRoot = Join-Path $SessionRoot $round
    New-Item -ItemType Directory -Force -Path $roundRoot | Out-Null
    $modes = @('stable', 'restart-or-reconnect', 'crowded-area')
    foreach ($mode in $modes) {
        $script:SessionId = "$round-$mode"
        # New-Session owns the process environment and must launch/restart the
        # app in that same process.  A later `capture` PowerShell invocation
        # cannot retroactively pass diagnostics paths to an already running
        # launcher.
        $path = New-Session
        $manifestPath = Join-Path $path 'session-manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest | Add-Member -NotePropertyName Scenario -NotePropertyValue $mode -Force
        Write-JsonFile $manifestPath $manifest
        Invoke-Capture $path
        Invoke-Analyze $path | Out-Null
        Invoke-Report $path
    }
    Remove-PassedRawArtifacts
    Write-Host "Đã hoàn tất round 3 session: $round"
}

switch ($Command) {
    'start-session' { $null = New-Session; break }
    'capture' { Invoke-Capture (Resolve-Session); break }
    'run-round' { Invoke-Round; break }
    'replay' { Invoke-Replay (Resolve-Session) | Format-List; break }
    'analyze' { Invoke-Analyze (Resolve-Session) | Format-List; break }
    'report' { Invoke-Report (Resolve-Session); break }
    'fix-loop' {
        $path = Resolve-Session
        $analysis = Invoke-Replay $path
        $fixturePath = New-RegressionFixture $path $analysis
        $statePath = Write-FixLoopState $path $analysis $fixturePath
        Invoke-Report $path
        if ($analysis.Status -eq 'NEED_DEVELOPER') {
            Write-Host "fix-loop: NEED_DEVELOPER; fixture saved at $fixturePath; state saved at $statePath"
        } elseif ($analysis.Status -eq 'PASS') {
            Write-Host "fix-loop: replay baseline PASS; fixture saved at $fixturePath; state saved at $statePath. Three live smoke sessions are still required before commit."
        } else {
            Write-Host "fix-loop: divergence reproduced; fixture saved at $fixturePath; state saved at $statePath. One root cause per iteration."
        }
    }
}
