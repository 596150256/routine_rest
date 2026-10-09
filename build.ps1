$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish src/RoutineRest.App/RoutineRest.App.csproj -c Release --self-contained false -o app
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Ready: app\RoutineRest.exe'
