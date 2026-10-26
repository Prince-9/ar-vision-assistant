import logging, subprocess, tempfile, os

logger = logging.getLogger("gateway.tts")
VOICE_MODEL_PATH = os.environ.get("PIPER_VOICE_PATH", "./voices/en_US-lessac-medium.onnx")

def synthesize(text: str) -> bytes:
    if not os.path.exists(VOICE_MODEL_PATH):
        raise RuntimeError(
            f"Piper voice model not found at {VOICE_MODEL_PATH}. "
            "Download from https://github.com/rhasspy/piper/blob/master/VOICES.md "
            "and set PIPER_VOICE_PATH, or update tts.py."
        )
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_wav:
        output_path = tmp_wav.name
    try:
        process = subprocess.run(
            ["piper", "--model", VOICE_MODEL_PATH, "--output_file", output_path],
            input=text.encode("utf-8"), capture_output=True, timeout=30,
        )
        if process.returncode != 0:
            raise RuntimeError(f"Piper failed: {process.stderr.decode('utf-8', errors='ignore')}")
        with open(output_path, "rb") as f:
            return f.read()
    finally:
        if os.path.exists(output_path):
            os.remove(output_path)
