[CmdletBinding()]
param([string]$OutputRoot, [string]$SourcesRoot, [string]$CMake, [switch]$ConfigureOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$nativeRoot = Join-Path $repoRoot 'native/CyEngineHub.CyUI'
$lock = Get-Content -LiteralPath (Join-Path $nativeRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
if (!$SourcesRoot) { $SourcesRoot = Join-Path $repoRoot 'artifacts/cyui-migration-dependencies' }
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts/cyui-native' }
if (!$CMake) {
    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($command) { $CMake = $command.Source }
    elseif ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $CMake = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    }
}
if (!$CMake -or !(Test-Path -LiteralPath $CMake)) { throw 'CMake 3.21+ and a C++17 toolchain are required.' }
New-Item -ItemType Directory -Path $SourcesRoot -Force | Out-Null
function Get-PinnedSource($name, $dependency) {
    $path = Join-Path $SourcesRoot ($name + '-' + $dependency.revision)
    if (!(Test-Path -LiteralPath $path)) {
        & git clone --filter=blob:none --no-checkout $dependency.repository $path
        if ($LASTEXITCODE -ne 0) { throw "Clone failed: $name" }
        & git -C $path fetch --depth 1 origin $dependency.revision
        if ($LASTEXITCODE -ne 0) { throw "Fetch failed: $name" }
        & git -C $path checkout --detach $dependency.revision
        if ($LASTEXITCODE -ne 0) { throw "Checkout failed: $name" }
    }
    $actual = & git -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual -ne $dependency.revision) { throw "Unexpected revision in $path" }
    if (@(& git -C $path status --porcelain --untracked-files=no).Count -ne 0) { throw "Dependency has local modifications: $path" }
    return $path
}
$cyui = Get-PinnedSource 'CyUI' $lock.cyui
$cimgui = Get-PinnedSource 'cimgui' $lock.cimgui
& git -C $cimgui submodule update --init --depth 1 imgui
if ($LASTEXITCODE -ne 0) { throw 'Pinned ImGui submodule could not be prepared.' }
$imguiRevision = & git -C (Join-Path $cimgui 'imgui') rev-parse HEAD
if ($imguiRevision -ne $lock.imgui.revision) { throw 'ImGui submodule does not match the C# ABI lock.' }
if (@(& git -C (Join-Path $cimgui 'imgui') status --porcelain --untracked-files=no).Count -ne 0) { throw 'Pinned ImGui sources have local modifications.' }
$options = @('-S', $nativeRoot, '-B', $OutputRoot, "-DCYHUB_CYUI_SOURCE=$cyui", "-DCYHUB_CIMGUI_SOURCE=$cimgui", '-DCMAKE_BUILD_TYPE=Release')
if ($IsWindows -or $env:OS -eq 'Windows_NT') { $options += @('-G', 'Visual Studio 17 2022', '-A', 'x64', '-T', 'v143') }
& $CMake @options
if ($LASTEXITCODE -ne 0) { throw 'CyUI native configuration failed.' }
if (!$ConfigureOnly) {
    & $CMake --build $OutputRoot --config Release --parallel 2
    if ($LASTEXITCODE -ne 0) { throw 'CyUI native build failed.' }
    & (Join-Path (Split-Path $CMake) 'ctest') --test-dir $OutputRoot -C Release --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'CyUI native contract tests failed.' }
}
