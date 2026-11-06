param([switch]$Force, [switch]$Check)
$ErrorActionPreference = 'Stop'
$workRoot = $PSScriptRoot
$dependency = Get-Content (Join-Path $workRoot 'dependency.json') -Raw | ConvertFrom-Json
$checkout = Join-Path $workRoot ('.cache/cydoc-source-' + $dependency.commit.Substring(0, 7))
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" }
}
$lockHasher = [Security.Cryptography.SHA256]::Create()
try { $lockId = [BitConverter]::ToString($lockHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($workRoot.ToLowerInvariant()))).Replace('-', '') }
finally { $lockHasher.Dispose() }
$generationLock = New-Object Threading.Mutex($false, ('CyEngineHub.CyDoc.' + $lockId))
$hasLock = $false
try {
    try { $hasLock = $generationLock.WaitOne(60000) } catch [Threading.AbandonedMutexException] { $hasLock = $true }
    if (!$hasLock) { throw 'Another documentation generation is running.' }
    $archive = Join-Path $workRoot '.cache/packages/cydoc-0.1.0.tgz'
    if (!(Test-Path -LiteralPath $archive)) {
        if (!(Test-Path -LiteralPath (Join-Path $checkout '.git'))) {
            Invoke-Checked git @('clone', '--filter=blob:none', '--no-checkout', $dependency.repository, $checkout)
            Invoke-Checked git @('-C', $checkout, 'checkout', '--detach', $dependency.commit)
        }
        if ((& git -C $checkout rev-parse HEAD) -ne $dependency.commit) { throw 'CyDoc revision mismatch.' }
        if (& git -C $checkout status --porcelain --untracked-files=no) { throw 'The CyDoc checkout has tracked changes.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null
        Push-Location $checkout
        try {
            Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
            Invoke-Checked npm @('pack', '--pack-destination', (Split-Path $archive))
        } finally { Pop-Location }
    }
    Push-Location $workRoot
    try {
        # Validate the packed dependency even when node_modules already exists.
        Invoke-Checked node @('check-dependency.mjs')
        if (!(Test-Path -LiteralPath 'node_modules/cydoc/dist/viewer.js')) { Invoke-Checked npm @('ci', '--no-audit', '--no-fund') }
        $arguments = @('generate.mjs')
        if ($Force) { $arguments += '--force' }
        if ($Check) { $arguments += @('--force','--check') }
        Invoke-Checked node $arguments
    } finally { Pop-Location }
} finally {
    if ($hasLock) { $generationLock.ReleaseMutex() }
    $generationLock.Dispose()
}
