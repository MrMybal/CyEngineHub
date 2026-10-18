param([Parameter(Mandatory=$true)][string[]]$EngineRoots, [string]$OutputRoot, [switch]$KeepHostProject, [switch]$StrictIncludes)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$plugin = Join-Path $repoRoot 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/CyHubNativeBridge.uplugin'
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts/native-texture-adapters' }
$adapterOutput = [IO.Path]::GetFullPath($OutputRoot)
if (-not $adapterOutput.StartsWith($repoRoot + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Adapter output must stay inside workspace artifacts.' }
foreach ($engineRoot in $EngineRoots) {
    $version = Get-Content -LiteralPath (Join-Path $engineRoot 'Engine/Build/Build.version') -Raw | ConvertFrom-Json
    if ($version.MajorVersion -ne 5 -or $version.MinorVersion -notin @(3,5,6,7)) { throw 'Supported build targets: Unreal 5.3, 5.5, 5.6 and 5.7.' }
    $minor = '5.' + $version.MinorVersion
    $package = [IO.Path]::GetFullPath((Join-Path $adapterOutput "UE_$minor"))
    if (-not $package.StartsWith($adapterOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package path.' }
    # UAT replaces this dedicated package directory. Reject junctions in existing ancestors.
    for ($ancestor = $package; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Junction refused: $ancestor" }
    }
    $uat = Join-Path $engineRoot 'Engine/Build/BatchFiles/RunUAT.bat'
    Write-Host "Building native adapter UE $minor in $package (dedicated package replaced)."
    $buildArgs = @('BuildPlugin', "-Plugin=$plugin", "-Package=$package", '-TargetPlatforms=Win64')
    if ($KeepHostProject) { $buildArgs += '-NoDeleteHostProject' }
    if ($StrictIncludes) { $buildArgs += '-StrictIncludes' }
    & $uat @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Native adapter build failed for UE $minor" }
}
