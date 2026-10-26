import logging
from pathlib import Path
import os
from faster_whisper import WhisperModel

logger = logging.getLogger("gateway.stt")
MODEL_SIZE = "base"
DEVICE = "cpu"
COMPUTE_TYPE = "int8"
_model = None

def get_model():
    global _model
    if _model is None:
        logger.info(f"Loading Whisper model '{MODEL_SIZE}' on {DEVICE}...")
        local_model = Path(__file__).resolve().parent.parent / "models" / "faster-whisper-base"
        model_source = os.environ.get("WHISPER_MODEL_PATH") or (str(local_model) if (local_model / "model.bin").is_file() else MODEL_SIZE)
        _model = WhisperModel(model_source, device=DEVICE, compute_type=COMPUTE_TYPE)
    return _model

def transcribe(audio_path: str) -> dict:
    model = get_model()
    segments, info = model.transcribe(audio_path, beam_size=5, vad_filter=True)
    full_text = " ".join(segment.text.strip() for segment in segments)
    return {"text": full_text.strip(), "language": info.language, "duration": info.duration}
