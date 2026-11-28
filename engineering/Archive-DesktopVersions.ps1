[CmdletBinding()]
param([string]$Root='D:/SupermarketPos',[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
[Diagnostics.Process]::GetCurrentProcess().PriorityClass='BelowNormal'
$rootPath=(Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
$outputPath=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $outputPath){throw 'Use a new archive file.'}
if($outputPath.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Stage the archive outside the live POS directory.'}
$backupRoot=Join-Path $rootPath 'Backups'
$latest=Get-ChildItem -LiteralPath $backupRoot -Filter 'Previous-application-*.zip' -File | Sort-Object Name -Descending | Select-Object -First 1
$folders=@(Get-ChildItem -LiteralPath $rootPath -Directory -Force | Where-Object {$_.Name -like 'App-before-*' -or $_.Name -like '.update-*'})+
    @(Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object {$_.Name -like 'App-before-*'})
$archives=@(Get-ChildItem -LiteralPath $backupRoot -Filter 'Previous-application-*.zip' -File | Where-Object {$_.FullName -ne $latest.FullName})
$sources=[Collections.Generic.List[object]]::new()
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$appPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$bytes=0L
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($outputPath)) -Force | Out-Null
$zip=[IO.Compression.ZipFile]::Open($outputPath,[IO.Compression.ZipArchiveMode]::Create)
try {
    function Add-Blob([IO.Stream]$Stream,[string]$Name,[long]$Length) {
        $name=$Name.Replace('\','/')
        if($name.StartsWith('/') -or $name.Contains(':') -or $name.Split('/') -contains '..' -or
            $name -match '(?i)(^|/)(Protected|Diagnostics|PDFs|Store Profile|Media)(/|$)|\.db($|-)|\.protected$'){throw "Business data or unsafe path in an app copy: $name"}
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Stream)).ToLowerInvariant()
        if($seen.Add($hash)) {
            $Stream.Position=0
            $entry=$zip.CreateEntry('blobs/'+$hash,[IO.Compression.CompressionLevel]::Fastest)
            $dest=$entry.Open();try{$Stream.CopyTo($dest)}finally{$dest.Dispose()}
        }
        return [pscustomobject]@{Path=$name;Sha256=$hash;Bytes=$Length}
    }
    foreach($folder in $folders | Sort-Object FullName) {
        if($folder.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Archive inputs must not be junctions.'}
        $all=@(Get-ChildItem -LiteralPath $folder.FullName -Recurse -Force)
        if(@($all | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}).Count){throw 'An app copy contains a junction.'}
        $files=@($all | Where-Object {!$_.PSIsContainer})
        $entries=@(foreach($file in $files | Sort-Object FullName){
            $null=$appPaths.Add($file.FullName.Substring($folder.FullName.Length+1).Replace('\','/'))
            $stream=[IO.File]::OpenRead($file.FullName)
            try{Add-Blob $stream $file.FullName.Substring($folder.FullName.Length+1) $file.Length}finally{$stream.Dispose()}
        })
        $assembly=Join-Path $folder.FullName 'SupermarketPos.dll'
        $size=[long](($files | Measure-Object Length -Sum).Sum);$bytes+=$size
        $sources.Add([pscustomobject]@{Id=$folder.FullName.Substring($rootPath.Length+1).Replace('\','/');Kind='directory';LocalPath=$folder.FullName;Bytes=$size;Version=if(Test-Path -LiteralPath $assembly){(Get-Item -LiteralPath $assembly).VersionInfo.ProductVersion}else{''};Files=$entries})
        Write-Output ('Archived app copy: '+$folder.Name)
    }
    foreach($folder in Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object {$_.Name -like 'Before-*' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'SupermarketPos.dll'))}) {
        $all=@(Get-ChildItem -LiteralPath $folder.FullName -Recurse -Force)
        if($folder.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or @($all | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}).Count){throw 'A mixed backup contains a junction.'}
        $files=@($all | Where-Object {!$_.PSIsContainer -and $appPaths.Contains($_.FullName.Substring($folder.FullName.Length+1).Replace('\','/'))})
        $entries=@(foreach($file in $files | Sort-Object FullName){
            $stream=[IO.File]::OpenRead($file.FullName)
            try{Add-Blob $stream $file.FullName.Substring($folder.FullName.Length+1) $file.Length}finally{$stream.Dispose()}
        })
        $size=[long](($files | Measure-Object Length -Sum).Sum);$bytes+=$size
        $sources.Add([pscustomobject]@{Id=$folder.FullName.Substring($rootPath.Length+1).Replace('\','/');Kind='mixed-directory';LocalPath=$folder.FullName;Bytes=$size;Version=(Get-Item -LiteralPath (Join-Path $folder.FullName 'SupermarketPos.dll')).VersionInfo.ProductVersion;Files=$entries})
        Write-Output ('Archived only app files; retained business data: '+$folder.Name)
    }
    foreach($file in $archives | Sort-Object Name) {
        $inputZip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            $entries=@(foreach($entry in $inputZip.Entries | Where-Object {!$_.FullName.EndsWith('/')} | Sort-Object FullName){
                $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
                try{$stream.CopyTo($memory);$memory.Position=0;Add-Blob $memory $entry.FullName $entry.Length}finally{$memory.Dispose();$stream.Dispose()}
            })
        } finally {$inputZip.Dispose()}
        $bytes+=$file.Length
        $sources.Add([pscustomobject]@{Id=$file.FullName.Substring($rootPath.Length+1).Replace('\','/');Kind='zip';LocalPath=$file.FullName;Bytes=$file.Length;ArchiveSha256=(Get-FileHash -LiteralPath $file.FullName).Hash;Files=$entries})
        Write-Output ('Archived application checkpoint: '+$file.Name)
    }
    $manifest=[pscustomobject]@{Format=1;CreatedUtc=[DateTime]::UtcNow.ToString('O');Root=$rootPath;PreservedRecoveryArchive=$latest.FullName;RemovableBytes=$bytes;UniqueBlobs=$seen.Count;Sources=$sources.ToArray()}
    $entry=$zip.CreateEntry('manifest.json',[IO.Compression.CompressionLevel]::Fastest)
    $writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
    try{$writer.Write(($manifest | ConvertTo-Json -Depth 8))}finally{$writer.Dispose()}
} finally {$zip.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($outputPath)
try {
    foreach($entry in $verify.Entries | Where-Object {$_.FullName.StartsWith('blobs/')}) {
        $stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
        if($entry.FullName -ne 'blobs/'+$hash){throw 'Archive verification failed.'}
    }
    if($verify.Entries.Count -ne $seen.Count+1){throw 'Archive entry count mismatch.'}
} finally {$verify.Dispose()}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($outputPath+'.manifest.json')
[pscustomobject]@{Archive=$outputPath;Sha256=(Get-FileHash -LiteralPath $outputPath).Hash;ArchiveBytes=(Get-Item -LiteralPath $outputPath).Length;Sources=$sources.Count;UniqueBlobs=$seen.Count;RemovableBytes=$bytes}|ConvertTo-Json|Set-Content -LiteralPath ($outputPath+'.proof.json')
Get-Content -LiteralPath ($outputPath+'.proof.json')
