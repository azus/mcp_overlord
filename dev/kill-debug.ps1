param(
    [string[]]$ProcessNames = @("mcp_overlord")
)

$ErrorActionPreference = "Continue"
$failed = $false

if (-not ("ProcessElevation" -as [type]))
{
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ProcessElevation
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int HighIntegrityRid = 0x3000;

    public static string Query(int processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return "unknown";
        }

        try
        {
            IntPtr token;
            if (!OpenProcessToken(process, TokenQuery, out token))
            {
                return "unknown";
            }

            try
            {
                return ReadIntegrity(token);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string ReadIntegrity(IntPtr token)
    {
        int needed;
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out needed);
        if (needed <= 0)
        {
            return "unknown";
        }

        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            int returned;
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out returned))
            {
                return "unknown";
            }

            IntPtr sid = Marshal.ReadIntPtr(buffer);
            IntPtr sidText;
            if (!ConvertSidToStringSid(sid, out sidText))
            {
                return "unknown";
            }

            try
            {
                string text = Marshal.PtrToStringUni(sidText) ?? "";
                int dash = text.LastIndexOf('-');
                int rid;
                if (dash < 0 || !int.TryParse(text.Substring(dash + 1), out rid))
                {
                    return "unknown";
                }

                return rid >= HighIntegrityRid ? "elevated" : "normal";
            }
            finally
            {
                LocalFree(sidText);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int length, out int returned);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
'@
}

foreach ($name in $ProcessNames)
{
    $processes = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    foreach ($process in $processes)
    {
        $elevation = [ProcessElevation]::Query($process.Id)
        if ($elevation -eq "elevated")
        {
            Write-Error "Process $($process.ProcessName) (PID $($process.Id)) is running elevated. Run dev\kill-debug.ps1 from an elevated PowerShell."
            $failed = $true
            continue
        }

        if ($elevation -eq "unknown")
        {
            Write-Error "Process $($process.ProcessName) (PID $($process.Id)) could not be queried and was not stopped. Run dev\kill-debug.ps1 from an elevated PowerShell."
            $failed = $true
            continue
        }

        try
        {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch
        {
            $stillThere = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($stillThere)
            {
                Write-Error "Failed to stop $($process.ProcessName) (PID $($process.Id)): $($_.Exception.Message)"
                $failed = $true
            }
        }
    }
}

foreach ($name in $ProcessNames)
{
    $remaining = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0)
    {
        $failed = $true
    }
}

if ($failed)
{
    exit 1
}

exit 0
