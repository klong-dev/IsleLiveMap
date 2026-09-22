[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start-session', 'preflight', 'open-map', 'capture', 'run-round', 'replay', 'analyze', 'stale-report', 'report', 'fix-loop')]
    [string]$Command = 'start-session',

    [string]$SessionRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\tracking-lab'),
    [string]$SessionId,
    [int]$DurationMinutes = 5,
    [switch]$KeepPassedRaw,
    [switch]$LaunchInstalledApp,
    [switch]$RestartLauncher,
    [string]$LauncherPath,
    [int]$OpenMapTimeoutSeconds = 90,
    [string]$ProLocalReleaseManifest
)

$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($ProLocalReleaseManifest)) {
    $resolvedProManifest = [System.IO.Path]::GetFullPath($ProLocalReleaseManifest)
    if (-not (Test-Path -LiteralPath $resolvedProManifest -PathType Leaf)) {
        throw "Không tìm thấy Pro local release manifest: $resolvedProManifest"
    }
    # ProReleaseManager performs the signed hash/signature verification. The
    # harness only supplies the signed local-debug manifest to the normal app
    # initialization flow; it never replaces files or bypasses entitlement.
    $env:ISLELIVEMAP_PRO_LOCAL_RELEASE_MANIFEST = $resolvedProManifest
}

function Write-JsonFile([string]$Path, $Value) {
    $Value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$script:RoundLockStream = $null

function Enter-RoundLock {
    $lockPath = Join-Path ([System.IO.Path]::GetFullPath($SessionRoot)) 'run-round.lock'
    New-Item -ItemType Directory -Force -Path (Split-Path $lockPath -Parent) | Out-Null
    try {
        $script:RoundLockStream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $script:RoundLockStream.SetLength(0)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes("PID=$PID`nStartedAt=$([DateTimeOffset]::UtcNow.ToString('O'))`n")
        $script:RoundLockStream.Write($bytes, 0, $bytes.Length)
        $script:RoundLockStream.Flush()
    } catch {
        if ($script:RoundLockStream) { $script:RoundLockStream.Dispose(); $script:RoundLockStream = $null }
        throw "RUN_ALREADY_ACTIVE: một run-round khác đang giữ $lockPath"
    }
}

function Exit-RoundLock {
    if ($script:RoundLockStream) {
        $script:RoundLockStream.Dispose()
        $script:RoundLockStream = $null
    }
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
        'TheIsle',
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
        ProLocalReleaseManifest = $ProLocalReleaseManifest
    }
}

function Test-BasePreflight {
    $full = Test-Preflight
    $missing = [System.Collections.Generic.List[string]]::new()
    if (-not $full.GameProcessFound) { $null = $missing.Add('GAME_NOT_RUNNING') }
    if (-not $full.NpcapLibraryFound) { $null = $missing.Add('NPCAP_NOT_FOUND') }
    if (-not $full.ProCredentialFileFound) { $null = $missing.Add('PRO_CREDENTIAL_NOT_FOUND') }
    if (-not $full.ProAgentExecutableFound) { $null = $missing.Add('PRO_AGENT_NOT_FOUND') }
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
        MissingRequirements = @($missing)
        PreflightScope = 'base'
    }
}

function New-Session {
    $SessionRoot = [System.IO.Path]::GetFullPath($SessionRoot)
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
    $path = [System.IO.Path]::GetFullPath($path)
    $env:ISLELIVEMAP_PRO_LIVE_COMPARE_PATH = Join-Path $path 'agent-live-compare.jsonl'
    $env:ISLE_MAP_DIAGNOSTICS_PATH = Join-Path $path 'map-diagnostics.jsonl'
    Write-Host "Session: $id"
    Write-Host "Artifacts: $path"
    if (-not $preflight.Ready) {
        Write-Warning "Base preflight failed: $($preflight.MissingRequirements -join ', '). IslePilot credential is optional for Pro tracking."
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

function Write-Preflight([string]$Path) {
    $preflight = Test-Preflight
    Write-JsonFile (Join-Path $Path 'preflight.json') $preflight
    return $preflight
}

function Get-InstalledLauncherPath {
    if (-not [string]::IsNullOrWhiteSpace($LauncherPath)) {
        return [System.IO.Path]::GetFullPath($LauncherPath)
    }

    return Join-Path $env:LOCALAPPDATA 'IsleLiveMap\current\IsleLiveMap.exe'
}

function Find-AutomationElementById($Root, [string]$AutomationId) {
    Add-Type -AssemblyName UIAutomationClient -ErrorAction SilentlyContinue
    Add-Type -AssemblyName UIAutomationTypes -ErrorAction SilentlyContinue
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-AutomationButton($Root) {
    $button = Find-AutomationElementById $Root 'OpenMapProButton'
    if ($null -eq $button) { $button = Find-AutomationElementById $Root 'OpenMapBasicButton' }
    if ($null -ne $button) { return $button }

    # Some published WPF builds expose AutomationProperties.Name but omit the
    # AutomationId from the generated tree. Keep the harness compatible with
    # those builds without resorting to coordinate clicks.
    $names = @('MỞ MAP PRO  →', 'MỞ MAP PRO →', 'MỞ LIVE MAP', 'MỞ MAP')
    foreach ($name in $names) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $candidate = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $candidate) { return $candidate }
    }
    return $null
}

function Start-LauncherWithSessionEnvironment([string]$Executable, [string]$Path) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path -Parent $Executable
    $info.EnvironmentVariables['ISLELIVEMAP_PRO_LIVE_COMPARE_PATH'] = Join-Path $Path 'agent-live-compare.jsonl'
    $info.EnvironmentVariables['ISLE_MAP_DIAGNOSTICS_PATH'] = Join-Path $Path 'map-diagnostics.jsonl'
    if (-not [string]::IsNullOrWhiteSpace($ProLocalReleaseManifest)) {
        # Environment variables do not flow from this harness invocation into
        # a newly created launcher automatically. Forward the signed release
        # manifest explicitly so the normal ProReleaseManager can verify and
        # install the requested Agent instead of silently reusing current.json.
        $info.EnvironmentVariables['ISLELIVEMAP_PRO_LOCAL_RELEASE_MANIFEST'] =
            [System.IO.Path]::GetFullPath($ProLocalReleaseManifest)
    }
    $process = [System.Diagnostics.Process]::Start($info)
    return $process
}

function Invoke-OpenMap([string]$Path) {
    $Path = [System.IO.Path]::GetFullPath($Path)
    $preflight = Write-Preflight $Path
    if (-not $preflight.NpcapLibraryFound -or
        -not $preflight.ProCredentialFileFound -or
        -not $preflight.ProAgentExecutableFound -or
        -not $preflight.GameProcessFound) {
        $manifestPath = Join-Path $Path 'session-manifest.json'
        if (Test-Path -LiteralPath $manifestPath) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_DEVELOPER' -Force
            $missing = @()
            if (-not $preflight.GameProcessFound) { $missing += 'game process' }
            if (-not $preflight.NpcapLibraryFound) { $missing += 'Npcap' }
            if (-not $preflight.ProCredentialFileFound) { $missing += 'Pro credential' }
            if (-not $preflight.ProAgentExecutableFound) { $missing += 'Pro Agent executable' }
            $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue ("Open-map preflight failed: " + ($missing -join ', ') + '.') -Force
            Write-JsonFile $manifestPath $manifest
        }
        Write-Warning ("Open-map stopped: base dependency preflight is not ready ($($missing -join ', ')).")
        return $false
    }

    $launcher = Get-InstalledLauncherPath
    if (-not (Test-Path -LiteralPath $launcher)) {
        throw "Không tìm thấy launcher: $launcher"
    }

    $env:ISLELIVEMAP_PRO_LIVE_COMPARE_PATH = Join-Path $Path 'agent-live-compare.jsonl'
    $env:ISLE_MAP_DIAGNOSTICS_PATH = Join-Path $Path 'map-diagnostics.jsonl'
    # A pre-existing launcher cannot receive changed environment variables.
    # Start an isolated harness-owned instance so Agent/Host recorders inherit
    # the session paths. Never terminate the developer's original instance.
    $existing = Start-LauncherWithSessionEnvironment $launcher $Path
    $startedByHarness = $true

    Add-Type -AssemblyName UIAutomationClient -ErrorAction SilentlyContinue
    Add-Type -AssemblyName UIAutomationTypes -ErrorAction SilentlyContinue
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(10, $OpenMapTimeoutSeconds))
    $button = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $button = Find-AutomationButton $root
        if ($null -ne $button) {
            try {
                $invoke = $button.GetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern)
                if ($button.Current.IsEnabled) {
                    $invoke.Invoke()
                    $manifestPath = Join-Path $Path 'session-manifest.json'
                    if (Test-Path -LiteralPath $manifestPath) {
                        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                        $manifest | Add-Member -NotePropertyName OpenMapAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
                        $manifest | Add-Member -NotePropertyName LauncherPid -NotePropertyValue $existing.Id -Force
                        $manifest | Add-Member -NotePropertyName LauncherStartedByHarness -NotePropertyValue $startedByHarness -Force
                        Write-JsonFile $manifestPath $manifest
                    }
                    Write-Host "Open-map invoked through UI Automation (launcher PID $($existing.Id))."
                    return $true
                }
            } catch {
                # The launcher may still be rebuilding the page/update gate.
            }
        }
    }

    Write-Warning 'Open-map timed out waiting for an enabled map button.'
    return $false
}

function Invoke-Capture([string]$Path) {
    $preflightPath = Join-Path $Path 'preflight.json'
    # Opening the map starts the Pro source asynchronously.  Do not sample
    # preflight in the small window before the Agent process has spawned.
    $preflight = Wait-ForTrackingRuntime -TimeoutSeconds 45
    Write-JsonFile $preflightPath $preflight
    $manifestPath = Join-Path $Path 'session-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $expectedAgentVersion = [string]$manifest.Preflight.ProAgentVersion
    if (-not [string]::IsNullOrWhiteSpace($expectedAgentVersion) -and
        -not [string]::IsNullOrWhiteSpace([string]$preflight.ProAgentVersion) -and
        $expectedAgentVersion -ne [string]$preflight.ProAgentVersion) {
        $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_STAGE_EVIDENCE' -Force
        $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue (
            "Pro Agent version mismatch: preflight expected $expectedAgentVersion, runtime reported $($preflight.ProAgentVersion).") -Force
        Write-JsonFile $manifestPath $manifest
        Write-Warning "Capture stopped: Pro Agent version mismatch ($expectedAgentVersion -> $($preflight.ProAgentVersion))."
        return
    }
    if (-not $preflight.Ready) {
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

function Wait-ForTrackingRuntime([int]$TimeoutSeconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(5, $TimeoutSeconds))
    do {
        $preflight = Test-Preflight
        if ($preflight.GameProcessFound -and
            $preflight.ProAgentFound -and
            $preflight.NpcapLibraryFound -and
            $preflight.ProCredentialFileFound -and
            $preflight.ProAgentExecutableFound) {
            return $preflight
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    return (Test-Preflight)
}

function Stop-HarnessLauncher([string]$Path) {
    $manifestPath = Join-Path $Path 'session-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { return }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (-not [bool](Get-Field $manifest @('LauncherStartedByHarness'))) { return }

    $launcherPid = Get-Field $manifest @('LauncherPid')
    if ($null -eq $launcherPid) { return }

    $launcher = Get-Process -Id ([int]$launcherPid) -ErrorAction SilentlyContinue
    if ($null -ne $launcher) {
        Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
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
    # ConvertFrom-Json returns a PSCustomObject for each JSONL line. Do not
    # emit with -NoEnumerate here: Windows PowerShell 5.1 can preserve the
    # internal List wrapper as the pipeline item, making every later field
    # lookup appear null and producing false missing-marker failures.
    foreach ($item in $items) { Write-Output $item }
}

function Get-DateTimeOffsetOrNull($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) { return [DateTimeOffset]$Value }
    try { return [DateTimeOffset]::Parse($Value.ToString()) } catch { return $null }
}

function Get-FrameTimestamp($Frame) {
    return Get-DateTimeOffsetOrNull (Get-Field $Frame @(
        'ReceivedAt', 'receivedAt',
        'FrameObservedAt', 'frameObservedAt',
        'ObservedAt', 'observedAt',
        'RecordedAt', 'recordedAt'
    ))
}

function Get-Field($Object, [string[]]$Names) {
    if ($null -eq $Object) { return $null }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property) { return $property.Value }
    }
    return $null
}

function Get-EntityProvisional($Entity) {
    return [bool](Get-Field $Entity @('IsProvisional', 'isProvisional', 'Provisional', 'provisional'))
}

function Get-EntityName($Entity) {
    return [string](Get-Field $Entity @('PlayerProofName', 'playerProofName', 'IngameName', 'ingameName'))
}

function Get-EntitySpeciesId($Entity) {
    return [string](Get-Field $Entity @('SpeciesId', 'speciesId'))
}

function Get-EntitySpeciesName($Entity) {
    return [string](Get-Field $Entity @('SpeciesShortName', 'speciesShortName', 'Species', 'species'))
}

function Get-EntityKind($Entity) {
    $value = Get-Field $Entity @('Kind', 'kind', 'EntityKind', 'entityKind', 'Type', 'type')
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return 'unknown' }
    return ([string]$value).ToLowerInvariant()
}

function Get-EntityHandle($Entity, [string[]]$Names) {
    $value = Get-Field $Entity $Names
    if ($null -eq $value) { return [uint64]0 }
    try { return [uint64]$value } catch { return [uint64]0 }
}

function Get-EntityKey($Entity, [string]$Endpoint, [long]$Generation = 1) {
    $kind = Get-EntityKind $Entity
    $trackId = Get-Field $Entity @('TrackId', 'trackId', 'Id', 'id')
    return "$Generation`:$Endpoint`:$kind`:$trackId"
}

function Test-EligibleEntity($Entity, $Frame) {
    $trackId = Get-Field $Entity @('TrackId', 'trackId', 'Id', 'id')
    if ($null -eq $Entity -or $null -eq $trackId -or [long]$trackId -le 0) { return $false }
    $kind = Get-EntityKind $Entity
    $location = Get-Field $Entity @('Location', 'location', 'Position', 'position')
    if ($null -eq $location -or $null -eq $location.X -or $null -eq $location.Y) { return $false }
    # Presence can refresh while a stationary actor keeps its last safe
    # coordinate. That is valid for a short stationary window, but an old
    # coordinate must not be treated as ground truth for marker accuracy.
    $locationAt = Get-DateTimeOffsetOrNull $Entity.LocationObservedAt
    $frameAt = Get-FrameTimestamp $Frame
    if ($null -eq $frameAt) { $frameAt = Get-DateTimeOffsetOrNull (Get-Field $Entity @('ObservedAt', 'observedAt')) }
    if ($null -ne $locationAt -and $null -ne $frameAt -and
        (($locationAt -gt $frameAt) -or
         (($frameAt - $locationAt).TotalSeconds -gt 2))) { return $false }
    if ($kind -eq 'ai') {
        return -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesId $Entity)) -and
            -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesName $Entity))
    }
    if ($kind -eq 'player') {
        if (Get-EntityProvisional $Entity) {
            return -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesId $Entity)) -and
                -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesName $Entity))
        }
        # A verified Iris actor can legitimately arrive without a player name.
        # Identity handles are authoritative; names are presentation metadata.
        return -not [string]::IsNullOrWhiteSpace((Get-EntityName $Entity)) -or
            (Get-EntityHandle $Entity @('ActorNetRefHandle', 'actorNetRefHandle')) -gt 0 -or
            (Get-EntityHandle $Entity @('PlayerStateNetRefHandle', 'playerStateNetRefHandle')) -gt 0 -or
            (Get-EntityHandle $Entity @('PawnNetRefHandle', 'pawnNetRefHandle')) -gt 0
    }
    return $false
}

function Get-EntityRejectionReason($Entity, $Frame) {
    $trackId = Get-Field $Entity @('TrackId', 'trackId', 'Id', 'id')
    $location = Get-Field $Entity @('Location', 'location', 'Position', 'position')
    if ($null -eq $Entity -or $null -eq $trackId -or [long]$trackId -le 0) { return 'InvalidTrackId' }
    if ($null -eq $location -or $null -eq $location.X -or $null -eq $location.Y) { return 'InvalidCoordinate' }
    $locationAt = Get-DateTimeOffsetOrNull $Entity.LocationObservedAt
    $frameAt = Get-FrameTimestamp $Frame
    if ($null -eq $frameAt) { $frameAt = Get-DateTimeOffsetOrNull (Get-Field $Entity @('ObservedAt', 'observedAt')) }
    if ($null -ne $locationAt -and $null -ne $frameAt -and ($frameAt - $locationAt).TotalSeconds -gt 2) { return 'StaleLocation' }
    $kind = Get-EntityKind $Entity
    if ($kind -eq 'ai' -and ([string]::IsNullOrWhiteSpace((Get-EntitySpeciesId $Entity)) -or [string]::IsNullOrWhiteSpace((Get-EntitySpeciesName $Entity)))) { return 'MissingSpecies' }
    if ($kind -eq 'player' -and -not (Get-EntityProvisional $Entity) -and
        [string]::IsNullOrWhiteSpace((Get-EntityName $Entity)) -and
        (Get-EntityHandle $Entity @('ActorNetRefHandle', 'actorNetRefHandle')) -eq 0 -and
        (Get-EntityHandle $Entity @('PlayerStateNetRefHandle', 'playerStateNetRefHandle')) -eq 0 -and
        (Get-EntityHandle $Entity @('PawnNetRefHandle', 'pawnNetRefHandle')) -eq 0) { return 'MissingPlayerProof' }
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
    # Replay/analyze is deliberately forbidden from reading live append-only
    # files. A session is evidence only after capture has copied a closed raw
    # snapshot into raw-capture. This prevents frames from a later session
    # leaking into an earlier result.
    $agent = @(Read-JsonLines $agentPath)
    $map = @(Read-JsonLines $mapPath)
    # Older captures only contain RemoteEntities, which is Agent output and
    # not proof that the host renderer received/rendered the entity. Do not
    # turn that schema gap into a false missing-marker failure.
    $stageEvidenceAvailable = @($agent | Where-Object {
        $null -ne $_.evidence -or
        $null -ne $_.ongoingCandidates -or
        $null -ne $_.mapOutputPlayers -or
        $null -ne $_.mapOutputAi
    }).Count -gt 0
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
        MovementOnlyNoPlayerProof = 0
        BlockedFusionOrValidation = 0
        PublishedStructuralAnonymous = 0
    }
    $candidateDiagnostics = [ordered]@{
        TotalObservations = 0
        DistinctHandles = 0
        AlreadyVerifiedOrGraphProvisional = 0
        PublishedExactActorSpecies = 0
        PublishedStructuralAnonymousPlayer = 0
        PublishedIslePilotCorroborated = 0
        MovementOnlyNoPlayerProof = 0
        BlockedByFusionOrValidation = 0
        HandlesLaterRenderedAsPlayer = @()
        LateProofUpgradedHandles = @()
    }
    $locationAges = [System.Collections.Generic.List[double]]::new()
    $locationAgeGt2s = 0
    $locationAgeGt6s = 0
    $locationAgeGt15s = 0
    $maxLocationAgeMs = 0d
    $maxQueueDroppedPackets = 0L
    $maxQueueDepth = 0
    $staleMarkerRows = 0
    $candidateHandlesByDecision = @{}
    $allCandidateHandles = [System.Collections.Generic.HashSet[string]]::new()
    $candidateRenderedPlayerHandles = [System.Collections.Generic.HashSet[string]]::new()
    $hasStageEvidence = $false
    foreach ($frame in $agent) {
        $frameAt = Get-FrameTimestamp $frame
        if ($null -ne $frame.PlayerSync) {
            $dropped = [long](Get-Field $frame.PlayerSync @('QueueDroppedPackets', 'queueDroppedPackets'))
            $depth = [int](Get-Field $frame.PlayerSync @('QueueDepth', 'queueDepth'))
            if ($dropped -gt $maxQueueDroppedPackets) { $maxQueueDroppedPackets = $dropped }
            if ($depth -gt $maxQueueDepth) { $maxQueueDepth = $depth }
        }
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
                $decision = [string]$candidate.decision
                $candidateDiagnostics.TotalObservations++
                if (-not $candidateHandlesByDecision.ContainsKey($decision)) {
                    $candidateHandlesByDecision[$decision] =
                        [System.Collections.Generic.HashSet[string]]::new()
                }
                $null = $candidateHandlesByDecision[$decision].Add(
                    [string]$candidate.actorHandle)
                $null = $allCandidateHandles.Add([string]$candidate.actorHandle)
                if ([string]$candidate.decision -in @('blocked-no-exact-player-proof', 'movement-only-no-player-proof')) { $stageCounters.BlockedNoProof++ }
                if ([string]$candidate.decision -eq 'movement-only-no-player-proof') { $stageCounters.MovementOnlyNoPlayerProof++ }
                if ([string]$candidate.decision -eq 'blocked-by-fusion-or-validation') { $stageCounters.BlockedFusionOrValidation++ }
                if ([string]$candidate.decision -eq 'published-structural-anonymous-player') { $stageCounters.PublishedStructuralAnonymous++ }
            }
        }
        foreach ($entity in $entities) {
            $locationAt = Get-DateTimeOffsetOrNull (Get-Field $entity @('LocationObservedAt', 'locationObservedAt'))
            if ($null -ne $locationAt -and $null -ne $frameAt -and $frameAt -ge $locationAt) {
                $ageMs = ($frameAt - $locationAt).TotalMilliseconds
                $locationAges.Add([double]$ageMs)
                if ($ageMs -gt 2000) { $locationAgeGt2s++ }
                if ($ageMs -gt 6000) { $locationAgeGt6s++ }
                if ($ageMs -gt 15000) { $locationAgeGt15s++ }
                if ($ageMs -gt $maxLocationAgeMs) { $maxLocationAgeMs = $ageMs }
            }
            $eligible = Test-EligibleEntity $entity $frame
            $key = Get-EntityKey $entity $endpoint $seenByEndpoint[$endpoint]
            $render = $null
            if ($eligible) {
                # Rendered marker keys intentionally carry a visual namespace
                # (currently `steam:pro-entity:...#slot`).  Ground truth keys
                # must not depend on that namespace or a provisional slot: the
                # stable identity for replay matching is entity kind + TrackId.
                # The previous prefix check assumed the key started directly
                # with `pro-entity`, so every valid marker was falsely reported
                # as missing when the runtime added the `steam:` namespace.
                $kindName = Get-EntityKind $entity
                $trackIdValue = Get-Field $entity @('TrackId', 'trackId', 'Id', 'id')
                $trackIdText = [regex]::Escape([string]$trackIdValue)
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
            $state = if (-not $eligible) { 'Rejected' }
                     elseif (-not $stageEvidenceAvailable) { 'ObservedBeforeRender' }
                     elseif ($renderedNow) { 'Visible' }
                     else { 'TemporarilyMissing' }
            $line = [pscustomobject]@{
                ReceivedAt = $frameAt
                Sequence = $frame.Sequence
                Key = $key
                TrackId = Get-Field $entity @('TrackId', 'trackId', 'Id', 'id')
                Kind = Get-EntityKind $entity
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
                if ($stageEvidenceAvailable) {
                    if ($render) { $renderLatencies.Add([double]$line.LatencyMs) } else { $missing.Add($line) }
                }
            }
            if ($render -and $frameAt) {
                $receivedAt = Get-DateTimeOffsetOrNull $frame.ReceivedAt
                $observedAt = Get-DateTimeOffsetOrNull $frame.ObservedAt
                if ($receivedAt -and $observedAt) { $captureDecodeLatencies.Add([Math]::Max(0, ($receivedAt - $observedAt).TotalMilliseconds)) }
                $agentUiLatencies.Add([Math]::Max(0, ($render.At - $frameAt).TotalMilliseconds))
            }
            $entityStates[$key] = $state
        }
        foreach ($entity in $entities | Where-Object {
            (Get-EntityKind $_) -eq 'player'
        }) {
            $trackId = Get-Field $entity @('TrackId', 'trackId', 'Id', 'id')
            if ($null -ne $trackId) {
                $null = $candidateRenderedPlayerHandles.Add([string]$trackId)
            }
        }
    }
    $candidateDiagnostics.DistinctHandles = $allCandidateHandles.Count
    foreach ($decision in @('already-verified-or-graph-provisional',
                            'published-exact-player-species',
                            'published-exact-actor-species',
                            'published-structural-anonymous-player',
                            'published-islepilot-corroborated',
                            'movement-only-no-player-proof',
                            'blocked-no-exact-player-proof',
                            'blocked-by-fusion-or-validation')) {
        $count = if ($candidateHandlesByDecision.ContainsKey($decision)) {
            $candidateHandlesByDecision[$decision].Count
        } else { 0 }
        switch ($decision) {
            'already-verified-or-graph-provisional' { $candidateDiagnostics.AlreadyVerifiedOrGraphProvisional = $count }
            'published-exact-player-species' { $candidateDiagnostics.PublishedExactActorSpecies += $count }
            'published-exact-actor-species' { $candidateDiagnostics.PublishedExactActorSpecies += $count }
            'published-structural-anonymous-player' { $candidateDiagnostics.PublishedStructuralAnonymousPlayer = $count }
            'published-islepilot-corroborated' { $candidateDiagnostics.PublishedIslePilotCorroborated = $count }
            'movement-only-no-player-proof' { $candidateDiagnostics.MovementOnlyNoPlayerProof = $count }
            'blocked-no-exact-player-proof' { $candidateDiagnostics.MovementOnlyNoPlayerProof += $count }
            'blocked-by-fusion-or-validation' { $candidateDiagnostics.BlockedByFusionOrValidation = $count }
        }
    }
    $candidateDiagnostics.HandlesLaterRenderedAsPlayer = @(
        $allCandidateHandles |
            Where-Object { $candidateRenderedPlayerHandles.Contains([string]$_) }
    )
    $movementOnlyHandles = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($decision in @('movement-only-no-player-proof', 'blocked-no-exact-player-proof')) {
        if ($candidateHandlesByDecision.ContainsKey($decision)) {
            foreach ($handle in $candidateHandlesByDecision[$decision]) {
                $null = $movementOnlyHandles.Add([string]$handle)
            }
        }
    }
    $candidateDiagnostics.LateProofUpgradedHandles = @(
        $movementOnlyHandles |
            Where-Object { $candidateRenderedPlayerHandles.Contains([string]$_) }
    )
    # Ground truth is an immutable replay artifact.  Re-running `analyze`,
    # `replay`, or `fix-loop` must not rewrite/duplicate a multi-megabyte
    # capture, especially while the Agent capture is being retained for
    # autonomous debugging.  A missing file is written once; subsequent
    # passes consume the existing snapshot.
    $groundTruthPath = Join-Path $Path 'replay-ground-truth.jsonl'
    if (-not (Test-Path -LiteralPath $groundTruthPath)) {
        $groundTruth | ForEach-Object { $_ | ConvertTo-Json -Depth 16 -Compress } |
            Set-Content -LiteralPath $groundTruthPath -Encoding UTF8
    }
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
        $staleMarkerRows += [int](Get-Field $diagnostic @('StaleCount', 'staleCount'))
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
        LocationAgeSamples = $locationAges.Count
        LocationAgeGt2s = $locationAgeGt2s
        LocationAgeGt6s = $locationAgeGt6s
        LocationAgeGt15s = $locationAgeGt15s
        P50LocationAgeMs = Get-Percentile $locationAges.ToArray() 0.50
        P95LocationAgeMs = Get-Percentile $locationAges.ToArray() 0.95
        MaxLocationAgeMs = $maxLocationAgeMs
        MaxQueueDroppedPackets = $maxQueueDroppedPackets
        MaxQueueDepth = $maxQueueDepth
        StaleMarkerRows = $staleMarkerRows
        MissingEntities = $missing
        GroundTruthPath = $groundTruthPath
        StageEvidenceAvailable = $hasStageEvidence -or $stageEvidenceAvailable
        StageCounters = $stageCounters
        CandidateDiagnostics = $candidateDiagnostics
        Status = if ($agent.Count -eq 0 -or $renderRows.Count -eq 0) { 'NEED_DEVELOPER' } elseif (-not $hasStageEvidence) { 'NEED_STAGE_EVIDENCE' } elseif ($missing.Count -gt 0) { 'FAIL' } else { 'PASS' }
        MissingMarkerProof = if (-not $hasStageEvidence) { 'Capture predates stage-level recorder schema; no claim about audio/candidate loss is allowed.' } elseif ($missing.Count -gt 0) { 'Eligible entity has no matching marker in render log.' } else { 'Every eligible replay entity has a marker in the observed render window.' }
    }
    Write-JsonFile (Join-Path $Path 'tracking-analysis.json') $result
    return $result
}

function Invoke-Replay([string]$Path) {
    # Replay must consume the immutable snapshot only. Never fall back to a
    # file that the running Agent may still be appending to.
    $agentPath = Join-Path $Path 'raw-capture\agent-live-compare.jsonl'
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
        '# KẾT QUẢ TRACKING LOOP',
        "- Phiên: $($a.SessionId)",
        "- Frame Agent: $($a.AgentFrames)",
        "- Dòng render: $($a.MapRenderRows)",
        "- Marker mẫu: $($a.RenderedMarkerSamples)",
        "- Marker key duy nhất: $($a.DistinctRenderedMarkerKeys)",
        "- Sequence gap: $($a.SequenceGapEstimate)",
        "- UI queue delay tối đa: $($a.MaxUiQueueDelayMs) ms",
        "- Snapshot age tối đa: $($a.MaxSnapshotAgeMs) ms",
        "- Dòng Pro tracking hoạt động: $($a.ProTrackingActiveRows)",
        "- Quan sát entity hợp lệ: $($a.EligibleEntityObservations)",
        "- Quan sát đã render: $($a.RenderedEligibleObservations)",
        "- Quan sát thiếu marker: $($a.MissingEligibleObservations)",
        "- Quan sát bị loại: $($a.RejectedEntityObservations)",
        "- Có stage evidence: $($a.StageEvidenceAvailable)",
        "- Stage counters: $(($a.StageCounters | ConvertTo-Json -Compress) -replace "`r?`n", '')",
        "- Candidate diagnostics: $(($a.CandidateDiagnostics | ConvertTo-Json -Compress) -replace "`r?`n", '')",
        "- P50 latency marker: $($a.P50MarkerLatencyMs) ms",
        "- P95 latency marker: $($a.P95MarkerLatencyMs) ms",
        "- P50 capture → decode: $($a.P50CaptureDecodeLatencyMs) ms",
        "- P95 capture → decode: $($a.P95CaptureDecodeLatencyMs) ms",
        "- P50 Agent → UI: $($a.P50AgentToUiLatencyMs) ms",
        "- P95 Agent → UI: $($a.P95AgentToUiLatencyMs) ms",
        "- Tuổi tọa độ: p50 $($a.P50LocationAgeMs) ms · p95 $($a.P95LocationAgeMs) ms · tối đa $($a.MaxLocationAgeMs) ms",
        "- Tọa độ cũ hơn 2/6/15 giây: $($a.LocationAgeGt2s) / $($a.LocationAgeGt6s) / $($a.LocationAgeGt15s)",
        "- Queue Agent: drop tối đa $($a.MaxQueueDroppedPackets) · depth tối đa $($a.MaxQueueDepth) · stale marker rows $($a.StaleMarkerRows)",
        "- Ground truth: $($a.GroundTruthPath)",
        "- Trạng thái: $($a.Status)",
        '',
        $a.MissingMarkerProof
    )
    $reportPath = Join-Path $Path 'tracking-report.md'
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host ($lines -join [Environment]::NewLine)
}

function Invoke-StaleAgeReport([string]$Path) {
    # This diagnostic deliberately streams JSONL. The live capture can exceed
    # 100 MB and must not be loaded into one PowerShell object graph.
    $agentPath = Join-Path $Path 'raw-capture\agent-live-compare.jsonl'
    $mapPath = Join-Path $Path 'raw-capture\map-diagnostics.jsonl'
    $ages = [System.Collections.Generic.List[double]]::new()
    $frames = 0
    $entities = 0
    $players = 0
    $ai = 0
    $gt2 = 0
    $gt6 = 0
    $gt15 = 0
    $maxAge = 0d
    $maxDrop = 0L
    $maxDepth = 0
    $reader = if (Test-Path -LiteralPath $agentPath) { [System.IO.StreamReader]::new($agentPath) } else { $null }
    try {
        while ($null -ne $reader -and $null -ne ($line = $reader.ReadLine())) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $frame = $line | ConvertFrom-Json -Depth 32 } catch { continue }
            if ($null -eq $frame.frameObservedAt -and $null -eq $frame.receivedAt) { continue }
            $frames++
            $frameAt = Get-FrameTimestamp $frame
            if ($frame.PlayerSync) {
                $drop = [long](Get-Field $frame.PlayerSync @('QueueDroppedPackets','queueDroppedPackets'))
                $depth = [int](Get-Field $frame.PlayerSync @('QueueDepth','queueDepth'))
                if ($drop -gt $maxDrop) { $maxDrop = $drop }
                if ($depth -gt $maxDepth) { $maxDepth = $depth }
            }
            foreach ($entity in @($frame.RemoteEntities)) {
                $entities++
                if ((Get-EntityKind $entity) -eq 'player') { $players++ } else { $ai++ }
                $locationAt = Get-DateTimeOffsetOrNull (Get-Field $entity @('LocationObservedAt','locationObservedAt'))
                if ($null -eq $locationAt -or $null -eq $frameAt -or $frameAt -lt $locationAt) { continue }
                $age = ($frameAt - $locationAt).TotalMilliseconds
                $ages.Add([double]$age)
                if ($age -gt 2000) { $gt2++ }
                if ($age -gt 6000) { $gt6++ }
                if ($age -gt 15000) { $gt15++ }
                if ($age -gt $maxAge) { $maxAge = $age }
            }
        }
    } finally { if ($reader) { $reader.Dispose() } }
    $renderRows = 0
    $staleRows = 0
    $maxUiDelay = 0d
    $maxSnapshotAge = 0d
    $mapReader = if (Test-Path -LiteralPath $mapPath) { [System.IO.StreamReader]::new($mapPath) } else { $null }
    try {
        while ($null -ne $mapReader -and $null -ne ($line = $mapReader.ReadLine())) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $row = $line | ConvertFrom-Json -Depth 32 } catch { continue }
            if ($row.stage -ne 'render-end') { continue }
            $renderRows++
            $staleRows += [int](Get-Field $row.ProTrackingDiagnostics @('StaleCount','staleCount'))
            $ui = [double]$row.UiQueueDelayMs
            $snap = [double]$row.SnapshotAgeMs
            if ($ui -gt $maxUiDelay) { $maxUiDelay = $ui }
            if ($snap -gt $maxSnapshotAge) { $maxSnapshotAge = $snap }
        }
    } finally { if ($mapReader) { $mapReader.Dispose() } }
    $ordered = @($ages | Sort-Object)
    $p = { param([double]$q) if ($ordered.Count -eq 0) { return $null }; return $ordered[[Math]::Max(0,[Math]::Min($ordered.Count - 1,[Math]::Ceiling($ordered.Count * $q)-1))] }
    $result = [pscustomobject]@{
        SessionId = Split-Path $Path -Leaf
        GeneratedAt = [DateTimeOffset]::UtcNow
        AgentFrames = $frames
        Entities = $entities
        Players = $players
        Ai = $ai
        LocationAgeSamples = $ages.Count
        LocationAgeGt2s = $gt2
        LocationAgeGt6s = $gt6
        LocationAgeGt15s = $gt15
        P50LocationAgeMs = & $p 0.5
        P95LocationAgeMs = & $p 0.95
        MaxLocationAgeMs = $maxAge
        RenderRows = $renderRows
        StaleMarkerRows = $staleRows
        MaxUiQueueDelayMs = $maxUiDelay
        MaxSnapshotAgeMs = $maxSnapshotAge
        MaxQueueDroppedPackets = $maxDrop
        MaxQueueDepth = $maxDepth
        RawCapturePresent = (Test-Path -LiteralPath $agentPath)
    }
    $output = Join-Path $Path 'stale-age-analysis.json'
    Write-JsonFile $output $result
    $result | ConvertTo-Json -Depth 8
    return $result
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
    $acceptancePath = Join-Path (Split-Path $Path -Parent) 'round2-acceptance.json'
    $roundAcceptancePassed = $false
    if (Test-Path -LiteralPath $acceptancePath) {
        $acceptance = Get-Content -LiteralPath $acceptancePath -Raw | ConvertFrom-Json
        $roundAcceptancePassed = $acceptance.Status -eq 'PASS' -and
            @($acceptance.Sessions | Where-Object { $_.Session -eq (Split-Path $Path -Leaf) -and $_.Status -eq 'PASS' }).Count -eq 1
    }
    $state = [pscustomobject]@{
        UpdatedAt = [DateTimeOffset]::UtcNow
        Iteration = 1
        BaselineStatus = $Analysis.Status
        RootCause = $rootCause
        FixturePath = $FixturePath
        ReplayStatus = $Analysis.Status
        RequiredNextAction = if ($roundAcceptancePassed) {
            'Autonomous live validation and replay acceptance gate đã hoàn tất; developer review commit trước khi publish.'
        } elseif ($Analysis.Status -eq 'NEED_DEVELOPER') {
            'Developer must provide a live game/Pro/Npcap capture.'
        } elseif ($Analysis.Status -eq 'FAIL') {
            'Create one regression test for RootCause, patch one cause, replay the same raw capture, then run three live smoke sessions.'
        } else {
            'Run three live smoke sessions before committing a fix.'
        }
        CommitAllowed = $roundAcceptancePassed
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
    Enter-RoundLock
    try {
    if ($DurationMinutes -ne 5) {
        Write-Warning "Autonomous acceptance round requires exactly 5 minutes per session; overriding DurationMinutes=$DurationMinutes to 5."
    }
    $roundDurationMinutes = 5
    $script:DurationMinutes = $roundDurationMinutes
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
        $manifest | Add-Member -NotePropertyName RequiredDurationMinutes -NotePropertyValue $roundDurationMinutes -Force
        Write-JsonFile $manifestPath $manifest
        if (-not (Invoke-OpenMap $path)) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_DEVELOPER' -Force
            Write-JsonFile $manifestPath $manifest
            continue
        }
        # New-Session reads the script parameter. Set the session manifest
        # explicitly and keep the round contract visible even when this script
        # is invoked with an accidental custom duration.
        try {
            Invoke-Capture $path
            Invoke-Analyze $path | Out-Null
            Invoke-Report $path
        }
        finally {
            # Every round session owns its launcher.  Closing it here prevents
            # later sessions from attaching to an older map/Agent and mixing
            # diagnostics or packets across session boundaries.
            Stop-HarnessLauncher $path
        }
    }
    Remove-PassedRawArtifacts
    $roundAnalyses = foreach ($mode in $modes) {
        $analysisPath = Join-Path $SessionRoot "$round-$mode\tracking-analysis.json"
        if (Test-Path -LiteralPath $analysisPath) {
            Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
        }
    }
    $roundStatus = if ($roundAnalyses.Count -ne 3) {
        'NEED_DEVELOPER'
    } elseif (@($roundAnalyses | Where-Object { $_.Status -ne 'PASS' }).Count -gt 0) {
        if (@($roundAnalyses | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0) { 'FAIL' } else { 'NEED_DEVELOPER' }
    } else {
        'PASS'
    }
    Write-JsonFile (Join-Path $roundRoot 'round-analysis.json') ([pscustomobject]@{
        Round = $round
        AnalyzedAt = [DateTimeOffset]::UtcNow
        RequiredSessions = $modes
        RequiredDurationMinutes = $roundDurationMinutes
        CompletedSessions = @($roundAnalyses | ForEach-Object SessionId)
        SessionStatuses = @($roundAnalyses | ForEach-Object {
            [pscustomobject]@{
                SessionId = $_.SessionId
                Status = $_.Status
                AgentFrames = $_.AgentFrames
                RenderRows = $_.MapRenderRows
                StageEvidenceAvailable = $_.StageEvidenceAvailable
                RawSnapshotPresent = Test-Path (Join-Path $SessionRoot "$($_.SessionId)\raw-capture\agent-live-compare.jsonl")
            }
        })
        Status = $roundStatus
        Acceptance = if ($roundStatus -eq 'PASS') { 'All three live sessions passed with stage evidence.' } else { 'Three live sessions with real game/Agent evidence are required.' }
    })
    Write-Host "Đã hoàn tất round 3 session: $round · Status: $roundStatus"
    } finally {
        Exit-RoundLock
    }
}

switch ($Command) {
    'start-session' { $null = New-Session; break }
    'preflight' {
        $path = Resolve-Session
        Write-Preflight $path | Format-List
        break
    }
    'open-map' {
        $path = Resolve-Session
        $null = Invoke-OpenMap $path
        break
    }
    'capture' { Invoke-Capture (Resolve-Session); break }
    'run-round' { Invoke-Round; break }
    'replay' { Invoke-Replay (Resolve-Session) | Format-List; break }
    'analyze' { Invoke-Analyze (Resolve-Session) | Format-List; break }
    'stale-report' { Invoke-StaleAgeReport (Resolve-Session) | Format-List; break }
    'report' { Invoke-Report (Resolve-Session); break }
    'fix-loop' {
        $path = Resolve-Session
        # A completed replay is immutable evidence.  The autonomous loop may
        # be resumed after a process interruption without parsing the raw
        # capture again (which can be hundreds of MB and can exhaust the
        # diagnostic volume).  Only replay when the analysis or ground truth
        # artifact is genuinely missing.
        $analysisPath = Join-Path $path 'tracking-analysis.json'
        $groundTruthPath = Join-Path $path 'replay-ground-truth.jsonl'
        if ((Test-Path -LiteralPath $analysisPath) -and (Test-Path -LiteralPath $groundTruthPath)) {
            $analysis = Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
        } else {
            $analysis = Invoke-Replay $path
        }
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
