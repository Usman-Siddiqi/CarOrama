[CmdletBinding()]
param(
    [string]$Python = '3.11'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    uv sync --project ml --python $Python
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $environmentPython = Join-Path $repository 'ml/.venv/Scripts/python.exe'
    & $environmentPython -c 'import torch; print(f"PyTorch {torch.__version__}"); print(f"CUDA available: {torch.cuda.is_available()}"); print(f"GPU: {torch.cuda.get_device_name(0)}" if torch.cuda.is_available() else "GPU: none")'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}