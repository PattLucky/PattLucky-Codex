$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compilerPaths = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.x Developer Tools.' }
$exe = Join-Path $root 'PattLuckyCodex.exe'
$source = Join-Path $root 'PattLuckyCodex.cs'
$icon = Join-Path $root 'PattLuckyCodex.ico'
& $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll "/win32icon:$icon" "/out:$exe" $source
if ($LASTEXITCODE -ne 0) { throw 'PattLucky Codex launcher compilation failed.' }
$bundleFiles = @('app.html','launch.ps1','setup-wizard.ps1','PattLuckyCodex.exe','PattLuckyLogo.png','README.md') | ForEach-Object { Join-Path $root $_ }
$bundle = Join-Path $root 'PattLuckyCodex-1.0.0.zip'
Compress-Archive -Path $bundleFiles -DestinationPath $bundle -Force
Write-Host "Built $exe and $bundle" -ForegroundColor Green
