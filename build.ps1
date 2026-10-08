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
$exe = Join-Path $dist 'PattLuckyCodex.exe'
$freshName = 'PattLuckyCodex-Updated-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.exe'
$freshExe = Join-Path $dist $freshName
$source = Join-Path $root 'src\PattLuckyCodex.cs'
$icon = Join-Path $root 'assets\PattLuckyCodex.ico'
$sourceText = Get-Content -LiteralPath $source -Raw
$versionMatch = [regex]::Match($sourceText,'AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.\d+"\)')
if (-not $versionMatch.Success) { throw 'Could not read the app version from src\PattLuckyCodex.cs.' }
$version = $versionMatch.Groups['version'].Value
& $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32icon:$icon" "/out:$freshExe" $source
if ($LASTEXITCODE -ne 0) { throw 'PattLucky Codex launcher compilation failed.' }
$bundle = Join-Path $dist ("PattLuckyCodex-{0}.zip" -f $version)
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
  $exeEntry = $archive.CreateEntry('PattLuckyCodex.exe',[System.IO.Compression.CompressionLevel]::Optimal)
  $inputStream = [IO.File]::OpenRead($freshExe); $outputStream = $exeEntry.Open()
  try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
} finally {
  if ($archive) { $archive.Dispose() }
  if ($bundleStream) { $bundleStream.Dispose() }
}
Write-Host "Built $bundle. Desktop executable: $freshExe" -ForegroundColor Green
