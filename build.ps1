param([string]$VersionOverride)
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
$freshName = 'PattGPTCodex-Updated-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.exe'
$freshExe = Join-Path $dist $freshName
$source = Join-Path $root 'src\PattLuckyCodex.cs'
$icon = Join-Path $root 'assets\PattLuckyCodex.ico'
# Read UTF-8 without relying on Windows PowerShell's ANSI default for files without a BOM.
$sourceText = [IO.File]::ReadAllText($source)
$versionMatch = [regex]::Match($sourceText,'AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.\d+"\)')
if (-not $versionMatch.Success) { throw 'Could not read the app version from src\PattLuckyCodex.cs.' }
$baseVersion = [version]$versionMatch.Groups['version'].Value
if (-not [string]::IsNullOrWhiteSpace($VersionOverride)) {
  try { $requestedVersion = [version]::Parse($VersionOverride) } catch { throw 'VersionOverride must use major.minor.patch format, for example 2.0.3.' }
  if ($requestedVersion.Build -lt 0 -or $requestedVersion.Revision -ge 0) { throw 'VersionOverride must use major.minor.patch format, for example 2.0.3.' }
  $version = $requestedVersion.ToString(3)
} else {
  $commitCountText = & git -C $root rev-list --count HEAD 2>$null
  if ($LASTEXITCODE -eq 0 -and $commitCountText -match '^\d+$') {
    $version = '{0}.{1}.{2}' -f $baseVersion.Major,$baseVersion.Minor,[int]$commitCountText
  } else {
    $version = $baseVersion.ToString(3)
    Write-Warning 'Git history is unavailable; using the source fallback version.'
  }
}
$buildSource = Join-Path $env:TEMP ('PattLuckyCodex-' + [Guid]::NewGuid().ToString('N') + '.cs')
$versionedSource = [regex]::Replace($sourceText,'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)',('AssemblyVersion("' + $version + '.0")'),1)
$versionedSource = [regex]::Replace($versionedSource,'AssemblyFileVersion\("\d+\.\d+\.\d+\.\d+"\)',('AssemblyFileVersion("' + $version + '.0")'),1)
Set-Content -LiteralPath $buildSource -Value $versionedSource -Encoding UTF8
try { & $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32icon:$icon" "/out:$freshExe" $buildSource }
finally { Remove-Item -LiteralPath $buildSource -Force -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0) { throw 'PattGPT Codex launcher compilation failed.' }
$bundle = Join-Path $dist ("PattGPTCodex-{0}.zip" -f $version)
$bundleStream = $null
$archive = $null
try {
  try { Copy-Item -LiteralPath $freshExe -Destination $exe -Force; Remove-Item -LiteralPath $freshExe -Force; $freshExe = $exe }
  catch { Write-Warning ("The app is open, so the current EXE is locked. The bundle and {0} contain the new build." -f $freshName) }
  Add-Type -AssemblyName System.IO.Compression
  $bundleStream = [IO.File]::Open($bundle,[IO.FileMode]::Create)
  $archive = New-Object System.IO.Compression.ZipArchive($bundleStream,[System.IO.Compression.ZipArchiveMode]::Create)
  $bundleFiles = @(
    @{ Name='app.html'; Path=(Join-Path $root 'ui\app.html') },
    @{ Name='launch.ps1'; Path=(Join-Path $root 'launch.ps1') },
    @{ Name='setup-wizard.ps1'; Path=(Join-Path $root 'setup-wizard.ps1') },
    @{ Name='PattLuckyCodex.ico'; Path=(Join-Path $root 'assets\PattLuckyCodex.ico') },
    @{ Name='PattLuckyLogo.png'; Path=(Join-Path $root 'assets\PattLuckyLogo.png') },
    @{ Name='README.md'; Path=(Join-Path $root 'README.md') }
  )
  foreach ($file in $bundleFiles) {
    $entry = $archive.CreateEntry($file.Name,[System.IO.Compression.CompressionLevel]::Optimal)
    $inputStream = [IO.File]::OpenRead($file.Path); $outputStream = $entry.Open()
    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
  }
  $exeEntry = $archive.CreateEntry('PattGPTCodex.exe',[System.IO.Compression.CompressionLevel]::Optimal)
  $inputStream = [IO.File]::OpenRead($freshExe); $outputStream = $exeEntry.Open()
  try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
} finally {
  if ($archive) { $archive.Dispose() }
  if ($bundleStream) { $bundleStream.Dispose() }
}
Write-Host "Built $bundle. Desktop executable: $freshExe" -ForegroundColor Green
