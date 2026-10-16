param([switch]$AppMode)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
function Find-AppFile([string]$name) {
  foreach ($candidate in @((Join-Path $root $name),(Join-Path (Join-Path $root 'assets') $name),(Join-Path (Join-Path $root 'ui') $name),(Join-Path (Join-Path $root 'dist') $name))) { if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate } }
  throw "Required app file '$name' was not found in the project or install folder."
}
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add('http://127.0.0.1:43771/')
$script:DataDirectory = Join-Path $env:LOCALAPPDATA 'PattGPT Codex'
$script:ModelsDirectory = Join-Path $script:DataDirectory 'models'
$script:EngineDirectory = Join-Path $script:DataDirectory 'engine\b11669'
$script:EngineArchive = Join-Path $script:DataDirectory 'engine\llama-b11669-bin-win-cpu-x64.zip'
$script:EngineHash = '37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b'
$script:EngineUrl = 'https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-cpu-x64.zip'
$script:RuntimeJob = $null
$script:DownloadJobs = @{}
$script:InferenceProcess = $null
$script:InferenceModel = $null
$script:Models = @{
  'qwen3-14b' = @{
    name='Qwen3 14B · General, writing & creative'; size=9001752960L
    files=@(@{name='Qwen3-14B-Q4_K_M.gguf';size=9001752960L;sha256='500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0';url='https://huggingface.co/Qwen/Qwen3-14B-GGUF/resolve/530227a7d994db8eca5ab5ced2fb692b614357fd/Qwen3-14B-Q4_K_M.gguf?download=true'})
  }
  'qwen3-coder-30b' = @{
    name='Qwen3 Coder 30B · Coding specialist'; size=18556689568L
    files=@(@{name='Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf';size=18556689568L;sha256='fadc3e5f8d42bf7e894a785b05082e47daee4df26680389817e2093056f088ad';url='https://huggingface.co/unsloth/Qwen3-Coder-30B-A3B-Instruct-GGUF/resolve/b17cb02dd882d5b6ab62fc777ad2995f19668350/Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf?download=true'})
  }
  'qwen3-8b' = @{
    name='Qwen3 8B · Recommended lightweight default'; size=5027783488L
    files=@(@{name='Qwen3-8B-Q4_K_M.gguf';size=5027783488L;sha256='d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785';url='https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/7c41481f57cb95916b40956ab2f0b139b296d974/Qwen3-8B-Q4_K_M.gguf?download=true'})
  }
  'qwen3-vl-8b' = @{
    name='Qwen3-VL 8B · Image understanding'; size=5780074528L
    files=@(
      @{name='Qwen3VL-8B-Instruct-Q4_K_M.gguf';size=5027784800L;sha256='67d1659bfe71b89d50b45a4ad1a9e5b997e5bb16ce5da66a6a6167abd569e9e2';url='https://huggingface.co/Qwen/Qwen3-VL-8B-Instruct-GGUF/resolve/f982a07559d4a2f6c8744d840bf6fccab30eea96/Qwen3VL-8B-Instruct-Q4_K_M.gguf?download=true'},
      @{name='mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf';size=752289728L;sha256='c6ba85508d82f42590e6eb77d5340369ab6fecf107a7561d809523d8aa5f3bfd';url='https://huggingface.co/Qwen/Qwen3-VL-8B-Instruct-GGUF/resolve/f982a07559d4a2f6c8744d840bf6fccab30eea96/mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf?download=true'}
    )
  }
}
$script:Agents = @{
  writer = @{system='You are PattLucky Essay & Document Writer. Help plan, draft, edit, and polish essays and documents. Think carefully, check your answer for unsupported claims, and be clear about uncertainty. Preserve user meaning, distinguish facts from opinions, and never invent citations. Write polished, direct answers with natural wording. Lead with the useful answer; use headings and bullets when they improve readability, and avoid filler, repetitive openings, or awkward phrasing.'}
  coding = @{system='You are PattLucky Coding Agent. Help design, understand, debug, and write software. Analyze the request carefully, provide correct and maintainable code, account for edge cases, and explain important tradeoffs. Ask for missing context when it blocks a sound answer. Never claim code was run unless it was. Lead with the useful answer and avoid filler.'}
  creative = @{system='You are PattLucky Creative Writing Agent. Create original stories, scenes, dialogue, characters, and imaginative ideas. Follow the requested voice and constraints, use vivid specific details, and keep prose engaging rather than generic. Think through continuity and pacing before answering. Avoid filler and repetitive openings.'}
  media = @{system='You are PattLucky Photo & Video Creator, a local creative director and prompt engineer for open-source image and video generation tools. Analyze attached images when present. Turn an idea into a production-ready visual prompt with subject, composition, environment, lighting, lens or camera movement, color, mood, and style. For video, provide a concise shot-by-shot storyboard and continuity notes. ComfyUI workflows perform actual rendering; never imply that you rendered media.'}
  business = @{system='You are PattLucky Business Operations Assistant. Create clear, usable business documents and workflows such as proposals, meeting records, standard operating procedures, project briefs, status reports, executive decision briefs, client communications, and risk registers. Preserve supplied facts, do not invent names, commitments, dates, prices, legal or regulatory claims, or performance metrics. Mark missing information with [TBD], distinguish assumptions from verified details, and surface material risks and decisions. Use professional, concise language and structure outputs with headings, numbered steps, or tables when useful. If an essential fact is missing, ask focused questions or provide a draft with explicit placeholders rather than pretending to know it.'}
  lite = @{system='You are PattLucky Lite Chat, a friendly everyday assistant. Answer clearly and accurately. Keep replies concise unless detail is requested. If unsure, say so rather than guessing. Avoid filler and repetitive openings.'}
}
function Get-EngineExecutable {
  if (-not (Test-Path -LiteralPath $script:EngineDirectory)) { return $null }
  $found = Get-ChildItem -LiteralPath $script:EngineDirectory -Filter 'llama-server.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($found) { return $found.FullName }
  return $null
}
function Get-ModelFilePath([string]$modelId, [string]$name) {
  return Join-Path (Join-Path $script:ModelsDirectory $modelId) $name
}
function Test-ModelInstalled([string]$modelId) {
  if (-not $script:Models.ContainsKey($modelId)) { return $false }
  foreach ($file in $script:Models[$modelId].files) {
    $path = Get-ModelFilePath $modelId $file.name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne [long]$file.size) { return $false }
  }
  return $true
}
function Start-BitsFile([string]$url, [string]$path, [string]$name) {
  $directory = Split-Path -Parent $path
  New-Item -ItemType Directory -Force -Path $directory | Out-Null
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
  $bits = Start-BitsTransfer -Source $url -Destination $path -DisplayName $name -Priority High -RetryInterval 60 -RetryTimeout 86400 -Asynchronous -ErrorAction Stop
  return @{id=[string]$bits.JobId;path=$path;done=$false}
}
function Start-EngineDownload {
  if (Get-EngineExecutable) { return }
  if ($script:RuntimeJob -and $script:RuntimeJob.state -eq 'downloading') { return }
  $script:RuntimeJob = @{state='downloading';stage='Downloading local engine';error='';transfer=(Start-BitsFile $script:EngineUrl $script:EngineArchive 'PattGPT local inference engine')}
}
function Update-EngineDownload {
  if (-not $script:RuntimeJob -or $script:RuntimeJob.state -ne 'downloading') { return }
  try {
    $bits = Get-BitsTransfer -JobId ([Guid]$script:RuntimeJob.transfer.id) -ErrorAction Stop
    if ($bits.JobState -eq 'Transferred') {
      Complete-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop | Out-Null
      $script:RuntimeJob.state = 'verifying'; $script:RuntimeJob.stage = 'Verifying local engine'
      $hash = (Get-FileHash -LiteralPath $script:EngineArchive -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
      if ($hash -ne $script:EngineHash) { throw 'The downloaded llama.cpp engine did not match its published SHA-256 checksum.' }
      $stage = Join-Path (Split-Path -Parent $script:EngineDirectory) 'b11669-staging'
      if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $stage | Out-Null
      Expand-Archive -LiteralPath $script:EngineArchive -DestinationPath $stage -Force
      $server = Get-ChildItem -LiteralPath $stage -Filter 'llama-server.exe' -File -Recurse | Select-Object -First 1
      if (-not $server) { throw 'The verified llama.cpp archive did not contain llama-server.exe.' }
      $serverFolder = $server.DirectoryName
      if (Test-Path -LiteralPath $script:EngineDirectory) { Remove-Item -LiteralPath $script:EngineDirectory -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $script:EngineDirectory | Out-Null
      Copy-Item -Path (Join-Path $serverFolder '*') -Destination $script:EngineDirectory -Recurse -Force
      if (-not (Get-EngineExecutable)) { throw 'llama-server.exe could not be installed from the verified archive.' }
      Remove-Item -LiteralPath $stage -Recurse -Force
      Remove-Item -LiteralPath $script:EngineArchive -Force
      $script:RuntimeJob.state = 'installed'; $script:RuntimeJob.stage = 'Local engine ready'
    } elseif ($bits.JobState -eq 'Error') {
      throw $bits.Error.Description
    }
  } catch {
    $script:RuntimeJob.state = 'failed'; $script:RuntimeJob.error = $_.Exception.Message; $script:RuntimeJob.stage = 'Engine setup failed'
  }
}
function Start-ModelFiles([string]$modelId) {
  $entry = $script:Models[$modelId]
  $transfers = @()
  $script:DownloadJobs[$modelId].files = @()
  foreach ($file in $entry.files) {
    $finalPath = Get-ModelFilePath $modelId $file.name
    if ((Test-Path -LiteralPath $finalPath -PathType Leaf) -and (Get-Item -LiteralPath $finalPath).Length -eq [long]$file.size) {
      $transfers += @{file=$file;final=$finalPath;path=$finalPath;done=$true}
    } else {
      if (Test-Path -LiteralPath $finalPath) { Remove-Item -LiteralPath $finalPath -Force }
      $partialPath = $finalPath + '.partial'
      $transfer = Start-BitsFile $file.url $partialPath ('PattGPT model ' + $modelId)
      $transfers += @{file=$file;final=$finalPath;path=$partialPath;id=$transfer.id;done=$false}
    }
    $script:DownloadJobs[$modelId].files = $transfers
  }
  $script:DownloadJobs[$modelId].state = 'downloading'
  $script:DownloadJobs[$modelId].stage = 'Downloading model'
}
function Update-ModelDownloads {
  Update-EngineDownload
  foreach ($modelId in @($script:DownloadJobs.Keys)) {
    $job = $script:DownloadJobs[$modelId]
    try {
      if ($job.state -eq 'waiting') {
        if ($script:RuntimeJob -and $script:RuntimeJob.state -eq 'failed') { throw $script:RuntimeJob.error }
        if (Get-EngineExecutable) { Start-ModelFiles $modelId }
        else { continue }
      }
      if ($job.state -eq 'downloading') {
        foreach ($file in $job.files) {
          if ($file.done) { continue }
          $bits = Get-BitsTransfer -JobId ([Guid]$file.id) -ErrorAction Stop
          if ($bits.JobState -eq 'Transferred') {
            Complete-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop | Out-Null
            $file.done = $true
          } elseif ($bits.JobState -eq 'Error') { throw $bits.Error.Description }
        }
        $allDone = $true; foreach ($file in $job.files) { if (-not $file.done) { $allDone = $false; break } }
        if ($allDone) {
          $job.stage = 'Verifying model files'; $job.state = 'verifying'
          $verifyItems = @($job.files | ForEach-Object { @{path=$_.path;sha256=$_.file.sha256} })
          $verifyJson = ConvertTo-Json -InputObject $verifyItems -Compress
          $job.verify = Start-Job -ArgumentList $verifyJson -ScriptBlock {
            param([string]$itemsJson)
            foreach ($item in @(ConvertFrom-Json $itemsJson)) {
              $actual = (Get-FileHash -LiteralPath $item.path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
              if ($actual -ne $item.sha256) { throw ('SHA-256 verification failed for ' + [IO.Path]::GetFileName($item.path)) }
            }
            'verified'
          }
        }
      }
      if ($job.state -eq 'verifying' -and $job.verify.State -in @('Completed','Failed','Stopped')) {
        if ($job.verify.State -ne 'Completed') {
          $failure = (Receive-Job -Job $job.verify -ErrorAction SilentlyContinue | Out-String).Trim()
          throw $(if ($failure) { $failure } else { 'Model checksum verification failed.' })
        }
        $verified = Receive-Job -Job $job.verify -ErrorAction Stop
        Remove-Job -Job $job.verify -Force
        if ($verified -notcontains 'verified') { throw 'Model file verification returned an unexpected result.' }
        foreach ($file in $job.files) {
          if (Test-Path -LiteralPath $file.final) { Remove-Item -LiteralPath $file.final -Force }
          Move-Item -LiteralPath $file.path -Destination $file.final -Force
        }
        $job.state = 'installed'; $job.stage = 'Ready to use'
      }
    } catch {
      if ($job.verify) { Stop-Job -Job $job.verify -ErrorAction SilentlyContinue; Remove-Job -Job $job.verify -Force -ErrorAction SilentlyContinue }
      foreach ($file in $job.files) { if ($file.id) { Get-BitsTransfer -JobId ([Guid]$file.id) -ErrorAction SilentlyContinue | Remove-BitsTransfer -Confirm:$false -ErrorAction SilentlyContinue } }
      $job.state = 'failed'; $job.stage = 'Download failed'; $job.error = $_.Exception.Message
    }
  }
}
function Get-ModelDownloadStatus {
  Update-ModelDownloads
  $status = @{}
  foreach ($modelId in $script:Models.Keys) {
    if (Test-ModelInstalled $modelId) { $status[$modelId] = @{state='installed';progress=100;stage='Ready to use'}; continue }
    if (-not $script:DownloadJobs.ContainsKey($modelId)) { continue }
    $job = $script:DownloadJobs[$modelId]; $progress = 0
    if ($job.state -eq 'downloading' -and $job.files) {
      [long]$done = 0; [long]$total = [long]$script:Models[$modelId].size
      foreach ($file in $job.files) {
        if ($file.done) { $done += [long]$file.file.size }
        elseif (Test-Path -LiteralPath $file.path) { $done += [long](Get-Item -LiteralPath $file.path).Length }
      }
      if ($total -gt 0) { $progress = [Math]::Min(99,[Math]::Max(0,[int]($done * 100.0 / $total))) }
    } elseif ($job.state -eq 'verifying') { $progress = 100 }
    $status[$modelId] = @{state=$job.state;progress=$progress;stage=$job.stage;error=$job.error}
  }
  if ($script:RuntimeJob -and $script:RuntimeJob.state -eq 'downloading') {
    $bits = Get-BitsTransfer -JobId ([Guid]$script:RuntimeJob.transfer.id) -ErrorAction SilentlyContinue
    $progress = if ($bits -and $bits.BytesTotal -gt 0) { [Math]::Min(99,[int]($bits.BytesTransferred * 100.0 / $bits.BytesTotal)) } else { 0 }
    foreach ($modelId in $script:DownloadJobs.Keys) { if ($script:DownloadJobs[$modelId].state -eq 'waiting') { $status[$modelId] = @{state='waiting';progress=$progress;stage='Downloading local engine';error=''} } }
  }
  return $status
}
function Stop-LocalInference {
  if ($script:InferenceProcess) {
    try { if (-not $script:InferenceProcess.HasExited) { Stop-Process -Id $script:InferenceProcess.Id -Force -ErrorAction Stop } }
    catch { Write-Warning ('Could not stop local inference engine: ' + $_.Exception.Message) }
    finally { $script:InferenceProcess.Dispose(); $script:InferenceProcess = $null; $script:InferenceModel = $null }
  }
}
function Quote-ProcessArgument([string]$value) {
  return '"' + $value.Replace('"','\"') + '"'
}
function Start-LocalInference([string]$modelId) {
  if (-not (Test-ModelInstalled $modelId)) { throw 'Download this model in Models before starting a chat.' }
  if ($script:InferenceProcess -and -not $script:InferenceProcess.HasExited -and $script:InferenceModel -eq $modelId) {
    try { Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:43772/health' -TimeoutSec 3 | Out-Null; return }
    catch { Stop-LocalInference }
  } elseif ($script:InferenceProcess) { Stop-LocalInference }
  $exe = Get-EngineExecutable
  if (-not $exe) { throw 'The built-in llama.cpp engine is not installed. Download a model to install it automatically.' }
  $model = $script:Models[$modelId]; $modelPath = Get-ModelFilePath $modelId $model.files[0].name
  $args = @('--model',(Quote-ProcessArgument $modelPath),'--host','127.0.0.1','--port','43772','--ctx-size','8192','--n-gpu-layers','0','--parallel','1','--jinja','--alias',(Quote-ProcessArgument 'local'))
  if ($modelId -eq 'qwen3-vl-8b') { $args += @('--mmproj',(Quote-ProcessArgument (Get-ModelFilePath $modelId $model.files[1].name))) }
  $logOut = Join-Path $script:DataDirectory 'engine-stdout.log'; $logErr = Join-Path $script:DataDirectory 'engine-stderr.log'
  foreach ($log in @($logOut,$logErr)) { if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force } }
  $process = Start-Process -FilePath $exe -ArgumentList ($args -join ' ') -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -PassThru -RedirectStandardOutput $logOut -RedirectStandardError $logErr
  if (-not $process) { throw 'Could not start the built-in llama.cpp inference engine.' }
  $script:InferenceProcess = $process; $script:InferenceModel = $modelId
  for ($i=0; $i -lt 900; $i++) {
    if ($process.HasExited) {
      $details = ''; if (Test-Path $logErr) { $details = (Get-Content -LiteralPath $logErr -Tail 12 -ErrorAction SilentlyContinue) -join ' ' }
      Stop-LocalInference
      throw ('The local model engine stopped while loading. ' + $details)
    }
    try { Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:43772/health' -TimeoutSec 3 | Out-Null; return }
    catch { Start-Sleep -Seconds 1 }
  }
  Stop-LocalInference
  throw 'The local model did not finish loading within 15 minutes. Check available system memory and the local engine log.'
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
      $downloadVersion = '2.0.6'
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
      $installed = @($script:Models.Keys | Where-Object { Test-ModelInstalled $_ })
      Send-Json $ctx 200 @{connected=$true;models=$installed;engine=[bool](Get-EngineExecutable)}
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/shutdown') { Send-Json $ctx 200 @{ok=$true}; $listener.Stop(); break }
    if ($ctx.Request.HttpMethod -eq 'GET' -and $path -eq '/api/pulls') {
      Send-Json $ctx 200 @{pulls=(Get-ModelDownloadStatus)}; continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/pull') {
      $payload = Read-Body $ctx; $modelId = [string]$payload.model
      if (!$script:Models.ContainsKey($modelId)) { Send-Json $ctx 400 @{error='Choose one of the supported local models.'}; continue }
      if (Test-ModelInstalled $modelId) { Send-Json $ctx 200 @{state='installed';model=$modelId}; continue }
      if ($script:DownloadJobs.ContainsKey($modelId) -and $script:DownloadJobs[$modelId].state -notin @('failed','installed')) { Send-Json $ctx 200 @{state=$script:DownloadJobs[$modelId].state;model=$modelId}; continue }
      try {
        if ($script:DownloadJobs.ContainsKey($modelId)) { $script:DownloadJobs.Remove($modelId) }
        $script:DownloadJobs[$modelId] = @{state='waiting';stage='Preparing local inference engine';error='';files=@()}
        Start-EngineDownload
        if (Get-EngineExecutable) { Start-ModelFiles $modelId }
        Send-Json $ctx 202 @{state=$script:DownloadJobs[$modelId].state;model=$modelId}
      } catch {
        $failedJob = $script:DownloadJobs[$modelId]
        foreach ($file in $failedJob.files) {
          if ($file.id) { Get-BitsTransfer -JobId ([Guid]$file.id) -ErrorAction SilentlyContinue | Remove-BitsTransfer -Confirm:$false -ErrorAction SilentlyContinue }
          if ($file.path -and $file.path.EndsWith('.partial') -and (Test-Path -LiteralPath $file.path)) { Remove-Item -LiteralPath $file.path -Force -ErrorAction SilentlyContinue }
        }
        $script:DownloadJobs[$modelId] = @{state='failed';stage='Download failed';error=$_.Exception.Message;files=@()}
        Send-Json $ctx 500 @{error=('Could not start downloading {0}: {1}' -f $modelId,$_.Exception.Message)}
      }
      continue
    }
    if ($ctx.Request.HttpMethod -eq 'POST' -and $path -eq '/api/chat') {
      $payload = Read-Body $ctx; $agent = [string]$payload.agent; $modelId = [string]$payload.model
      if (!$script:Agents.ContainsKey($agent)) { Send-Json $ctx 400 @{error='Choose one of the supported local agents.'}; continue }
      if (!$script:Models.ContainsKey($modelId)) { Send-Json $ctx 400 @{error='Choose a supported local model.'}; continue }
      if (-not (Test-ModelInstalled $modelId)) { Send-Json $ctx 409 @{error=('Download {0} from Models before chatting.' -f $script:Models[$modelId].name)}; continue }
      $config = $script:Agents[$agent]
      $recentMessages = @($payload.messages | Select-Object -Last 12)
      $hasImages = $false
      foreach ($item in $recentMessages) { if ($item.images -and @($item.images).Count -gt 0) { $hasImages = $true; break } }
      if ($hasImages -and $modelId -ne 'qwen3-vl-8b') { Send-Json $ctx 400 @{error='Image messages require the Qwen3-VL vision model. Download it from Models and resend.'}; continue }
      if ($hasImages -and $modelId -eq 'qwen3-vl-8b' -and $agent -ne 'media') {
        $config = @{system='You are a thoughtful local assistant. Analyze the attached images and answer the user accurately. Be specific about what is visible and say when something cannot be determined.'}
      }
      try { Start-LocalInference $modelId }
      catch { Send-Json $ctx 502 @{error=$_.Exception.Message}; continue }
      try {
        $messages = New-Object 'System.Collections.Generic.List[object]'
        $messages.Add(@{role='system';content=$config.system})
        foreach ($item in $recentMessages) {
          if ($item.role -notin @('user','assistant')) { continue }
          if ($item.role -eq 'user' -and $item.images -and @($item.images).Count -gt 0) {
            $parts = New-Object 'System.Collections.Generic.List[object]'
            if (-not [String]::IsNullOrWhiteSpace([string]$item.content)) { $parts.Add(@{type='text';text=[string]$item.content}) }
            foreach ($image in @($item.images)) {
              $dataUri = [string]$image
              if ($dataUri -notmatch '^data:image/(png|jpeg|gif|bmp|tiff);base64,[A-Za-z0-9+/=]+$') { throw 'An image attachment was not encoded as a local image.' }
              $parts.Add(@{type='image_url';image_url=@{url=$dataUri}})
            }
            $messages.Add(@{role='user';content=$parts.ToArray()})
          } else { $messages.Add(@{role=$item.role;content=[string]$item.content}) }
        }
        $body = @{model='local';messages=$messages.ToArray();stream=$false;max_tokens=3072;temperature=0.6;top_p=0.95;presence_penalty=1.5} | ConvertTo-Json -Depth 30 -Compress
        $answer = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:43772/v1/chat/completions' -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 1800
        $content = $answer.choices[0].message.content
        if ($content -is [Array]) { $content = (($content | ForEach-Object { if ($_.text) { $_.text } }) -join "`n") }
        Send-Json $ctx 200 @{content=[string]$content;model=$modelId}
      } catch {
        Send-Json $ctx 502 @{error=$_.Exception.Message}
      }
      continue
    }
    Send-Response $ctx 404 'text/plain; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes('Not found'))
  } catch {
    if ($ctx) { try { Send-Json $ctx 500 @{error=$_.Exception.Message} } catch {} }
  }
}
$listener.Stop(); $listener.Close()
Stop-LocalInference
foreach ($modelId in @($script:DownloadJobs.Keys)) {
  foreach ($file in $script:DownloadJobs[$modelId].files) {
    if ($file.id) { Get-BitsTransfer -JobId ([Guid]$file.id) -ErrorAction SilentlyContinue | Remove-BitsTransfer -Confirm:$false -ErrorAction SilentlyContinue }
    if ($file.path -and $file.path.EndsWith('.partial') -and (Test-Path -LiteralPath $file.path)) { Remove-Item -LiteralPath $file.path -Force -ErrorAction SilentlyContinue }
  }
  if ($script:DownloadJobs[$modelId].verify) { Stop-Job -Job $script:DownloadJobs[$modelId].verify -ErrorAction SilentlyContinue; Remove-Job -Job $script:DownloadJobs[$modelId].verify -Force -ErrorAction SilentlyContinue }
}
if ($script:RuntimeJob -and $script:RuntimeJob.transfer.id) { Get-BitsTransfer -JobId ([Guid]$script:RuntimeJob.transfer.id) -ErrorAction SilentlyContinue | Remove-BitsTransfer -Confirm:$false -ErrorAction SilentlyContinue }
if ($script:RuntimeJob -and $script:RuntimeJob.state -ne 'installed') {
  if (Test-Path -LiteralPath $script:EngineArchive) { Remove-Item -LiteralPath $script:EngineArchive -Force -ErrorAction SilentlyContinue }
  $staging = Join-Path (Split-Path -Parent $script:EngineDirectory) 'b11669-staging'
  if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
