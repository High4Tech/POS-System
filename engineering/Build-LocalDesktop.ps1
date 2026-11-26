[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Version,
    [string]$DataDirectory = 'D:\SupermarketPos',
    [string]$Dotnet = 'dotnet',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$out = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $out) { throw 'Use an empty, new staging directory for each build.' }
$directory = [IO.Path]::GetFullPath($DataDirectory)
if ($out.StartsWith($directory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Stage the build outside the live POS data and application directory.'
}
$revision = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the POS source revision.' }
$filePaths = git -C $repo ls-files --cached --others --exclude-standard -- src
if ($LASTEXITCODE -ne 0) { throw 'Cannot list POS source files.' }
$lines = foreach ($name in ($filePaths | Sort-Object -Unique)) {
    $file = Join-Path $repo $name
    if (Test-Path -LiteralPath $file -PathType Leaf) { "$name $((Get-FileHash -LiteralPath $file).Hash)" }
}
New-Item -ItemType Directory -Path $out | Out-Null
$manifest = "$out.source.sha256"
[IO.File]::WriteAllLines($manifest, [string[]]$lines)
$fingerprint = (Get-FileHash -LiteralPath $manifest).Hash
$builtAt = [DateTime]::UtcNow.ToString('O')
$build = "$Version+g$($revision.Substring(0,7)).$($fingerprint.Substring(0,12).ToLowerInvariant())"
$project = Join-Path $repo 'src\SupermarketPos.Wpf\SupermarketPos.Wpf.csproj'
$restoreOptions = if($NoRestore) { ,@('--no-restore') } else { ,@() }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $out @restoreOptions "-p:Version=$Version" `
    '-p:IncludeSourceRevisionInInformationalVersion=false' "-p:InformationalVersion=$build" `
    "-p:BuildTimestampUtc=$builtAt" "-p:BuildSourceFingerprint=$fingerprint" "-p:PosDataDirectory=$directory"
if ($LASTEXITCODE -ne 0) { throw 'POS publish failed.' }

$assembly = Join-Path $out 'SupermarketPos.dll'
& $Dotnet run --project (Join-Path $PSScriptRoot 'ModelTools\ModelTools.csproj') @restoreOptions -- verify-local-build $assembly
if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }
[pscustomobject]@{
    Version = $build
    Configuration = 'Release'
    SourceRevision = $revision
    SourceFingerprint = $fingerprint
    BuildTimestampUtc = $builtAt
    ExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $out 'SupermarketPos.exe')).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath $assembly).Hash
} | ConvertTo-Json | Set-Content -LiteralPath "$out.build.json"
