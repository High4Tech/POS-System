[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('High4Tech/POS-System','High4Tech/pos_backup')][string]$Repository,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Title,
    [Parameter(Mandatory)][string]$NotesFile,
    [Parameter(Mandatory)][string[]]$Files,
    [Parameter(Mandatory)][string]$ProofFile
)
$ErrorActionPreference='Stop'
$env:GIT_TERMINAL_PROMPT='0';$env:GCM_INTERACTIVE='never'
$lines=@("protocol=https`nhost=github.com`n`n" | git credential fill 2>$null)
if($LASTEXITCODE -ne 0){throw 'Existing Git authentication is unavailable.'}
$credential=@{};foreach($line in $lines){if($line -match '^([^=]+)=(.*)$'){$credential[$matches[1]]=$matches[2]}}
if(!$credential.password){throw 'No existing Git credential returned.'}
$client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromMinutes(30)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('High4Tech-POS-Releases')
$client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$credential.password)
$client.DefaultRequestHeaders.Add('X-GitHub-Api-Version','2026-03-10')
try {
    function Json-Response([Net.Http.HttpResponseMessage]$Response) {
        $body=$Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if(!$Response.IsSuccessStatusCode){throw ('GitHub HTTP '+[int]$Response.StatusCode+': '+$body)}
        return $body|ConvertFrom-Json
    }
    $base='https://api.github.com/repos/'+$Repository
    $response=$client.GetAsync($base+'/releases/tags/'+[Uri]::EscapeDataString($Tag)).GetAwaiter().GetResult()
    if([int]$response.StatusCode -eq 404) {
        $response.Dispose()
        $json=@{tag_name=$Tag;name=$Title;body=(Get-Content -LiteralPath $NotesFile -Raw);prerelease=$true;draft=$false;make_latest='false'}|ConvertTo-Json
        $body=[Net.Http.StringContent]::new($json,[Text.Encoding]::UTF8,'application/json')
        try{$response=$client.PostAsync($base+'/releases',$body).GetAwaiter().GetResult()}finally{$body.Dispose()}
    }
    try{$release=Json-Response $response}finally{$response.Dispose()}
    $proof=[Collections.Generic.List[object]]::new()
    foreach($path in $Files) {
        $file=Get-Item -LiteralPath $path
        $hash=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
        $asset=@($release.assets|Where-Object {$_.name -ceq $file.Name})
        if($asset.Count -eq 0) {
            Write-Output ('Uploading version asset: '+$file.Name+' ('+$file.Length+' bytes)')
            $stream=[IO.File]::OpenRead($file.FullName);$content=[Net.Http.StreamContent]::new($stream)
            $content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
            $content.Headers.ContentLength=$file.Length
            try {
                $url=($release.upload_url -replace '\{.*$','')+'?name='+[Uri]::EscapeDataString($file.Name)
                $reply=$client.PostAsync($url,$content).GetAwaiter().GetResult()
                try{$item=Json-Response $reply}finally{$reply.Dispose()}
            } finally {$content.Dispose();$stream.Dispose()}
        } elseif($asset.Count -eq 1){$item=$asset[0]}else{throw 'Ambiguous release asset name.'}
        if($item.state -ne 'uploaded' -or $item.size -ne $file.Length -or $item.digest -ine ('sha256:'+$hash)){throw 'Remote asset digest or length differs from the local file. No local version may be removed.'}
        $proof.Add([pscustomobject]@{Name=$file.Name;Bytes=$file.Length;Sha256=$hash;RemoteDigest=$item.digest;Download=$item.browser_download_url;AssetId=$item.id})
        Write-Output ('Verified remote SHA256: '+$file.Name)
    }
    $result=[pscustomobject]@{Repository=$Repository;Tag=$Tag;Release=$release.html_url;Assets=$proof.ToArray()}
    $result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $ProofFile
    $result|ConvertTo-Json -Depth 5
} finally {$client.Dispose();$credential.Clear();$lines=$null}
