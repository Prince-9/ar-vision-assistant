import logging, tempfile, os
import json
import asyncio
from contextlib import suppress
from pydantic import BaseModel, Field, ValidationError
from typing import Literal
from uuid import UUID
from io import BytesIO
from PIL import Image, UnidentifiedImageError
from starlette.concurrency import run_in_threadpool
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import Response
from services import stt, llm, ocr, detection, tts
from services import session_memory

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("gateway")
app = FastAPI(title="Local AI Gateway")

async def expire_sessions():
    while True:
        await asyncio.sleep(60)
        session_memory.store.expire()

@app.on_event("startup")
async def start_session_cleanup():
    app.state.memory_cleanup = asyncio.create_task(expire_sessions())

@app.on_event("shutdown")
async def stop_session_cleanup():
    app.state.memory_cleanup.cancel()
    with suppress(asyncio.CancelledError):
        await app.state.memory_cleanup

class ChatMessage(BaseModel):
    role: Literal["user", "assistant"]
    content: str = Field(min_length=1, max_length=1500)

class ChatHistory(BaseModel):
    messages: list[ChatMessage] = Field(default_factory=list, max_length=6)

def parse_history(raw: str) -> list[dict]:
    if len(raw) > 16000:
        raise HTTPException(status_code=422, detail="Chat history is too large.")
    try:
        history = ChatHistory.model_validate(json.loads(raw))
        if sum(len(m.content) for m in history.messages) > 5000:
            raise ValueError("Chat history exceeds the context budget.")
        return [m.model_dump() for m in history.messages]
    except (ValueError, ValidationError) as exc:
        raise HTTPException(status_code=422, detail="Invalid chat history. Use up to six user/assistant messages within 5000 characters.") from exc

@app.get("/health")
def health():
    return {"status": "ok", "session_memory": True}

def valid_session(value: str | None):
    if value is None:
        return None
    try:
        return str(UUID(value))
    except ValueError as exc:
        raise HTTPException(status_code=422, detail="Invalid session ID.") from exc

def validate_memory_image(data):
    if not data:
        return
    if len(data) > 12 * 1024 * 1024:
        raise HTTPException(status_code=413, detail="Camera image is too large.")
    try:
        with Image.open(BytesIO(data)) as image:
            if image.width * image.height > 16000000:
                raise ValueError("Image dimensions too large")
            image.verify()
    except (UnidentifiedImageError, OSError, ValueError, Image.DecompressionBombError) as exc:
        raise HTTPException(status_code=422, detail="A valid camera image is required.") from exc

@app.delete("/session/{session_id}")
def clear_session(session_id: str):
    session_memory.store.clear(valid_session(session_id))
    return {"status": "cleared"}

@app.post("/listen")
async def listen(audio: UploadFile = File(...)):
    suffix = os.path.splitext(audio.filename or "audio.wav")[1] or ".wav"
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(await audio.read())
        tmp_path = tmp.name
    try:
        return await run_in_threadpool(stt.transcribe, tmp_path)
    except Exception as e:
        logger.exception("STT failed")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        os.remove(tmp_path)

@app.post("/ask")
async def ask(question: str = Form(...), model: str | None = Form(None), image: UploadFile | None = File(None), history: str = Form('{"messages":[]}'), session_id: str | None = Form(None)):
    session_id = valid_session(session_id)
    if not question.strip() or len(question) > 1500:
        raise HTTPException(status_code=422, detail="Question must contain 1 to 1500 characters.")
    messages = parse_history(history)
    image_bytes = await image.read() if image is not None else None
    if session_id:
        validate_memory_image(image_bytes)
        result = await run_in_threadpool(session_memory.ask_with_memory, session_id, question, image_bytes, model, llm)
    else:
        result = await run_in_threadpool(llm.ask, question=question, image_bytes=image_bytes, model_override=model, history=messages)
    if result.get("error"):
        raise HTTPException(status_code=502, detail=result["answer"])
    return result

@app.post("/ocr")
async def run_ocr(image: UploadFile = File(...), session_id: str | None = Form(None)):
    session_id = valid_session(session_id)
    session = session_memory.store.get(session_id) if session_id else None
    image_bytes = await image.read()
    if session_id:
        validate_memory_image(image_bytes)
    try:
        result = await run_in_threadpool(ocr.extract_text, image_bytes)
        if session_id:
            result = await run_in_threadpool(session_memory.remember_scan, session, image_bytes, result)
        return result
    except Exception as e:
        logger.exception("OCR failed")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/detect")
async def run_detect(image: UploadFile = File(...), confidence: float = Form(0.4)):
    image_bytes = await image.read()
    try:
        return detection.detect_objects(image_bytes, confidence_threshold=confidence)
    except Exception as e:
        logger.exception("Detection failed")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/speak")
async def speak(text: str = Form(...)):
    try:
        audio_bytes = tts.synthesize(text)
        return Response(content=audio_bytes, media_type="audio/wav")
    except Exception as e:
        logger.exception("TTS failed")
        raise HTTPException(status_code=500, detail=str(e))
