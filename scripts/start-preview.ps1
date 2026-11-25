$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $sdk = & ./scripts/resolve-dotnet.ps1
    & $sdk build src/ControllerFusion.App/ControllerFusion.App.csproj -c Release -p:Platform=x64 --nologo
    if ($LASTEXITCODE) { throw 'Preview build failed.' }
    $dll = & $sdk msbuild src/ControllerFusion.App/ControllerFusion.App.csproj -p:Configuration=Release -p:Platform=x64 -getProperty:TargetPath
    if ($LASTEXITCODE -or !(Test-Path -LiteralPath $dll)) { throw 'Preview output missing.' }
    if ($args -contains "--electron") {
        $env:HUB_SDK = $sdk
        $env:HUB_APP = $dll
        $env:ELECTRON_RUN_AS_NODE = $null
        $runner = "desktop/node_modules/electron/dist/electron.exe"
        if (!(Test-Path $runner)) {
            $localLink = "../LocalLink/desktop/node_modules/electron/dist/electron.exe"
            if (Test-Path $localLink) { $runner = $localLink }
            else {
                & npm.cmd --prefix desktop install
                if ($LASTEXITCODE) { throw "Electron setup failed." }
            }
        }
        & (Resolve-Path $runner).Path (Resolve-Path desktop).Path | Out-Host
    } else {
        & $sdk $dll @args
    }
    exit $LASTEXITCODE
} finally { Pop-Location }
