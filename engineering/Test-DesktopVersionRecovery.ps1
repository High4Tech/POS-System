[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testRoot=Join-Path $temp ('pos-recovery-selfcheck-'+[guid]::NewGuid().ToString('N'))
$root=Join-Path $testRoot 'store'
try {
    foreach($folder in @('App','App-before-test','Backups/Before-test','seed')) {
        New-Item -ItemType Directory -Path (Join-Path $root $folder) -Force|Out-Null
    }
    Set-Content -LiteralPath (Join-Path $root 'App/live.txt') -Value 'keep live app'
    foreach($folder in @('App-before-test','Backups/Before-test','seed')) {
        Set-Content -LiteralPath (Join-Path $root ($folder+'/SupermarketPos.dll')) -Value 'same application bytes'
    }
    $database=Join-Path $root 'Backups/Before-test/pos.db'
    Set-Content -LiteralPath $database -Value 'private business data'
    $databaseHash=(Get-FileHash -LiteralPath $database).Hash
    $old=Join-Path $root 'Backups/Previous-application-20260101.zip'
    $latest=Join-Path $root 'Backups/Previous-application-20260102.zip'
    foreach($path in @($old,$latest)){[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'seed'),$path)}
    $archive=Join-Path $testRoot 'versions.zip'
    & (Join-Path $PSScriptRoot 'Archive-DesktopVersions.ps1') -Root $root -Output $archive
    $manifest=Get-Content -LiteralPath ($archive+'.manifest.json') -Raw|ConvertFrom-Json
    if($manifest.Sources.Count -ne 3 -or $manifest.UniqueBlobs -ne 1){throw 'Expected three versions sharing one content blob.'}
    if(@($manifest.Sources.Files|Where-Object {$_.Path -match '\.db($|-)'}).Count){throw 'Business data entered the archive.'}
    $restore=Join-Path $testRoot 'restore'
    & (Join-Path $PSScriptRoot 'Restore-DesktopVersion.ps1') -Archive $archive -VersionId 'App-before-test' -Destination $restore
    if((Get-FileHash -LiteralPath (Join-Path $restore 'SupermarketPos.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'App-before-test/SupermarketPos.dll')).Hash){throw 'Restored bytes changed.'}
    $proof=Join-Path $testRoot 'remote-proof.json'
    $hash=(Get-FileHash -LiteralPath $archive).Hash
    $receipt=@{Repository='High4Tech/POS-System';Release='synthetic self-check';Assets=@(@{Sha256='invalid';RemoteDigest='invalid';Bytes=(Get-Item -LiteralPath $archive).Length})}
    $receipt|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $proof
    $refused=$false
    try{& (Join-Path $PSScriptRoot 'Remove-ArchivedDesktopCopies.ps1') -Root $root -Archive $archive -RemoteProof $proof}catch{$refused=$true}
    if(!$refused -or !(Test-Path -LiteralPath $old) -or !(Test-Path -LiteralPath (Join-Path $root 'App-before-test'))){throw 'Invalid proof must prevent deletion.'}
    $receipt.Assets[0].Sha256=$hash;$receipt.Assets[0].RemoteDigest='sha256:'+$hash
    $receipt|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $proof
    & (Join-Path $PSScriptRoot 'Remove-ArchivedDesktopCopies.ps1') -Root $root -Archive $archive -RemoteProof $proof
    if((Test-Path -LiteralPath $old) -or (Test-Path -LiteralPath (Join-Path $root 'App-before-test')) -or (Test-Path -LiteralPath (Join-Path $root 'Backups/Before-test/SupermarketPos.dll'))){throw 'Obsolete application files remain.'}
    if(!(Test-Path -LiteralPath $latest) -or !(Test-Path -LiteralPath (Join-Path $root 'App/live.txt')) -or (Get-FileHash -LiteralPath $database).Hash -ne $databaseHash){throw 'Live app, recovery copy and business data must remain unchanged.'}
    Write-Output 'Desktop archive, restore and guarded cleanup self-check passed.'
} finally {
    if(Test-Path -LiteralPath $testRoot) {
        $resolved=(Resolve-Path -LiteralPath $testRoot).Path
        if($resolved -ine $testRoot -or !$resolved.StartsWith($temp+'\pos-recovery-selfcheck-',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe self-check cleanup target.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
