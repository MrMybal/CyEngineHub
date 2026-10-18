param([Parameter(Mandatory=$true)][string]$EngineRoot,[Parameter(Mandatory=$true)][string]$Package)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildPackage=[IO.Path]::GetFullPath($Package)
if(-not $buildPackage.StartsWith($repository+'\artifacts\',[StringComparison]::OrdinalIgnoreCase)){throw 'Build package must stay in artifacts.'}
for($ancestor=$buildPackage;$ancestor;$ancestor=Split-Path -Parent $ancestor){if((Test-Path -LiteralPath $ancestor)-and((Get-Item -LiteralPath $ancestor).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Junction refused.'}}
$source=Join-Path $repository 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge'
$hostPlugin=Join-Path $buildPackage 'HostProject/Plugins/CyHubNativeBridge'
$hostProject=Join-Path $buildPackage 'HostProject/HostProject.uproject'
if(-not(Test-Path -LiteralPath $hostProject)){throw 'A previous BuildPlugin -KeepHostProject package is required.'}
Copy-Item -LiteralPath (Join-Path $source 'Source') -Destination $hostPlugin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $source 'CyHubNativeBridge.uplugin') -Destination $hostPlugin -Force
& (Join-Path $EngineRoot 'Engine/Build/BatchFiles/Build.bat') UnrealEditor Win64 Development "-Project=$hostProject" "-plugin=$hostPlugin/CyHubNativeBridge.uplugin" -WaitMutex -nohotreload
if($LASTEXITCODE-ne 0){throw 'Incremental native build failed.'}
Copy-Item -LiteralPath (Join-Path $hostPlugin 'CyHubNativeBridge.uplugin') -Destination $buildPackage -Force
New-Item -ItemType Directory -Path (Join-Path $buildPackage 'Binaries/Win64') -Force|Out-Null
foreach($file in @('UnrealEditor-CyHubNativeBridge.dll','UnrealEditor.modules')){Copy-Item -LiteralPath (Join-Path $hostPlugin "Binaries/Win64/$file") -Destination (Join-Path $buildPackage 'Binaries/Win64') -Force}
