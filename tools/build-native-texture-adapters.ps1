param([Parameter(Mandatory=$true)][string[]]$EngineRoots)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$plugin = Join-Path $repoRoot 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/CyHubNativeBridge.uplugin'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/native-texture-adapters'))
foreach ($engineRoot in $EngineRoots) {
    $version = Get-Content -LiteralPath (Join-Path $engineRoot 'Engine/Build/Build.version') -Raw | ConvertFrom-Json
    if ($version.MajorVersion -ne 5 -or $version.MinorVersion -notin @(5,6)) { throw 'Only Unreal 5.5 and 5.6 are supported.' }
    $minor = '5.' + $version.MinorVersion
    $package = [IO.Path]::GetFullPath((Join-Path $outputRoot "UE_$minor"))
    if (-not $package.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package path.' }
    # UAT replaces this dedicated package directory. Reject junctions in existing ancestors.
    for ($ancestor = $package; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Junction refused: $ancestor" }
    }
    $uat = Join-Path $engineRoot 'Engine/Build/BatchFiles/RunUAT.bat'
    Write-Host "Building native adapter UE $minor in $package (dedicated package replaced)."
    & $uat BuildPlugin "-Plugin=$plugin" "-Package=$package" -TargetPlatforms=Win64
    if ($LASTEXITCODE -ne 0) { throw "Native adapter build failed for UE $minor" }
}
