$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/RoutineRest.Tests/RoutineRest.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Behavioral tests failed.' }
