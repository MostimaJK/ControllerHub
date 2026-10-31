param([Parameter(Mandatory)][int]$AppProcessId,[Parameter(Mandatory)][string]$ExpectedExe,[Parameter(Mandatory)][long]$StartedUtcTicks,[Parameter(Mandatory)][string]$Report,[ValidateRange(100,180000)][int]$TimeoutMilliseconds=15000)
$ErrorActionPreference='Stop'
$taskProcess=Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue
if(!$taskProcess){Set-Content -LiteralPath $Report -Value 'Application already exited.';exit}
if($taskProcess.Path -ne $ExpectedExe -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne $StartedUtcTicks){throw 'Watchdog ownership check failed.'}
if($taskProcess.WaitForExit($TimeoutMilliseconds)){Set-Content -LiteralPath $Report -Value ("Application exited before the "+($TimeoutMilliseconds/1000)+" second deadline.");exit}
$taskProcess=Get-Process -Id $AppProcessId -ErrorAction SilentlyContinue
if($taskProcess -and $taskProcess.Path -eq $ExpectedExe -and $taskProcess.StartTime.ToUniversalTime().Ticks -eq $StartedUtcTicks){Stop-Process -Id $AppProcessId -Force;Set-Content -LiteralPath $Report -Value ("Owned diagnostic application terminated at the "+($TimeoutMilliseconds/1000)+" second deadline.")}
