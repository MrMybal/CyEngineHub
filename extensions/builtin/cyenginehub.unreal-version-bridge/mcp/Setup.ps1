param(
    [Parameter(Mandatory=$true)][string]$Python,
    [Parameter(Mandatory=$true)][string]$EnvironmentDirectory
)
$ErrorActionPreference = 'Stop'
# Explicit first-use installation only. Never run automatically on app startup.
if (Test-Path -LiteralPath $EnvironmentDirectory) { throw 'Choose a new private Python environment directory; existing environments are not overwritten.' }
& $Python -c 'import sys; assert sys.version_info >= (3,11), "Python 3.11+ is required"'
if ($LASTEXITCODE -ne 0) { throw 'Unsupported Python interpreter.' }
& $Python -m venv $EnvironmentDirectory
if ($LASTEXITCODE -ne 0) { throw 'Could not create private Python environment.' }
$privatePython = Join-Path $EnvironmentDirectory 'Scripts/python.exe'
Push-Location $PSScriptRoot
try {
    & $privatePython -m pip --disable-pip-version-check --keyring-provider disabled install -r requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
    & $privatePython -c 'from importlib.metadata import version; assert version("cytools-core")=="0.10.1"; assert version("mcp")=="1.30.0"'
    if ($LASTEXITCODE -ne 0) { throw 'SDK version check failed.' }
} finally { Pop-Location }
Write-Output "Configured interpreter: $privatePython"
