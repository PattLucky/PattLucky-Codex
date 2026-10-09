# PattGPT Codex Ver 2.0.4. portable setup wizard
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
function Find-PackageFile([string]$name) {
  foreach ($candidate in @((Join-Path $source $name),(Join-Path (Join-Path $source 'assets') $name),(Join-Path (Join-Path $source 'ui') $name),(Join-Path (Join-Path $source 'dist') $name))) { if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate } }
  throw "Required install file '$name' was not found. Rebuild or re-extract the app bundle."
}
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object Windows.Forms.Form; $form.Text='PattGPT Codex · Setup Wizard'; $form.Size='500,300'; $form.StartPosition='CenterScreen'; $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false
$title=New-Object Windows.Forms.Label; $title.Text='PattGPT Codex'; $title.Font=New-Object Drawing.Font('Segoe UI',18,[Drawing.FontStyle]::Bold); $title.Location='24,20'; $title.Size='420,34'; $form.Controls.Add($title)
$appVersion='2.0.4'; try { $versionInfo=[Diagnostics.FileVersionInfo]::GetVersionInfo((Find-PackageFile 'PattGPTCodex.exe')); if($versionInfo.FileVersion){$appVersion=([version]$versionInfo.FileVersion).ToString(3)} } catch {}
$desc=New-Object Windows.Forms.Label; $desc.Text="Ver $appVersion  ·  Lightweight native Windows desktop app`r`nRequires Ollama for local AI models. No browser required."; $desc.Location='26,62'; $desc.Size='430,48'; $form.Controls.Add($desc)
$choose=New-Object Windows.Forms.Button; $choose.Text='Choose install folder'; $choose.Location='26,126'; $choose.Size='150,34'; $form.Controls.Add($choose)
$ollama=New-Object Windows.Forms.Button; $ollama.Text='Get Ollama (required)'; $ollama.Location='26,168'; $ollama.Size='170,34'; $form.Controls.Add($ollama)
$folder=New-Object Windows.Forms.Label; $folder.Text=Join-Path $env:USERPROFILE 'PattGPT Codex'; $folder.Location='185,135'; $folder.Size='270,25'; $form.Controls.Add($folder)
$install=New-Object Windows.Forms.Button; $install.Text='Install & launch'; $install.Location='320,200'; $install.Size='140,38'; $install.BackColor=[Drawing.Color]::FromArgb(163,230,53); $form.Controls.Add($install)
$choose.Add_Click({$dialog=New-Object Windows.Forms.FolderBrowserDialog; $dialog.SelectedPath=$folder.Text; if($dialog.ShowDialog() -eq 'OK'){$folder.Text=Join-Path $dialog.SelectedPath 'PattGPT Codex'}})
$ollama.Add_Click({Start-Process 'https://ollama.com/download'})
$install.Add_Click({try { $target=$folder.Text; New-Item -ItemType Directory -Force -Path $target|Out-Null; foreach($name in @('app.html','launch.ps1','PattGPTCodex.exe','PattLuckyCodex.ico','PattLuckyLogo.png','setup-wizard.ps1')) { Copy-Item -LiteralPath (Find-PackageFile $name) -Destination (Join-Path $target $name) -Force }; $shortcutPath=Join-Path ([Environment]::GetFolderPath('Desktop')) 'PattGPT Codex.lnk'; $shell=New-Object -ComObject WScript.Shell; $shortcut=$shell.CreateShortcut($shortcutPath); $shortcut.TargetPath=Join-Path $target 'PattGPTCodex.exe'; $shortcut.WorkingDirectory=$target; $shortcut.Save(); Start-Process (Join-Path $target 'PattGPTCodex.exe'); [Windows.Forms.MessageBox]::Show('Installed. Launch PattGPT Codex from your desktop shortcut.','Setup complete')|Out-Null; $form.Close() } catch {[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Setup failed')|Out-Null}})
[void]$form.ShowDialog()
