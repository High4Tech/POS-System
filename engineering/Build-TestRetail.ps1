[CmdletBinding()]
param(
    [string]$Output,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Vpk,
    [string]$Dotnet='dotnet',
    [switch]$NoRestore,
    [switch]$StandaloneRetail
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Dotnet -PathType Leaf){$env:DOTNET_ROOT=Split-Path -Parent ([IO.Path]::GetFullPath($Dotnet))}
if(-not $Output){
    if([IO.Path]::GetFileName($Version) -ne $Version -or $Version -in @('.', '..')){throw 'Use a version without directory separators.'}
    $Output=if($StandaloneRetail){"D:/Retail POS/Releases/$Version"}else{Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "High4TechPosBuild/artifacts/windows/$Version/test"}
}
$out=[IO.Path]::GetFullPath($Output)
foreach($syncRoot in @($env:OneDrive,$env:OneDriveConsumer,$env:OneDriveCommercial)|Where-Object{$_}){
    $syncRoot=[IO.Path]::GetFullPath($syncRoot).TrimEnd('\')
    if($out -eq $syncRoot-or$out.StartsWith($syncRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Stage builds outside OneDrive; omit Output to use the local build archive.'}
}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if($out -eq $repo-or$out.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Stage builds outside the source checkout.'}
if(Test-Path -LiteralPath $out){throw 'Choose a new, empty test release folder.'}
New-Item -ItemType Directory -Path $out | Out-Null
$previousProcessorCount=$env:DOTNET_PROCESSOR_COUNT
$buildProcess=[Diagnostics.Process]::GetCurrentProcess()
$previousPriority=$buildProcess.PriorityClass
try {
    $env:DOTNET_PROCESSOR_COUNT='2'
    $buildProcess.PriorityClass='BelowNormal'
$payload=Join-Path $out 'app'
$mainExe=if($StandaloneRetail){'Wijgandu.exe'}else{'High4TechPosTest.exe'}
$packId=if($StandaloneRetail){'WijganduRetailApp'}else{'High4TechPosTestApp'}
$title=if($StandaloneRetail){'Wijgandu'}else{'Wijgandu Test'}
& (Join-Path $PSScriptRoot 'Build-LocalDesktop.ps1') -Output $payload -Version $Version -Dotnet $Dotnet -TestRetailEdition:(!$StandaloneRetail) -RetailStandaloneEdition:$StandaloneRetail -NoRestore:$NoRestore
Move-Item -LiteralPath (Join-Path $payload 'SupermarketPos.exe') -Destination (Join-Path $payload $mainExe)
$notes=@'
High4Tech POS Test — isolated Tech Retail edition
Create your local store name, username and password on first start. No cloud signup or activation is required.
The test app installs separately from Tech Store POS. Its data is stored separately in %LOCALAPPDATA%\High4TechPosTest\Data.
Digital services are manual records. Complete the real activation or transfer through your authorized provider; provider references in the POS are optional.
Catalog prices are dated reference data. Confirm the current package, eligibility and your own selling price/provider cost before use.
This local testing installer is unsigned. It is not the final production distribution.
'@
if($StandaloneRetail){$notes="Wijgandu Retail POS`nCreate your store details, logo and local username/password on first start. Store data is separate in %LOCALAPPDATA%\WijganduRetail\Data.`nDigital services record your manual transactions; complete the actual operation through your authorized provider.`nThis installer is unsigned."}
Set-Content -LiteralPath (Join-Path $payload 'EDITION.txt') -Value $notes -Encoding utf8
& $Vpk pack --packId $packId --packTitle $title --packVersion $Version --packDir $payload --mainExe $mainExe --channel testing --runtime win-x64 --icon (Join-Path $repo 'src/SupermarketPos.Wpf/Assets/app.ico') --outputDir (Join-Path $out 'packages')
if($LASTEXITCODE -ne 0){throw 'Test edition packaging failed.'}
$full=@(Get-ChildItem -LiteralPath (Join-Path $out 'packages') -Filter '*-full.nupkg')
if($full.Count -ne 1){throw 'Expected exactly one test release package.'}
$zip=[IO.Compression.ZipFile]::OpenRead($full[0].FullName)
try {
    foreach($file in @($mainExe,'SupermarketPos.dll','SupermarketPos.runtimeconfig.json')) {
        $entry=@($zip.Entries | Where-Object { $_.FullName.Replace('\','/').EndsWith('/'+$file) })
        if($entry.Count -ne 1){throw "Missing packaged test binary: $file"}
        $stream=$entry[0].Open()
        try { $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if($hash -ne (Get-FileHash -LiteralPath (Join-Path $payload $file)).Hash){throw "Packaged hash mismatch: $file"}
    }
} finally { $zip.Dispose() }
Get-ChildItem -LiteralPath (Join-Path $out 'packages') -File | Get-FileHash | Select-Object Path,Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'package-hashes.json')
Write-Output "Verified isolated test installer: $out"

} finally {
    $env:DOTNET_PROCESSOR_COUNT=$previousProcessorCount
    $buildProcess.PriorityClass=$previousPriority
}
