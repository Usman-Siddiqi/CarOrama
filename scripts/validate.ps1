[CmdletBinding()]
param(
    [string]$GodotExecutable = $env:CARORAMA_GODOT
)

$ErrorActionPreference = 'Stop'

dotnet restore CarOrama.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test CarOrama.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    $godotCommand = Get-Command godot -ErrorAction SilentlyContinue
    if ($null -eq $godotCommand) {
        throw 'Set CARORAMA_GODOT to the Godot 4.6.1 .NET executable, or add godot to PATH.'
    }

    $GodotExecutable = $godotCommand.Source
}

& $GodotExecutable --headless --path src/CarOrama.Game --build-solutions --quit
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $GodotExecutable --headless --path src/CarOrama.Game -- --smoke-test
exit $LASTEXITCODE

