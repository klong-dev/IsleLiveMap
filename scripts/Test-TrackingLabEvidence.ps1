$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'TrackingLab.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($definition in $ast.FindAll({ param($n)
    $n -is [Management.Automation.Language.FunctionDefinitionAst]
}, $true)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

$at = [DateTimeOffset]::Parse('2026-09-23T07:09:22.1234567Z')
$frame = [pscustomobject]@{ sessionId = 'one'; serverEndpoint = 'server:7777'; sequence = 7 }
function Entry([double]$Delay = 0.25, [string]$Session = 'one', [string]$Endpoint = 'server:7777',
    [long]$Sequence = 7, [bool]$Stale = $false, [bool]$Provisional = $false) {
    $marker = [pscustomobject]@{ Key = 'steam:pro-entity:player:42#0'; IsStale = $Stale; IsProvisional = $Provisional }
    $row = [pscustomobject]@{ ReceivedAt = $at.AddSeconds($Delay); ProPlayerSessionId = $Session
        ProPlayerServerEndpoint = $Endpoint; ProPlayerSequence = $Sequence; RenderedMarkers = @($marker) }
    return [pscustomobject]@{ At = $row.ReceivedAt; Row = $row; Marker = $marker }
}
$valid = Entry
if ($null -eq (Find-RenderEntry $frame $at @($valid))) { throw 'Valid marker was not matched.' }
foreach ($bad in @((Entry -Delay -0.1), (Entry -Delay 2.001), (Entry -Session 'two'),
    (Entry -Endpoint 'other:7777'), (Entry -Sequence 6), (Entry -Stale $true))) {
    if ($null -ne (Find-RenderEntry $frame $at @($bad))) { throw 'Invalid render was accepted.' }
}
$provisional = Entry -Provisional $true
if ($null -eq (Find-RenderEntry $frame $at @($provisional))) {
    throw 'Provisional marker was incorrectly treated as absent.'
}
$noStale = Entry
$noStale.Marker.PSObject.Properties.Remove('IsStale')
if ($null -ne (Find-RenderEntry $frame $at @($noStale))) { throw 'Missing stale evidence was accepted.' }
$indexed = New-RenderIndex @((Entry -Session 'two').Row, $valid.Row, (Entry -Delay -1).Row)
$key = Get-RenderIdentityKey $frame 'player' '42'
if ($indexed.Count -ne 2) { throw 'Session index collided.' }
if ((Find-RenderEntry $frame $at $indexed[$key]).At -ne $valid.At) { throw 'Past render was matched.' }

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracking-evidence-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testRoot 'raw-capture') | Out-Null
try {
    $validPath = Join-Path $testRoot 'valid.jsonl'
    [IO.File]::WriteAllLines($validPath, @('{"one":1}', '{"two":2}'))
    if (-not (Test-JsonlSnapshot $validPath).Valid) { throw 'Valid JSONL was rejected.' }
    $frozenPath = Join-Path $testRoot 'frozen.jsonl'
    $writerLock = [IO.File]::Open($validPath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try {
        $blocked = $false
        try { Copy-ClosedEvidence $validPath $frozenPath | Out-Null } catch { $blocked = $true }
        if (-not $blocked) { throw 'Snapshot accepted an active writer.' }
    } finally { $writerLock.Dispose() }
    $frozen = Copy-ClosedEvidence $validPath $frozenPath
    if ($frozen.Sha256 -ne (Get-FileHash -LiteralPath $validPath).Hash) { throw 'Frozen hash mismatch.' }
    $blocked = $false
    try { Copy-ClosedEvidence $validPath $frozenPath | Out-Null } catch { $blocked = $true }
    if (-not $blocked) { throw 'Snapshot overwrote existing evidence.' }
    $badPath = Join-Path $testRoot 'raw-capture/agent-live-compare.jsonl'
    [IO.File]::WriteAllLines($badPath, @('{"one":1}', 'broken line'))
    $before = (Get-FileHash -LiteralPath $badPath).Hash
    $check = Test-JsonlSnapshot $badPath
    if ($check.Valid -or $check.InvalidLine -ne 2) { throw 'Malformed JSONL was not identified.' }
    $result = Invoke-Analyze $testRoot
    $saved = Get-Content (Join-Path $testRoot 'tracking-analysis.json') -Raw | ConvertFrom-Json
    if ($result.Status -ne 'NEED_STAGE_EVIDENCE' -or $saved.Status -ne $result.Status) {
        throw 'Invalid evidence status was not persisted.'
    }
    if ((Get-FileHash -LiteralPath $badPath).Hash -ne $before) { throw 'Analyzer modified raw input.' }
    Write-Output 'PASS: bounded matching, session/endpoint isolation, sequence, stale/provisional, JSONL integrity and failure persistence.'
} finally {
    # This directory is a newly allocated fixture root, never a session artifact.
    [IO.Directory]::Delete($testRoot, $true)
}
