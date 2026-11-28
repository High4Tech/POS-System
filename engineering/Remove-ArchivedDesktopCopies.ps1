[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$RemoteProof,[string]$Root='D:/SupermarketPos')
$ErrorActionPreference='Stop'
$rootPath=(Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
$archivePath=(Resolve-Path -LiteralPath $Archive).Path
$hash=(Get-FileHash -LiteralPath $archivePath).Hash
$receipt=Get-Content -LiteralPath $RemoteProof -Raw|ConvertFrom-Json
$asset=@($receipt.Assets|Where-Object {$_.Sha256 -ieq $hash -and $_.RemoteDigest -ieq ('sha256:'+$hash) -and $_.Bytes -eq (Get-Item -LiteralPath $archivePath).Length})
if($receipt.Repository -ne 'High4Tech/POS-System' -or $asset.Count -ne 1){throw 'A matching verified GitHub archive is required before deletion.'}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{$reader=[IO.StreamReader]::new($zip.GetEntry('manifest.json').Open());try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}}finally{$zip.Dispose()}
if($manifest.Format -ne 1 -or $manifest.Root -ine $rootPath){throw 'Archive root mismatch.'}
$recovery=(Resolve-Path -LiteralPath $manifest.PreservedRecoveryArchive).Path
if([IO.Path]::GetDirectoryName($recovery) -ine (Join-Path $rootPath 'Backups')){throw 'Recovery checkpoint is outside the backup folder.'}
$running=@(Get-CimInstance Win32_Process -Filter "Name='SupermarketPos.exe' OR Name='Wijgandu.exe'")
$plan=[Collections.Generic.List[object]]::new()
foreach($source in $manifest.Sources) {
    $path=(Resolve-Path -LiteralPath $source.LocalPath).Path
    $expected=[IO.Path]::GetFullPath((Join-Path $rootPath $source.Id))
    if($path -ine $expected -or !$path.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase) -or $path -ieq $recovery){throw 'Invalid removal target.'}
    $item=Get-Item -LiteralPath $path -Force
    if($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Removal target is a junction.'}
    $parent=[IO.Path]::GetDirectoryName($path);$backupRoot=Join-Path $rootPath 'Backups'
    $valid=($source.Kind -eq 'directory' -and (($parent -ieq $rootPath -and ($item.Name -like 'App-before-*' -or $item.Name -like '.update-*')) -or ($parent -ieq $backupRoot -and $item.Name -like 'App-before-*'))) -or
        ($source.Kind -eq 'mixed-directory' -and $parent -ieq $backupRoot -and $item.Name -like 'Before-*') -or
        ($source.Kind -eq 'zip' -and $parent -ieq $backupRoot -and $item.Name -like 'Previous-application-*.zip')
    if(!$valid){throw 'Target is not an obsolete application copy.'}
    if(@($running | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){throw 'An archived app is still running.'}
    if($source.Kind -eq 'zip') {
        if((Get-FileHash -LiteralPath $path).Hash -ine $source.ArchiveSha256){throw 'An application archive changed after preservation.'}
    } else {
        $all=@(Get-ChildItem -LiteralPath $path -Recurse -Force)
        if(@($all | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}).Count){throw 'A removal subtree contains a junction.'}
        if($source.Kind -eq 'directory' -and @($all|Where-Object {!$_.PSIsContainer}).Count -ne $source.Files.Count){throw 'An application copy changed after preservation.'}
        foreach($file in $source.Files) {
            $target=[IO.Path]::GetFullPath((Join-Path $path $file.Path))
            if(!$target.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase) -or $target -match '(?i)\.db($|-)|\.protected$'){throw 'Unsafe application file target.'}
            $entry=Get-Item -LiteralPath $target
            if($entry.Length -ne $file.Bytes -or (Get-FileHash -LiteralPath $target).Hash -ine $file.Sha256){throw 'An app file changed after preservation.'}
        }
    }
    $plan.Add($source)
}
# All inputs are verified before any deletion; mixed backups retain unlisted business files.
$removed=0L
foreach($source in $plan) {
    if($source.Kind -eq 'mixed-directory') {
        foreach($file in $source.Files){Remove-Item -LiteralPath ([IO.Path]::GetFullPath((Join-Path $source.LocalPath $file.Path))) -Force}
    } else {Remove-Item -LiteralPath $source.LocalPath -Recurse -Force}
    $removed+=$source.Bytes
    Write-Output ('Removed archived app copy: '+$source.Id)
}
if(!(Test-Path -LiteralPath $recovery)){throw 'The recovery archive must remain.'}
[pscustomobject]@{RemovedSources=$plan.Count;ReclaimedBytes=$removed;KeptRecovery=$recovery;RemoteRelease=$receipt.Release;ArchiveSha256=$hash}|ConvertTo-Json
