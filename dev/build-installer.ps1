<#
.SYNOPSIS
    Builds the mcp_overlord MSI installer from a Release, self-contained publish.

.DESCRIPTION
    1. Installs missing build requirements (dev\install_requirements.ps1): .NET SDK 9,
       the Visual C++ toolset, Windows SDK 10.0.22621 or newer, and the WiX Toolset
       v5.0.2 command line from the official signed MSI.
    2. Stops running mcp_overlord instances (kill-debug.ps1).
    3. Publishes src\mcp_overlord for win-x64 as self-contained, so the .NET runtime
       (including ASP.NET Core and WPF) ships with the application.
    4. Builds installer\mcp_overlord\Package.wxs with wix.exe.
       The UI extension is added to the user WiX cache first.
       After each download, the download mark is removed so Windows Sandbox can load
       those assemblies. MSI validation is skipped there because the sandbox has no
       VBScript engine.

    Output: artifacts\installer\mcp_overlord-<version>-win-x64.msi

.PARAMETER Version
    Product version (major.minor.patch). Defaults to the <Version> of mcp_overlord.csproj.

.PARAMETER Configuration
    Build configuration used for the publish. Defaults to Release.

.PARAMETER NoPublish
    Reuse the existing artifacts\publish\mcp_overlord folder instead of publishing again.
#>
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [switch]$NoPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$applicationProject = Join-Path $root "src\mcp_overlord\mcp_overlord.csproj"
$packageSource = Join-Path $root "installer\mcp_overlord\Package.wxs"
$iconPath = Join-Path $root "src\mcp_overlord\Resources\Icons\app.ico"
$publishDirectory = Join-Path $root "artifacts\publish\mcp_overlord"
$installerDirectory = Join-Path $root "artifacts\installer"
$wixVersion = "5.0.2"
$wixUiExtension = "WixToolset.UI.wixext/$wixVersion"

function Invoke-Checked
{
    param([string]$Description, [scriptblock]$Command)

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0)
    {
        Write-Error "$Description failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

function Get-ProjectVersion
{
    $value = (& dotnet msbuild $applicationProject -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value))
    {
        return "1.0.0"
    }

    return $value
}

function Clear-MarkOfTheWeb
{
    $packageRoot = $env:NUGET_PACKAGES
    if ([string]::IsNullOrWhiteSpace($packageRoot))
    {
        $packageRoot = Join-Path $env:USERPROFILE ".nuget\packages"
    }

    if (-not (Test-Path -LiteralPath $packageRoot))
    {
        return
    }

    Write-Host "==> Allow downloaded NuGet tools" -ForegroundColor Cyan
    Write-Host "Windows blocks assemblies that were just downloaded. Removing that mark in $packageRoot."
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
    $ErrorActionPreference = $previousErrorAction
}

function Clear-DownloadedAssemblyMark
{
    param(
        [string]$Directory,
        [string]$Title,
        [string]$Detail
    )

    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory))
    {
        return
    }

    Write-Host "==> $Title" -ForegroundColor Cyan
    Write-Host "$Detail $Directory."
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    Get-ChildItem -LiteralPath $Directory -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
    $ErrorActionPreference = $previousErrorAction
}

function Get-WixUserExtensionDirectory
{
    $extensionRoot = $env:WIX_EXTENSIONS
    if ([string]::IsNullOrWhiteSpace($extensionRoot))
    {
        $extensionRoot = $env:USERPROFILE
    }

    return (Join-Path $extensionRoot ".wix\extensions")
}

function Clear-WixExtensionMark
{
    Clear-DownloadedAssemblyMark -Directory (Get-WixUserExtensionDirectory) -Title "Allow downloaded WiX extensions" -Detail "Windows blocks assemblies that were just downloaded. Removing that mark in"
}

function Convert-WixPath
{
    param(
        [string]$Path,
        [switch]$TrailingSlash
    )

    $converted = $Path.Replace("\", "/")
    if ($TrailingSlash -and -not $converted.EndsWith("/"))
    {
        $converted = "$converted/"
    }

    return $converted
}

function Get-WixToolsetVersion
{
    param([string]$Executable)

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $output = & $Executable --version 2>&1
        if ($LASTEXITCODE -ne 0)
        {
            return ""
        }
    }
    catch
    {
        return ""
    }
    finally
    {
        $ErrorActionPreference = $previousErrorAction
    }

    foreach ($item in @($output))
    {
        $text = "$item".Trim()
        if ($text -match '(\d+\.\d+\.\d+)')
        {
            return $Matches[1]
        }
    }

    return ""
}

function Get-WixExecutable
{
    $roots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramW6432))
    {
        $roots.Add($env:ProgramW6432)
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles))
    {
        $roots.Add($env:ProgramFiles)
    }

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)}))
    {
        $alreadyListed = $false
        foreach ($rootPath in $roots)
        {
            if ($rootPath.Equals(${env:ProgramFiles(x86)}, [System.StringComparison]::OrdinalIgnoreCase))
            {
                $alreadyListed = $true
            }
        }

        if (-not $alreadyListed)
        {
            $roots.Add(${env:ProgramFiles(x86)})
        }
    }

    foreach ($rootPath in $roots)
    {
        if (-not (Test-Path -LiteralPath $rootPath))
        {
            continue
        }

        $directories = @(Get-ChildItem -LiteralPath $rootPath -Directory -Filter "WiX Toolset*" -ErrorAction SilentlyContinue)
        foreach ($directory in $directories)
        {
            $candidate = Join-Path $directory.FullName "bin\wix.exe"
            if (-not (Test-Path -LiteralPath $candidate))
            {
                continue
            }

            if ((Get-WixToolsetVersion -Executable $candidate) -eq $wixVersion)
            {
                return $candidate
            }
        }
    }

    $command = Get-Command wix -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source))
    {
        if ((Get-WixToolsetVersion -Executable $command.Source) -eq $wixVersion)
        {
            return $command.Source
        }
    }

    return ""
}

function Test-BundledWixExtension
{
    $roots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:CommonProgramW6432))
    {
        $roots.Add($env:CommonProgramW6432)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CommonProgramFiles))
    {
        $alreadyListed = $false
        foreach ($rootPath in $roots)
        {
            if ($rootPath.Equals($env:CommonProgramFiles, [System.StringComparison]::OrdinalIgnoreCase))
            {
                $alreadyListed = $true
            }
        }

        if (-not $alreadyListed)
        {
            $roots.Add($env:CommonProgramFiles)
        }
    }

    foreach ($rootPath in $roots)
    {
        $extensionDll = Join-Path $rootPath "WixToolset\extensions\WixToolset.UI.wixext\$wixVersion\wixext5\WixToolset.UI.wixext.dll"
        if (Test-Path -LiteralPath $extensionDll)
        {
            return $true
        }
    }

    return $false
}

function Test-WixExtensionPresent
{
    param([string]$WixExecutable)

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $output = & $WixExecutable extension list -g 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $previousErrorAction
    }

    if ($exitCode -ne 0)
    {
        return $false
    }

    foreach ($item in @($output))
    {
        $text = "$item".Trim()
        if ($text -match 'WixToolset\.UI\.wixext' -and $text -match [regex]::Escape($wixVersion))
        {
            return $true
        }
    }

    return $false
}

function Install-WixExtension
{
    param([string]$WixExecutable)

    if (Test-BundledWixExtension)
    {
        Write-Host "==> WiX UI extension $wixVersion is installed with wix.exe" -ForegroundColor Cyan
        return
    }

    if (Test-WixExtensionPresent -WixExecutable $WixExecutable)
    {
        Write-Host "==> WiX UI extension $wixVersion is already installed" -ForegroundColor Cyan
        Clear-WixExtensionMark
        return
    }

    Invoke-Checked "Add WiX UI extension $wixUiExtension" {
        & $WixExecutable extension add -g $wixUiExtension
    }
    Clear-WixExtensionMark
}

function Update-SessionPath
{
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ([string]::IsNullOrEmpty($machinePath) -and [string]::IsNullOrEmpty($userPath))
    {
        return
    }

    if ([string]::IsNullOrEmpty($machinePath))
    {
        $env:Path = $userPath
    }
    elseif ([string]::IsNullOrEmpty($userPath))
    {
        $env:Path = $machinePath
    }
    else
    {
        $env:Path = "$machinePath;$userPath"
    }
}

Write-Host "==> Install build requirements" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "install_requirements.ps1")
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "==> Reload PATH" -ForegroundColor Cyan
Update-SessionPath

& (Join-Path $PSScriptRoot "kill-debug.ps1")
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = Get-ProjectVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$')
{
    Write-Error "Version '$Version' is not valid for an MSI. Use major.minor.patch (for example 1.2.0)."
    exit 1
}

if (-not $NoPublish)
{
    if (Test-Path -LiteralPath $publishDirectory)
    {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    Clear-MarkOfTheWeb
    Invoke-Checked "Restore mcp_overlord" {
        dotnet restore $applicationProject `
            -r win-x64 `
            -p:SelfContained=true `
            -p:Version=$Version
    }
    Clear-MarkOfTheWeb
    $nativeOutput = Join-Path $root "src\mcp_overlord\Native\win-x64"
    if (Test-Path -LiteralPath $nativeOutput)
    {
        Write-Host "==> Compile snapenc.dll during this build" -ForegroundColor Cyan
        Remove-Item -LiteralPath $nativeOutput -Recurse -Force
    }

    Invoke-Checked "Publish mcp_overlord $Version ($Configuration, win-x64, self-contained)" {
        dotnet publish $applicationProject `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            --no-restore `
            -o $publishDirectory `
            -p:Version=$Version `
            -p:DebugType=None `
            -p:DebugSymbols=false
    }
}

$executable = Join-Path $publishDirectory "mcp_overlord.exe"
if (-not (Test-Path -LiteralPath $executable))
{
    Write-Error "Published executable not found: $executable"
    exit 1
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "snapenc.dll")))
{
    Write-Error "snapenc.dll is missing from $publishDirectory."
    exit 1
}

$wixExecutable = Get-WixExecutable
if ([string]::IsNullOrWhiteSpace($wixExecutable))
{
    Write-Error "WiX Toolset $wixVersion (wix.exe) was not found. install_requirements.ps1 should have installed it."
    exit 1
}

Write-Host "==> WiX $wixVersion" -ForegroundColor Cyan
Write-Host $wixExecutable
Install-WixExtension -WixExecutable $wixExecutable

if (-not (Test-Path -LiteralPath $installerDirectory))
{
    New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
}

$publishForWix = Convert-WixPath -Path $publishDirectory -TrailingSlash
$iconForWix = Convert-WixPath -Path $iconPath
$msi = Join-Path $installerDirectory "mcp_overlord-$Version-win-x64.msi"
Invoke-Checked "Build MSI" {
    & $wixExecutable build $packageSource `
        -arch x64 `
        -ext WixToolset.UI.wixext `
        -d "ProductVersion=$Version" `
        -d "AppPublishDir=$publishForWix" `
        -d "AppIconPath=$iconForWix" `
        -o $msi
}

$vbScriptLibrary = Join-Path $env:SystemRoot "System32\vbscript.dll"
if (($env:USERNAME -eq "WDAGUtilityAccount") -or (-not (Test-Path -LiteralPath $vbScriptLibrary)))
{
    Write-Host "==> Skip MSI validation" -ForegroundColor Cyan
    Write-Host "Windows Installer validation runs VBScript checks. This environment has no VBScript engine, so those checks are skipped."
}
else
{
    Invoke-Checked "Validate MSI" {
        & $wixExecutable msi validate $msi
    }
}

if (-not (Test-Path -LiteralPath $msi))
{
    Write-Error "Installer not found: $msi"
    exit 1
}

$sizeInMegabytes = [math]::Round((Get-Item -LiteralPath $msi).Length / 1MB, 1)
Write-Host "Installer: $msi ($sizeInMegabytes MB)" -ForegroundColor Green
