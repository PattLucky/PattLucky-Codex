param([switch]$AppMode)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
function Find-AppFile([string]$name) {
  foreach ($candidate in @((Join-Path $root $name),(Join-Path (Join-Path $root 'assets') $name),(Join-Path (Join-Path $root 'ui') $name),(Join-Path (Join-Path $root 'dist') $name))) { if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate } }
  throw "Required app file '$name' was not found in the project or install folder."
}
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add('http://127.0.0.1:43771/')
$script:PullJobs = @{}
$script:Agents = @{
  writer = @{model='gemma4:12b';system='You are PattLucky Essay & Document Writer. Help plan, draft, edit, and polish essays and documents. Use clear structure and natural prose. Preserve the user provided meaning, distinguish facts from opinions, and flag claims that need sources instead of inventing citations.'}
  coding = @{model='qwen3-coder:30b';system='You are PattLucky Coding Agent. Help design, understand, debug, and write software. Give accurate code with brief explanations, ask for missing context when it blocks progress, and never claim that code was run unless it was.'}
  creative = @{model='qwen3.5:9b';system='You are PattLucky Creative Writing Agent. Help create original stories, scenes, dialogue, characters, and imaginative ideas. Follow the requested voice and constraints, use vivid specific details, and keep the prose engaging rather than generic.'}
}
try { $listener.Start() } catch { Write-Host 'PattLucky Codex could not start its local server. Close any other copy and try again.' -ForegroundColor Red; Read-Host 'Press Enter to close'; exit 1 }
if (-not $AppMode) { Start-Process 'http://127.0.0.1:43771/' }
Write-Host 'PattLucky Codex is running locally. Close this window to stop it.' -ForegroundColor Green
function Send-Response($context, [int]$status, [string]$type, [byte[]]$body) {
  $context.Response.StatusCode = $status; $context.Response.ContentType = $type; $context.Response.Headers.Add('Cache-Control','no-store'); $context.Response.ContentLength64 = $body.Length; $context.Response.OutputStream.Write($body,0,$body.Length); $context.Response.Close()
}
function Send-Json($context, [int]$status, $data) {
  $json = $data | ConvertTo-Json -Depth 30 -Compress
  Send-Response $context $status 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}
function Read-Body($context) {
  $reader = [IO.StreamReader]::new($context.Request.InputStream, $context.Request.ContentEncoding)
  return ($reader.ReadToEnd() | ConvertFrom-Json)
}
while ($listener.IsListening) {
  $ctx = $null
  try {
    $ctx = $listener.GetContext(); $path = $ctx.Request.Url.AbsolutePath
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/') { Send-Response $ctx 200 'text/html; charset=utf-8' ([IO.File]::ReadAllBytes((Find-AppFile 'app.html'))); continue }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/PattLuckyLogo.png') { Send-Response $ctx 200 'image/png' ([IO.File]::ReadAllBytes((Find-AppFile 'PattLuckyLogo.png'))); continue }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/setup-wizard.ps1') { Send-Response $ctx 200 'application/octet-stream' ([IO.File]::ReadAllBytes((Join-Path $root 'setup-wizard.ps1'))); continue }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/download-bundle.zip') {
      Add-Type -AssemblyName System.IO.Compression
      $memory = [IO.MemoryStream]::new(); $archive = [IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
      try {
        foreach ($name in @('app.html','launch.ps1','setup-wizard.ps1','PattLuckyCodex.exe','PattLuckyCodex.ico','PattLuckyLogo.png','README.md')) {
          $entry = $archive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal); $inputStream = [IO.File]::OpenRead((Find-AppFile $name)); $outputStream = $entry.Open()
          try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
      } finally { $archive.Dispose() }
      $ctx.Response.Headers.Add('Content-Disposition','attachment; filename="PattLucky-Codex-1.0.0.zip"')
      $bundle = $memory.ToArray(); Send-Response $ctx 200 'application/zip' $bundle; $memory.Dispose(); continue
    }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/api/models') {
      try { $response = Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 5; Send-Json $ctx 200 @{connected=$true;models=@($response.models|ForEach-Object name)} }
      catch { Send-Json $ctx 200 @{connected=$false;models=@();error='Ollama was not found at http://127.0.0.1:11434. Install and start Ollama, then refresh.'} }
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/shutdown') { Send-Json $ctx 200 @{ok=$true}; $listener.Stop(); break }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/api/pulls') {
      $jobs = @{}
      foreach ($model in @($script:PullJobs.Keys)) {
        $job = $script:PullJobs[$model]
        if ($job.Process.HasExited) { $jobs[$model] = @{state=if($job.Process.ExitCode -eq 0){'installed'}else{'failed'};exitCode=$job.Process.ExitCode}; $job.Process.Dispose(); $script:PullJobs.Remove($model) }
        else { $jobs[$model] = @{state='downloading'} }
      }
      Send-Json $ctx 200 @{pulls=$jobs}; continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/pull') {
      $payload = Read-Body $ctx; $agent = [string]$payload.agent
      if (!$script:Agents.ContainsKey($agent)) { Send-Json $ctx 400 @{error='Choose one of the supported local agents.'}; continue }
      $model = $script:Agents[$agent].model
      if ($script:PullJobs.ContainsKey($model)) { Send-Json $ctx 200 @{state='downloading';model=$model}; continue }
      try {
        $ollama = Get-Command ollama -ErrorAction Stop
        $proc = Start-Process -FilePath $ollama.Source -ArgumentList @('pull',$model) -WindowStyle Hidden -PassThru
        $script:PullJobs[$model] = @{Process=$proc}; Send-Json $ctx 202 @{state='downloading';model=$model}
      } catch { Send-Json $ctx 500 @{error='Could not start the Ollama download. Install Ollama, then try again.'} }
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/chat') {
      $payload = Read-Body $ctx; $agent = [string]$payload.agent
      if (!$script:Agents.ContainsKey($agent)) { Send-Json $ctx 400 @{error='Choose one of the supported local agents.'}; continue }
      $config = $script:Agents[$agent]
      $messages = @(@{role='system';content=$config.system}) + @($payload.messages|ForEach-Object {@{role=$_.role;content=$_.content}})
      $body = @{model=$config.model;messages=$messages;stream=$false;think=$true;keep_alive='10m'} | ConvertTo-Json -Depth 30
      try {
        $answer = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:11434/api/chat' -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 1800
        Send-Json $ctx 200 @{content=$answer.message.content;thinking=$answer.message.thinking;model=$answer.model;eval_count=$answer.eval_count;eval_duration=$answer.eval_duration;total_duration=$answer.total_duration}
      } catch {
        $detail = $_.ErrorDetails.Message; if (!$detail) { $detail=$_.Exception.Message }
        Send-Json $ctx 502 @{error=$detail}
      }
      continue
    }
    Send-Response $ctx 404 'text/plain; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes('Not found'))
  } catch {
    if ($ctx) { try { Send-Json $ctx 500 @{error=$_.Exception.Message} } catch {} }
  }
}
$listener.Stop(); $listener.Close()
