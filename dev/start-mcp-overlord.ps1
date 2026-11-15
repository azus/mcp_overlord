param(
    [string]$ConfigPath,
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ConfigPath))
{
    $ConfigPath = Join-Path $root "config_sample\mcp_overlord\config_sample.overlord"
}

$ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
& (Join-Path $PSScriptRoot "kill-debug.ps1")
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if (-not $NoBuild)
{
    dotnet build (Join-Path $root "dev\game_automation_in_csharp.sln") -c $Configuration
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

$exe = Join-Path $root "src\mcp_overlord\bin\$Configuration\net9.0-windows10.0.22621.0\mcp_overlord.exe"
if (-not (Test-Path -LiteralPath $exe))
{
    Write-Error "Executable not found: $exe"
    exit 1
}

$port = 5000
$json = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if ($null -ne $json.server.httpPort)
{
    $port = [int]$json.server.httpPort
}

Start-Process -FilePath $exe -ArgumentList @("--config", $ConfigPath)

$deadline = (Get-Date).AddSeconds(30)
$listening = $false
while ((Get-Date) -lt $deadline)
{
    $client = New-Object System.Net.Sockets.TcpClient
    try
    {
        $client.Connect("127.0.0.1", $port)
        $listening = $true
        break
    }
    catch
    {
        Start-Sleep -Milliseconds 250
    }
    finally
    {
        $client.Dispose()
    }
}

if (-not $listening)
{
    Write-Error "Timed out waiting for 127.0.0.1:$port"
    exit 1
}

Write-Output "http://127.0.0.1:$port/mcp"
