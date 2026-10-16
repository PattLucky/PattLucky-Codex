# PattGPT Codex

PattGPT Codex is a lightweight Windows desktop chat app for local AI models. Its main interface is a native Windows Forms window; it does not open or require a browser. A small local PowerShell service runs a bundled llama.cpp inference engine and keeps chat requests on this PC.

## Project layout

- `src/` — native Windows app source
- `ui/` — optional browser preview interface
- `Assets/` — app logo and icon
- `dist/` — current executable and versioned installer bundle
- `dist/archive/` — older builds retained outside the current release folder
- `build.ps1`, `launch.ps1`, `setup-wizard.ps1` — build, local service, and installer scripts

## Install

1. Extract the latest `dist/PattGPTCodex-<version>.zip` bundle from the project, or download a bundle from a release.
2. Run `setup-wizard.ps1` and choose **Install & launch**.
3. Open PattGPT Codex from the desktop shortcut.

The app requires Windows PowerShell 5.1 and .NET Framework 4.x. **Lite chat with Qwen3 8B is the default for new chats**; open **Models** to download it when you are ready. The app downloads and verifies the pinned llama.cpp CPU runtime and selected GGUF model files under `%LOCALAPPDATA%\PattGPT Codex`; inference does not depend on hosted AI services. Nothing downloads just by opening PattGPT Codex. The first engine/model download needs internet and several gigabytes of free disk space. After downloading, inference runs locally and chats can work offline without API keys or per-message fees. Only one PattGPT Codex instance can run at a time.

## Local agents

- Essays and documents: Qwen3 14B Q4_K_M (about 9.0 GB; 16 GB RAM recommended)
- Coding: Qwen3 Coder 30B A3B Q4_K_M (about 18.6 GB; 32 GB RAM recommended)
- Creative writing: Qwen3 14B Q4_K_M (about 9.0 GB; 16 GB RAM recommended)
- Lite chat (default): Qwen3 8B Q4_K_M (about 5.0 GB; 8 GB RAM recommended)
- Image understanding: Qwen3-VL 8B plus its vision projector (about 5.8 GB; 8 GB RAM recommended)

Models are downloaded on demand, not bundled with the app. The four model downloads total about 38.4 GB if you install all of them. The built-in runtime currently uses the CPU, so generation speed depends on your processor and available memory.

## Chat images and attachments

In the native Windows app, use **＋ Attach** or drag files into the message composer. A message can include up to five PNG, JPEG, GIF, BMP, or TIFF images (8 MB per image and 20 MB combined), plus plain-text or code files such as TXT, Markdown, CSV, JSON, XML, HTML, CSS, JavaScript, TypeScript, C#, PowerShell, Python, Java, SQL, and LOG (32 KB per file and 64 KB combined). Images are analyzed by the local Qwen3-VL model; install it from **Models** to use image understanding. To keep requests within memory limits, the app sends up to 20 MB of the most recent image attachments with each request. Text and code files are included in the model prompt as attachment content. Attachments are copied locally under `%LOCALAPPDATA%\PattGPT Codex\attachments` and remain available with saved chats; they are not sent to a hosted service. PDFs and other binary document formats are not currently supported.

## Business workflows

Choose **Business workflows** for a professional local assistant that marks missing facts as `[TBD]` instead of inventing details. The **Templates** button in the message composer inserts reusable starting prompts for client proposals, SOPs, client emails, meeting minutes and action items, project briefs, weekly status reports, executive decision briefs, and risk registers. Existing text in the composer is preserved as source context when a template is inserted. Review generated business content before sending it externally; the model does not verify legal, regulatory, financial, or operational claims.

## Local photo and video generation

Choose **Photo & video creator (BETA in img gen)** for help writing prompts, shot lists, and visual direction with the local Qwen3 14B model. Open **Media Studio · BETA** in the native Windows app to render actual images or videos through [ComfyUI](https://github.com/Comfy-Org/ComfyUI), an open-source tool that runs on your computer. Image generation is beta. PattGPT Codex does not send prompts to a hosted provider, require an API key, or charge per generation. The optional browser preview is chat-only.

ComfyUI and its generation models are installed separately and can require substantial disk space and a supported GPU; model downloads may use internet bandwidth. Install ComfyUI and the image or video models you choose, start it at `http://127.0.0.1:8188`, then export a workflow in ComfyUI's **API format** and load that JSON file in Media Studio. Select the positive prompt input, enter a prompt, and choose **Generate locally**. Outputs are saved under `%LOCALAPPDATA%\PattGPT Codex\Media\Images` or `Videos`. The workflow determines the model, resolution, and output format; ComfyUI and model licenses apply. Media Studio accepts loopback addresses only and will not connect to hosted providers. Only load workflows and custom nodes you trust.

This integration does not bundle ComfyUI or model weights and does not install them automatically. You stay in control of which open-source workflows and models you download.

Chat history is saved locally under `%LOCALAPPDATA%\PattGPT Codex\chats.json`, written atomically with a previous-save backup, and can be exported from the app.

Only the latest 12 chat messages are sent with each request; older messages remain in local chat history to keep prompts bounded. The local model stays loaded while the selected model is in use. Switching to another model stops the previous local inference process.

At startup, the installed version and GitHub update status appear beside each other in the app header. The checker compares against the latest published GitHub release and offers its release page when newer. The version in `AssemblyVersion` and `AssemblyFileVersion` in `src/PattLuckyCodex.cs` is the single release version. Publish the matching `dist/PattGPTCodex-<version>.zip` as a GitHub release for the updater to detect it.

## Build from source

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` from the project folder. The version in `AssemblyVersion` and `AssemblyFileVersion` in `src/PattLuckyCodex.cs` is the single release version. The build writes only that version's installer bundle and current executable to the top of `dist/`; older generated builds are preserved in `dist/archive/`. The installer archive is flattened so the setup wizard can install it directly.

To preview the desktop app from the terminal after building, run `.\dist\PattGPTCodex.exe` from the project folder.

For an optional browser preview, run `launch.ps1` in PowerShell. That preview uses your default browser; the installed desktop app does not need a browser.
