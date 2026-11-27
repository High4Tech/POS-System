[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$DataDirectory,
    [string]$Executable='SupermarketPos.exe',
    [string]$ExpectedTitle='Wijgandu'
)
$ErrorActionPreference='Stop'
$data=[IO.Path]::GetFullPath($DataDirectory)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
if(-not $data.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase)){throw 'Entry checks must use temporary QA data.'}
$folder=(Resolve-Path -LiteralPath $Payload).Path
$exe=(Resolve-Path -LiteralPath (Join-Path $folder $Executable)).Path
New-Item -ItemType Directory -Path $data -Force | Out-Null
if((Get-Item -LiteralPath $data).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Use an ordinary QA directory.'}
if(-not ('WijganduEntryWindows' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WijganduEntryWindows {
    private delegate bool Callback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
    public static string[] Titles(int process) {
        var titles=new List<string>();
        EnumWindows((window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner==process){var text=new StringBuilder(512);GetWindowText(window,text,text.Capacity);if(text.Length>0)titles.Add(text.ToString());}return true;},IntPtr.Zero);
        return titles.ToArray();
    }
}
'@
}
$started=[DateTime]::UtcNow
$entry=Start-Process -FilePath $exe -ArgumentList @('--data-directory',('"{0}"' -f $data)) -WorkingDirectory $folder -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 250
        $entry.Refresh()
        if($entry.HasExited){throw 'The actual executable exited before its WPF window was created.'}
        $titles=@([WijganduEntryWindows]::Titles($entry.Id) | Where-Object {$_.StartsWith($ExpectedTitle,[StringComparison]::Ordinal)})
    } until($titles.Count -gt 0 -or [DateTime]::UtcNow -ge $deadline)
    if($titles.Count -eq 0){throw 'The actual executable did not create the expected WPF window.'}
    Start-Sleep -Seconds 2
    $entry.Refresh()
    if($entry.HasExited){throw 'The executable exited while loading its content.'}
    $reports=@(Get-ChildItem -LiteralPath (Join-Path $data 'Diagnostics') -Filter '*.json' -File -ErrorAction SilentlyContinue | Where-Object {$_.LastWriteTimeUtc -ge $started})
    if($reports.Count){throw 'The actual executable generated a startup/UI diagnostic report.'}
    [pscustomobject]@{Executable=$exe;Version=(Get-Item -LiteralPath $exe).VersionInfo.ProductVersion;ProcessId=$entry.Id;WindowTitles=$titles;DataDirectory=$data;NewDiagnosticReports=$reports.Count} | ConvertTo-Json
} finally {
    $owned=Get-CimInstance Win32_Process -Filter ('ProcessId='+$entry.Id) -ErrorAction SilentlyContinue
    if($owned -and $owned.ExecutablePath -eq $exe -and $owned.CommandLine.Contains($data)){Stop-Process -Id $entry.Id}
}
