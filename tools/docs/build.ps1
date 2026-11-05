param([switch]$Force)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dependency = Get-Content (Join-Path $PSScriptRoot 'dependency.json') -Raw | ConvertFrom-Json
$checkout = Join-Path $repoRoot ('artifacts/cydoc-source-' + $dependency.commit.Substring(0, 7))
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" }
}
# Multiple project-reference/build invocations share one generated export. Serialize
# generation rather than allowing one process to remove another's staging files.
$lockHasher = [Security.Cryptography.SHA256]::Create()
try { $lockId = [BitConverter]::ToString($lockHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($repoRoot.ToLowerInvariant()))).Replace('-', '') }
finally { $lockHasher.Dispose() }
$generationLock = New-Object Threading.Mutex($false, ('CyEngineHub.CyDoc.' + $lockId))
$hasLock = $false
try {
try { $hasLock = $generationLock.WaitOne(60000) } catch [Threading.AbandonedMutexException] { $hasLock = $true }
if (!$hasLock) { throw 'Another documentation generation is still running. Retry after it completes.' }
if (!(Test-Path -LiteralPath (Join-Path $checkout '.git'))) {
    Invoke-Checked git @('clone', '--filter=blob:none', '--no-checkout', $dependency.repository, $checkout)
    Invoke-Checked git @('-C', $checkout, 'checkout', '--detach', $dependency.commit)
}
$actual = & git -C $checkout rev-parse HEAD
if ($actual -ne $dependency.commit) { throw 'CyDoc dependency revision does not match dependency.json.' }
$changes = & git -C $checkout status --porcelain --untracked-files=no
if ($changes) { throw 'The isolated CyDoc dependency contains changes; refusing to build a modified dependency.' }
Push-Location $checkout
try {
    if (!(Test-Path -LiteralPath 'node_modules/.package-lock.json')) { Invoke-Checked npm @('ci', '--no-audit', '--no-fund') }
    # Compile the library, not its demonstration site or sample assets.
    if (!(Test-Path -LiteralPath 'dist/viewer.js') -or $Force) { Invoke-Checked npm @('exec', '--', 'tsc') }
} finally { Pop-Location }
Invoke-Checked node @((Join-Path $PSScriptRoot 'generate.mjs'), $checkout, $(if ($Force) { '--force' } else { '--cached' }))
} finally {
    if ($hasLock) { $generationLock.ReleaseMutex() }
    $generationLock.Dispose()
}
