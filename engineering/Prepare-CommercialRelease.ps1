[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [Parameter(Mandatory)][uri]$PublisherApi,
 [Parameter(Mandatory)][string]$PublicKeyFile,
 [Parameter(Mandatory)][datetimeoffset]$PublishedAt,
 [ValidateSet('testing','stable')][string]$Channel='testing',
 [string]$Dotnet='dotnet',
 [string]$Support='Contact your software supplier',
 [switch]$CreateInstaller,
 [string]$Vpk='vpk',
 [string]$SigningThumbprint,
 [string]$TimestampUrl='http://timestamp.digicert.com'
)
$ErrorActionPreference='Stop'
$repo=Join-Path $PSScriptRoot '..'
if($PublisherApi.Scheme -ne 'https'){throw 'Commercial API must use HTTPS.'}
$publicScript='const fs=require("node:fs"),c=require("node:crypto");const k=c.createPublicKey(fs.readFileSync(process.argv[1]));if(k.asymmetricKeyType!=="ec"||k.asymmetricKeyDetails.namedCurve!=="prime256v1")throw Error("Use P-256");process.stdout.write(k.export({type:"spki",format:"der"}).toString("base64"));'
$publicBase64=& node -e $publicScript (Resolve-Path -LiteralPath $PublicKeyFile).Path
if($LASTEXITCODE -ne 0){throw 'Invalid public verification key.'}
$output=Join-Path $PSScriptRoot "release-staging\$Channel\$Version"
if(Test-Path -LiteralPath $output){throw 'Use a new version/output folder. Existing release output will not be overwritten.'}
New-Item -ItemType Directory -Path $output | Out-Null
$properties=@("-p:Version=$Version",'-p:CommercialBuild=true',"-p:PublisherApi=$($PublisherApi.AbsoluteUri.TrimEnd('/'))","-p:PublisherPublicKey=$publicBase64","-p:ReleaseChannel=$Channel","-p:ReleasePublishedAt=$($PublishedAt.ToUniversalTime().ToString('O'))","-p:PublisherSupport=$Support")
& $Dotnet publish (Join-Path $repo 'src\SupermarketPos.Wpf\SupermarketPos.Wpf.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $output 'app') @properties
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
if(-not $CreateInstaller){Write-Output "Prepared binaries: $output. No installer has been created.";return}
# Explicit final packaging action; use only when the owner requests the shareable installer.
if($SigningThumbprint -notmatch '^[A-Fa-f0-9]{40}$'){throw 'A code-signing certificate thumbprint is required to package.'}
$cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
if(-not $cert.HasPrivateKey){throw 'The signing certificate must have its private key available.'}
$signParams="/sha1 $SigningThumbprint /s My /fd sha256 /td sha256 /tr $TimestampUrl"
& $Vpk pack --packId SupermarketPos --packVersion $Version --packDir (Join-Path $output 'app') --mainExe SupermarketPos.exe --channel $Channel --runtime win-x64 --outputDir (Join-Path $output 'packages') --signParams $signParams
if($LASTEXITCODE -ne 0){throw 'Velopack packaging/signing failed.'}
$full=@(Get-ChildItem -LiteralPath (Join-Path $output 'packages') -Filter '*-full.nupkg')
if($full.Count -ne 1){throw 'Expected one full release package.'}
$zip=[IO.Compression.ZipFile]::OpenRead($full[0].FullName)
try{
 $entry=@($zip.Entries | Where-Object {$_.FullName.Replace('\','/').EndsWith('/SupermarketPos.exe')})
 if($entry.Count -ne 1){throw 'The main executable was not found in the full package.'}
 $checkFile=Join-Path $output 'verified-packaged-SupermarketPos.exe'
 [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0],$checkFile,$false)
}finally{$zip.Dispose()}
$signature=Get-AuthenticodeSignature -LiteralPath $checkFile
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $SigningThumbprint){throw 'Publisher signature verification failed. Do not publish this output.'}
Write-Output "Signed packaging output: $output. Generate a signed release envelope separately; nothing has been uploaded."
