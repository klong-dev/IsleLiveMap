$ErrorActionPreference = 'Stop'
# Load only pure eligibility helpers, never the harness command dispatcher.
$parseErrors = $null
$tokens = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'TrackingLab.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$helpers = @('Get-Field', 'Get-DateTimeOffsetOrNull', 'Get-FrameTimestamp',
    'Get-EntityProvisional', 'Get-EntityName', 'Get-EntitySpeciesId',
    'Get-EntitySpeciesName', 'Get-EntityKind', 'Get-EntityHandle',
    'Test-EligibleEntity', 'Get-EntityRejectionReason')
foreach ($definition in $ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)) {
    if ($definition.Name -in $helpers) {
        . ([scriptblock]::Create($definition.Extent.Text))
    }
}
$at = '2026-09-23T07:09:22+00:00'
$frame = [pscustomobject]@{ frameObservedAt = $at }
$entity = [pscustomobject]@{
    trackId = 42; kind = 'Player'; ingameName = 'metadata-only'
    speciesId = 'tyrannosaurus'; species = 'T-Rex'; provisional = $false
    location = [pscustomobject]@{ x = 100; y = 200 }; locationObservedAt = $at
    actorNetRefHandle = 0; playerStateNetRefHandle = 0; pawnNetRefHandle = 0
}
foreach ($provisional in @($false, $true)) {
    $entity.provisional = $provisional
    if (Test-EligibleEntity $entity $frame) { throw 'Name-only player was eligible.' }
    if ((Get-EntityRejectionReason $entity $frame) -ne 'MissingPlayerProof') {
        throw 'Name-only rejection reason was not MissingPlayerProof.'
    }
}
$entity.provisional = $false
$entity.ingameName = $null
$entity.speciesId = $null
$entity.species = $null
foreach ($handle in @('actorNetRefHandle', 'playerStateNetRefHandle', 'pawnNetRefHandle')) {
    $entity.$handle = 42
    if (-not (Test-EligibleEntity $entity $frame)) { throw "Anonymous $handle was rejected." }
    $entity.$handle = 0
}
$entity.actorNetRefHandle = 42
$entity.locationObservedAt = '2026-09-23T07:08:22+00:00'
if (Test-EligibleEntity $entity $frame) { throw 'Stale location was eligible.' }
if ((Get-EntityRejectionReason $entity $frame) -ne 'StaleLocation') { throw 'Missing stale reason.' }
Write-Output 'PASS: name-only rejection, anonymous structural handles, stale location.'
