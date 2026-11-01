# Select a compatible SDK without changing PATH or installing system software.
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskCandidates=@()
if($env:CONTROLLERHUB_DOTNET){$taskCandidates+=$env:CONTROLLERHUB_DOTNET}
$taskCandidates+=(Join-Path $taskRoot 'artifacts/toolchains/dotnet10/dotnet.exe')
$taskCommand=Get-Command dotnet -ErrorAction SilentlyContinue
if($taskCommand){$taskCandidates+=$taskCommand.Source}
foreach($taskCandidate in ($taskCandidates | Select-Object -Unique)) {
    if(-not (Test-Path -LiteralPath $taskCandidate)){continue}
    $taskVersion=& $taskCandidate --version 2>$null
    if($LASTEXITCODE -eq 0 -and $taskVersion -match '^10\.') {Write-Output $taskCandidate;return}
}
throw 'A compatible .NET 10 SDK is required by global.json. Install the SDK or set CONTROLLERHUB_DOTNET to its dotnet.exe.'
