[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Checkpoint,
    [Parameter(Mandatory)]
    [string]$Dataset,
    [Parameter(Mandatory)]
    [string]$Output,
    [ValidateSet('training', 'validation', 'test')]
    [string]$Split = 'test',
    [string]$Device = 'auto'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repository 'ml/.venv/Scripts/python.exe'
if (-not (Test-Path $python)) {
    throw 'The ML environment is missing. Run ./scripts/setup-ml.ps1 first.'
}

Push-Location $repository
try {
    & $python -m carorama_ml.evaluate --checkpoint $Checkpoint --dataset $Dataset --split $Split --output $Output --device $Device
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}