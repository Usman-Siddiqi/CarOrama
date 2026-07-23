[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TrainingDataset,
    [Parameter(Mandatory)]
    [string]$ValidationDataset,
    [Parameter(Mandatory)]
    [string]$Output,
    [string]$Config = 'config/imitation-baseline.json',
    [string]$Device = 'auto',
    [int]$Epochs
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repository 'ml/.venv/Scripts/python.exe'
if (-not (Test-Path $python)) {
    throw 'The ML environment is missing. Run ./scripts/setup-ml.ps1 first.'
}

$arguments = @(
    '-m', 'carorama_ml.train',
    '--training-dataset', $TrainingDataset,
    '--validation-dataset', $ValidationDataset,
    '--output', $Output,
    '--config', $Config,
    '--device', $Device
)
if ($Epochs -gt 0) {
    $arguments += @('--epochs', $Epochs.ToString())
}

Push-Location $repository
try {
    & $python @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}