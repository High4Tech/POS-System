[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [string]$InstallationRoot = 'D:\SupermarketPos',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $InstallationRoot).Path
$source = (Resolve-Path -LiteralPath $Payload).Path
$app = Join-Path $root 'App'
$backups = Join-Path $root 'Backups'
foreach ($path in @($root, $app, $backups, $source)) {
    $directory = Get-Item -LiteralPath $path
    if (-not $directory.PSIsContainer -or $directory.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "Expected an ordinary directory at $path."
    }
}
if ($source.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase) -or $source -eq $app) {
    throw 'Use a separately verified publish folder.'
}

$exe = 'SupermarketPos.exe'
$assembly = 'SupermarketPos.dll'
$target = Join-Path $app $exe
$incomingInfo = (Get-Item -LiteralPath (Join-Path $source $assembly)).VersionInfo
$installedInfo = (Get-Item -LiteralPath (Join-Path $app $assembly)).VersionInfo
$incoming = $incomingInfo.ProductVersion
$installed = $installedInfo.ProductVersion
if (-not $incoming -or -not $installed -or -not (Test-Path -LiteralPath (Join-Path $root 'pos.db') -PathType Leaf)) {
    throw 'Cannot identify the existing desktop app and database or the new build.'
}
$config = Get-Content -LiteralPath (Join-Path $source 'SupermarketPos.runtimeconfig.json') -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($config.runtimeOptions.configProperties.'SupermarketPos.DataDirectory') -ne $root) {
    throw 'The payload data folder does not match this installation.'
}
if ([version]$incomingInfo.FileVersion -lt [version]$installedInfo.FileVersion) { throw 'The payload is older than the installed app.' }

function Assert-Closed {
    if (Get-Process -Name SupermarketPos -ErrorAction SilentlyContinue) {
        throw 'Finish pending sales and purchases, then close the POS before retrying the update.'
    }
}
function Get-PayloadHashes([string]$Folder) {
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Folder -Recurse -File) {
        $name = $file.FullName.Substring($Folder.Length + 1)
        $hashes[$name] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    return $hashes
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
$stage = Join-Path $root ".update-$stamp"
$previous = Join-Path $root ".rollback-$stamp"
$dataBackup = Join-Path $backups "Before-update-$stamp.zip"
$binaryBackup = Join-Path $backups "Previous-application-$stamp.zip"
$lock = [IO.File]::Open((Join-Path $root '.desktop-update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    Assert-Closed
    $tool = Join-Path $PSScriptRoot 'ModelTools\ModelTools.csproj'
    & $Dotnet run --project $tool --no-build -- verify-local-build (Join-Path $source $assembly)
    if ($LASTEXITCODE -ne 0) { throw 'The Release payload failed build metadata verification.' }
    Copy-Item -LiteralPath $source -Destination $stage -Recurse
    $hashes = Get-PayloadHashes $source
    $stagedHashes = Get-PayloadHashes $stage
    if ($hashes.Count -ne $stagedHashes.Count -or @($hashes.Keys | Where-Object { $hashes[$_] -ne $stagedHashes[$_] }).Count) {
        throw 'Staged files differ from the verified payload.'
    }
    & $Dotnet run --project $tool --no-build -- backup $root $dataBackup
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dataBackup)) { throw 'The POS backup failed.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $binaryBackup, [IO.Compression.CompressionLevel]::Fastest, $false)
    Assert-Closed
    foreach ($path in @($app, $stage)) {
        $resolved = Get-Item -LiteralPath $path
        if ($resolved.FullName -ne $path -or $resolved.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw "Unexpected update directory $path."
        }
    }
    Move-Item -LiteralPath $app -Destination $previous
    try {
        Move-Item -LiteralPath $stage -Destination $app
        foreach ($name in @($exe, $assembly, 'SupermarketPos.runtimeconfig.json')) {
            if ((Get-FileHash -LiteralPath (Join-Path $app $name)).Hash -ne $hashes[$name]) {
                throw "Installed file verification failed for $name."
            }
        }
    }
    catch {
        if (Test-Path -LiteralPath $app) { Move-Item -LiteralPath $app -Destination $stage }
        Move-Item -LiteralPath $previous -Destination $app
        throw
    }

    $process = Start-Process -FilePath $target -WorkingDirectory $app -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 300
        $process.Refresh()
    } until ($process.HasExited -or $process.MainWindowHandle -ne 0 -or [DateTime]::UtcNow -ge $deadline)
    if ($process.HasExited -or $process.MainWindowHandle -eq 0 -or $process.Path -ne $target) {
        if ($process.HasExited) {
            Move-Item -LiteralPath $app -Destination $stage
            Move-Item -LiteralPath $previous -Destination $app
        }
        throw "Could not verify startup. Binary checkpoint: $binaryBackup. Database checkpoint: $dataBackup."
    }

    $old = Get-Item -LiteralPath $previous
    if ($old.FullName -eq $previous -and -not $old.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $previous -Recurse
    }
    [pscustomobject]@{
        PreviousVersion = $installed
        InstalledVersion = $incoming
        ProcessPath = $process.Path
        ProcessId = $process.Id
        ExecutableSha256 = $hashes[$exe]
        AssemblySha256 = $hashes[$assembly]
        DataBackup = $dataBackup
        BinaryBackup = $binaryBackup
    } | ConvertTo-Json
}
finally {
    $lock.Dispose()
}
