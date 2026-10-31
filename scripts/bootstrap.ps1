param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskLock = Get-Content -LiteralPath (Join-Path $taskRoot 'DEPENDENCIES.lock.json') -Raw | ConvertFrom-Json
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required to restore pinned upstream sources.' }
function Restore-Source($Name, $Repository, $Revision) {
    $taskTarget = Join-Path $taskRoot ('upstream/' + $Name)
    if (-not (Test-Path -LiteralPath (Join-Path $taskTarget '.git'))) {
        if ($VerifyOnly) { throw "Missing dependency $Name. Run ./scripts/bootstrap.ps1 first." }
        if ((Test-Path -LiteralPath $taskTarget) -and (Get-ChildItem -LiteralPath $taskTarget -Force | Select-Object -First 1)) { throw "Refusing to replace nonempty dependency directory: $taskTarget" }
        New-Item -ItemType Directory -Path $taskTarget -Force | Out-Null
        & git -C $taskTarget init --quiet
        if ($LASTEXITCODE -ne 0) { throw "git init failed: $Name" }
        & git -C $taskTarget config core.longpaths true
        if ($LASTEXITCODE -ne 0) { throw "Could not enable long paths in dependency checkout: $Name" }
        & git -C $taskTarget remote add origin $Repository
        if ($LASTEXITCODE -ne 0) { throw "git remote failed: $Name" }
        & git -C $taskTarget fetch --depth 1 origin $Revision
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed: $Name" }
        & git -C $taskTarget checkout --detach --quiet FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw "git checkout failed: $Name" }
    }
    $taskHead = & git -C $taskTarget rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $taskHead.Trim() -ne $Revision) { throw "Unexpected $Name revision. Expected $Revision; existing checkout was not modified." }
    return $taskTarget
}
$taskDs4 = Restore-Source 'DS4Windows' $taskLock.ds4windows.repository $taskLock.ds4windows.commit
$taskPro2 = Restore-Source 'PRO2' $taskLock.pro2.repository $taskLock.pro2.commit
$taskPatch = Join-Path $taskRoot $taskLock.ds4windows.localChanges
& git -C $taskDs4 apply --reverse --check $taskPatch 2>$null
if ($LASTEXITCODE -ne 0) {
    if ($VerifyOnly) { throw 'The integration patch is missing or differs. Run bootstrap on a clean pinned checkout.' }
    & git -C $taskDs4 apply --check $taskPatch
    if ($LASTEXITCODE -ne 0) { throw 'Integration patch conflicts; existing source changes were not overwritten.' }
    & git -C $taskDs4 apply $taskPatch
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply integration patch.' }
}
$taskChecks = @(
    @{ Path = Join-Path $taskPro2 'source/final-three-in-one/tools/viiper/haptic-v0.8.0/viiper-haptic-v2.exe'; Hash = $taskLock.viiper.sha256 },
    @{ Path = Join-Path $taskPro2 'source/final-three-in-one/tools/usbip-win2/v0.9.7.7/USBip-0.9.7.7-x64.exe'; Hash = $taskLock.usbip.sha256 },
    @{ Path = Join-Path $taskDs4 'extras/HidHide_1.5.230_x64.exe'; Hash = $taskLock.hidhide.sha256 }
)
foreach ($taskCheck in $taskChecks) {
    if (-not (Test-Path -LiteralPath $taskCheck.Path)) { throw "Missing binary dependency: $($taskCheck.Path)" }
    if ((Get-FileHash -LiteralPath $taskCheck.Path -Algorithm SHA256).Hash -ne $taskCheck.Hash) { throw "Dependency hash mismatch: $($taskCheck.Path)" }
}
foreach ($taskFont in $taskLock.googleSans.files.PSObject.Properties) {
    $taskPath = Join-Path $taskRoot ('src/ControllerFusion.App/Assets/Fonts/' + $taskFont.Name)
    if ((Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskFont.Value) { throw "Bundled font hash mismatch: $taskPath" }
}
Write-Host 'Pinned source revisions, integration patch, backend hashes and fonts verified.'
