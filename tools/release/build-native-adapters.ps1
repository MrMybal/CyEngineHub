[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string[]]$EngineRoots,
    [Parameter(Mandatory=$true)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputRoot)
$artifacts = Join-Path $repository 'artifacts'
if (-not $output.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an output under repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new native output directory.' }
for ($ancestor = $output; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Junction refused.' }
}
[void](New-Item -ItemType Directory -Path $output)
foreach ($engine in $EngineRoots) {
        $engine = [IO.Path]::GetFullPath($engine).TrimEnd('\')
        $version = Get-Content -LiteralPath (Join-Path $engine 'Engine/Build/Build.version') -Raw | ConvertFrom-Json
        if ($version.MajorVersion -ne 5 -or $version.MinorVersion -notin @(3,5,6,7)) { throw 'Unsupported native target.' }
        $name = 'UE_5.' + $version.MinorVersion
        $temporary = Join-Path $output ('.build-' + $name)
        $plugins = Join-Path $temporary 'Plugins'
        [void](New-Item -ItemType Directory -Path $plugins -Force)
        $pluginSource = Join-Path $repository 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge'
        Copy-Item -LiteralPath $pluginSource -Destination $plugins -Recurse
        $package = Join-Path $plugins 'CyHubNativeBridge'
        $project = Join-Path $temporary 'NativeRelease.uproject'
        [IO.File]::WriteAllText($project, '{"FileVersion":3,"Plugins":[{"Name":"CyHubNativeBridge","Enabled":true}]}', [Text.UTF8Encoding]::new($false))
        # Pass options explicitly: accelerated compiler processes need not inherit
        # _CL_. Never reuse an unrelated PCH, and inspect the resulting DLL.
        $compilerArguments = "/d1trimfile:`"$repository`" /d1trimfile:`"$engine`""
        & (Join-Path $engine 'Engine/Build/BatchFiles/Build.bat') UnrealEditor Win64 Development "-Project=$project" "-plugin=$package/CyHubNativeBridge.uplugin" -NoPCH -NoSharedPCH -OverrideBuildEnvironment "-CompilerArguments=$compilerArguments" -WaitMutex -nohotreload
        if ($LASTEXITCODE -ne 0) { throw "Native compilation failed for $name" }
        $destination = Join-Path $output $name
        foreach ($relative in @('LICENSE', 'NOTICE.md', 'CyHubNativeBridge.uplugin', 'Binaries/Win64/UnrealEditor-CyHubNativeBridge.dll', 'Binaries/Win64/UnrealEditor.modules')) {
            $target = Join-Path $destination $relative
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force)
            Copy-Item -LiteralPath (Join-Path $package $relative) -Destination $target
        }
        & python (Join-Path $repository 'scripts/check-release-privacy.py') --directory $destination
        if ($LASTEXITCODE -ne 0) { throw 'Native package inspection failed; temporary build retained.' }
        $resolved = (Resolve-Path -LiteralPath $temporary).Path
        if (-not $resolved.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase) -or (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Unsafe intermediate cleanup.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
}
