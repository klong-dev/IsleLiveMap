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
    [string]$ProLocalReleaseManifest,
    [string]$RawCaptureExecutable
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

function Get-Sha256OrNull([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Copy-ClosedEvidence([string]$Source, [string]$Destination) {
    # Fail if any producer still owns a write handle. CreateNew also protects
    # earlier evidence from a retry of the capture command.
    $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream); $outputStream.Flush($true) }
        finally { $outputStream.Dispose() }
    } finally { $inputStream.Dispose() }
    [pscustomobject]@{
        Bytes = (Get-Item -LiteralPath $Destination).Length
        Sha256 = Get-Sha256OrNull $Destination
    }
}

function Get-BinaryFileInventory([string]$Executable) {
    if ([string]::IsNullOrWhiteSpace($Executable) -or -not (Test-Path -LiteralPath $Executable -PathType Leaf)) { return @() }
    @(Get-ChildItem -LiteralPath (Split-Path $Executable -Parent) -File -Recurse |
        Where-Object { $_.Extension -in @('.exe', '.dll', '.json') } |
        Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{ Path = $_.FullName; Bytes = $_.Length; Sha256 = Get-Sha256OrNull $_.FullName }
        })
}

function Get-BinaryManifest([string]$SessionPath) {
    $hostPath = if ([string]::IsNullOrWhiteSpace($LauncherPath)) {
        Join-Path $env:LOCALAPPDATA 'IsleLiveMap\current\IsleLiveMap.exe'
    } else {
        [System.IO.Path]::GetFullPath($LauncherPath)
    }
    $agentDescriptor = Join-Path $env:LOCALAPPDATA 'KLongDev\IsleLiveMap\Pro\current.json'
    $agentVersion = $null
    $agentPath = $null
    if (Test-Path -LiteralPath $agentDescriptor -PathType Leaf) {
        try {
            $descriptor = Get-Content -LiteralPath $agentDescriptor -Raw | ConvertFrom-Json
            $agentVersion = [string]$descriptor.version
            if (-not [string]::IsNullOrWhiteSpace($agentVersion)) {
                $agentPath = Join-Path $env:LOCALAPPDATA "KLongDev\IsleLiveMap\Pro\versions\$agentVersion\IsleLiveMap.Pro.Agent.exe"
            }
        } catch { }
    }
    [pscustomobject]@{
        SchemaVersion = 1
        CapturedAt = [DateTimeOffset]::UtcNow
        Host = [pscustomobject]@{
            Path = $hostPath
            Version = if (Test-Path -LiteralPath $hostPath) { (Get-Item $hostPath).VersionInfo.ProductVersion } else { $null }
            Sha256 = Get-Sha256OrNull $hostPath
            Files = @(Get-BinaryFileInventory $hostPath)
            Commit = $null
            CommitEvidence = 'Unknown: checkout HEAD is not proof of the binary build commit.'
        }
        Agent = [pscustomobject]@{
            Path = $agentPath
            Version = $agentVersion
            Sha256 = Get-Sha256OrNull $agentPath
            IpcApiMajor = 2
            Files = @(Get-BinaryFileInventory $agentPath)
            Commit = $null
        }
        Npcap = [pscustomobject]@{
            Library = @(
                (Join-Path $env:SystemRoot 'System32\wpcap.dll'),
                (Join-Path $env:SystemRoot 'SysWOW64\wpcap.dll')
            ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
        SessionPath = [System.IO.Path]::GetFullPath($SessionPath)
        EvidenceScope = 'preflight-installed-not-runtime'
    }
}

function Get-RuntimeBinaryManifest([string]$SessionPath, [int]$LauncherProcessId) {
    $processes = @(Get-CimInstance Win32_Process)
    $hostProcess = @($processes | Where-Object { $_.ProcessId -eq $LauncherProcessId })
    $agents = @($processes | Where-Object {
        $_.ParentProcessId -eq $LauncherProcessId -and $_.Name -eq 'IsleLiveMap.Pro.Agent.exe'
    })
    if ($hostProcess.Count -ne 1 -or $agents.Count -ne 1) {
        throw 'NEED_STAGE_EVIDENCE: expected exactly one session-owned launcher and child Agent.'
    }
    $hostExecutable = [string]$hostProcess[0].ExecutablePath
    $agentExecutable = [string]$agents[0].ExecutablePath
    if ([string]::IsNullOrWhiteSpace($hostExecutable) -or [string]::IsNullOrWhiteSpace($agentExecutable)) {
        throw 'NEED_STAGE_EVIDENCE: runtime executable paths could not be read.'
    }
    [pscustomobject]@{
        SchemaVersion = 2
        CapturedAt = [DateTimeOffset]::UtcNow
        EvidenceScope = 'runtime-process-path-and-on-disk-hashes'
        SessionPath = [IO.Path]::GetFullPath($SessionPath)
        Host = [pscustomobject]@{
            Pid = $LauncherProcessId; Path = $hostExecutable
            Version = (Get-Item -LiteralPath $hostExecutable).VersionInfo.ProductVersion
            Sha256 = Get-Sha256OrNull $hostExecutable
            Files = @(Get-BinaryFileInventory $hostExecutable)
            Commit = $null
        }
        Agent = [pscustomobject]@{
            Pid = $agents[0].ProcessId; Path = $agentExecutable
            Version = (Get-Item -LiteralPath $agentExecutable).VersionInfo.ProductVersion
            Sha256 = Get-Sha256OrNull $agentExecutable
            Files = @(Get-BinaryFileInventory $agentExecutable)
            Commit = $null
        }
    }
}

function Get-RawCaptureExecutable {
    if (-not [string]::IsNullOrWhiteSpace($RawCaptureExecutable)) {
        return [System.IO.Path]::GetFullPath($RawCaptureExecutable)
    }
    $proRoot = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'IsleLiveMap-Pro'
    $candidate = Join-Path $proRoot 'tools\IsleLiveMap.Pro.LiveCapture\bin\Release\net8.0\IsleLiveMap.Pro.LiveCapture.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    return $null
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
    $gameServerEndpoint = $null
    if ($null -ne $game) {
        $gameServerEndpoint = Get-NetUDPEndpoint -OwningProcess $game.Id -ErrorAction SilentlyContinue |
            Where-Object {
                $_.RemoteAddress -and
                $_.RemoteAddress -notin @('0.0.0.0', '::') -and
                $_.RemotePort -gt 0
            } |
            Select-Object -First 1
    }
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
        GameServerConnected = $null -ne $gameServerEndpoint
        GameServerRemoteAddress = if ($gameServerEndpoint) { $gameServerEndpoint.RemoteAddress } else { $null }
        GameServerRemotePort = if ($gameServerEndpoint) { $gameServerEndpoint.RemotePort } else { $null }
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
    if (-not $full.GameServerConnected) { $null = $missing.Add('GAME_SERVER_NOT_CONNECTED') }
    if (-not $full.NpcapLibraryFound) { $null = $missing.Add('NPCAP_NOT_FOUND') }
    if (-not $full.ProCredentialFileFound) { $null = $missing.Add('PRO_CREDENTIAL_NOT_FOUND') }
    if (-not $full.ProAgentExecutableFound) { $null = $missing.Add('PRO_AGENT_NOT_FOUND') }
    [pscustomobject]@{
        CheckedAt = $full.CheckedAt
        Ready = [bool]($full.GameServerConnected -and
                 $full.NpcapLibraryFound -and
                 $full.ProCredentialFileFound -and
                 $full.ProAgentExecutableFound)
        GameProcessFound = $full.GameProcessFound
        GamePid = $full.GamePid
        GameServerConnected = $full.GameServerConnected
        GameServerRemoteAddress = $full.GameServerRemoteAddress
        GameServerRemotePort = $full.GameServerRemotePort
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
    if (Test-Path -LiteralPath $path) { throw "Session already exists; refusing to overwrite: $path" }
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
    Write-JsonFile (Join-Path $path 'binary-preflight.json') (Get-BinaryManifest $path)
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
    $info.EnvironmentVariables['ISLELIVEMAP_PRO_HOST_COMPARE_PATH'] = Join-Path $Path 'host-ipc-compare.jsonl'
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
    # The Isle uses an unconnected UDP socket, so Get-NetUDPEndpoint cannot
    # reliably prove the remote server connection. The Agent's matched packet
    # count/last packet/server endpoint are authoritative after map startup;
    # do not block UI automation on the old socket probe.
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
    $env:ISLELIVEMAP_PRO_HOST_COMPARE_PATH = Join-Path $Path 'host-ipc-compare.jsonl'
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
    # New-Session probes before the launcher has spawned the Agent. That
    # snapshot can therefore contain the previous installed version (or no
    # Agent at all). The runtime probe after open-map is authoritative for
    # this capture. Only compare it with an explicitly requested signed local
    # release; never compare it with the stale startup snapshot.
    $requestedAgentVersion = $null
    if (-not [string]::IsNullOrWhiteSpace($ProLocalReleaseManifest) -and
        (Test-Path -LiteralPath $ProLocalReleaseManifest -PathType Leaf)) {
        try {
            $requestedAgentVersion = [string](Get-Content -LiteralPath $ProLocalReleaseManifest -Raw | ConvertFrom-Json).version
        } catch {
            $requestedAgentVersion = $null
        }
    }
    $manifest | Add-Member -NotePropertyName RuntimePreflight -NotePropertyValue $preflight -Force
    $manifest | Add-Member -NotePropertyName RuntimeAgentVersion -NotePropertyValue $preflight.ProAgentVersion -Force
    Write-JsonFile $manifestPath $manifest
    if (-not [string]::IsNullOrWhiteSpace($requestedAgentVersion) -and
        $requestedAgentVersion -ne [string]$preflight.ProAgentVersion) {
        $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_STAGE_EVIDENCE' -Force
        $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue (
            "Pro Agent version mismatch: requested $requestedAgentVersion, runtime reported $($preflight.ProAgentVersion).") -Force
        Write-JsonFile $manifestPath $manifest
        Write-Warning "Capture stopped: Pro Agent version mismatch ($expectedAgentVersion -> $($preflight.ProAgentVersion))."
        return
    }
    $baseRuntimeReady = $preflight.GameProcessFound -and
        $preflight.NpcapLibraryFound -and
        $preflight.ProCredentialFileFound -and
        $preflight.ProAgentExecutableFound
    if (-not $baseRuntimeReady) {
        $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_DEVELOPER' -Force
        $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue 'Preflight failed; no bypass.' -Force
        Write-JsonFile $manifestPath $manifest
        Write-Warning 'Capture stopped because live Pro preflight is not ready. Open Live Map to start the Agent, then retry capture.'
        return
    }
    $binaryManifestPath = Join-Path $Path 'binary-manifest.json'
    if (Test-Path -LiteralPath $binaryManifestPath) { throw 'Runtime manifest already exists; create a new capture session.' }
    $runtimeBinaries = Get-RuntimeBinaryManifest $Path ([int]$manifest.LauncherPid)
    if ($requestedAgentVersion -and ($runtimeBinaries.Agent.Version -split '\+')[0] -ne $requestedAgentVersion) {
        throw "NEED_STAGE_EVIDENCE: actual child Agent version $($runtimeBinaries.Agent.Version) differs from $requestedAgentVersion."
    }
    Write-JsonFile $binaryManifestPath $runtimeBinaries
    foreach ($name in @('agent-live-compare.jsonl', 'host-ipc-compare.jsonl', 'map-diagnostics.jsonl')) {
        $target = Join-Path $Path $name
        if (-not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType File -Force -Path $target | Out-Null
        }
    }
    $seconds = [Math]::Max(60, $DurationMinutes * 60)
    Write-Host "Capturing for $DurationMinutes minutes. No game input is sent."
    $rawCapturePath = Join-Path $Path 'raw-capture\udp-capture.bin'
    if (Test-Path -LiteralPath $rawCapturePath) { throw "Raw capture already exists: $rawCapturePath" }
    $captureExecutable = Get-RawCaptureExecutable
    $captureProcess = $null
    if ($null -ne $captureExecutable) {
        $captureProcess = Start-Process -FilePath $captureExecutable -ArgumentList @(('"' + $rawCapturePath + '"'), $seconds, 'inbound') -WorkingDirectory (Split-Path $captureExecutable) -WindowStyle Hidden -PassThru
        $manifest | Add-Member -NotePropertyName RawCaptureExecutable -NotePropertyValue $captureExecutable -Force
        $manifest | Add-Member -NotePropertyName RawCapturePid -NotePropertyValue $captureProcess.Id -Force
        $manifest | Add-Member -NotePropertyName RawCaptureStartedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
        Write-JsonFile $manifestPath $manifest
    } else {
        Write-Warning 'Raw UDP capture executable not found; session will be NEED_STAGE_EVIDENCE unless an external capture is supplied.'
    }
    $captureDeadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
    while ([DateTimeOffset]::UtcNow -lt $captureDeadline) {
        Start-Sleep -Seconds ([Math]::Min(10, [Math]::Max(1, [int]($captureDeadline - [DateTimeOffset]::UtcNow).TotalSeconds)))
    }
    if ($null -ne $captureProcess) {
        if (-not $captureProcess.WaitForExit(15000)) {
            throw "Raw capture is still running (PID $($captureProcess.Id)); do not freeze or replay its file."
        }
        if ($captureProcess.ExitCode -ne 0) { throw "Raw capture failed with exit code $($captureProcess.ExitCode)." }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest | Add-Member -NotePropertyName RawCaptureCompletedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
        $manifest | Add-Member -NotePropertyName RawCaptureSha256 -NotePropertyValue (Get-Sha256OrNull $rawCapturePath) -Force
        $rawCaptureBytes = if (Test-Path -LiteralPath $rawCapturePath) { (Get-Item $rawCapturePath).Length } else { 0 }
        $manifest | Add-Member -NotePropertyName RawCaptureBytes -NotePropertyValue $rawCaptureBytes -Force
        Write-JsonFile $manifestPath $manifest
    }
    # Stop the launcher owned by this session before freezing files. The Agent
    # and Host are separate writers and must not be copied while appending.
    Stop-HarnessLauncher $Path
    Start-Sleep -Milliseconds 750
    $manifestPath = Join-Path $Path 'session-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName CompletedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
    $manifest | Add-Member -NotePropertyName CaptureFiles -NotePropertyValue @(
        'agent-live-compare.jsonl', 'host-ipc-compare.jsonl', 'map-diagnostics.jsonl'
    ) -Force
    $snapshotIntegrity = [ordered]@{}
    foreach ($name in @('agent-live-compare.jsonl', 'host-ipc-compare.jsonl', 'map-diagnostics.jsonl')) {
        $source = Join-Path $Path $name
        if (Test-Path -LiteralPath $source) {
            $copyEvidence = Copy-ClosedEvidence $source (Join-Path $Path "raw-capture\$name")
            $snapshotIntegrity[$name] = Test-JsonlSnapshot (Join-Path $Path "raw-capture\$name")
            $snapshotIntegrity[$name] | Add-Member -NotePropertyName Sha256 -NotePropertyValue $copyEvidence.Sha256
            $snapshotIntegrity[$name] | Add-Member -NotePropertyName Bytes -NotePropertyValue $copyEvidence.Bytes
        } else {
            $snapshotIntegrity[$name] = [pscustomobject]@{
                Valid = $false
                LineCount = 0
                InvalidLine = $null
                Error = 'Snapshot file was not produced.'
            }
        }
    }
    $manifest | Add-Member -NotePropertyName RawSnapshotIntegrity -NotePropertyValue $snapshotIntegrity -Force
    $invalidSnapshots = @($snapshotIntegrity.GetEnumerator() | Where-Object { -not $_.Value.Valid })
    if ($invalidSnapshots.Count -gt 0) {
        $manifest | Add-Member -NotePropertyName Status -NotePropertyValue 'NEED_STAGE_EVIDENCE' -Force
        $manifest | Add-Member -NotePropertyName StoppedReason -NotePropertyValue (
            'Frozen JSONL snapshot is incomplete: ' +
            (($invalidSnapshots | ForEach-Object { $_.Key }) -join ', ') + '.') -Force
    }
    Write-JsonFile $manifestPath $manifest
    Write-Host "Capture complete: $Path"
}

function Test-JsonlSnapshot([string]$Path) {
    $lineCount = 0
    $reader = [System.IO.StreamReader]::new(
        $Path,
        [System.Text.Encoding]::UTF8,
        $true,
        1048576)
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $lineCount++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                if ($PSVersionTable.PSVersion.Major -ge 6) {
                    $null = ConvertFrom-Json -InputObject $line -DateKind String
                } else {
                    $null = ConvertFrom-Json -InputObject $line
                }
            } catch {
                return [pscustomobject]@{
                    Valid = $false
                    LineCount = $lineCount
                    InvalidLine = $lineCount
                    Error = $_.Exception.Message
                }
            }
        }
    } finally {
        $reader.Dispose()
    }
    return [pscustomobject]@{
        Valid = $true
        LineCount = $lineCount
        InvalidLine = $null
        Error = $null
    }
}

function Wait-ForTrackingRuntime([int]$TimeoutSeconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(5, $TimeoutSeconds))
    do {
        $preflight = Test-Preflight
        # The Pro Agent is a child of the overlay's telemetry source and is
        # therefore lazy: it may not exist until the map has subscribed to the
        # source. Executable/credential/Npcap plus a live game are the
        # authoritative preconditions; capture health and JSONL output prove
        # that the child actually connected. Do not report NEED_DEVELOPER just
        # because the lazy child has not spawned at the first poll.
        if ($preflight.GameProcessFound -and
            $preflight.GameServerConnected -and
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
        $openedAt = Get-DateTimeOffsetOrNull $manifest.OpenMapAt
        if ($null -eq $openedAt -or $launcher.StartTime.ToUniversalTime() -gt $openedAt.UtcDateTime) {
            throw 'Launcher ownership cannot be verified; refusing to close a reused PID.'
        }
        $children = @(Get-CimInstance Win32_Process | Where-Object {
            $_.ParentProcessId -eq $launcher.Id -and $_.Name -eq 'IsleLiveMap.Pro.Agent.exe'
        } | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
        if (-not $launcher.CloseMainWindow() -or -not $launcher.WaitForExit(15000)) {
            throw 'Launcher did not close gracefully; keep live evidence unfrozen.'
        }
        foreach ($child in $children) {
            if (-not $child.WaitForExit(15000)) {
                throw 'Session Agent is still running; keep live evidence unfrozen.'
            }
        }
    }
}

function Read-JsonLines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $items = [System.Collections.Generic.List[object]]::new()
    # Do not ReadAllLines here. A live session can contain hundreds of MB of
    # JSONL; materialising both the string[] and the object list caused replay
    # to spend many minutes in GC and made the harness look hung. Stream one
    # line at a time while retaining the existing object shape for callers.
    $reader = [System.IO.StreamReader]::new($Path, [System.Text.Encoding]::UTF8, $true, 1048576)
    $lineNumber = 0
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $lineNumber++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            # Preserve ISO timestamps as strings. PowerShell's default JSON
            # conversion truncates fractional seconds to whole seconds.
            try {
                if ($PSVersionTable.PSVersion.Major -ge 6) {
                    $value = ConvertFrom-Json -InputObject $line -DateKind String
                } else {
                    $value = ConvertFrom-Json -InputObject $line
                }
                $null = $items.Add($value)
            } catch {
                throw "NEED_STAGE_EVIDENCE: invalid JSONL at ${Path}:$lineNumber. Preserve the original capture; analysis cannot certify incomplete evidence."
            }
        }
    } finally {
        $reader.Dispose()
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
        $hasStructuralProof =
            (Get-EntityHandle $Entity @('ActorNetRefHandle', 'actorNetRefHandle')) -gt 0 -or
            (Get-EntityHandle $Entity @('PlayerStateNetRefHandle', 'playerStateNetRefHandle')) -gt 0 -or
            (Get-EntityHandle $Entity @('PawnNetRefHandle', 'pawnNetRefHandle')) -gt 0
        if (-not $hasStructuralProof) { return $false }
        if (Get-EntityProvisional $Entity) {
            return -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesId $Entity)) -and
                -not [string]::IsNullOrWhiteSpace((Get-EntitySpeciesName $Entity))
        }
        # A verified Iris actor can legitimately arrive without a player name.
        # Identity handles are authoritative; names are presentation metadata.
        return $true
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
    if ($kind -eq 'player' -and
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

function Get-WorldDistance($Left, $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $null }
    if ($null -eq $Left.X -or $null -eq $Left.Y -or
        $null -eq $Right.X -or $null -eq $Right.Y) { return $null }
    $zLeft = if ($null -eq $Left.Z) { 0d } else { [double]$Left.Z }
    $zRight = if ($null -eq $Right.Z) { 0d } else { [double]$Right.Z }
    $dx = [double]$Left.X - [double]$Right.X
    $dy = [double]$Left.Y - [double]$Right.Y
    $dz = $zLeft - $zRight
    return [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

function Get-TrackingSessionId($Value) {
    return [string](Get-Field $Value @('ProPlayerSessionId', 'SessionId'))
}

function Get-TrackingEndpoint($Value) {
    return [string](Get-Field $Value @('ProPlayerServerEndpoint', 'ServerEndpoint'))
}

function Test-TrackingSessionMatch($Frame, $RenderRow) {
    $session = Get-TrackingSessionId $Frame
    $otherSession = Get-TrackingSessionId $RenderRow
    $endpoint = Get-TrackingEndpoint $Frame
    $otherEndpoint = Get-TrackingEndpoint $RenderRow
    # Missing identity is missing evidence, never permission to match by name.
    return -not [string]::IsNullOrWhiteSpace($session) -and
        [string]::Equals($session, $otherSession, [StringComparison]::Ordinal) -and
        -not [string]::IsNullOrWhiteSpace($endpoint) -and
        -not [string]::IsNullOrWhiteSpace($otherEndpoint) -and
        [string]::Equals($endpoint.Trim(), $otherEndpoint.Trim(), [StringComparison]::OrdinalIgnoreCase)
}

function Get-RenderIdentityKey($Row, [string]$Kind, [string]$TrackId) {
    $session = Get-TrackingSessionId $Row
    $endpoint = Get-TrackingEndpoint $Row
    if ([string]::IsNullOrWhiteSpace($session) -or
        [string]::IsNullOrWhiteSpace($endpoint)) { return $null }
    return '{0}|{1}|{2}|{3}' -f $session, $endpoint.Trim().ToLowerInvariant(), $Kind.ToLowerInvariant(), $TrackId
}

function New-RenderIndex($Rows) {
    $index = @{}
    foreach ($row in $Rows) {
        $at = Get-DateTimeOffsetOrNull $row.ReceivedAt
        if ($null -eq $at) { continue }
        foreach ($marker in @($row.RenderedMarkers)) {
            $visualKey = Get-RenderedKey $marker
            if ($visualKey -notmatch '(?:^|:)pro-entity:(player|ai):([0-9]+)(?:#|$)') { continue }
            $key = Get-RenderIdentityKey $row $Matches[1] $Matches[2]
            if ($null -eq $key) { continue }
            if (-not $index.ContainsKey($key)) { $index[$key] = [Collections.Generic.List[object]]::new() }
            $index[$key].Add([pscustomobject]@{ At = $at; Marker = $marker; Row = $row })
        }
    }
    foreach ($key in @($index.Keys)) { $index[$key] = @($index[$key] | Sort-Object At) }
    return $index
}

function Find-RenderEntry($Frame, $FrameAt, $Candidates) {
    if ($null -eq $FrameAt -or $null -eq $Candidates -or $Candidates.Count -eq 0) { return $null }
    # Binary search bounds work even when capture and publication are reordered.
    $low = 0
    $high = $Candidates.Count
    while ($low -lt $high) {
        $mid = $low + [int][Math]::Floor(($high - $low) / 2)
        if ($Candidates[$mid].At -lt $FrameAt) { $low = $mid + 1 } else { $high = $mid }
    }
    $sequence = Get-Field $Frame @('Sequence')
    for ($i = $low; $i -lt $Candidates.Count; $i++) {
        $candidate = $Candidates[$i]
        if (($candidate.At - $FrameAt).TotalSeconds -gt 2) { break }
        $renderSequence = Get-Field $candidate.Row @('ProPlayerSequence')
        $stale = Get-Field $candidate.Marker @('IsStale')
            $provisional = Get-Field $candidate.Marker @('IsProvisional')
        if ((Test-TrackingSessionMatch $Frame $candidate.Row) -and
            $null -ne $sequence -and $null -ne $renderSequence -and
            [long]$renderSequence -ge [long]$sequence -and
            $null -ne $stale -and -not [bool]$stale -and
            $null -ne $provisional) { return $candidate }
    }
    return $null
}

function Write-EvidenceFailure([string]$Path, [string]$Reason) {
    $result = [pscustomobject]@{
        SessionId = Split-Path $Path -Leaf
        AnalyzedAt = [DateTimeOffset]::UtcNow
        Status = 'NEED_STAGE_EVIDENCE'
        StageEvidenceAvailable = $false
        AgentFrames = 0
        MapRenderRows = 0
        MissingMarkerProof = $Reason
        EvidenceError = $Reason
    }
    Write-JsonFile (Join-Path $Path 'tracking-analysis.json') $result
    return $result
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
    try {
        $agent = @(Read-JsonLines $agentPath)
        $map = @(Read-JsonLines $mapPath)
    } catch {
        return (Write-EvidenceFailure $Path $_.Exception.Message)
    }
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
    $renderIndex = New-RenderIndex $renderRows
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
        AiOwnerBatchesObserved = 0
        AiActorCreationsObserved = 0
        AiSeedsCreated = 0
        AiVerifiedActivations = 0
        AiCandidateActivations = 0
        AiRejectedActivations = 0
        AiAnonymousBatchesObserved = 0
        AiAnonymousCandidatesObserved = 0
        AiFollowVectorsAccepted = 0
        AiFollowVectorsMissing = 0
        AiPendingSeeds = 0
        AiTracks = 0
        AiVerifiedTracks = 0
        AiCandidateTracks = 0
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
    $freshMarkerCounts = [System.Collections.Generic.List[int]]::new()
    $freshMarkerSamples = [System.Collections.Generic.List[object]]::new()
    $distanceViolations = [System.Collections.Generic.List[object]]::new()
    $minimumFreshMarkerCount = 0
    # Unreal world coordinates used by The Isle are centimetres.
    $maximumFreshDistanceWorldUnits = [double]500000
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
        $sessionId = Get-TrackingSessionId $frame
        $sessionEndpoint = "$sessionId|$endpoint"
        if (-not $seenByEndpoint.ContainsKey($sessionEndpoint)) { $seenByEndpoint[$sessionEndpoint] = 1 }
        # The live recorder stores the post-fusion output in separate player
        # and AI lanes. Keep compatibility with older captures, but never
        # silently treat a new-schema frame as an empty roster.
        $entities = if ($null -ne $frame.mapOutputPlayers -or
                        $null -ne $frame.mapOutputAi) {
            @($frame.mapOutputPlayers) + @($frame.mapOutputAi)
        } else {
            @($frame.RemoteEntities)
        }
        $freshCountForFrame = 0
        $frameDistanceViolations = [System.Collections.Generic.List[object]]::new()
        foreach ($candidateEntity in $entities) {
            $candidateLocationAt = Get-DateTimeOffsetOrNull (Get-Field $candidateEntity @('LocationObservedAt', 'locationObservedAt'))
            $candidateEligible = Test-EligibleEntity $candidateEntity $frame
            if (-not $candidateEligible -or $null -eq $candidateLocationAt -or $null -eq $frameAt) { continue }
            $candidateAgeMs = ($frameAt - $candidateLocationAt).TotalMilliseconds
            if ($candidateAgeMs -lt 0 -or $candidateAgeMs -gt 2000) { continue }
            $candidateKind = Get-EntityKind $candidateEntity
            $candidateTrackId = Get-Field $candidateEntity @('TrackId', 'trackId', 'Id', 'id')
            $candidateIdentityKey = Get-RenderIdentityKey $frame $candidateKind $candidateTrackId
            $candidateRenders = if ($candidateIdentityKey -and $renderIndex.ContainsKey($candidateIdentityKey)) {
                @($renderIndex[$candidateIdentityKey])
            } else { @() }
            $hasFreshMarker = $null -ne (Find-RenderEntry $frame $frameAt $candidateRenders)
            $candidateLocation = Get-Field $candidateEntity @('Location', 'location', 'Position', 'position')
            $localLocation = Get-Field $frame @('Local', 'local')
            $distance = Get-WorldDistance $candidateLocation $localLocation
            if ($null -ne $distance -and $distance -gt $maximumFreshDistanceWorldUnits) {
                $frameDistanceViolations.Add([pscustomobject]@{
                    TrackId = $candidateTrackId
                    Kind = $candidateKind
                    DistanceWorldUnits = [Math]::Round($distance, 2)
                })
            }
            if ($hasFreshMarker) {
                $freshCountForFrame++
            }
        }
        $freshMarkerCounts.Add($freshCountForFrame)
        if ($freshCountForFrame -lt $minimumFreshMarkerCount) {
            $freshMarkerSamples.Add([pscustomobject]@{
                At = $frameAt
                Sequence = $frame.Sequence
                FreshMarkerCount = $freshCountForFrame
                DistanceViolations = @($frameDistanceViolations)
            })
        }
        foreach ($violation in $frameDistanceViolations) { $distanceViolations.Add($violation) }
        if ($null -ne $frame.evidence -or $null -ne $frame.ongoingCandidates) {
            $hasStageEvidence = $true
            if ($frame.evidence) {
                $stageCounters.CandidateActors += [int]$frame.evidence.candidateActors
                $stageCounters.SpeciesEvidenceActors += [int]$frame.evidence.speciesEvidenceActors
                $stageCounters.LocatedActors += [int]$frame.evidence.locatedActors
            }
            $stageCounters.InboundPlayers += @($frame.inboundPlayers).Count
            $stageCounters.InboundAi += @($frame.inboundAi).Count
            $aiDiagnostics = Get-Field $frame @('aiMovementDiagnostics', 'AiMovementDiagnostics')
            if ($null -ne $aiDiagnostics) {
                $stageCounters.AiOwnerBatchesObserved += [long](Get-Field $aiDiagnostics @('ownerBatchesObserved', 'OwnerBatchesObserved'))
                $stageCounters.AiActorCreationsObserved += [long](Get-Field $aiDiagnostics @('actorCreationsObserved', 'ActorCreationsObserved'))
                $stageCounters.AiSeedsCreated += [long](Get-Field $aiDiagnostics @('seedsCreated', 'SeedsCreated'))
                $stageCounters.AiVerifiedActivations += [long](Get-Field $aiDiagnostics @('verifiedActivations', 'VerifiedActivations'))
                $stageCounters.AiCandidateActivations += [long](Get-Field $aiDiagnostics @('candidateActivations', 'CandidateActivations'))
                $stageCounters.AiRejectedActivations += [long](Get-Field $aiDiagnostics @('rejectedActivations', 'RejectedActivations'))
                $stageCounters.AiAnonymousBatchesObserved += [long](Get-Field $aiDiagnostics @('anonymousBatchesObserved', 'AnonymousBatchesObserved'))
                $stageCounters.AiAnonymousCandidatesObserved += [long](Get-Field $aiDiagnostics @('anonymousCandidatesObserved', 'AnonymousCandidatesObserved'))
                $stageCounters.AiFollowVectorsAccepted += [long](Get-Field $aiDiagnostics @('followVectorsAccepted', 'FollowVectorsAccepted'))
                $stageCounters.AiFollowVectorsMissing += [long](Get-Field $aiDiagnostics @('followVectorsMissing', 'FollowVectorsMissing'))
                $stageCounters.AiPendingSeeds = [long](Get-Field $aiDiagnostics @('pendingSeeds', 'PendingSeeds'))
                $stageCounters.AiTracks = [long](Get-Field $aiDiagnostics @('tracks', 'Tracks'))
                $stageCounters.AiVerifiedTracks = [long](Get-Field $aiDiagnostics @('verifiedTracks', 'VerifiedTracks'))
                $stageCounters.AiCandidateTracks = [long](Get-Field $aiDiagnostics @('candidateTracks', 'CandidateTracks'))
            }
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
            $key = '{0}|{1}' -f $sessionId, (Get-EntityKey $entity $endpoint $seenByEndpoint[$sessionEndpoint])
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
                $identityKey = Get-RenderIdentityKey $frame $kindName $trackIdValue
                $candidates = if ($identityKey -and $renderIndex.ContainsKey($identityKey)) {
                    @($renderIndex[$identityKey])
                } else { @() }
                $render = Find-RenderEntry $frame $frameAt $candidates
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
                SessionId = $sessionId
                ServerEndpoint = $endpoint
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
    $gaps = 0
    $duplicates = 0
    $reorders = 0
    $pipelineSequenceGaps = [System.Collections.Generic.List[long]]::new()
    foreach ($frame in $agent) {
        $pipeline = Get-Field $frame @('pipeline', 'Pipeline')
        if ($null -ne $pipeline) {
            $gap = [long](Get-Field $pipeline @('sequenceGapPackets', 'SequenceGapPackets'))
            if ($gap -gt 0) { $pipelineSequenceGaps.Add($gap) }
            $duplicates += [long](Get-Field $pipeline @('duplicatePackets', 'DuplicatePackets'))
            $reorders += [long](Get-Field $pipeline @('reorderedPackets', 'ReorderedPackets'))
        }
    }
    if ($pipelineSequenceGaps.Count -gt 0) { $gaps = ($pipelineSequenceGaps | Measure-Object -Sum).Sum }
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
    $minimumFreshCountObserved = if ($freshMarkerCounts.Count -gt 0) {
        ($freshMarkerCounts | Measure-Object -Minimum).Minimum
    } else { $null }
    $freshMarkerGateHasSamples = $freshMarkerCounts.Count -gt 0
    # These counters describe the observation window, not an independent
    # oracle for actor position or complete capture coverage.
    $freshMarkerGatePassed = $null
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
        CapturedPackets = ($agent | ForEach-Object { [long](Get-Field $_.pipeline @('capturedPackets', 'CapturedPackets')) } | Measure-Object -Maximum).Maximum
        AgentRecordCount = $agent.Count
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
        MinimumFreshMarkerCount = $minimumFreshMarkerCount
        FreshMarkerSampleCount = $freshMarkerCounts.Count
        MinimumFreshMarkerCountObserved = $minimumFreshCountObserved
        P10FreshMarkerCount = if ($freshMarkerCounts.Count -gt 0) { Get-Percentile $freshMarkerCounts.ToArray() 0.10 } else { $null }
        FreshMarkerWindowViolations = $freshMarkerSamples
        FreshMarkerWindowViolationCount = $freshMarkerSamples.Count
        FreshMarkerGateHasSamples = $freshMarkerGateHasSamples
        FreshMarkerGatePassed = $freshMarkerGatePassed
        MaximumFreshDistanceWorldUnits = $maximumFreshDistanceWorldUnits
        DistanceViolationCount = $distanceViolations.Count
        MaxQueueDroppedPackets = $maxQueueDroppedPackets
        MaxQueueDepth = $maxQueueDepth
        StaleMarkerRows = $staleMarkerRows
        MissingEntities = $missing
        GroundTruthPath = $groundTruthPath
        StageEvidenceAvailable = $hasStageEvidence -or $stageEvidenceAvailable
        StageCounters = $stageCounters
        CandidateDiagnostics = $candidateDiagnostics
        AcceptancePolicy = 'v2-provenance-required-no-count-or-distance-gate'
        GroundTruthIndependent = $false
        Status = if ($agent.Count -eq 0 -or $renderRows.Count -eq 0) { 'NEED_DEVELOPER' } elseif (-not $hasStageEvidence) { 'NEED_STAGE_EVIDENCE' } elseif ($missing.Count -gt 0) { 'FAIL' } else { 'NEED_STAGE_EVIDENCE' }
        MissingMarkerProof = if ($missing.Count -gt 0) { 'Eligible entity has no matching marker in render log.' } else { 'Agent-to-render comparison is not an independent packet decode oracle. Position provenance and lifecycle validation are still required; marker count, age alone and distance cannot certify this session.' }
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
        "- AI pipeline: owner=$($a.StageCounters.AiOwnerBatchesObserved) · creation=$($a.StageCounters.AiActorCreationsObserved) · seed=$($a.StageCounters.AiSeedsCreated) · verified=$($a.StageCounters.AiVerifiedActivations) · candidate=$($a.StageCounters.AiCandidateActivations) · rejected=$($a.StageCounters.AiRejectedActivations)",
        "- AI anonymous: batch=$($a.StageCounters.AiAnonymousBatchesObserved) · candidate=$($a.StageCounters.AiAnonymousCandidatesObserved) · vector nhận=$($a.StageCounters.AiFollowVectorsAccepted) · vector thiếu=$($a.StageCounters.AiFollowVectorsMissing)",
        "- AI track hiện tại: pending=$($a.StageCounters.AiPendingSeeds) · total=$($a.StageCounters.AiTracks) · verified=$($a.StageCounters.AiVerifiedTracks) · candidate=$($a.StageCounters.AiCandidateTracks)",
        "- Candidate diagnostics: $(($a.CandidateDiagnostics | ConvertTo-Json -Compress) -replace "`r?`n", '')",
        "- P50 latency marker: $($a.P50MarkerLatencyMs) ms",
        "- P95 latency marker: $($a.P95MarkerLatencyMs) ms",
        "- P50 capture → decode: $($a.P50CaptureDecodeLatencyMs) ms",
        "- P95 capture → decode: $($a.P95CaptureDecodeLatencyMs) ms",
        "- P50 Agent → UI: $($a.P50AgentToUiLatencyMs) ms",
        "- P95 Agent → UI: $($a.P95AgentToUiLatencyMs) ms",
        "- Tuổi tọa độ: p50 $($a.P50LocationAgeMs) ms · p95 $($a.P95LocationAgeMs) ms · tối đa $($a.MaxLocationAgeMs) ms",
        "- Tọa độ cũ hơn 2/6/15 giây: $($a.LocationAgeGt2s) / $($a.LocationAgeGt6s) / $($a.LocationAgeGt15s)",
        "- Fresh marker gate: tối thiểu $($a.MinimumFreshMarkerCount) / quan sát thấp nhất $($a.MinimumFreshMarkerCountObserved) / p10 $($a.P10FreshMarkerCount)",
        "- Frame vi phạm fresh marker: $($a.FreshMarkerWindowViolationCount) · ngoài bán kính 5 km: $($a.DistanceViolationCount)",
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
