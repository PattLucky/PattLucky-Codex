# PattLucky Codex Ver 1.0.0.

PattLucky Codex installs as a lightweight Windows desktop app. It opens in an app-style Microsoft Edge window, while a hidden local PowerShell service connects the interface to Ollama. Ollama is still required for local AI responses; Microsoft Edge is required to display the app.

## Install

1. Run PattLucky Codex and select **Download app bundle (.zip)**, or use the ZIP in the project folder.
2. Extract the ZIP to a folder.
3. Run `setup-wizard.ps1` and select **Install & launch**.
4. Use the new desktop shortcut to open PattLucky Codex.

Install and start [Ollama for Windows](https://ollama.com/download), then choose an agent in PattLucky Codex and download its model. Model files are free to download; your computer supplies the storage, RAM, and processing power. The first model download needs internet.

## Local agents

- Essays & documents: `gemma4:12b` (about 7.7–8 GB)
- Coding: `qwen3-coder:30b` (about 19 GB)
- Creative writing: `qwen3.5:9b` (about 6.6–7.6 GB)

Each model downloads only when selected. All three take roughly 34–35 GB of storage. The app does not require API keys or charge per message. Chat history is stored in the browser profile used by the desktop app and can be exported from the sidebar.

For a browser preview instead, run `launch.ps1` from PowerShell. The app is served at `http://127.0.0.1:43771`.
