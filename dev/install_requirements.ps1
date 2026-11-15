<#
.SYNOPSIS
    Installs the tools required to build this repository on a clean Windows machine.

.DESCRIPTION
    Checks for the .NET SDK 9.0 (x64), the Visual C++ x64 toolset, Windows SDK
    10.0.22621.0 or newer, and the WiX Toolset v5.0.2 command line. Each missing
    tool is installed. When an install is required and the process is not
    elevated, the script restarts itself with User Account Control and waits
    for that elevated run to finish.

    The Windows SDK, Visual C++ build tools, and WiX install with no window.
    Their logs are printed here as steps.

    Windows Sandbox enables Smart App Control, which blocks NuGet assemblies.
    On that sandbox account the script turns Smart App Control off and refreshes
    the code integrity policy. The host computer is left unchanged.

    WiX comes from the official signed wix-cli-x64.msi, installed into Program Files,
    so Windows trusts wix.exe. It is not restored from the NuGet cache.

    A step log is written to %TEMP%\install_requirements.log. After an elevated
    restart, the original window prints that log.
#>
$ErrorActionPreference = "Stop"
if (Test-Path variable:PSNativeCommandUseErrorActionPreference)
{
    $PSNativeCommandUseErrorActionPreference = $false
}

$script:LogPath = Join-Path $env:TEMP "install_requirements.log"
$script:ExitCodePath = Join-Path $env:TEMP "install_requirements.exit"
$script:PidPath = Join-Path $env:TEMP "install_requirements.pid"
$script:DownloadDirectory = Join-Path $env:TEMP "mcp_overlord-requirements"
$script:RebootRequired = $false
$script:DotNetInstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1"
$script:BuildToolsUrl = "https://aka.ms/vs/17/release/vs_buildtools.exe"
$script:WindowsSdkUrl = "https://go.microsoft.com/fwlink/?linkid=2237387"
$script:WixVersion = "5.0.2"
$script:WixCliUrl = "https://github.com/wixtoolset/wix/releases/download/v5.0.2/wix-cli-x64.msi"
$script:VisualCppComponent = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"
$script:WindowsSdkMinimum = [version]"10.0.22621.0"
$script:MissingDotNet = ".NET SDK 9.0 (x64), including the Windows Desktop and ASP.NET Core targeting packs"
$script:MissingVisualCpp = "Visual Studio 2022 Visual C++ x64 toolset (Microsoft.VisualStudio.Component.VC.Tools.x86.x64)"
$script:MissingWindowsSdk = "Windows SDK 10.0.22621.0 or newer"
$script:MissingWix = "WiX Toolset v5.0.2 command line (wix.exe)"
$script:WindowsSdkStepPattern = '(?i)(Planned package:|Applying execute package:|Applied execute package:|Acquiring package|Verified acquired|\b[ie]\d{3}:|Error )'
$script:VisualStudioStepPattern = '(?i)(Installing package|was downloaded|was successfully installed|Install completed|Download of |Downloading:|ERROR|Error:|Erreur|Échec|Télécharg)'
$script:MsiStepPattern = '(?i)(Action start |Début de l''action |Action ended |Fin de l''action |Installation completed|Installation r[eé]ussie|Product:|Produit\s*:|Return value 3|Valeur renvoy[eé]e\s*:?\s*3|error \d+|erreur \d+)'
$script:LogReadState = @{}

function Write-Step
{
    param(
        [string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    Write-Host $line -ForegroundColor $Color
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
}

function Write-Section
{
    param([string]$Title)

    Write-Step "---- $Title ----"
}

function Initialize-Log
{
    $header = "[{0}] install_requirements.ps1" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    Set-Content -LiteralPath $script:LogPath -Value $header -Encoding UTF8
    Write-Host $header -ForegroundColor Cyan
}

function Test-IsAdministrator
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsWindowsSandbox
{
    return ($env:USERNAME -eq "WDAGUtilityAccount")
}

function Disable-SandboxSmartAppControl
{
    if (-not (Test-IsWindowsSandbox))
    {
        return
    }

    Write-Section "Smart App Control"
    Write-Step "Smart App Control is blocking NuGet assemblies in this sandbox."
    $policyKey = "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy"
    if (-not (Test-Path -LiteralPath $policyKey))
    {
        New-Item -Path $policyKey -Force | Out-Null
    }

    $currentState = $null
    try
    {
        $currentState = (Get-ItemProperty -LiteralPath $policyKey -Name VerifiedAndReputablePolicyState -ErrorAction Stop).VerifiedAndReputablePolicyState
    }
    catch
    {
        $currentState = "unset"
    }

    Write-Step "Current VerifiedAndReputablePolicyState: $currentState"
    Set-ItemProperty -LiteralPath $policyKey -Name VerifiedAndReputablePolicyState -Value 0 -Type DWord
    Write-Step "Set VerifiedAndReputablePolicyState to 0 (Off)."

    $ciTool = Join-Path $env:SystemRoot "System32\CiTool.exe"
    if (-not (Test-Path -LiteralPath $ciTool))
    {
        Write-Step "CiTool.exe was not found. Smart App Control may stay active until the sandbox restarts."
        return
    }

    Write-Step "Refreshing the code integrity policy."
    $output = & cmd.exe /d /c "echo.| `"$ciTool`" -r" 2>&1
    foreach ($line in @($output))
    {
        if (-not [string]::IsNullOrWhiteSpace([string]$line))
        {
            Write-Step "  $line"
        }
    }

    Write-Step "CiTool exit code: $LASTEXITCODE"
}

function Stop-RequirementScript
{
    param([int]$ExitCode)

    Write-Step "Exit code: $ExitCode"
    Set-Content -LiteralPath $script:ExitCodePath -Value "$ExitCode" -Encoding ASCII
    if (Test-Path -LiteralPath $script:PidPath)
    {
        Remove-Item -LiteralPath $script:PidPath -Force -ErrorAction SilentlyContinue
    }

    exit $ExitCode
}

function Format-CommandArguments
{
    param([string[]]$Arguments)

    $formatted = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @($Arguments))
    {
        if ([string]::IsNullOrEmpty($argument))
        {
            $formatted.Add('""')
            continue
        }

        if ($argument -match '[\s"]')
        {
            $escaped = $argument -replace '"', '\"'
            $formatted.Add('"' + $escaped + '"')
        }
        else
        {
            $formatted.Add($argument)
        }
    }

    return ($formatted -join " ")
}

function Get-NativeProgramFiles
{
    if (-not [string]::IsNullOrEmpty($env:ProgramW6432))
    {
        return $env:ProgramW6432
    }

    return $env:ProgramFiles
}

function Get-PowerShellExecutable
{
    if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem)
    {
        return (Join-Path $env:SystemRoot "Sysnative\WindowsPowerShell\v1.0\powershell.exe")
    }

    return (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe")
}

function Update-SessionPath
{
    Write-Step "Reloading PATH from the machine and user environment."
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    Write-Step "Machine PATH: $machinePath"
    Write-Step "User PATH: $userPath"

    if ([string]::IsNullOrEmpty($machinePath) -and [string]::IsNullOrEmpty($userPath))
    {
        Write-Step "PATH is empty; leaving the current session PATH unchanged."
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

function Add-MachinePathEntry
{
    param([string]$Entry)

    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $entries = @()
    if (-not [string]::IsNullOrEmpty($machinePath))
    {
        $entries = $machinePath -split ";"
    }

    foreach ($existing in $entries)
    {
        if ($existing.TrimEnd("\").Equals($Entry.TrimEnd("\"), [System.StringComparison]::OrdinalIgnoreCase))
        {
            Write-Step "Machine PATH already contains $Entry"
            return
        }
    }

    $updated = $Entry
    if (-not [string]::IsNullOrEmpty($machinePath))
    {
        $updated = "$machinePath;$Entry"
    }

    Write-Step "Adding to machine PATH: $Entry"
    [Environment]::SetEnvironmentVariable("Path", $updated, "Machine")
}

function Save-WebFile
{
    param(
        [string]$Url,
        [string]$Destination
    )

    $directory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $directory))
    {
        Write-Step "Creating directory: $directory"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $temporary = "$Destination.download"
    Write-Step "Downloading: $Url"
    Write-Step "Destination: $Destination"
    if (Test-Path -LiteralPath $temporary)
    {
        Write-Step "Removing partial download: $temporary"
        Remove-Item -LiteralPath $temporary -Force
    }

    $savedProgress = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try
    {
        Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing -MaximumRedirection 10
    }
    finally
    {
        $ProgressPreference = $savedProgress
    }

    $size = (Get-Item -LiteralPath $temporary).Length
    Write-Step "Downloaded $size bytes."
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
    Write-Step "Saved: $Destination"
}

function Find-RecentLogs
{
    param(
        [string]$Directory,
        [string]$Pattern,
        [datetime]$NotBefore
    )

    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory))
    {
        return @()
    }

    $cutoff = $NotBefore.AddSeconds(-5)
    $found = @(Get-ChildItem -LiteralPath $Directory -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationTime -ge $cutoff } |
        Sort-Object CreationTime)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($item in $found)
    {
        $paths.Add($item.FullName)
    }

    return @($paths)
}

function Find-NewestLog
{
    param(
        [string]$Directory,
        [string]$Pattern,
        [datetime]$NotBefore
    )

    $recent = @(Find-RecentLogs -Directory $Directory -Pattern $Pattern -NotBefore $NotBefore)
    $newestPath = ""
    $newestWrite = [datetime]::MinValue
    foreach ($path in $recent)
    {
        if ([string]::IsNullOrWhiteSpace($path))
        {
            continue
        }

        $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($null -eq $item)
        {
            continue
        }

        if ($item.LastWriteTime -ge $newestWrite)
        {
            $newestWrite = $item.LastWriteTime
            $newestPath = $item.FullName
        }
    }

    return $newestPath
}

function Read-NewLogLines
{
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path))
    {
        return @()
    }

    $fileStream = $null
    $reader = $null
    $leaveOpen = $false
    try
    {
        $fileStream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)

        if (-not $script:LogReadState.ContainsKey($Path))
        {
            $script:LogReadState[$Path] = @{
                Offset = [int64]0
                Encoding = $null
                Pending = ""
            }
        }

        $state = $script:LogReadState[$Path]
        if ($state.Offset -gt $fileStream.Length)
        {
            $state.Offset = [int64]0
            $state.Encoding = $null
            $state.Pending = ""
        }

        if ($fileStream.Length -le $state.Offset)
        {
            return @()
        }

        $fileStream.Position = [int64]$state.Offset
        $leaveOpen = $null -ne $state.Encoding
        if ($leaveOpen)
        {
            $reader = New-Object System.IO.StreamReader($fileStream, $state.Encoding, $false, 4096, $true)
        }
        else
        {
            $reader = New-Object System.IO.StreamReader($fileStream, $true)
        }

        $text = $reader.ReadToEnd()
        $state.Encoding = $reader.CurrentEncoding
        $state.Offset = $reader.BaseStream.Position

        $combined = $state.Pending + $text
        $state.Pending = ""
        if ([string]::IsNullOrEmpty($combined))
        {
            return @()
        }

        $parts = @($combined -split '\r?\n', -1)
        $endsWithBreak = $combined.EndsWith("`n") -or $combined.EndsWith("`r")
        if (-not $endsWithBreak)
        {
            $state.Pending = [string]$parts[$parts.Length - 1]
            if ($parts.Length -eq 1)
            {
                return @()
            }

            $parts = @($parts[0..($parts.Length - 2)])
        }

        $lines = New-Object System.Collections.Generic.List[string]
        foreach ($part in $parts)
        {
            if ($part -ne "")
            {
                $lines.Add([string]$part)
            }
        }

        return @($lines)
    }
    catch
    {
        return @()
    }
    finally
    {
        if ($null -ne $reader)
        {
            $reader.Dispose()
        }

        $readerOwnsStream = (-not $leaveOpen) -and ($null -ne $reader)
        if ((-not $readerOwnsStream) -and ($null -ne $fileStream))
        {
            $fileStream.Dispose()
        }
    }
}

function Write-InstallerLogSteps
{
    param(
        [string[]]$Logs,
        [string]$StepPattern
    )

    foreach ($log in @($Logs))
    {
        if ([string]::IsNullOrWhiteSpace($log))
        {
            continue
        }

        $lines = @(Read-NewLogLines -Path $log)
        foreach ($line in $lines)
        {
            if ([string]::IsNullOrWhiteSpace($line))
            {
                continue
            }

            $trimmed = $line.Trim()
            if ($trimmed -eq "")
            {
                continue
            }

            if ([string]::IsNullOrWhiteSpace($StepPattern) -or $trimmed -match $StepPattern)
            {
                Write-Step "  $trimmed"
            }
        }
    }
}

function Add-RecentInstallerLogs
{
    param(
        [System.Collections.Generic.List[string]]$KnownLogs,
        [string]$LogDirectory,
        [string]$LogPattern,
        [datetime]$NotBefore
    )

    if ([string]::IsNullOrWhiteSpace($LogDirectory) -or [string]::IsNullOrWhiteSpace($LogPattern))
    {
        return ""
    }

    $recent = @(Find-RecentLogs -Directory $LogDirectory -Pattern $LogPattern -NotBefore $NotBefore)
    foreach ($path in $recent)
    {
        if ([string]::IsNullOrWhiteSpace($path))
        {
            continue
        }

        $alreadyKnown = $false
        foreach ($known in $KnownLogs)
        {
            if ($known.Equals($path, [System.StringComparison]::OrdinalIgnoreCase))
            {
                $alreadyKnown = $true
            }
        }

        if (-not $alreadyKnown)
        {
            Write-Step "Reading installer log: $path"
            $KnownLogs.Add($path)
        }
    }

    return (Find-NewestLog -Directory $LogDirectory -Pattern $LogPattern -NotBefore $NotBefore)
}

function Start-QuietInstaller
{
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$LogPath,
        [string]$LogDirectory,
        [string]$LogPattern,
        [string]$StepPattern
    )

    $argumentLine = Format-CommandArguments -Arguments $Arguments
    Write-Step "Command: $FilePath $argumentLine"
    Write-Step "The installer runs with no window. Its log is printed here as steps."

    if (-not [string]::IsNullOrWhiteSpace($LogPath) -and (Test-Path -LiteralPath $LogPath))
    {
        Write-Step "Removing previous log: $LogPath"
        Remove-Item -LiteralPath $LogPath -Force
    }

    $script:LogReadState = @{}
    $startedAt = Get-Date
    $knownLogs = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($LogPath))
    {
        $knownLogs.Add($LogPath)
        Write-Step "Installer log: $LogPath"
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $argumentLine
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    Write-Step "Started process $($process.Id)."

    $nextHeartbeat = $startedAt.AddSeconds(30)
    $newestLog = ""
    do
    {
        $currentNewest = Add-RecentInstallerLogs -KnownLogs $knownLogs -LogDirectory $LogDirectory -LogPattern $LogPattern -NotBefore $startedAt
        if (-not [string]::IsNullOrWhiteSpace($currentNewest) -and ($currentNewest -ne $newestLog))
        {
            $newestLog = $currentNewest
            Write-Step "Newest installer log: $newestLog"
        }

        Write-InstallerLogSteps -Logs @($knownLogs) -StepPattern $StepPattern
        if ((Get-Date) -ge $nextHeartbeat)
        {
            $elapsedMinutes = [math]::Round(((Get-Date) - $startedAt).TotalMinutes, 1)
            Write-Step "Still installing, $elapsedMinutes min elapsed."
            $nextHeartbeat = (Get-Date).AddSeconds(30)
        }
    }
    while (-not $process.WaitForExit(1000))

    Start-Sleep -Milliseconds 500
    $currentNewest = Add-RecentInstallerLogs -KnownLogs $knownLogs -LogDirectory $LogDirectory -LogPattern $LogPattern -NotBefore $startedAt
    if (-not [string]::IsNullOrWhiteSpace($currentNewest) -and ($currentNewest -ne $newestLog))
    {
        Write-Step "Newest installer log: $currentNewest"
    }

    Write-InstallerLogSteps -Logs @($knownLogs) -StepPattern $StepPattern
    Write-Step "Exit code: $($process.ExitCode)"
    foreach ($log in $knownLogs)
    {
        if (-not [string]::IsNullOrWhiteSpace($log) -and (Test-Path -LiteralPath $log))
        {
            Write-Step "Log: $log"
        }
    }

    return [int]$process.ExitCode
}

function Test-InstallerSucceeded
{
    param(
        [int]$ExitCode,
        [string]$ProductName
    )

    if ($ExitCode -eq 0 -or $ExitCode -eq 1638)
    {
        if ($ExitCode -eq 1638)
        {
            Write-Step "$ProductName reports that it is already installed (exit code 1638)."
        }
        else
        {
            Write-Step "$ProductName completed."
        }

        return $true
    }

    $rebootCodes = @(3010, 1641, 5007, 8001)
    if ($rebootCodes -contains $ExitCode)
    {
        $script:RebootRequired = $true
        Write-Step "$ProductName returned exit code $ExitCode, which means a reboot is required."
        return $true
    }

    if ($ExitCode -eq 1618)
    {
        Write-Step "$ProductName could not start because another installation is already running."
    }
    else
    {
        Write-Step "$ProductName failed with exit code $ExitCode."
    }

    return $false
}

function Get-VsWherePath
{
    return (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe")
}

function Invoke-VsWhere
{
    param([string[]]$Arguments)

    $vswhere = Get-VsWherePath
    Write-Step "vswhere: $vswhere"
    if (-not (Test-Path -LiteralPath $vswhere))
    {
        Write-Step "vswhere is missing. Visual Studio or Build Tools is not installed."
        return @()
    }

    Write-Step "vswhere does not list Build Tools unless -products * is passed."
    $argumentLine = Format-CommandArguments -Arguments $Arguments
    Write-Step "Command: $vswhere $argumentLine"

    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $output = & $vswhere @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $savedPreference
    }

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($output))
    {
        if ($null -eq $item)
        {
            continue
        }

        $text = "$item".Trim()
        if ($text -eq "")
        {
            continue
        }

        Write-Step "  $text"
        $paths.Add($text)
    }

    Write-Step "Exit code: $exitCode"
    return @($paths)
}

function Test-CompilerBanner
{
    param([string]$InstallPath)

    $vcvars = Join-Path $InstallPath "VC\Auxiliary\Build\vcvars64.bat"
    Write-Step "vcvars64.bat: $vcvars"
    if (-not (Test-Path -LiteralPath $vcvars))
    {
        Write-Step "vcvars64.bat is missing."
        return $false
    }

    if (-not (Test-Path -LiteralPath $script:DownloadDirectory))
    {
        Write-Step "Creating directory: $script:DownloadDirectory"
        New-Item -ItemType Directory -Path $script:DownloadDirectory -Force | Out-Null
    }

    $launcher = Join-Path $script:DownloadDirectory "show-cl.cmd"
    $bannerFile = Join-Path $script:DownloadDirectory "cl-banner.txt"
    $launcherText = "@echo off`r`ncall `"%~1`"`r`ncl`r`n"
    Write-Step "Writing compiler check: $launcher"
    foreach ($line in ($launcherText -split '\r?\n'))
    {
        if ($line -ne "")
        {
            Write-Step "  $line"
        }
    }

    [System.IO.File]::WriteAllText($launcher, $launcherText)
    if (Test-Path -LiteralPath $bannerFile)
    {
        Remove-Item -LiteralPath $bannerFile -Force
    }

    Write-Step "Success is the Microsoft C/C++ banner. cl.exe may exit 0 or 2 when no source file is given."
    $commandInterpreter = $env:ComSpec
    if ([string]::IsNullOrEmpty($commandInterpreter))
    {
        $commandInterpreter = "cmd.exe"
    }

    $quotedVcvars = '"' + ($vcvars -replace '"', '') + '"'
    $arguments = "/d /c show-cl.cmd $quotedVcvars > cl-banner.txt 2>&1"
    Write-Step "Working directory: $script:DownloadDirectory"
    Write-Step "Command: $commandInterpreter $arguments"

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $commandInterpreter
    $startInfo.Arguments = $arguments
    $startInfo.WorkingDirectory = $script:DownloadDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    Write-Step "Waiting for process $($process.Id) to exit."
    $process.WaitForExit()
    Write-Step "Exit code: $($process.ExitCode)"

    if (-not (Test-Path -LiteralPath $bannerFile))
    {
        Write-Step "The compiler banner file was not created: $bannerFile"
        return $false
    }

    $matched = $false
    foreach ($line in @(Get-Content -LiteralPath $bannerFile))
    {
        Write-Step "  $line"
        if ($line -match 'C/C\+\+')
        {
            $matched = $true
        }
    }

    if (-not $matched)
    {
        Write-Step "The Microsoft C/C++ compiler banner was not found."
    }

    return $matched
}

function Test-TargetingPack
{
    param(
        [string]$DotNetRoot,
        [string]$PackName
    )

    $packRoot = Join-Path (Join-Path $DotNetRoot "packs") $PackName
    Write-Step "Checking targeting pack: $packRoot"
    if (-not (Test-Path -LiteralPath $packRoot))
    {
        Write-Step "Targeting pack is missing: $PackName"
        return $false
    }

    $found = $false
    $directories = @(Get-ChildItem -LiteralPath $packRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($directories.Count -eq 0)
    {
        Write-Step "Targeting pack $PackName has no version folders."
        return $false
    }

    foreach ($directory in $directories)
    {
        Write-Step "  folder: $($directory.Name)"
        if ($directory.Name -like "9.*")
        {
            $found = $true
        }
    }

    if (-not $found)
    {
        Write-Step "Targeting pack $PackName has no 9.x folder."
    }

    return $found
}

function Get-DotNetCandidates
{
    $candidates = New-Object System.Collections.Generic.List[string]
    $programFiles = Get-NativeProgramFiles
    $defaultExecutable = Join-Path $programFiles "dotnet\dotnet.exe"
    Write-Step "Preferred dotnet: $defaultExecutable"
    if (Test-Path -LiteralPath $defaultExecutable)
    {
        $candidates.Add($defaultExecutable)
    }
    else
    {
        Write-Step "Preferred dotnet is not installed."
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command)
    {
        Write-Step "dotnet is not on PATH."
        return @($candidates)
    }

    Write-Step "dotnet on PATH: $($command.Source)"
    $alreadyListed = $false
    foreach ($candidate in $candidates)
    {
        if ($candidate.Equals($command.Source, [System.StringComparison]::OrdinalIgnoreCase))
        {
            $alreadyListed = $true
        }
    }

    if (-not $alreadyListed)
    {
        $candidates.Add($command.Source)
    }

    return @($candidates)
}

function Update-DotNetStatus
{
    param(
        $Report,
        $Missing
    )

    Write-Section ".NET SDK 9.0"
    $candidates = @(Get-DotNetCandidates)
    if ($candidates.Count -eq 0)
    {
        Write-Step ".NET SDK 9.0 is missing."
        $Missing.Add($script:MissingDotNet)
        return
    }

    foreach ($candidate in $candidates)
    {
        Write-Step "Checking $candidate"
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try
        {
            Write-Step "Command: $candidate --list-sdks"
            $output = & $candidate --list-sdks 2>&1
            $exitCode = $LASTEXITCODE
        }
        catch
        {
            Write-Step "dotnet failed: $($_.Exception.Message)"
            continue
        }
        finally
        {
            $ErrorActionPreference = $savedPreference
        }

        Write-Step "Exit code: $exitCode"
        $selectedVersion = ""
        $selectedRoot = ""
        foreach ($item in @($output))
        {
            if ($null -eq $item)
            {
                continue
            }

            $text = "$item".Trim()
            if ($text -eq "")
            {
                continue
            }

            Write-Step "  $text"
            if ($text -notmatch '^(\d+\.\d+\.\d+)\s+\[(.+)\]\s*$')
            {
                continue
            }

            $sdkVersion = $Matches[1]
            try
            {
                $parsed = [version]$sdkVersion
            }
            catch
            {
                continue
            }

            if ($parsed.Major -ne 9)
            {
                continue
            }

            if ($selectedVersion -eq "" -or $parsed -gt ([version]$selectedVersion))
            {
                $selectedVersion = $sdkVersion
                $selectedRoot = Split-Path -Parent ($Matches[2].Trim())
            }
        }

        if ($selectedVersion -eq "")
        {
            Write-Step "No 9.x SDK was reported by $candidate"
            continue
        }

        Write-Step "Selected SDK $selectedVersion at $selectedRoot"
        $corePack = Test-TargetingPack -DotNetRoot $selectedRoot -PackName "Microsoft.NETCore.App.Ref"
        $desktopPack = Test-TargetingPack -DotNetRoot $selectedRoot -PackName "Microsoft.WindowsDesktop.App.Ref"
        $aspNetPack = Test-TargetingPack -DotNetRoot $selectedRoot -PackName "Microsoft.AspNetCore.App.Ref"
        if ($corePack -and $desktopPack -and $aspNetPack)
        {
            $Report.DotNetOk = $true
            $Report.DotNetExe = $candidate
            $Report.DotNetSdkVersion = $selectedVersion
            Write-Step ".NET SDK 9.0 is present."
            return
        }

        Write-Step "SDK $selectedVersion is missing one or more 9.x targeting packs."
    }

    Write-Step ".NET SDK 9.0 is missing."
    $Missing.Add($script:MissingDotNet)
}

function Update-VisualCppStatus
{
    param(
        $Report,
        $Missing
    )

    Write-Section "Visual C++ toolset"
    $componentArguments = @(
        "-latest",
        "-products", "*",
        "-requires", $script:VisualCppComponent,
        "-property", "installationPath"
    )
    $componentPaths = @(Invoke-VsWhere -Arguments $componentArguments)
    $installPath = ""
    foreach ($path in $componentPaths)
    {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path))
        {
            $installPath = $path
            break
        }
    }

    $versionArguments = @(
        "-latest",
        "-products", "*",
        "-version", "[17.0,18.0)",
        "-property", "installationPath"
    )
    $versionPaths = @(Invoke-VsWhere -Arguments $versionArguments)
    foreach ($path in $versionPaths)
    {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path))
        {
            $Report.VisualStudio2022Path = $path
            Write-Step "Visual Studio 2022 instance: $path"
            break
        }
    }

    if ($Report.VisualStudio2022Path -eq "")
    {
        Write-Step "No Visual Studio 2022 instance was found."
    }

    if ($installPath -eq "")
    {
        Write-Step "The Visual C++ x64 toolset is missing."
        $Missing.Add($script:MissingVisualCpp)
        return
    }

    Write-Step "Visual C++ component found at $installPath"
    if (Test-CompilerBanner -InstallPath $installPath)
    {
        $Report.MsvcOk = $true
        $Report.MsvcPath = $installPath
        Write-Step "The Visual C++ x64 toolset is present."
        return
    }

    Write-Step "The Visual C++ x64 toolset is missing."
    $Missing.Add($script:MissingVisualCpp)
}

function Update-WindowsSdkStatus
{
    param(
        $Report,
        $Missing
    )

    Write-Section "Windows SDK"
    $kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\Include"
    Write-Step "Kit root: $kitRoot"
    Write-Step "Required version: $($script:WindowsSdkMinimum) or newer."
    if (-not (Test-Path -LiteralPath $kitRoot))
    {
        Write-Step "Windows SDK include directory is missing."
        $Missing.Add($script:MissingWindowsSdk)
        return
    }

    $best = $null
    $directories = @(Get-ChildItem -LiteralPath $kitRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($directories.Count -eq 0)
    {
        Write-Step "Windows SDK include directory has no version folders."
        $Missing.Add($script:MissingWindowsSdk)
        return
    }

    foreach ($directory in $directories)
    {
        try
        {
            $parsed = [version]$directory.Name
        }
        catch
        {
            Write-Step "Ignoring unparsable folder: $($directory.Name)"
            continue
        }

        if ($parsed -ge $script:WindowsSdkMinimum)
        {
            Write-Step "Found acceptable SDK: $($directory.Name)"
            if ($null -eq $best -or $parsed -gt $best)
            {
                $best = $parsed
            }
        }
        else
        {
            Write-Step "Found older SDK: $($directory.Name)"
        }
    }

    if ($null -eq $best)
    {
        Write-Step "Windows SDK $($script:WindowsSdkMinimum) or newer is missing."
        $Missing.Add($script:MissingWindowsSdk)
        return
    }

    $Report.WindowsSdkOk = $true
    $Report.WindowsSdkVersion = $best.ToString()
    Write-Step "Selected Windows SDK: $($Report.WindowsSdkVersion)"
}

function Get-WixInstallDirectories
{
    $directories = New-Object System.Collections.Generic.List[string]
    $roots = New-Object System.Collections.Generic.List[string]
    $nativeProgramFiles = Get-NativeProgramFiles
    if (-not [string]::IsNullOrWhiteSpace($nativeProgramFiles))
    {
        $roots.Add($nativeProgramFiles)
    }

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)}))
    {
        $alreadyListed = $false
        foreach ($root in $roots)
        {
            if ($root.Equals(${env:ProgramFiles(x86)}, [System.StringComparison]::OrdinalIgnoreCase))
            {
                $alreadyListed = $true
            }
        }

        if (-not $alreadyListed)
        {
            $roots.Add(${env:ProgramFiles(x86)})
        }
    }

    foreach ($root in $roots)
    {
        if (-not (Test-Path -LiteralPath $root))
        {
            continue
        }

        $found = @(Get-ChildItem -LiteralPath $root -Directory -Filter "WiX Toolset*" -ErrorAction SilentlyContinue)
        foreach ($directory in $found)
        {
            $directories.Add($directory.FullName)
        }
    }

    return @($directories)
}

function Get-WixExecutableCandidates
{
    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($directory in @(Get-WixInstallDirectories))
    {
        if ([string]::IsNullOrWhiteSpace($directory))
        {
            continue
        }

        $executable = Join-Path $directory "bin\wix.exe"
        Write-Step "Checking $executable"
        if (Test-Path -LiteralPath $executable)
        {
            $candidates.Add($executable)
        }
        else
        {
            Write-Step "wix.exe is not in that folder."
        }
    }

    $uninstallRoots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($uninstallRoot in $uninstallRoots)
    {
        $items = @(Get-ItemProperty -Path $uninstallRoot -ErrorAction SilentlyContinue)
        foreach ($item in $items)
        {
            $displayName = "$($item.DisplayName)"
            if ($displayName -notlike "*WiX Toolset*")
            {
                continue
            }

            $location = "$($item.InstallLocation)".Trim()
            if ([string]::IsNullOrWhiteSpace($location))
            {
                continue
            }

            $executable = Join-Path $location "bin\wix.exe"
            if (-not (Test-Path -LiteralPath $executable))
            {
                $executable = Join-Path $location "wix.exe"
            }

            if (-not (Test-Path -LiteralPath $executable))
            {
                continue
            }

            $alreadyListed = $false
            foreach ($candidate in $candidates)
            {
                if ($candidate.Equals($executable, [System.StringComparison]::OrdinalIgnoreCase))
                {
                    $alreadyListed = $true
                }
            }

            if (-not $alreadyListed)
            {
                Write-Step "Uninstall entry '$displayName' points at $executable"
                $candidates.Add($executable)
            }
        }
    }

    $command = Get-Command wix -ErrorAction SilentlyContinue
    if ($null -eq $command)
    {
        Write-Step "wix is not on PATH."
        return @($candidates)
    }

    Write-Step "wix on PATH: $($command.Source)"
    $alreadyListed = $false
    foreach ($candidate in $candidates)
    {
        if ($candidate.Equals($command.Source, [System.StringComparison]::OrdinalIgnoreCase))
        {
            $alreadyListed = $true
        }
    }

    if (-not $alreadyListed)
    {
        $candidates.Add($command.Source)
    }

    return @($candidates)
}

function Get-WixToolsetVersion
{
    param([string]$Executable)

    Write-Step "Command: $Executable --version"
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        $output = & $Executable --version 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch
    {
        Write-Step "wix failed: $($_.Exception.Message)"
        return ""
    }
    finally
    {
        $ErrorActionPreference = $savedPreference
    }

    Write-Step "Exit code: $exitCode"
    $version = ""
    foreach ($item in @($output))
    {
        if ($null -eq $item)
        {
            continue
        }

        $text = "$item".Trim()
        if ($text -eq "")
        {
            continue
        }

        Write-Step "  $text"
        if ($version -eq "" -and $text -match '(\d+\.\d+\.\d+)')
        {
            $version = $Matches[1]
        }
    }

    return $version
}

function Find-WixExecutableInLog
{
    param([string]$LogPath)

    if (-not (Test-Path -LiteralPath $LogPath))
    {
        return ""
    }

    foreach ($line in @(Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue))
    {
        if ($line -match '([A-Za-z]:\\[^"\r\n]*?\\wix\.exe)')
        {
            $candidate = $Matches[1].Trim()
            if (Test-Path -LiteralPath $candidate)
            {
                Write-Step "Install log points at $candidate"
                return $candidate
            }
        }
    }

    return ""
}

function Update-WixStatus
{
    param(
        $Report,
        $Missing
    )

    Write-Section "WiX Toolset $($script:WixVersion)"
    Write-Step "Required version: $($script:WixVersion)."
    $candidates = @(Get-WixExecutableCandidates)
    foreach ($candidate in $candidates)
    {
        if ([string]::IsNullOrWhiteSpace($candidate))
        {
            continue
        }

        $version = Get-WixToolsetVersion -Executable $candidate
        if ($version -eq $script:WixVersion)
        {
            $Report.WixOk = $true
            $Report.WixExe = $candidate
            $Report.WixVersion = $version
            Write-Step "WiX Toolset $($script:WixVersion) is present."
            return
        }

        if ($version -eq "")
        {
            Write-Step "Could not read the WiX version from $candidate"
        }
        else
        {
            Write-Step "Found WiX $version at $candidate. This build needs $($script:WixVersion)."
        }
    }

    Write-Step "WiX Toolset $($script:WixVersion) is missing."
    $Missing.Add($script:MissingWix)
}

function Get-RequirementReport
{
    $report = [pscustomobject]@{
        DotNetOk = $false
        MsvcOk = $false
        WindowsSdkOk = $false
        WixOk = $false
        DotNetExe = ""
        DotNetSdkVersion = ""
        MsvcPath = ""
        VisualStudio2022Path = ""
        WindowsSdkVersion = ""
        WixExe = ""
        WixVersion = ""
        Missing = @()
    }

    $missing = New-Object System.Collections.Generic.List[string]
    Update-DotNetStatus -Report $report -Missing $missing
    Update-VisualCppStatus -Report $report -Missing $missing
    Update-WindowsSdkStatus -Report $report -Missing $missing
    Update-WixStatus -Report $report -Missing $missing
    $report.Missing = @($missing)
    return $report
}

function Write-RequirementSummary
{
    param($Report)

    Write-Section "Summary"
    if ($Report.DotNetOk)
    {
        Write-Step ".NET SDK 9: present ($($Report.DotNetSdkVersion), $($Report.DotNetExe))"
    }
    else
    {
        Write-Step ".NET SDK 9: missing"
    }

    if ($Report.MsvcOk)
    {
        Write-Step "Visual C++ toolset: present ($($Report.MsvcPath))"
    }
    else
    {
        Write-Step "Visual C++ toolset: missing"
    }

    if ($Report.WindowsSdkOk)
    {
        Write-Step "Windows SDK: present ($($Report.WindowsSdkVersion))"
    }
    else
    {
        Write-Step "Windows SDK: missing"
    }

    if ($Report.WixOk)
    {
        Write-Step "WiX Toolset: present ($($Report.WixVersion), $($Report.WixExe))"
    }
    else
    {
        Write-Step "WiX Toolset: missing"
    }
}

function Test-RequirementsMet
{
    param($Report)

    return ($Report.DotNetOk -and $Report.MsvcOk -and $Report.WindowsSdkOk -and $Report.WixOk)
}

function Write-DotNetInstallWrapper
{
    param(
        [string]$WrapperPath,
        [string]$InstallerPath,
        [string]$InstallDirectory
    )

    $template = @'
function Write-Host
{
    [CmdletBinding()]
    param(
        [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
        [object[]]$Object,
        [switch]$NoNewline,
        [object]$ForegroundColor,
        [object]$BackgroundColor,
        [object]$Separator
    )

    if ($null -eq $Object)
    {
        return
    }

    $text = ($Object | ForEach-Object { "$_" }) -join " "
    Write-Output $text
}

& "__INSTALLER__" -Channel 9.0 -Architecture x64 -InstallDir "__DIRECTORY__"
exit $LASTEXITCODE
'@
    $content = $template.Replace("__INSTALLER__", $InstallerPath).Replace("__DIRECTORY__", $InstallDirectory)
    Set-Content -LiteralPath $WrapperPath -Value $content -Encoding UTF8
}

function Install-DotNetSdk
{
    Write-Section "Install .NET SDK 9.0"
    Write-Step "The dotnet-install script downloads the SDK archive and extracts it. It does not use Windows Installer."
    $installer = Join-Path $script:DownloadDirectory "dotnet-install.ps1"
    Save-WebFile -Url $script:DotNetInstallScriptUrl -Destination $installer
    $dotNetDirectory = Join-Path (Get-NativeProgramFiles) "dotnet"
    $wrapper = Join-Path $script:DownloadDirectory "run-dotnet-install.ps1"
    Write-DotNetInstallWrapper -WrapperPath $wrapper -InstallerPath $installer -InstallDirectory $dotNetDirectory
    $powershell = Get-PowerShellExecutable
    Write-Step "Command: $powershell -NoProfile -ExecutionPolicy Bypass -File `"$wrapper`""
    Write-Step "Install directory: $dotNetDirectory"

    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try
    {
        & $powershell -NoProfile -ExecutionPolicy Bypass -File $wrapper 2>&1 | ForEach-Object {
            if ($null -eq $_)
            {
                return
            }

            $text = "$_".TrimEnd()
            if ($text -ne "")
            {
                Write-Step "  $text"
            }
        }
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $savedPreference
    }

    if ($null -eq $exitCode)
    {
        $exitCode = 1
    }

    Write-Step "Exit code: $exitCode"
    if ($exitCode -ne 0)
    {
        Write-Step ".NET SDK 9.0 install script failed with exit code $exitCode."
        Stop-RequirementScript -ExitCode 1
    }

    Write-Step ".NET SDK 9.0 install script completed."

    $dotNetExecutable = Join-Path $dotNetDirectory "dotnet.exe"
    if (Test-Path -LiteralPath $dotNetExecutable)
    {
        Write-Step "Installed dotnet: $dotNetExecutable"
        Add-MachinePathEntry -Entry $dotNetDirectory
    }
    else
    {
        Write-Step "dotnet.exe was not found at $dotNetExecutable after the installer exited."
    }
}

function Get-BuildToolsBootstrapper
{
    $destination = Join-Path $script:DownloadDirectory "vs_buildtools.exe"
    Save-WebFile -Url $script:BuildToolsUrl -Destination $destination
    return $destination
}

function Install-WindowsSdk
{
    Write-Section "Install Windows SDK"
    Write-Step "The Windows SDK installs with no window. Progress is printed from its log."
    $destination = Join-Path $script:DownloadDirectory "winsdksetup.exe"
    Save-WebFile -Url $script:WindowsSdkUrl -Destination $destination
    $logPath = Join-Path $script:DownloadDirectory "winsdk.log"
    $exitCode = Start-QuietInstaller -FilePath $destination -Arguments @(
        "/features",
        "OptionId.DesktopCPPx64",
        "/quiet",
        "/norestart",
        "/log",
        $logPath) -LogPath $logPath -StepPattern $script:WindowsSdkStepPattern
    if (-not (Test-InstallerSucceeded -ExitCode $exitCode -ProductName "Windows SDK"))
    {
        Stop-RequirementScript -ExitCode 1
    }
}

function Install-VisualCppBuildTools
{
    param($Report)

    Write-Section "Install Visual C++ build tools"
    Write-Step "Only the C++ x64/x86 build tools are selected. The installer runs with no window."
    $arguments = New-Object System.Collections.Generic.List[string]
    $installPath = $Report.VisualStudio2022Path
    $installerPath = ""
    if (-not [string]::IsNullOrWhiteSpace($installPath))
    {
        Write-Step "Modifying the Visual Studio 2022 instance at $installPath"
        $setup = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\setup.exe"
        $arguments.Add("modify")
        $arguments.Add("--installPath")
        $arguments.Add($installPath)
        if (Test-Path -LiteralPath $setup)
        {
            $installerPath = $setup
        }
        else
        {
            Write-Step "Visual Studio setup.exe was not found: $setup"
            $installerPath = Get-BuildToolsBootstrapper
        }
    }
    else
    {
        Write-Step "No Visual Studio 2022 instance was found. Installing Build Tools 2022."
        $installerPath = Get-BuildToolsBootstrapper
    }

    Write-Step "Component: $($script:VisualCppComponent)"
    $arguments.Add("--add")
    $arguments.Add($script:VisualCppComponent)
    $arguments.Add("--quiet")
    $arguments.Add("--wait")
    $arguments.Add("--norestart")

    $exitCode = Start-QuietInstaller -FilePath $installerPath -Arguments @($arguments) -LogDirectory $env:TEMP -LogPattern "dd_*.log" -StepPattern $script:VisualStudioStepPattern
    if (-not (Test-InstallerSucceeded -ExitCode $exitCode -ProductName "Visual Studio Build Tools"))
    {
        Stop-RequirementScript -ExitCode 1
    }
}

function Install-VisualCppToolchain
{
    param($Report)

    if (-not $Report.WindowsSdkOk)
    {
        Install-WindowsSdk
    }
    else
    {
        Write-Step "Skipping the Windows SDK install; the check passed."
    }

    if (-not $Report.MsvcOk)
    {
        Install-VisualCppBuildTools -Report $Report
    }
    else
    {
        Write-Step "Skipping the Visual C++ toolset install; the check passed."
    }
}

function Install-Wix
{
    Write-Section "Install WiX Toolset $($script:WixVersion)"
    Write-Step "The official signed WiX installer runs with no window. Windows trusts this copy because it is installed into Program Files."
    $destination = Join-Path $script:DownloadDirectory "wix-cli-x64.msi"
    Save-WebFile -Url $script:WixCliUrl -Destination $destination
    $logPath = Join-Path $script:DownloadDirectory "wix-cli.log"
    $msiexec = Join-Path $env:SystemRoot "System32\msiexec.exe"
    $exitCode = Start-QuietInstaller -FilePath $msiexec -Arguments @(
        "/i",
        $destination,
        "/qn",
        "/norestart",
        "/l*v",
        $logPath) -LogPath $logPath -StepPattern $script:MsiStepPattern
    if (-not (Test-InstallerSucceeded -ExitCode $exitCode -ProductName "WiX Toolset"))
    {
        Stop-RequirementScript -ExitCode 1
    }

    $executable = ""
    foreach ($candidate in @(Get-WixExecutableCandidates))
    {
        if ([string]::IsNullOrWhiteSpace($candidate))
        {
            continue
        }

        $version = Get-WixToolsetVersion -Executable $candidate
        if ($version -eq $script:WixVersion)
        {
            $executable = $candidate
            break
        }
    }

    if ($executable -eq "")
    {
        $executable = Find-WixExecutableInLog -LogPath $logPath
        if ($executable -ne "")
        {
            $version = Get-WixToolsetVersion -Executable $executable
            if ($version -ne $script:WixVersion)
            {
                Write-Step "The installer produced WiX '$version', which is not $($script:WixVersion)."
                $executable = ""
            }
        }
    }

    if ($executable -eq "")
    {
        Write-Step "wix.exe $($script:WixVersion) was not found after the installer exited."
        Stop-RequirementScript -ExitCode 1
    }

    $binDirectory = Split-Path -Parent $executable
    Add-MachinePathEntry -Entry $binDirectory
    Write-Step "Installed WiX: $executable"
}

function Wait-ElevatedExitCode
{
    $deadline = (Get-Date).AddHours(3)
    $nextHeartbeat = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline)
    {
        if (Test-Path -LiteralPath $script:ExitCodePath)
        {
            $text = (Get-Content -LiteralPath $script:ExitCodePath -Raw).Trim()
            $parsed = 0
            if ([int]::TryParse($text, [ref]$parsed))
            {
                return $parsed
            }

            Write-Host "[$(Get-Date -Format "HH:mm:ss")] Elevated exit code file was not a number: $text"
            return 1
        }

        if (Test-Path -LiteralPath $script:PidPath)
        {
            $pidText = (Get-Content -LiteralPath $script:PidPath -Raw).Trim()
            $elevatedProcessId = 0
            if ([int]::TryParse($pidText, [ref]$elevatedProcessId))
            {
                $elevatedProcess = Get-Process -Id $elevatedProcessId -ErrorAction SilentlyContinue
                if ($null -eq $elevatedProcess)
                {
                    Start-Sleep -Seconds 2
                    if (Test-Path -LiteralPath $script:ExitCodePath)
                    {
                        continue
                    }

                    Write-Host "[$(Get-Date -Format "HH:mm:ss")] The elevated script ended without an exit code."
                    return 1
                }
            }
        }

        if ((Get-Date) -ge $nextHeartbeat)
        {
            Write-Host "[$(Get-Date -Format "HH:mm:ss")] Waiting for the elevated install to finish."
            $nextHeartbeat = (Get-Date).AddSeconds(30)
        }

        Start-Sleep -Seconds 2
    }

    Write-Host "[$(Get-Date -Format "HH:mm:ss")] Timed out waiting for the elevated script."
    return 1
}

function Write-ElevatedLog
{
    Write-Host ""
    Write-Host "----- Elevated install log ($script:LogPath) -----"
    if (-not (Test-Path -LiteralPath $script:LogPath))
    {
        Write-Host "The elevated install log was not found: $script:LogPath"
        return
    }

    $logLines = @(Get-Content -LiteralPath $script:LogPath)
    foreach ($logLine in $logLines)
    {
        Write-Host $logLine
    }
}

function Restart-Elevated
{
    param(
        $Report,
        [string]$Reason
    )

    if ([string]::IsNullOrWhiteSpace($Reason))
    {
        Write-Step "Missing requirements:"
        foreach ($item in @($Report.Missing))
        {
            Write-Step "  - $item"
        }

        $Reason = "Administrator permission is required to install the missing requirements."
    }

    if (Test-Path -LiteralPath $script:ExitCodePath)
    {
        Remove-Item -LiteralPath $script:ExitCodePath -Force
    }

    if (Test-Path -LiteralPath $script:PidPath)
    {
        Remove-Item -LiteralPath $script:PidPath -Force
    }

    $powershell = Get-PowerShellExecutable
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Write-Step $Reason
    Write-Step "Approve the User Account Control prompt to restart this script elevated."
    Write-Step "Command: $powershell $argumentLine"

    try
    {
        $process = Start-Process -FilePath $powershell -ArgumentList $argumentLine -Verb RunAs -Wait -PassThru
    }
    catch
    {
        Write-Step "Elevation was cancelled or failed: $($_.Exception.Message)"
        exit 1
    }

    $exitCode = 1
    if (Test-Path -LiteralPath $script:ExitCodePath)
    {
        $text = (Get-Content -LiteralPath $script:ExitCodePath -Raw).Trim()
        $parsed = 0
        if ([int]::TryParse($text, [ref]$parsed))
        {
            $exitCode = $parsed
        }
    }
    else
    {
        $stillRunning = $false
        if ($null -ne $process)
        {
            try
            {
                $stillRunning = -not $process.HasExited
            }
            catch
            {
                $stillRunning = $false
            }
        }

        if ($stillRunning)
        {
            try
            {
                $process.WaitForExit()
            }
            catch
            {
                Write-Step "The elevated process handle could not be waited on: $($_.Exception.Message)"
            }
        }

        $exitCode = Wait-ElevatedExitCode
    }

    Write-ElevatedLog
    exit $exitCode
}

function Write-HostFacts
{
    Write-Section "Host"
    Write-Step "Script: $PSCommandPath"
    Write-Step "User: $env:USERDOMAIN\$env:USERNAME"
    Write-Step "Elevated: $(Test-IsAdministrator)"
    Write-Step "OS: $([Environment]::OSVersion.VersionString)"
    Write-Step "64-bit OS: $([Environment]::Is64BitOperatingSystem)"
    Write-Step "64-bit process: $([Environment]::Is64BitProcess)"
    Write-Step "PowerShell: $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
    Write-Step "Log: $script:LogPath"
    $protocols = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    [Net.ServicePointManager]::SecurityProtocol = $protocols
    Write-Step "TLS protocols: $([Net.ServicePointManager]::SecurityProtocol)"
}

function Complete-IfReady
{
    param($Report)

    Write-RequirementSummary -Report $Report
    if (-not (Test-RequirementsMet -Report $Report))
    {
        return $false
    }

    Update-SessionPath
    if ($script:RebootRequired)
    {
        Write-Step "The installer requested a reboot. The checks passed; a reboot is still recommended."
    }

    Write-Step "All requirements are present." -Color Green
    Stop-RequirementScript -ExitCode 0
    return $true
}

function Invoke-RequirementInstall
{
    Initialize-Log
    Write-HostFacts
    if (Test-IsAdministrator)
    {
        Write-Step "Recording process id $PID"
        Set-Content -LiteralPath $script:PidPath -Value "$PID" -Encoding ASCII
    }

    if (-not [Environment]::Is64BitOperatingSystem)
    {
        Write-Step "This repository builds for win-x64 and needs a 64-bit edition of Windows."
        Stop-RequirementScript -ExitCode 1
    }

    Write-Section "Check"
    $report = Get-RequirementReport
    if (Test-IsWindowsSandbox)
    {
        if (-not (Test-IsAdministrator))
        {
            Restart-Elevated -Report $report -Reason "Administrator permission is required to turn off Smart App Control in this sandbox."
            return
        }

        Disable-SandboxSmartAppControl
    }

    if (Complete-IfReady -Report $report)
    {
        return
    }

    if (-not (Test-IsAdministrator))
    {
        Restart-Elevated -Report $report
        return
    }

    if (-not $report.DotNetOk)
    {
        Install-DotNetSdk
    }
    else
    {
        Write-Step "Skipping the .NET SDK install; the check passed."
    }

    if (-not $report.WixOk)
    {
        Install-Wix
    }
    else
    {
        Write-Step "Skipping the WiX install; the check passed."
    }

    if ((-not $report.MsvcOk) -or (-not $report.WindowsSdkOk))
    {
        Install-VisualCppToolchain -Report $report
    }
    else
    {
        Write-Step "Skipping the Visual C++ and Windows SDK install; the checks passed."
    }

    Update-SessionPath
    Write-Section "Re-check"
    $report = Get-RequirementReport
    if (Complete-IfReady -Report $report)
    {
        return
    }

    if ($script:RebootRequired)
    {
        Write-Step "A reboot is required before every requirement can be verified. Reboot, then run this script again."
    }

    Write-Step "Requirements are still missing."
    Stop-RequirementScript -ExitCode 1
}

try
{
    Invoke-RequirementInstall
}
catch
{
    if (-not (Test-Path -LiteralPath $script:LogPath))
    {
        Initialize-Log
    }

    Write-Step "Failed: $($_.Exception.Message)"
    if ($_.ScriptStackTrace)
    {
        Write-Step $_.ScriptStackTrace
    }

    if (Test-IsAdministrator)
    {
        Stop-RequirementScript -ExitCode 1
    }

    Write-Step "Exit code: 1"
    exit 1
}
