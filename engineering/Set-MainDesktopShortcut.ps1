[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Target,
    [string]$DesktopDirectory=[Environment]::GetFolderPath('DesktopDirectory')
)
$ErrorActionPreference='Stop'
$targetPath=(Resolve-Path -LiteralPath $Target).Path
$desktop=(Resolve-Path -LiteralPath $DesktopDirectory).Path
$folder=Split-Path -Parent $targetPath
$icon=Join-Path $folder 'Assets/app.ico'
if(-not(Test-Path -LiteralPath $icon -PathType Leaf)){throw 'The installed application icon is missing.'}
$shell=New-Object -ComObject WScript.Shell
$path=Join-Path $desktop 'ORMA.lnk'
if(Test-Path -LiteralPath $path){
    if($shell.CreateShortcut($path).TargetPath -ne $targetPath){throw 'An unrelated ORMA desktop shortcut already exists.'}
}
$link=$shell.CreateShortcut($path)
$link.TargetPath=$targetPath;$link.WorkingDirectory=$folder;$link.IconLocation=$icon+',0';$link.Description='ORMA POS';$link.Save()
$saved=$shell.CreateShortcut($path)
if($saved.TargetPath -ne $targetPath -or $saved.IconLocation -ne $icon+',0'){throw 'ORMA shortcut verification failed.'}
$old=Join-Path $desktop 'Wijgandu.lnk'
if((Test-Path -LiteralPath $old)-and $shell.CreateShortcut($old).TargetPath -eq $targetPath){Remove-Item -LiteralPath $old}
$path
