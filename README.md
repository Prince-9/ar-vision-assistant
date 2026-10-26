# AR Vision Assistant

An AR chat assistant for Meta Quest and Unity. It sends camera frames and questions to a Python gateway for image understanding, OCR, speech recognition, and short-term conversation memory. The gateway uses Ollama for language and vision models.

## Repository layout

- `Assets/`, `Packages/`, `ProjectSettings/`: Unity application
- `Backend/`: Python API gateway and service code

## Requirements

- Unity **6000.6.0f1** (see `ProjectSettings/ProjectVersion.txt`)
- Android Build Support with Android SDK & NDK Tools and OpenJDK in Unity Hub, for Quest builds
- Python 3.10 or 3.11 on Windows
- Ollama with `moondream` and `qwen3.5:9b`
- Tesseract OCR installed and available on `PATH` for Scan Text

## 1. Set up the backend

Open PowerShell in `Backend/` and run:

```powershell
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

Install/start Ollama, then download the models expected by the gateway:

```powershell
ollama pull moondream
ollama pull qwen3.5:9b
```

Install Tesseract OCR and ensure `tesseract.exe` is on `PATH`. Start the gateway:

```powershell
.\start-gateway.ps1
```

The script starts the API at `http://127.0.0.1:8001`. Check it with:

```powershell
Invoke-RestMethod http://127.0.0.1:8001/health
```

By default, the gateway calls Ollama at `http://127.0.0.1:11434`. To use a different Ollama host, set `OLLAMA_URL` to its `/api/chat` endpoint before starting the backend.

## 2. Set up and run Unity

1. Clone this repository and open its root folder in Unity Hub.
2. Let Unity resolve the packages listed in `Packages/manifest.json`.
3. Open `Assets/Scenes/MainScene.unity` and press **Play** to test in the Editor. The Editor uses the selected PC webcam.
4. Press **Play**. In the Unity Editor the app automatically uses the local gateway at `http://127.0.0.1:8001`.

For a Quest headset, build and install the Android/Meta Quest profile. The headset cannot use the PC's `127.0.0.1`; expose the gateway at an HTTPS address reachable by the Quest (for example, a dev tunnel forwarding to local port **8001**) and set `GatewayConfig.BaseUrl` to that public backend URL. Keep Ollama behind the gateway; the Unity app should call only the backend. Grant camera and microphone access when prompted.

## Features

- Ask questions about the current camera view
- Scan visible text with OCR
- Ask follow-up questions using recent conversation and saved views
- Use speech input and spoken responses where configured

See [`Backend/README-chat.md`](Backend/README-chat.md) for backend behavior and troubleshooting. Model weights, Unity-generated folders, build outputs, logs, virtual environments, and recordings are intentionally excluded from Git.\n