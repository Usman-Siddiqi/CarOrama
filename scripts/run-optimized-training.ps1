[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DatasetId,
    [Parameter(Mandatory)][string]$ExperimentId,
    [Parameter(Mandatory)][string]$InitCheckpoint,
    [string]$TrainingConfig = 'config/imitation-generalization-optimized.json',
    [ValidateSet('auto','cpu','cuda')][string]$Device = 'cuda'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$dataset = Join-Path (Join-Path $repository 'artifacts/datasets') $DatasetId
$experiment = Join-Path (Join-Path $repository 'artifacts/experiments') $ExperimentId
$checkpoint = Join-Path $repository $InitCheckpoint
$python = Join-Path $repository 'ml/.venv/Scripts/python.exe'
$config = Join-Path $repository $TrainingConfig

foreach ($path in @($dataset, $checkpoint, $python, $config)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path does not exist: $path" }
}
if (-not (Test-Path -LiteralPath (Join-Path $dataset '_COMPLETE'))) {
    throw "Dataset is not marked complete: $dataset"
}
if (Test-Path -LiteralPath $experiment) { throw "Experiment already exists: $experiment" }

Push-Location $repository
try {
    $env:PYTHONUNBUFFERED = '1'
    & $python -m carorama_ml.train `
        --training-dataset $dataset `
        --validation-dataset $dataset `
        --output $experiment `
        --config $config `
        --init-checkpoint $checkpoint `
        --device $Device
    if ($LASTEXITCODE -ne 0) { throw "Training failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}