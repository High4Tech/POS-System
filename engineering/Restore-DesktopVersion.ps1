[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$VersionId,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
if(Test-Path -LiteralPath $target){throw 'Restore to a new, empty directory.'}
if($target -eq 'D:\SupermarketPos' -or $target.StartsWith('D:\SupermarketPos\',[StringComparison]::OrdinalIgnoreCase)){throw 'Do not restore an obsolete application inside the live store directory.'}
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
try {
    $reader=[IO.StreamReader]::new($zip.GetEntry('manifest.json').Open())
    try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    if($manifest.Format -ne 1){throw 'Unsupported archive format.'}
    $version=@($manifest.Sources|Where-Object {$_.Id -ceq $VersionId})
    if($version.Count -ne 1){throw 'Choose an exact version ID from manifest.json.'}
    New-Item -ItemType Directory -Path $target | Out-Null
    foreach($file in $version[0].Files) {
        $path=[IO.Path]::GetFullPath((Join-Path $target $file.Path))
        if(!$path.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe archived path.'}
        if($file.Sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Invalid archived content identity.'}
        $entry=$zip.GetEntry('blobs/'+$file.Sha256);if($entry.Length -ne $file.Bytes){throw 'Archived length mismatch.'}
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
        $incoming=$entry.Open();$output=[IO.File]::Create($path)
        try{$incoming.CopyTo($output)}finally{$output.Dispose();$incoming.Dispose()}
        if((Get-FileHash -LiteralPath $path).Hash -ine $file.Sha256){throw 'Restored content differs from its archived hash.'}
    }
    Write-Output ('Verified exact restore: '+$VersionId+' ('+$version[0].Files.Count+' files). No application was launched.')
} finally {$zip.Dispose()}
