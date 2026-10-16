$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compilerPaths = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.x Developer Tools.' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'PattGPTCodex.exe'
$freshExe = Join-Path $env:TEMP ('PattGPTCodex-' + [Guid]::NewGuid().ToString('N') + '.exe')
$source = Join-Path $root 'src\PattLuckyCodex.cs'
$icon = Join-Path $root 'Assets\PattLuckyCodex.ico'
# Read UTF-8 without relying on Windows PowerShell's ANSI default for files without a BOM.
$sourceText = [IO.File]::ReadAllText($source)
$versionMatch = [regex]::Match($sourceText,'AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.\d+"\)')
if (-not $versionMatch.Success) { throw 'Could not read the app version from src\PattLuckyCodex.cs.' }
$version = ([version]$versionMatch.Groups['version'].Value).ToString(3)
$buildSource = Join-Path $env:TEMP ('PattLuckyCodex-' + [Guid]::NewGuid().ToString('N') + '.cs')
$versionedSource = [regex]::Replace($sourceText,'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)',('AssemblyVersion("' + $version + '.0")'),1)
$versionedSource = [regex]::Replace($versionedSource,'AssemblyFileVersion\("\d+\.\d+\.\d+\.\d+"\)',('AssemblyFileVersion("' + $version + '.0")'),1)
Set-Content -LiteralPath $buildSource -Value $versionedSource -Encoding UTF8
try { & $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32icon:$icon" "/out:$freshExe" $buildSource }
finally { Remove-Item -LiteralPath $buildSource -Force -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0) { throw 'PattGPT Codex launcher compilation failed.' }
$bundle = Join-Path $dist ("PattGPTCodex-{0}.zip" -f $version)
$bundleExe = $freshExe
$bundleStream = $null
$archive = $null
$exeUpdated = $false
try {
  try { Copy-Item -LiteralPath $freshExe -Destination $exe -Force; $bundleExe = $exe; $exeUpdated = $true }
  catch { Write-Warning 'The installed app executable is locked. The versioned ZIP contains the new build.' }
  Add-Type -AssemblyName System.IO.Compression
  $bundleStream = [IO.File]::Open($bundle,[IO.FileMode]::Create)
  $archive = New-Object System.IO.Compression.ZipArchive($bundleStream,[System.IO.Compression.ZipArchiveMode]::Create)
  $bundleFiles = @(
    @{ Name='app.html'; Path=(Join-Path $root 'ui\app.html') },
    @{ Name='launch.ps1'; Path=(Join-Path $root 'launch.ps1') },
    @{ Name='setup-wizard.ps1'; Path=(Join-Path $root 'setup-wizard.ps1') },
    @{ Name='PattLuckyCodex.ico'; Path=(Join-Path $root 'Assets\PattLuckyCodex.ico') },
    @{ Name='PattLuckyLogo.png'; Path=(Join-Path $root 'Assets\PattLuckyLogo.png') },
    @{ Name='README.md'; Path=(Join-Path $root 'README.md') }
  )
  foreach ($file in $bundleFiles) {
    $entry = $archive.CreateEntry($file.Name,[System.IO.Compression.CompressionLevel]::Optimal)
    $inputStream = [IO.File]::OpenRead($file.Path); $outputStream = $entry.Open()
    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
  }
  $exeEntry = $archive.CreateEntry('PattGPTCodex.exe',[System.IO.Compression.CompressionLevel]::Optimal)
  $inputStream = [IO.File]::OpenRead($bundleExe); $outputStream = $exeEntry.Open()
  try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
} finally {
  if ($archive) { $archive.Dispose() }
  if ($bundleStream) { $bundleStream.Dispose() }
  if (Test-Path -LiteralPath $freshExe) { Remove-Item -LiteralPath $freshExe -Force -ErrorAction SilentlyContinue }
}
$archiveDirectory = Join-Path $dist 'archive'
New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $dist -Force) {
  if ($item.Name -eq 'archive' -or $item.Name -eq ("PattGPTCodex-{0}.zip" -f $version)) { continue }
  if ($item.Name -notmatch '^Patt(?:GPT|Lucky)Codex(?:[-.]|$)') { continue }
  if ($item.Name -eq 'PattGPTCodex.exe') {
    if ($exeUpdated) { continue }
    try {
      $installedVersion = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($item.FullName).FileVersion).ToString(3)
      if ($installedVersion -eq $version) { continue }
    } catch {}
  }
  $destination = Join-Path $archiveDirectory $item.Name
  if (Test-Path -LiteralPath $destination) { $destination = Join-Path $archiveDirectory ($item.BaseName + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + $item.Extension) }
  try { Move-Item -LiteralPath $item.FullName -Destination $destination -ErrorAction Stop }
  catch { Write-Warning ("Could not archive {0}; it may be in use." -f $item.FullName) }
}
$legacyRootExe = Join-Path $root 'PattLuckyCodex-Updated.exe'
if (Test-Path -LiteralPath $legacyRootExe) {
  try { Move-Item -LiteralPath $legacyRootExe -Destination (Join-Path $archiveDirectory 'PattLuckyCodex-Updated.exe') -ErrorAction Stop }
  catch { Write-Warning 'Could not archive the legacy root executable; it may be in use.' }
}
Write-Host "Built version ${version}: $bundle. Desktop executable: $exe" -ForegroundColor Green
