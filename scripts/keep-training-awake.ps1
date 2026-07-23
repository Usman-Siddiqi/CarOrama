[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$TrainingProcessId
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class TrainingPowerState
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint executionState);
}
'@

$continuous = [uint32]::Parse('80000000', [Globalization.NumberStyles]::HexNumber)
$systemRequired = [uint32]0x00000001
$request = [TrainingPowerState]::SetThreadExecutionState($continuous -bor $systemRequired)
if ($request -eq 0) {
    throw 'Windows rejected the training keep-awake request.'
}

try {
    while (Get-Process -Id $TrainingProcessId -ErrorAction SilentlyContinue) {
        Start-Sleep -Seconds 30
    }
}
finally {
    [void][TrainingPowerState]::SetThreadExecutionState($continuous)
}
