param([switch]$Check,[switch]$Package,[string]$BuildRoot='')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./scripts/bootstrap.ps1 -VerifyOnly
    $taskDotnet = & ./scripts/resolve-dotnet.ps1
    $taskProductVersion = ([xml](Get-Content -LiteralPath 'src/ControllerFusion.App/ControllerFusion.App.csproj' -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
    if ($taskProductVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
    $taskPublishArgs = @('publish','src/ControllerFusion.App/ControllerFusion.App.csproj','-c','Release','-p:Platform=x64','--self-contained','false','-o','dist','--nologo','-v:minimal')
    if ($BuildRoot) { $taskPublishArgs += "-p:BaseOutputPath=$BuildRoot" }
    & $taskDotnet @taskPublishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    # Remove only generated artifacts from older publishes of this project.
    $taskDist = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
    foreach ($taskRelative in @('extras','DS4Windows.exe','DS4Windows.deps.json','DS4Windows.runtimeconfig.json','DS4Windows.release','.ds4windows-managed-files.txt','docs','licenses')) {
        $taskGenerated = [IO.Path]::GetFullPath((Join-Path $taskDist $taskRelative))
        if (-not $taskGenerated.StartsWith($taskDist + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Publish cleanup outside dist rejected' }
        if (Test-Path -LiteralPath $taskGenerated) { Remove-Item -LiteralPath $taskGenerated -Recurse -Force }
    }
    Copy-Item -LiteralPath README.md,COPYING,THIRD_PARTY_NOTICES.md,DEPENDENCIES.lock.json -Destination dist -Force
    Copy-Item -LiteralPath docs -Destination dist -Recurse -Force
    Copy-Item -LiteralPath licenses -Destination dist -Recurse -Force
    if ($Check) {
        $taskEvidence = Join-Path $PSScriptRoot 'artifacts/latest-validation'
        New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
        $taskCheckResult = Join-Path $taskEvidence 'offline-checks.txt'
        $taskCheckProcess = Start-Process -FilePath $taskDotnet -ArgumentList ('"' + (Join-Path $PSScriptRoot 'dist/ControllerHub.dll') + '"'),'--self-test',('"' + $taskCheckResult + '"') -WindowStyle Hidden -PassThru -Wait
        if ($taskCheckProcess.ExitCode -ne 0) { throw "Self check failed: $taskCheckResult" }
        Get-Content -LiteralPath $taskCheckResult
    }
    if ($Package) {
        $taskRelease = Join-Path $PSScriptRoot 'releases/latest'
        New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
        $taskArchive = Join-Path $taskRelease "ControllerHub-v$taskProductVersion-preview-win-x64.zip"
        Compress-Archive -Path (Join-Path $taskDist '*') -DestinationPath $taskArchive -Force
        $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath ($taskArchive + '.sha256') -Value ($taskHash + '  ' + [IO.Path]::GetFileName($taskArchive)) -Encoding ascii
        Write-Host "Preview package: $taskArchive"
    }
} finally { Pop-Location }
