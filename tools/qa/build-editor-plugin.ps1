[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$EngineRoots, [Parameter(Mandatory=$true)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputRoot)
if (-not $output.StartsWith((Join-Path $repository 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
function Assert-PlainPath([string]$path) {
    for ($ancestor = $path; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse point refused.' }
    }
}
Assert-PlainPath $output
[void](New-Item -ItemType Directory -Path $output)
foreach ($engine in $EngineRoots) {
    $engine = [IO.Path]::GetFullPath($engine)
    Assert-PlainPath $engine
    $version = Get-Content -LiteralPath (Join-Path $engine 'Engine/Build/Build.version') -Raw | ConvertFrom-Json
    if (-not (($version.MajorVersion -eq 4 -and $version.MinorVersion -eq 27) -or ($version.MajorVersion -eq 5 -and $version.MinorVersion -ge 0 -and $version.MinorVersion -le 8))) { throw 'Unsupported editor target.' }
    $name = 'UE_' + $version.MajorVersion + '.' + $version.MinorVersion
    $work = Join-Path $output ('.build-' + $name)
    [void](New-Item -ItemType Directory -Path (Join-Path $work 'Plugins') -Force)
    Copy-Item -LiteralPath (Join-Path $repository 'extensions/unreal/CyEngineHub') -Destination (Join-Path $work 'Plugins') -Recurse
    $project = Join-Path $work 'CyHubEditorHost.uproject'
    [IO.File]::WriteAllText($project, '{"FileVersion":3,"Plugins":[{"Name":"CyEngineHub","Enabled":true}]}', [Text.UTF8Encoding]::new($false))
    $target = if ($version.MajorVersion -eq 4) { 'UE4Editor' } else { 'UnrealEditor' }
    $plugin = Join-Path $work 'Plugins/CyEngineHub'
    $trim = "/d1trimfile:`"$repository`" /d1trimfile:`"$engine`""
    & (Join-Path $engine 'Engine/Build/BatchFiles/Build.bat') $target Win64 Development "-Project=$project" "-plugin=$plugin/CyEngineHub.uplugin" -NoPCH -NoSharedPCH -OverrideBuildEnvironment "-CompilerArguments=$trim" -NoUBA -MaxParallelActions=4 -WaitMutex -NoHotReload
    if ($LASTEXITCODE -ne 0) { throw "Editor plugin compilation failed: $name. Temporary files retained for diagnostics." }
    $destination = Join-Path $output "$name/CyEngineHub"
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
    Copy-Item -LiteralPath (Join-Path $repository 'extensions/unreal/CyEngineHub') -Destination $destination -Recurse
    [void](New-Item -ItemType Directory -Path (Join-Path $destination 'Binaries/Win64') -Force)
    foreach ($file in @("$target-CyEngineHub.dll", "$target.modules")) {
        Copy-Item -LiteralPath (Join-Path $plugin "Binaries/Win64/$file") -Destination (Join-Path $destination 'Binaries/Win64')
    }
    $resolved = (Resolve-Path -LiteralPath $work).Path
    if (-not $resolved.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup target.' }
    Assert-PlainPath $resolved
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Unsafe cleanup tree.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Output "PASS $name $($version.PatchVersion) $destination"
}
