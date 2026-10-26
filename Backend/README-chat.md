# AR chat prototype

## Run the updated backend

Run `\.\start-gateway.ps1` from this folder. The gateway listens locally on port 8001. Configure the Quest HTTPS tunnel to forward to local port 8001. Keep Ollama listening on port 11434.

MainScene uses the HTTPS backend tunnel at
https://6z7xkwds-8000.inc1.devtunnels.ms so the Quest can reach the PC backend.
Unity sends chat, OCR, voice and session requests to that backend. The backend
calls Ollama itself; when both run on this PC it uses http://127.0.0.1:11434.
The Ollama tunnel (https://6z7xkwds-11434.inc1.devtunnels.ms) is for server-side
Ollama access when needed, and should not be called directly by the Unity app.
Moondream must be available in the Ollama service.

## Unity

Open Assets/Scenes/MainScene.unity and press Play.

- Send captures the current camera view and asks Moondream.
- General image questions use the current frame to describe scenes, objects,
  actions, spatial relationships, diagrams, and clearly visible details.
- Scan Text captures the view and runs Tesseract through /ocr.
- Follow-up questions include recent successful exchanges and scanned text.
- Clear chat clears both displayed messages and model context.
- Memory lasts for the current Play session only. It keeps up to three recent
  exchanges within 5000 characters, truncating individual entries to 1500.
- The panel shows progress and disables Send/Scan/Clear during a request.
- Microphone uses the existing Windows dictation implementation.

The panel is a world-space Unity canvas. Headset passthrough and XR hand/controller
input still require separate integration.

## OCR

Tesseract was installed using the UB-Mannheim Windows package.
The service discovers tesseract.exe on PATH or in Program Files/Tesseract-OCR.
TESSERACT_CMD can explicitly configure its path.

## Checks

    .\venv\Scripts\python.exe -m unittest discover -s tests -v

Unity: Tools > AR Vision > Test Chat Integration.
This uses a generated poster fixture, checks camera capture and scene bindings,
checks memory and Clear chat, and calls the real OCR and Moondream services.
Results go to tests/unity-smoke-result.txt.

Moondream's installed single-question template receives recent text context
inside the current prompt. The model may still miss context or misread details.
OCR text is displayed directly, without rewriting it through the model.

Tools > AR Vision > Polish Chat Panel reapplies the saved panel design.
Backups of the original scripts and scene are in .backups/chat-v1-*.

## Voice input update

The Microphone button records only Microphone Array (Realtek(R) Audio).
Click once to record and again to finish (maximum 29 seconds).
Speech recognition runs locally inside the gateway, then the question is sent
automatically to the assistant with the current wireless-camera frame.
There is no transcription-review step and no need to press Send for voice.

Run .\start-gateway.ps1 to keep the gateway running in a separate background
process on port 8001. The local Whisper model is downloaded separately if needed and is not committed.
The UI uses higher-resolution world-space text, larger body text and a full-width
voice status line. Use 1x Game view zoom for a sharp editor preview.

## Visual conversation memory

Chat now sends a unique session ID with questions and text scans. The gateway
keeps up to 12 compressed camera views in RAM, with descriptions and OCR text.
It also retains the last exchanges and bounded extractive notes from older ones.
No user camera snapshots are written to disk by this feature.

Show an object and ask about it to save a view. Looking at something silently
does not save it. Say "What book did I show you earlier?" to recall the book,
or "Read its title again" to use the saved OCR. Ask "What is around me now?"
to explicitly use the live camera instead. If several saved views match,
reply with the view number shown in chat. Repeated captures may represent the
same physical object as separate views; this version does not track objects.

Clear chat waits for the gateway to delete the session, then starts a new one.
Scene exit sends best-effort cleanup. If the app exits abruptly or the gateway
cannot be reached, abandoned sessions expire after two hours of inactivity,
checked once per minute. A gateway restart clears all sessions. The store is
bounded to 16 sessions. The oldest images/notes are evicted as limits are reached.

References use explicit words, object labels and recent focus. This is a
bounded prototype, not unlimited or perfect recall. Earlier model descriptions
can be wrong; extracted text and saved images remain available for checking.
A Moondream empty answer receives one labeled description fallback.

Tests:
- python -m unittest discover -s tests -v
- python tests/live_visual_memory.py
- Unity menu: Tools > AR Vision > Test Session Memory

