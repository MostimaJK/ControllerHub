param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    & ./scripts/bootstrap.ps1 -VerifyOnly
    $taskLock = Get-Content -LiteralPath DEPENDENCIES.lock.json -Raw | ConvertFrom-Json
    $taskVersion = $taskLock.portableRuntime.version
    if (-not $taskVersion) { throw 'Missing portable runtime lock.' }
    $taskWork = Join-Path $taskRoot 'artifacts/portable-build'
    $taskPayload = Join-Path $taskWork 'payload'
    New-Item -ItemType Directory -Path $taskWork -Force | Out-Null
    if (-not $SkipPublish) {
        & dotnet publish src/ControllerFusion.App/ControllerFusion.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true "-p:ControllerHubPortableRuntimeVersion=$taskVersion" -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $taskPayload --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    }
    $taskRuntime = Get-Content -LiteralPath (Join-Path $taskPayload 'ControllerHub.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($taskRuntime.runtimeOptions.framework -or $taskRuntime.runtimeOptions.frameworks -or @($taskRuntime.runtimeOptions.includedFrameworks).Count -ne 2) { throw 'Payload is not self-contained.' }
    foreach ($taskFramework in $taskRuntime.runtimeOptions.includedFrameworks) {
        if ($taskFramework.version -ne $taskVersion) { throw "Wrong runtime version: $($taskFramework.name)" }
    }
    # Remove only known generated DS4Windows publishing artifacts, never source.
    foreach ($taskRelative in @('extras','DS4Windows.exe','DS4Windows.deps.json','DS4Windows.runtimeconfig.json','DS4Windows.release','.ds4windows-managed-files.txt','docs','licenses')) {
        $taskGenerated = [IO.Path]::GetFullPath((Join-Path $taskPayload $taskRelative))
        if (-not $taskGenerated.StartsWith($taskPayload + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Payload cleanup outside publish directory.' }
        if (Test-Path -LiteralPath $taskGenerated) { Remove-Item -LiteralPath $taskGenerated -Recurse -Force }
    }
    Copy-Item -LiteralPath README.md,COPYING,THIRD_PARTY_NOTICES.md,DEPENDENCIES.lock.json -Destination $taskPayload -Force
    foreach ($taskDirectory in @('docs','licenses')) { Copy-Item -LiteralPath $taskDirectory -Destination $taskPayload -Recurse -Force }
    $taskNuget = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*',''
    foreach ($taskPack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $taskPackRoot = Join-Path $taskNuget ($taskPack + '/' + $taskVersion)
        $taskNotices = @(Get-ChildItem -LiteralPath $taskPackRoot -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
        if ($taskNotices.Count -eq 0) { throw "Runtime license not found: $taskPackRoot" }
        $taskNoticeTarget = Join-Path $taskPayload ('licenses/' + $taskPack)
        New-Item -ItemType Directory -Path $taskNoticeTarget -Force | Out-Null
        $taskNotices | Copy-Item -Destination $taskNoticeTarget
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $taskZip = Join-Path $taskWork ('payload-' + [Guid]::NewGuid().ToString('N') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($taskPayload,$taskZip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $taskId = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskManifest = Join-Path $taskWork 'payload.manifest'
    $taskLines = [Collections.Generic.List[string]]::new()
    $taskLines.Add($taskId); $taskLines.Add($taskVersion)
    foreach ($taskFile in (Get-ChildItem -LiteralPath $taskPayload -Recurse -File | Sort-Object FullName)) {
        $taskName = [IO.Path]::GetRelativePath($taskPayload,$taskFile.FullName).Replace('\','/')
        if ($taskName -match '[\t\r\n]') { throw 'Invalid payload filename.' }
        $taskHash = (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $taskLines.Add($taskHash + "`t" + $taskFile.Length + "`t" + $taskName)
    }
    [IO.File]::WriteAllLines($taskManifest,$taskLines,[Text.UTF8Encoding]::new($false))
    $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'The Windows .NET Framework compiler is required to build the small launcher.' }
    $taskFrameworkRoot = Split-Path -Parent $taskCompiler
    $taskRelease = Join-Path $taskRoot 'releases/latest'
    New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
    $taskExe = Join-Path $taskRelease 'ControllerHub-v0.3.0-preview-portable-win-x64.exe'
    $taskSources = @('src/ControllerHub.Portable/Launcher.cs','src/ControllerHub.Portable/PortableFiles.cs') | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }
    $taskArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-','/langversion:5','/codepage:65001',"/out:$taskExe",'/win32icon:src/ControllerFusion.App/app.ico',"/resource:$taskZip,ControllerHub.Payload.zip","/resource:$taskManifest,ControllerHub.Payload.manifest",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',('/reference:' + (Join-Path $taskFrameworkRoot 'System.IO.Compression.dll')))
    & $taskCompiler @taskArguments @taskSources
    if ($LASTEXITCODE -ne 0) { throw 'Portable launcher compilation failed.' }
    $taskHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($taskExe + '.sha256',$taskHash + '  ' + [IO.Path]::GetFileName($taskExe) + "`n",[Text.Encoding]::ASCII)
    [pscustomobject]@{PayloadHash=$taskId;RuntimeVersion=$taskVersion;Files=$taskLines.Count-2;Executable=$taskExe;Sha256=$taskHash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskWork 'build-info.json') -Encoding utf8
    Write-Host "Portable EXE: $taskExe"
} finally { Pop-Location }
