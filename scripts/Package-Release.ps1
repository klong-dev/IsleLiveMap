param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version
)

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = Join-Path $projectRoot 'artifacts\staging\win-x64'
$releaseDirectory = Join-Path $projectRoot 'artifacts\distribution'
$projectFile = Join-Path $projectRoot 'src\TheIsleOverlay.App\TheIsleOverlay.App.csproj'
$iconFile = Join-Path $projectRoot 'src\TheIsleOverlay.App\Assets\IsleLiveMap.ico'
$releaseNotes = Join-Path $projectRoot 'RELEASE_NOTES.md'

& (Join-Path $PSScriptRoot 'New-AppIcon.ps1') -OutputPath $iconFile
if (-not $?) { throw 'Icon generation failed.' }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }

dotnet test (Join-Path $projectRoot 'TheIsleOverlay.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false --output $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

dotnet vpk pack --packId IsleLiveMap --packVersion $Version --packDir $publishDirectory `
    --mainExe IsleLiveMap.exe --packTitle 'Isle Live Map' --packAuthors 'klong-dev' `
    --runtime win-x64 --icon $iconFile --releaseNotes $releaseNotes --outputDir $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

Get-ChildItem -LiteralPath $releaseDirectory | Select-Object Name,Length,LastWriteTime
