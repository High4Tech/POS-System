[CmdletBinding()]
param(
    [string]$Output,
    [Parameter(Mandatory)][string]$Version,
    [string]$DataDirectory = 'D:\SupermarketPos',
    [string]$Dotnet = 'dotnet',
    [switch]$NoRestore,
    [switch]$TestRetailEdition
)

$ErrorActionPreference = 'Stop'
$env:MSBUILDDISABLENODEREUSE = '1'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $Output) {
    if ([IO.Path]::GetFileName($Version) -ne $Version -or $Version -in @('.', '..')) { throw 'Use a version without directory separators.' }
    $Output = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "High4TechPosBuild/artifacts/windows/$Version/desktop"
}
$out = [IO.Path]::GetFullPath($Output)
$syncRoots = @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial) | Where-Object { $_ }
foreach ($syncRoot in $syncRoots) {
    $syncRoot = [IO.Path]::GetFullPath($syncRoot).TrimEnd('\')
    if ($out -eq $syncRoot -or $out.StartsWith($syncRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage builds outside OneDrive; omit Output to use the local build archive.' }
}
if ($out -eq $repo -or $out.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage builds outside the source checkout.' }
if (Test-Path -LiteralPath $out) { throw 'Use an empty, new staging directory for each build.' }
$directory = [IO.Path]::GetFullPath($DataDirectory)
if ($out.StartsWith($directory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Stage the build outside the live POS data and application directory.'
}
$revision = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the POS source revision.' }
$filePaths = git -C $repo ls-files --cached --others --exclude-standard -- src assets
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
$editionOptions = if($TestRetailEdition) { @('-p:RetailTestEdition=true','-p:CommercialBuild=false','-p:PosDataDirectory=') } else { @('-p:RetailTestEdition=false',"-p:PosDataDirectory=$directory") }
$previousProcessorCount = $env:DOTNET_PROCESSOR_COUNT
$buildProcess = [Diagnostics.Process]::GetCurrentProcess()
$previousPriority = $buildProcess.PriorityClass
try {
    $env:DOTNET_PROCESSOR_COUNT = '2'
    $buildProcess.PriorityClass = 'BelowNormal'
    & $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $out --disable-build-servers '-m:1' '-nodeReuse:false' '-p:UseSharedCompilation=false' @restoreOptions "-p:Version=$Version" `
        '-p:IncludeSourceRevisionInInformationalVersion=false' "-p:InformationalVersion=$build" `
        "-p:BuildTimestampUtc=$builtAt" "-p:BuildSourceFingerprint=$fingerprint" @editionOptions
    if ($LASTEXITCODE -ne 0) { throw 'POS publish failed.' }

    $assembly = Join-Path $out 'SupermarketPos.dll'
    & $Dotnet run --project (Join-Path $PSScriptRoot 'ModelTools\ModelTools.csproj') --disable-build-servers --property:UseSharedCompilation=false --property:BuildInParallel=false @restoreOptions -- verify-local-build $assembly
    if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }

} finally {
    $env:DOTNET_PROCESSOR_COUNT = $previousProcessorCount
    $buildProcess.PriorityClass = $previousPriority
}
if($TestRetailEdition) {
    $runtime = Get-Content -LiteralPath (Join-Path $out 'SupermarketPos.runtimeconfig.json') -Raw | ConvertFrom-Json
    if($runtime.runtimeOptions.configProperties.'SupermarketPos.DataDirectory') { throw 'A test edition must not contain the production data path.' }
}
[pscustomobject]@{
    Version = $build
    Configuration = 'Release'
    SourceRevision = $revision
    SourceFingerprint = $fingerprint
    BuildTimestampUtc = $builtAt
    ExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $out 'SupermarketPos.exe')).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath $assembly).Hash
} | ConvertTo-Json | Set-Content -LiteralPath "$out.build.json"
