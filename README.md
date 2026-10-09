# PattGPT Codex

PattGPT Codex is a lightweight Windows desktop chat app for local AI models. Its main interface is a native Windows Forms window; it does not open or require the Microsoft Edge browser. A small local PowerShell service connects the app to Ollama.

## Project layout

- `src/` — native Windows app source
- `ui/` — optional browser preview interface
- `Assets/` — app logo and icon
- `dist/` — compiled app and downloadable installer bundle
- `build.ps1`, `launch.ps1`, `setup-wizard.ps1` — build, local service, and installer scripts

## Install

1. Extract the latest `dist/PattGPTCodex-<version>.zip` bundle from the project, or download a bundle from a release.
2. Run `setup-wizard.ps1` and choose **Install & launch**.
3. Open PattGPT Codex from the desktop shortcut.

The app requires Windows PowerShell 5.1 and .NET Framework 4.x. Open **Models** to download and launch the official Ollama installer, start Ollama, and download any of the four agent models. Internet is needed for the initial Ollama and model downloads. Afterward, model inference and chat run on your computer without API keys or per-message fees.

## Local agents

- Essays and documents: `gemma4:12b` (about 7.7–8 GB)
- Coding: `qwen3-coder:30b` (about 19 GB)
- Creative writing: `qwen3.5:9b` (about 6.6–7.6 GB)
- Lite chat: `qwen3.5:0.8b` (about 1.2–1.3 GB)

## Local photo and video generation

Choose **Photo & video creator (BETA in img gen)** for help writing prompts, shot lists, and visual direction with the local `qwen3.5:9b` model. Open **Media Studio · BETA** in the native Windows app to render actual images or videos through [ComfyUI](https://github.com/Comfy-Org/ComfyUI), an open-source tool that runs on your computer. Image generation is beta. PattGPT Codex does not send prompts to a hosted provider, require an API key, or charge per generation. The optional browser preview remains chat-only.

ComfyUI and its generation models are installed separately and can require substantial disk space and a supported GPU; model downloads may use internet bandwidth. Install ComfyUI and the image or video models you choose, start it at `http://127.0.0.1:8188`, then export a workflow in ComfyUI's **API format** and load that JSON file in Media Studio. Select the positive prompt input, enter a prompt, and choose **Generate locally**. Outputs are saved under `%LOCALAPPDATA%\PattGPT Codex\Media\Images` or `Videos`. The workflow determines the model, resolution, and output format; ComfyUI and model licenses apply. Media Studio accepts loopback addresses only and will not connect to hosted providers. Only load workflows and custom nodes you trust.

This integration does not bundle ComfyUI or model weights and does not install them automatically. You stay in control of which open-source workflows and models you download.

Each model downloads only when selected. The three larger models use roughly 34–35 GB combined; Lite chat is about 1.2–1.3 GB. Chat history is saved locally under `%LOCALAPPDATA%\PattGPT Codex\chats.json`, written atomically with a previous-save backup, and can be exported from the app.

Responses use speed-oriented Ollama defaults: thinking is disabled, answers are capped at 1,536 tokens, the context window is 8,192 tokens, and only the latest 12 chat messages are sent with each request. Older messages remain in local chat history but are left out of the model prompt to prevent long conversations from slowing down over time. Ollama keeps the active model loaded for up to 30 minutes after use to reduce delays between prompts.

At startup, the installed version and GitHub update status appear beside each other in the app header. The checker compares against the latest published GitHub release and offers its release page when newer. Git builds automatically use `major.minor.commit-count` as the app version, so each new commit produces a higher build version without manually editing assembly metadata. Publish the matching `dist/PattGPTCodex-<version>.zip` as a GitHub release for the updater to detect it. A source archive without Git history falls back to the assembly version in `src/PattLuckyCodex.cs`.

## Build from source

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` from the project folder. The script compiles `src/PattLuckyCodex.cs` with the version derived from the Git commit count and writes the app and versioned ZIP bundle into `dist/`. For a fixed release build, pass `-VersionOverride 2.0.4`. The installer archive is flattened so the setup wizard can install it directly.

To preview the desktop app from the terminal after building, run `.\dist\PattGPTCodex.exe` from the project folder.

For an optional browser preview, run `launch.ps1` in PowerShell. That preview uses your default browser; the installed desktop app does not need a browser.
