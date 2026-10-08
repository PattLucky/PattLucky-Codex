# PattLucky Codex Ver 1.0.0.

PattLucky Codex is a lightweight Windows desktop chat app for local AI models. Its main interface is a native Windows Forms window; it does not open or require the Microsoft Edge browser. A small local PowerShell service connects the app to Ollama.

## Project layout

- `src/` — native Windows app source
- `ui/` — optional browser preview interface
- `Assets/` — app logo and icon
- `dist/` — compiled app and downloadable installer bundle
- `build.ps1`, `launch.ps1`, `setup-wizard.ps1` — build, local service, and installer scripts

## Install

1. Extract `dist/PattLuckyCodex-1.0.0.zip` from the project, or download the bundle from a release.
2. Run `setup-wizard.ps1` and choose **Install & launch**.
3. Open PattLucky Codex from the desktop shortcut.

The app requires Windows PowerShell 5.1, .NET Framework 4.x, and [Ollama for Windows](https://ollama.com/download). Install and start Ollama, choose an agent, and download its model. The first model download needs internet. After downloading, model inference and chat run on your computer without API keys or per-message fees.

## Local agents

- Essays and documents: `gemma4:12b` (about 7.7–8 GB)
- Coding: `qwen3-coder:30b` (about 19 GB)
- Creative writing: `qwen3.5:9b` (about 6.6–7.6 GB)

Each model downloads only when selected. All three use roughly 34–35 GB of storage. Chat history is saved locally under `%LOCALAPPDATA%\PattLucky Codex\chats.json`, written atomically with a previous-save backup, and can be exported from the app.

At startup, the app checks the latest GitHub release and shows an update prompt when its version is newer. The checker needs internet access and a published release on the public repository; without a public release it reports that no update is available to check. Bump the assembly version in `src/PattLuckyCodex.cs`, rebuild, then attach the matching `dist/PattLuckyCodex-<version>.zip` as a GitHub release asset for future updates.

## Build from source

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` from the project folder. The script compiles `src/PattLuckyCodex.cs` and writes the app and versioned ZIP bundle into `dist/`. The installer archive is flattened so the setup wizard can install it directly.

To preview the desktop app from the terminal after building, run `.\dist\PattLuckyCodex.exe` from the project folder.

For an optional browser preview, run `launch.ps1` in PowerShell. That preview uses your default browser; the installed desktop app does not need a browser.
