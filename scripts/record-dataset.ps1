[CmdletBinding()]
param(
    [string]$GodotExecutable = $env:CARORAMA_GODOT,
    [string]$Config = 'config/scenario-splits.json',
    [string]$Output = 'artifacts/datasets',
    [string]$DatasetId = (Get-Date -Format "yyyyMMdd-HHmmss"),
    [string]$BuildId,
    [ValidateSet('Training', 'Validation', 'Test')]
    [string]$Split,
    [string]$Scenario,
    [int]$MaxEpisodes,
    [switch]$NoCameras,
    [int]$CameraWidth = 1280,
    [int]$CameraHeight = 720,
    [double]$CameraHz = 30.0
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/CarOrama.Game'
$configPath = if ([System.IO.Path]::IsPathRooted($Config)) {
    [System.IO.Path]::GetFullPath($Config)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repository $Config))
}
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repository $Output))
}

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    $godotCommand = Get-Command godot -ErrorAction SilentlyContinue
    if ($null -eq $godotCommand) {
        throw 'Set CARORAMA_GODOT to the Godot 4.6.1 .NET executable, or add godot to PATH.'
    }

    $GodotExecutable = $godotCommand.Source
}

if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = (& git -C $repository rev-parse HEAD).Trim()
    if (-not [string]::IsNullOrWhiteSpace((& git -C $repository status --porcelain))) {
        $BuildId += '-dirty'
    }
}

& $GodotExecutable --headless --path $project --build-solutions --quit
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$engineArguments = @('--fixed-fps', '120', '--path', $project)
if ($NoCameras) {
    $engineArguments = @('--headless') + $engineArguments
}
else {
    $engineArguments += @('--rendering-method', 'gl_compatibility', '--disable-vsync')
}

$simulationArguments = @(
    '--record-dataset',
    '--config', $configPath,
    '--output', $outputPath,
    '--dataset-id', $DatasetId,
    '--build-id', $BuildId,
    '--camera-width', $CameraWidth.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--camera-height', $CameraHeight.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--camera-hz', $CameraHz.ToString([Globalization.CultureInfo]::InvariantCulture)
)
if ($NoCameras) { $simulationArguments += '--no-cameras' }
if ($Split) { $simulationArguments += @('--split', $Split) }
if ($Scenario) { $simulationArguments += @('--scenario', $Scenario) }
if ($MaxEpisodes -gt 0) { $simulationArguments += @('--max-episodes', $MaxEpisodes.ToString()) }

& $GodotExecutable @engineArguments -- @simulationArguments
exit $LASTEXITCODE
