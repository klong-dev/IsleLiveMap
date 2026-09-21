[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start-session', 'capture', 'run-round', 'analyze', 'report', 'fix-loop')]
    [string]$Command = 'start-session',

    [string]$SessionRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\tracking-lab'),
    [string]$SessionId,
    [int]$DurationMinutes = 5,
    [switch]$KeepPassedRaw,
    [switch]$LaunchInstalledApp
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
    $game = Get-Process -Name 'TheIsle-Win64-Shipping' -ErrorAction SilentlyContinue
    $agent = Get-Process -Name 'IsleLiveMap.Pro.Agent' -ErrorAction SilentlyContinue
    $npcapDll = @(
        (Join-Path $env:SystemRoot 'System32\wpcap.dll'),
        (Join-Path $env:SystemRoot 'SysWOW64\wpcap.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $credential = Join-Path $env:LOCALAPPDATA 'IsleLiveMap\islepilot-credentials.json'
    [pscustomobject]@{
        CheckedAt = [DateTimeOffset]::UtcNow
        GameProcessFound = $null -ne $game
        GamePid = if ($game) { $game.Id } else { $null }
        ProAgentFound = $null -ne $agent
        ProAgentPid = if ($agent) { $agent.Id } else { $null }
        NpcapLibraryFound = $null -ne $npcapDll
        NpcapLibrary = $npcapDll
        IslePilotCredentialFileFound = Test-Path -LiteralPath $credential
    }
}

function New-Session {
    New-Item -ItemType Directory -Force -Path $SessionRoot | Out-Null
    $id = if ($SessionId) { $SessionId } else { "session-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), ([Guid]::NewGuid().ToString('N').Substring(0, 6)) }
    $path = Join-Path $SessionRoot $id
    New-Item -ItemType Directory -Force -Path $path, (Join-Path $path 'raw-capture'), (Join-Path $path 'screenshots') | Out-Null
    $preflight = Test-Preflight
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
    if (-not $preflight.NpcapLibraryFound -or -not $preflight.IslePilotCredentialFileFound) {
        Write-Warning 'Npcap hoặc credential IslePilot chưa được phát hiện. Không bypass quyền; live tracking có thể không khởi động.'
    }
    if ($LaunchInstalledApp) {
        $exe = Join-Path $env:LOCALAPPDATA 'IsleLiveMap\current\IsleLiveMap.exe'
        if (-not (Test-Path -LiteralPath $exe)) { throw "Không tìm thấy app đã cài: $exe" }
        Start-Process -FilePath $exe | Out-Null
        Write-Host 'Đã khởi chạy app cài sẵn. Hãy vào game/server và AFK.'
    }
    return $path
}

function Invoke-Capture([string]$Path) {
    $seconds = [Math]::Max(60, $DurationMinutes * 60)
    Write-Host "Đang thu thập $DurationMinutes phút. Không tự gửi input vào game."
    Start-Sleep -Seconds $seconds
    $manifestPath = Join-Path $Path 'session-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName CompletedAt -NotePropertyValue ([DateTimeOffset]::UtcNow) -Force
    $manifest | Add-Member -NotePropertyName CaptureFiles -NotePropertyValue @(
        'agent-live-compare.jsonl', 'map-diagnostics.jsonl'
    ) -Force
    Write-JsonFile $manifestPath $manifest
    Write-Host "Đã kết thúc capture: $Path"
}

function Read-JsonLines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    Get-Content -LiteralPath $Path | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_)) { return }
        try { $_ | ConvertFrom-Json } catch { }
    }
}

function Invoke-Analyze([string]$Path) {
    $agent = @(Read-JsonLines (Join-Path $Path 'agent-live-compare.jsonl'))
    $map = @(Read-JsonLines (Join-Path $Path 'map-diagnostics.jsonl'))
    $renderRows = @($map | Where-Object { $_.stage -eq 'render-end' })
    $rendered = @($renderRows | ForEach-Object { @($_.RenderedMarkers) } | Where-Object { $_ })
    $sequences = @($agent | Where-Object { $null -ne $_.Sequence } | ForEach-Object { [long]$_.Sequence })
    $gaps = 0
    for ($i = 1; $i -lt $sequences.Count; $i++) { if ($sequences[$i] -gt ($sequences[$i - 1] + 1)) { $gaps += $sequences[$i] - $sequences[$i - 1] - 1 } }
    $result = [pscustomobject]@{
        SessionId = (Split-Path $Path -Leaf)
        AnalyzedAt = [DateTimeOffset]::UtcNow
        AgentFrames = $agent.Count
        MapRenderRows = $renderRows.Count
        RenderedMarkerSamples = $rendered.Count
        DistinctRenderedMarkerKeys = @($rendered | ForEach-Object Key | Sort-Object -Unique).Count
        SequenceGapEstimate = $gaps
        MaxUiQueueDelayMs = if ($renderRows) { ($renderRows | Measure-Object UiQueueDelayMs -Maximum).Maximum } else { $null }
        MaxSnapshotAgeMs = if ($renderRows) { ($renderRows | Measure-Object SnapshotAgeMs -Maximum).Maximum } else { $null }
        ProTrackingActiveRows = @($renderRows | Where-Object ProPlayerTrackingActive).Count
        Status = if ($agent.Count -eq 0 -or $renderRows.Count -eq 0) { 'CẦN DEVELOPER' } else { 'ĐÃ THU THẬP' }
        MissingMarkerProof = 'Cần agent-live-compare và map diagnostics cùng timestamp để kết luận eligible entity bị mất.'
    }
    Write-JsonFile (Join-Path $Path 'tracking-analysis.json') $result
    return $result
}

function Invoke-Report([string]$Path) {
    $analysisPath = Join-Path $Path 'tracking-analysis.json'
    if (-not (Test-Path -LiteralPath $analysisPath)) { $null = Invoke-Analyze $Path }
    $a = Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
    $lines = @(
        '# KẾT QUẢ TRACKING LOOP',
        "- Session: $($a.SessionId)",
        "- Agent frame: $($a.AgentFrames)",
        "- Render row: $($a.MapRenderRows)",
        "- Marker sample: $($a.RenderedMarkerSamples)",
        "- Marker key duy nhất: $($a.DistinctRenderedMarkerKeys)",
        "- Sequence gap ước tính: $($a.SequenceGapEstimate)",
        "- UI queue delay tối đa: $($a.MaxUiQueueDelayMs) ms",
        "- Snapshot age tối đa: $($a.MaxSnapshotAgeMs) ms",
        "- Pro tracking active rows: $($a.ProTrackingActiveRows)",
        "- Trạng thái: $($a.Status)",
        '',
        $a.MissingMarkerProof
    )
    $reportPath = Join-Path $Path 'tracking-report.md'
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host ($lines -join [Environment]::NewLine)
}

function Remove-PassedRawArtifacts {
    if ($KeepPassedRaw) { return }
    Get-ChildItem -LiteralPath $SessionRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $analysisPath = Join-Path $_.FullName 'tracking-analysis.json'
        if (-not (Test-Path -LiteralPath $analysisPath)) { return }
        $analysis = Get-Content -LiteralPath $analysisPath -Raw | ConvertFrom-Json
        if ($analysis.Status -eq 'ĐÃ THU THẬP') {
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
    'analyze' { Invoke-Analyze (Resolve-Session) | Format-List; break }
    'report' { Invoke-Report (Resolve-Session); break }
    'fix-loop' {
        $path = Resolve-Session
        $analysis = Invoke-Analyze $path
        Invoke-Report $path
        Write-Host 'fix-loop hiện dừng sau baseline/analyze: chỉ tiếp tục sửa khi analysis có fixture tái hiện được root cause.'
    }
}
