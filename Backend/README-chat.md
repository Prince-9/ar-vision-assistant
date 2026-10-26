# Backend gateway

The Unity client sends image questions, OCR scans, speech requests, and conversation-session requests to this FastAPI service. Ollama runs behind the gateway; the headset should call the gateway only.

## Start it

From this folder, create the virtual environment and install dependencies as described in the repository-root README, then run:

```powershell
.\start-gateway.ps1
```

The script listens on `127.0.0.1:8001`. Confirm startup with `Invoke-RestMethod http://127.0.0.1:8001/health`.

The gateway expects Ollama at `http://127.0.0.1:11434` unless `OLLAMA_URL` points to another `/api/chat` endpoint. It uses `moondream` for image questions and `qwen3.5:9b` for text-only requests. Pull both models with Ollama before asking questions. Tesseract must be installed and discoverable on `PATH` for OCR.

## Connect Unity

Set the scene's `GatewayConfig.BaseUrl` to the backend URL. Use `http://127.0.0.1:8001` for Unity Editor on the same PC. For Quest, use the HTTPS address from a tunnel configured to forward to local port `8001`; `127.0.0.1` on the headset refers to the headset itself.

## Conversation memory

Each chat session keeps up to 12 compressed camera views in memory, with descriptions and OCR text. It retains recent exchanges and bounded notes from older ones. Memory lasts until Clear chat, leaving Play mode, gateway restart, or the two-hour inactivity expiry. Camera snapshots are not written to disk by session memory.

Show an object and ask about it to save a view. Then ask a follow-up such as “What book did I show you earlier?” Clear chat deletes the session. This is bounded prototype memory; model descriptions can be wrong, and repeated views are not guaranteed to be recognized as the same physical object.

## OCR and speech

Scan Text uses Tesseract and returns extracted text directly. Microphone input runs speech recognition through the gateway and sends the recognized question automatically. A local Whisper model may be downloaded on first use; model weights and recordings are not included in Git.

