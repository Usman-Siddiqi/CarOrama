[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GodotExecutable,
    [Parameter(Mandatory)][string]$DatasetId,
    [Parameter(Mandatory)][string]$ExperimentId
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/CarOrama.Game'
$config = Join-Path $repository 'config/scenario-splits-long-training.json'
$trainingConfig = Join-Path $repository 'config/imitation-generalization-long.json'
$datasetParent = Join-Path $repository 'artifacts/datasets'
$dataset = Join-Path $datasetParent $DatasetId
$experiment = Join-Path (Join-Path $repository 'artifacts/experiments') $ExperimentId
$python = Join-Path $repository 'ml/.venv/Scripts/python.exe'
$recordingLog = Join-Path $datasetParent ($DatasetId + '-godot.log')

if (-not (Test-Path -LiteralPath $GodotExecutable)) { throw "Godot not found: $GodotExecutable" }
if (-not (Test-Path -LiteralPath $python)) { throw 'ML environment is missing.' }
if (Test-Path -LiteralPath $dataset) { throw "Dataset already exists: $dataset" }
if (Test-Path -LiteralPath $experiment) { throw "Experiment already exists: $experiment" }

Push-Location $repository
try {
    dotnet build 'src/CarOrama.Game/CarOrama.Game.csproj' --no-restore --verbosity minimal -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Game build failed with exit code $LASTEXITCODE." }

    $buildId = (& git rev-parse HEAD).Trim() + '-dirty'
    & $GodotExecutable `
        --log-file $recordingLog `
        --fixed-fps 120 `
        --path $project `
        --rendering-method gl_compatibility `
        --disable-vsync `
        -- `
        --record-dataset `
        --config $config `
        --output $datasetParent `
        --dataset-id $DatasetId `
        --build-id $buildId `
        --camera-width 320 `
        --camera-height 180 `
        --camera-hz 10
    $recordExit = $LASTEXITCODE
    if ($recordExit -notin 0, 3) { throw "Dataset recording failed with exit code $recordExit." }
    if (-not (Test-Path -LiteralPath (Join-Path $dataset '_COMPLETE'))) {
        throw 'Dataset recording did not produce a complete dataset marker.'
    }

    $env:PYTHONUNBUFFERED = '1'
    & $python -m carorama_ml.train `
        --training-dataset $dataset `
        --validation-dataset $dataset `
        --output $experiment `
        --config $trainingConfig `
        --device cuda
    if ($LASTEXITCODE -ne 0) { throw "Training failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}