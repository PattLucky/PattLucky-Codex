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
$script:ManagedOllamaProcessIds = [System.Collections.Generic.List[int]]::new()
$script:ManagedOllamaProcessIdSet = [System.Collections.Generic.HashSet[int]]::new()
$script:Agents = @{
  writer = @{model='gemma4:12b';system='You are PattLucky Essay & Document Writer. Help plan, draft, edit, and polish essays and documents. Use clear structure and natural prose. Preserve the user provided meaning, distinguish facts from opinions, and flag claims that need sources instead of inventing citations. Write polished, direct answers with natural wording. Lead with the useful answer; use short headings and bullets for longer replies, and use **bold** or *italics* sparingly to emphasize key ideas. Avoid filler, repetitive openings, and awkward phrasing.'}
  coding = @{model='qwen3-coder:30b';system='You are PattLucky Coding Agent. Help design, understand, debug, and write software. Give accurate code with brief explanations, ask for missing context when it blocks progress, and never claim that code was run unless it was. Write polished, direct answers with natural wording. Lead with the useful answer; use short headings and bullets for longer replies, and use **bold** or *italics* sparingly to emphasize key ideas. Avoid filler, repetitive openings, and awkward phrasing.'}
  creative = @{model='qwen3.5:9b';system='You are PattLucky Creative Writing Agent. Help create original stories, scenes, dialogue, characters, and imaginative ideas. Follow the requested voice and constraints, use vivid specific details, and keep the prose engaging rather than generic. Write polished, natural responses. Use headings or bullets only when they help, and use **bold** or *italics* sparingly for emphasis. Avoid filler and repetitive openings.'}
  media = @{model='qwen3.5:9b';system='You are PattLucky Photo & Video Creator, a local creative director and prompt engineer for open-source image and video generation tools. Turn a user idea into a production-ready visual prompt with subject, composition, environment, lighting, lens or camera movement, color, mood, and style. For video, provide a concise shot-by-shot storyboard, duration guidance, motion direction, and continuity notes. When helpful, separate positive and negative prompts and suggest aspect ratio and resolution. Be clear that you design prompts and storyboards; local ComfyUI workflows perform the actual rendering. Never suggest paid APIs or imply you rendered media when you did not.'}
  lite = @{model='qwen3.5:0.8b';system='You are PattLucky Lite Chat, a fast, friendly everyday assistant. Answer directly and clearly. Keep replies concise unless the user asks for detail. Be honest when a task needs more capability than this small model can provide. Use natural wording and clean formatting; use short bullets when helpful and **bold** sparingly for key points. Avoid filler and repetitive openings.'}
}
function Find-OllamaExecutable {
  $command = Get-Command 'ollama.exe' -ErrorAction SilentlyContinue
  if (!$command) { $command = Get-Command 'ollama' -ErrorAction SilentlyContinue }
  if ($command) { return $command.Source }
  foreach ($candidate in @((Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),(Join-Path $env:ProgramFiles 'Ollama\ollama.exe'))) {
    if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
  }
  return $null
}
function Register-OllamaProcessTree {
  try { $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop) } catch { return }
  $pending = [System.Collections.Generic.Queue[int]]::new()
  foreach ($process in $processes) {
    if ($process.Name -match '^ollama(?: app|_.*)?\.exe$') {
      $processId = [int]$process.ProcessId
      if ($script:ManagedOllamaProcessIdSet.Add($processId)) { $script:ManagedOllamaProcessIds.Add($processId); $pending.Enqueue($processId) }
    }
  }
  while ($pending.Count -gt 0) {
    $parentId = $pending.Dequeue()
    foreach ($process in $processes) {
      $processId = [int]$process.ProcessId
      if ([int]$process.ParentProcessId -eq $parentId -and $script:ManagedOllamaProcessIdSet.Add($processId)) {
        $script:ManagedOllamaProcessIds.Add($processId); $pending.Enqueue($processId)
      }
    }
  }
}
function Stop-ManagedOllamaProcesses {
  Register-OllamaProcessTree
  $processIds = $script:ManagedOllamaProcessIds.ToArray()
  [Array]::Reverse($processIds)
  foreach ($processId in $processIds) {
    try {
      $process = Get-Process -Id $processId -ErrorAction Stop
      if ($process.ProcessName -like 'ollama*') { Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue }
    } catch {}
  }
  $script:ManagedOllamaProcessIds.Clear(); $script:ManagedOllamaProcessIdSet.Clear()
}
try { $listener.Start() } catch { Write-Host 'PattGPT Codex could not start its local server. Close any other copy and try again.' -ForegroundColor Red; Read-Host 'Press Enter to close'; exit 1 }
if (-not $AppMode) { Start-Process 'http://127.0.0.1:43771/' }
Write-Host 'PattGPT Codex is running locally. Close this window to stop it.' -ForegroundColor Green
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
      $bundleExe = Find-AppFile 'PattGPTCodex.exe'
      $updatedCandidates = @(Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter 'PattGPTCodex-Updated-*.exe' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
      if ($updatedCandidates.Count -gt 0) {
        try {
          $currentFileVersion = [version][Diagnostics.FileVersionInfo]::GetVersionInfo($bundleExe).FileVersion
          $candidateFileVersion = [version][Diagnostics.FileVersionInfo]::GetVersionInfo($updatedCandidates[0].FullName).FileVersion
          if ($candidateFileVersion -gt $currentFileVersion -or ($candidateFileVersion -eq $currentFileVersion -and $updatedCandidates[0].LastWriteTimeUtc -gt (Get-Item -LiteralPath $bundleExe).LastWriteTimeUtc)) { $bundleExe = $updatedCandidates[0].FullName }
        } catch {}
      }
      $downloadVersion = '1.0.0'
      try { $downloadVersion = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($bundleExe).FileVersion).ToString(3) } catch {}
      $memory = [IO.MemoryStream]::new(); $archive = [IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
      try {
        foreach ($name in @('app.html','launch.ps1','setup-wizard.ps1','PattLuckyCodex.ico','PattLuckyLogo.png','README.md')) {
          $entry = $archive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal); $inputStream = [IO.File]::OpenRead((Find-AppFile $name)); $outputStream = $entry.Open()
          try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
        $entry = $archive.CreateEntry('PattGPTCodex.exe',[IO.Compression.CompressionLevel]::Optimal); $inputStream = [IO.File]::OpenRead($bundleExe); $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
      } finally { $archive.Dispose() }
      $ctx.Response.Headers.Add('Content-Disposition',('attachment; filename="PattGPT-Codex-{0}.zip"' -f $downloadVersion))
      $bundle = $memory.ToArray(); Send-Response $ctx 200 'application/zip' $bundle; $memory.Dispose(); continue
    }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/api/models') {
      try { $response = Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 5; if ($script:ManagedOllamaProcessIds.Count -eq 0) { Register-OllamaProcessTree }; Send-Json $ctx 200 @{connected=$true;models=@($response.models|ForEach-Object name)} }
      catch { Send-Json $ctx 200 @{connected=$false;models=@();error='Ollama was not found at http://127.0.0.1:11434. Install and start Ollama, then refresh.'} }
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/api/ollama/check') {
      $ollamaPath = Find-OllamaExecutable; $running = $false
      try { Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; $running = $true; if ($script:ManagedOllamaProcessIds.Count -eq 0) { Register-OllamaProcessTree } } catch {}
      Send-Json $ctx 200 @{installed=[bool]$ollamaPath;running=$running;path=$ollamaPath}; continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/ollama/start') {
      try {
        try { Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; Register-OllamaProcessTree; Send-Json $ctx 200 @{state='running'}; continue } catch {}
        $ollamaPath = Find-OllamaExecutable
        if (!$ollamaPath) { Send-Json $ctx 404 @{error='Ollama is not installed. Install Ollama for Windows, then use Start Ollama here.'}; continue }
        $ollamaProcess = Start-Process -FilePath $ollamaPath -ArgumentList @('serve') -WindowStyle Hidden -PassThru
        if ($ollamaProcess -and $script:ManagedOllamaProcessIdSet.Add([int]$ollamaProcess.Id)) { $script:ManagedOllamaProcessIds.Add([int]$ollamaProcess.Id) }
        Send-Json $ctx 202 @{state='starting'}
      } catch { Send-Json $ctx 500 @{error='Could not start Ollama. Check that it is installed and try again.'} }
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
        $ollamaPath = Find-OllamaExecutable
        if (!$ollamaPath) { throw 'Ollama is not installed.' }
        $proc = Start-Process -FilePath $ollamaPath -ArgumentList @('pull',$model) -WindowStyle Hidden -PassThru
        if ($script:ManagedOllamaProcessIdSet.Add([int]$proc.Id)) { $script:ManagedOllamaProcessIds.Add([int]$proc.Id) }
        $script:PullJobs[$model] = @{Process=$proc}; Send-Json $ctx 202 @{state='downloading';model=$model}
      } catch { Send-Json $ctx 500 @{error=('Ollama could not start downloading {0}: {1}' -f $model,$_.Exception.Message)} }
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/chat') {
      Register-OllamaProcessTree
      $payload = Read-Body $ctx; $agent = [string]$payload.agent
      if (!$script:Agents.ContainsKey($agent)) { Send-Json $ctx 400 @{error='Choose one of the supported local agents.'}; continue }
      $config = $script:Agents[$agent]
      # Bound prompt size so long-running chats do not get progressively slower.
      # Keep the system instruction plus the latest six user/assistant exchanges.
      $recentMessages = @($payload.messages | Select-Object -Last 12)
      $messages = @(@{role='system';content=$config.system}) + @($recentMessages | ForEach-Object {@{role=$_.role;content=$_.content}})
      # Fast-response defaults: avoid chain-of-thought generation, cap long answers,
      # and use a moderate context window to reduce memory use and prompt evaluation.
      $body = @{model=$config.model;messages=$messages;stream=$false;think=$false;keep_alive='30m';options=@{num_ctx=8192;num_predict=1536;temperature=0.7}} | ConvertTo-Json -Depth 30
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
try { $listener.Stop(); $listener.Close() } finally { Stop-ManagedOllamaProcesses }
