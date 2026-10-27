param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$staging = $null
try {
    $version = (dotnet msbuild LinkPeek.Windows/LinkPeek.Windows.csproj -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') {
        throw 'Cannot read a valid Windows application version from the project.'
    }
    dotnet test LinkPeek.Core.Tests/LinkPeek.Core.Tests.csproj --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    # A fresh output directory prevents obsolete DLLs from entering a later ZIP.
    $staging = Join-Path $PSScriptRoot "artifacts/.staging/$([guid]::NewGuid())"
    $output = Join-Path $staging $Runtime
    dotnet publish LinkPeek.Windows/LinkPeek.Windows.csproj --configuration Release `
        --runtime $Runtime --self-contained true --output $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $sourceCommit = 'unknown (build from a source archive without Git metadata)'
    if ($env:GITHUB_SHA -match '^[0-9a-fA-F]{40}$') {
        $sourceCommit = $env:GITHUB_SHA
    } elseif (Get-Command git -ErrorAction SilentlyContinue) {
        $headCommit = git rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $headCommit -match '^[0-9a-fA-F]{40}$') { $sourceCommit = $headCommit }
    }
    @("LinkPeek Windows $version", "Runtime: $Runtime", "Source commit: $sourceCommit") |
        Set-Content (Join-Path $output 'BUILD-INFO.txt') -Encoding utf8
    $zip = Join-Path $PSScriptRoot "artifacts/LinkPeek-Windows-$version-$Runtime.zip"
    $stagedZip = Join-Path $staging (Split-Path $zip -Leaf)
    Compress-Archive -Path "$output/*" -DestinationPath $stagedZip
    $hash = (Get-FileHash $stagedZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zip -Leaf)" | Set-Content "$stagedZip.sha256" -Encoding ascii
    Move-Item $stagedZip $zip -Force
    Move-Item "$stagedZip.sha256" "$zip.sha256" -Force
    Write-Output "Portable unsigned Windows app: $zip"
    Write-Output 'Extract the entire ZIP and run LinkPeek.exe. No admin or separate .NET runtime needed.'
} finally {
    if ($staging -and (Test-Path $staging)) { Remove-Item $staging -Recurse -Force }
    Pop-Location
}
