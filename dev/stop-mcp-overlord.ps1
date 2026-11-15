param(
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = "SilentlyContinue"
$processes = @(Get-Process -Name "mcp_overlord")
if ($processes.Count -eq 0)
{
    Write-Output "mcp_overlord is not running."
    exit 0
}

foreach ($process in $processes)
{
    $process.CloseMainWindow() | Out-Null
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline)
{
    $still = @(Get-Process -Name "mcp_overlord")
    if ($still.Count -eq 0)
    {
        Write-Output "Stopped."
        exit 0
    }

    Start-Sleep -Milliseconds 200
}

Stop-Process -Name "mcp_overlord" -Force
Write-Output "Stopped."
exit 0
