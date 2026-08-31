[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InnoCompiler,
    [string]$OutputRoot,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot ('artifacts\release-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw "Use a new output directory: $OutputRoot" }
if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) { throw 'Inno Setup ISCC.exe is required.' }
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid release version.' }
$appStage = Join-Path $OutputRoot 'app'
$assets = Join-Path $OutputRoot 'assets'
New-Item -ItemType Directory -Path $appStage, $assets | Out-Null

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

Push-Location $repoRoot
try {
    if (-not $SkipTests) { Invoke-DotNet @('test', 'CyEngineHub.sln', '-c', 'Release', '--nologo') }
    $publishArguments = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:RuntimeFrameworkVersion=8.0.30',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None',
        '-p:DebugSymbols=false', '-p:SkipBuiltInExtensionCopy=true', '--nologo')
    Invoke-DotNet (@('publish', 'src/CyEngineHub.Desktop/CyEngineHub.Desktop.csproj') + $publishArguments + @('-o', $appStage))
    Invoke-DotNet (@('publish', 'src/CyEngineHub.Updater/CyEngineHub.Updater.csproj') + $publishArguments + @('-o', (Join-Path $appStage 'Updater')))
    Invoke-DotNet (@('publish', 'src/CyEngineHub.AssetBridge.Tool/CyEngineHub.AssetBridge.Tool.csproj') + $publishArguments + @('-o', (Join-Path $appStage 'Extensions\cyenginehub.asset-bridge\tools')))

    # Some native NuGet packages supply symbols even when DebugSymbols=false.
    foreach ($symbols in Get-ChildItem -LiteralPath $appStage -Filter '*.pdb' -Recurse -File) {
        $resolved = [IO.Path]::GetFullPath($symbols.FullName)
        if (-not $resolved.StartsWith($appStage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Symbol file is outside the generated staging directory.'
        }
        Remove-Item -LiteralPath $resolved
    }
    # Executable project references may contribute orphan hosts at the app root;
    # the real tools live in their own self-contained folders above.
    foreach ($toolName in @('CyEngineHub.Updater', 'CyEngineHub.AssetBridge.Tool')) {
        foreach ($extension in @('.exe', '.deps.json', '.runtimeconfig.json')) {
            $orphan = Join-Path $appStage ($toolName + $extension)
            if (Test-Path -LiteralPath $orphan -PathType Leaf) { Remove-Item -LiteralPath $orphan }
        }
    }

    $extensionsRoot = Join-Path $repoRoot 'extensions\builtin'
    foreach ($file in Get-ChildItem -LiteralPath $extensionsRoot -Recurse -File) {
        # Copy extension manifests/scripts, never local bytecode or test outputs.
        if ($file.Extension -notin @('.json', '.py')) { continue }
        $relative = [IO.Path]::GetRelativePath($extensionsRoot, $file.FullName)
        $destination = Join-Path (Join-Path $appStage 'Extensions') $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'), (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $appStage

    # Keep the notices supplied by NuGet authors alongside the bundled runtime.
    $licenseRoot = Join-Path $appStage 'Licenses'
    New-Item -ItemType Directory -Path $licenseRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MIT.txt') -Destination $licenseRoot
    $packageAssets = Get-Content -LiteralPath 'src/CyEngineHub.Desktop/obj/project.assets.json' -Raw | ConvertFrom-Json
    $packageRoots = @($packageAssets.packageFolders.PSObject.Properties.Name)
    $notices = [Collections.Generic.List[string]]::new()
    foreach ($library in $packageAssets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        foreach ($packageRoot in $packageRoots) {
            $directory = Join-Path $packageRoot $library.Value.path
            if (-not (Test-Path -LiteralPath $directory)) { continue }
            $destination = Join-Path $licenseRoot ($library.Name.Replace('/', '-'))
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            # Nuspec metadata preserves copyright, authors and the license expression
            # even for packages that don't ship a separate license text.
            Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' -File | Copy-Item -Destination $destination
            $files = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '(?i)license|notice|copying|copyright' })
            if ($files.Count -gt 0) {
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                $files | Copy-Item -Destination $destination
            }
            $notices.Add("$($library.Name) - https://www.nuget.org/packages/$($library.Name)")
            break
        }
    }
    [IO.File]::WriteAllLines((Join-Path $licenseRoot 'packages.txt'), $notices)
    foreach ($packageRoot in $packageRoots) {
        $runtimeRoot = Join-Path $packageRoot 'microsoft.netcore.app.runtime.win-x64'
        if (-not (Test-Path -LiteralPath $runtimeRoot)) { continue }
        $runtimeVersion = (Get-Content -LiteralPath (Join-Path $appStage 'CyEngineHub.Desktop.runtimeconfig.json') -Raw |
            ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App'
        $runtimeDirectory = Join-Path $runtimeRoot $runtimeVersion.version
        if (Test-Path -LiteralPath $runtimeDirectory) {
            $destination = Join-Path $licenseRoot ('Microsoft.NETCore.App-' + $runtimeVersion.version)
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Get-ChildItem -LiteralPath $runtimeDirectory -File | Where-Object { $_.Name -match '(?i)license|notice' } |
                Copy-Item -Destination $destination
        }
    }

    $required = @('CyEngineHub.Desktop.exe', 'coreclr.dll', 'Updater\CyEngineHub.Updater.exe',
        'Updater\coreclr.dll', 'Extensions\cyenginehub.asset-bridge\tools\CyEngineHub.AssetBridge.Tool.exe',
        'Extensions\cyenginehub.unreal-version-bridge\scripts\source-export.py',
        'Extensions\cyenginehub.unreal-relocated-install\scripts\relocate-pack.py')
    foreach ($relative in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $appStage $relative) -PathType Leaf)) { throw "Missing packaged file: $relative" }
    }
    $unexpected = @(Get-ChildItem -LiteralPath $appStage -Recurse -File | Where-Object {
        $_.Extension -in @('.db', '.log', '.pyc', '.pdb', '.pfx') -or $_.Name -eq 'portable.flag'
    })
    if ($unexpected.Count -gt 0) { throw 'Private/generated files unexpectedly present in the package.' }

    & $InnoCompiler '/Qp' "/DAppVersion=$version" "/DAppSource=$appStage" "/DReleaseOutput=$assets" (Join-Path $PSScriptRoot 'CyEngineHub.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

    # The standalone archive uses the same per-user data directory as the installer.
    # Creating portable.flag beside the executable remains an explicit user choice.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Join-Path $assets "CyEngineHub-$version-win-x64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($appStage, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hashes = @(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
    })
    [IO.File]::WriteAllLines((Join-Path $assets 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
    Write-Host "Release $version ready: $assets"
    Get-ChildItem -LiteralPath $assets | Select-Object Name, Length
} finally { Pop-Location }
