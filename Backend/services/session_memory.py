"""Bounded, RAM-only visual and conversation memory for one Unity Play session."""
from dataclasses import dataclass, field
from io import BytesIO
import hashlib
import re
import threading
import time
from PIL import Image, ImageOps
from services import ocr

MAX_VIEWS = 12
MAX_SESSIONS = 16
IDLE_SECONDS = 2 * 60 * 60
NOUNS = {"book", "poster", "notice", "document", "sign", "bottle", "cup", "mug",
         "phone", "laptop", "chair", "table", "box", "plant", "pen", "bag", "screen"}
STOP = set("a an the this that it its is are was were i you me my your we our what which when where how why did do does can could would should please show shown showed see saw seen earlier before previous previously again about tell remember read describe explain title text information of on in at to and or from for with have has had look looking".split())

def words(text):
    return {w.rstrip("s") if w.endswith("s") and len(w) > 3 else w
            for w in re.findall(r"[a-z0-9]+", text.lower())} - STOP

@dataclass
class View:
    id: int
    image: bytes
    digest: str
    question: str
    description: str
    text: str = ""

    @property
    def tokens(self):
        return words(self.question + " " + self.description + " " + self.text)

    @property
    def label(self):
        nouns = sorted(self.tokens & NOUNS)
        subject = ", ".join(nouns[:2]) or "scene"
        detail = (self.text or self.description).replace("\n", " ")[:100]
        return f"#{self.id} {subject}: {detail}"

@dataclass
class Session:
    touched: float = field(default_factory=time.monotonic)
    turns: list = field(default_factory=list)
    summary: str = ""
    views: list = field(default_factory=list)
    next_id: int = 1
    focus: int | None = None
    pending: list = field(default_factory=list)
    pending_question: str = ""
    active: bool = True
    lock: threading.RLock = field(default_factory=threading.RLock)

    def remember_turn(self, question, answer):
        self.turns.extend([{"role": "user", "content": question[:1000]},
                           {"role": "assistant", "content": answer[:1000]}])
        while len(self.turns) > 6:
            old = self.turns[:2]
            del self.turns[:2]
            # Extractive notes avoid inventing new facts while keeping older topics.
            note = f"User asked: {old[0]['content'][:160]} Answer: {old[1]['content'][:260]}"
            self.summary = (self.summary + "\n" + note)[-1600:]

    def context(self, selected=None):
        context = []
        notes = self.summary
        if selected:
            notes += f"\nSaved view #{selected.id}. Earlier model description (may be imperfect): {selected.description[:650]}"
            if selected.text:
                notes += "\nOCR text from that earlier view: " + selected.text[:1200]
        if notes.strip():
            context.append({"role": "user", "content": "Earlier session notes, not a current observation:\n" + notes.strip()})
        context.extend(self.turns[-4:])
        return context

    def remember_view(self, image, question, description, text=""):
        with Image.open(BytesIO(image)) as source:
            img = ImageOps.exif_transpose(source).convert("RGB")
            img.thumbnail((1024, 1024))
            output = BytesIO()
            img.save(output, format="JPEG", quality=85)
        encoded = output.getvalue()
        digest = hashlib.sha256(encoded).hexdigest()
        existing = next((v for v in self.views if v.digest == digest), None)
        if existing:
            if text:
                existing.text = text[:6000]
            existing.description = description[:1800]
            self.focus = existing.id
            return existing
        view = View(self.next_id, encoded, digest, question[:600], description[:1800], text[:6000])
        self.next_id += 1
        self.views.append(view)
        self.views = self.views[-MAX_VIEWS:]
        self.focus = view.id
        return view

class Store:
    def __init__(self):
        self.sessions = {}
        self.lock = threading.RLock()

    def expire(self):
        with self.lock:
            now = time.monotonic()
            for key in [k for k, v in self.sessions.items() if now - v.touched > IDLE_SECONDS]:
                self.sessions.pop(key).active = False

    def get(self, key):
        with self.lock:
            now = time.monotonic()
            for old in [k for k, v in self.sessions.items() if now - v.touched > IDLE_SECONDS]:
                self.sessions.pop(old).active = False
            if key not in self.sessions:
                if len(self.sessions) >= MAX_SESSIONS:
                    oldest = min(self.sessions, key=lambda k: self.sessions[k].touched)
                    self.sessions.pop(oldest).active = False
                self.sessions[key] = Session()
            session = self.sessions[key]
            session.touched = now
            return session

    def clear(self, key):
        with self.lock:
            session = self.sessions.pop(key, None)
            if session:
                session.active = False
                # An in-flight request may retain this object until it finishes.
                # Marking it inactive prevents it from publishing new memory.
            return session is not None

store = Store()

def select_view(session, question):
    """Return (selected view, source, clarification). Never silently pick between tied views."""
    q = question.lower().strip()
    explicit = re.search(r"(?:#|view\s+|number\s+)(\d+)", q)
    current = bool(re.search(r"\b(now|currently|around me|in front of me|this|right here|what do you see|what can you see)\b", q))
    historical = bool(re.search(r"\b(earlier|before|previous|previously|again|remember|showed|shown|did i show|i showed|you saw)\b", q))
    pronoun = bool(re.search(r"\b(it|its|that|them|their)\b", q))
    generic_history = bool(re.search(r"\b(what did (i|we)|what have (i|we)|our conversation|we discuss|we talk)\b", q))
    if generic_history and not (words(q) & NOUNS):
        return None, "conversation", ""

    views = session.views
    if explicit:
        selected = next((v for v in views if v.id == int(explicit.group(1))), None)
        return selected, "memory", "" if selected else "That saved view is no longer in this session. Please show it again."

    if current and not historical:
        session.pending = []
        session.pending_question = ""
        return None, "current", ""

    candidates = views
    if session.pending:
        candidates = [v for v in views if v.id in session.pending]
        ordinal = re.search(r"\b(first|second|third|last|latest)\b", q)
        if ordinal:
            index = {"first": 0, "second": 1, "third": 2, "last": -1, "latest": -1}[ordinal.group(1)]
            if -len(candidates) <= index < len(candidates):
                return candidates[index], "memory", ""
        if re.fullmatch(r"\d+", q):
            selected = next((v for v in candidates if v.id == int(q)), None)
            if selected:
                return selected, "memory", ""

    query = words(q)
    nouns = query & NOUNS
    if nouns:
        candidates = [v for v in candidates if nouns & v.tokens]
    elif pronoun and not session.pending:
        selected = next((v for v in views if v.id == session.focus), None)
        if selected:
            return selected, "memory", ""

    scores = [(len(query & v.tokens), v) for v in candidates]
    scores.sort(key=lambda pair: pair[0], reverse=True)
    relevant = [v for score, v in scores if score > 0]
    wants_memory = historical or pronoun or bool(session.pending) or bool(relevant)
    if not wants_memory:
        return None, "current", ""
    if not relevant and not nouns:
        relevant = candidates if historical else []
    if not relevant:
        return None, "memory", "I don't have a saved view matching that in this session. Please show it and ask about it first."
    if len(relevant) == 1:
        return relevant[0], "memory", ""
    if re.search(r"\b(last|latest|most recent)\b", q):
        return max(relevant, key=lambda v: v.id), "memory", ""
    if scores and scores[0][0] > (scores[1][0] if len(scores) > 1 else 0) and scores[0][0] > 1:
        return scores[0][1], "memory", ""
    session.pending = [v.id for v in relevant]
    if not session.pending_question:
        session.pending_question = question
    return None, "clarification", "Which earlier view do you mean? Reply with its number.\n" + "\n".join(v.label for v in relevant)

def ask_with_memory(session_id, question, image, model, llm):
    session = store.get(session_id)
    with session.lock:
        selected, source, clarification = select_view(session, question)
        if clarification:
            return {"answer": clarification, "model": "session-memory", "source": source,
                    "memory_count": len(session.views)}
        if source == "conversation":
            if not session.turns and not session.summary:
                answer = "We haven't discussed anything in this session yet."
            else:
                lines = ["Earlier in this session:"]
                if session.summary:
                    lines.append(session.summary)
                lines += [f"You: {m['content']}" if m["role"] == "user" else f"Assistant: {m['content']}"
                          for m in session.turns[-4:]]
                answer = "\n".join(lines)
            return {"answer": answer, "model": "session-memory", "source": source, "memory_count": len(session.views)}
        effective_question = session.pending_question if selected and session.pending_question else question
        # A recall request can use the saved observation directly. Never ask the
        # vision model to pretend it is seeing an earlier object in the live view.
        recall = selected and re.search(r"\b(remember|did i show|showed|shown)\b", effective_question.lower())
        read_text = selected and re.search(r"\b(read|title|text|author|written|say|says)\b", effective_question.lower())
        if selected and (recall or (read_text and selected.text)):
            content = ("Text extracted from that view:\n" + selected.text if read_text and selected.text
                       else selected.description)
            answer = f"From your earlier view (#{selected.id}): " + content
            session.focus = selected.id
            session.pending = []
            session.pending_question = ""
            session.remember_turn(question, answer)
            return {"answer": answer, "model": "session-memory", "source": "memory",
                    "memory_count": len(session.views), "view_id": selected.id}
        if selected:
            input_image = selected.image
            prompt = ("Use this saved image from earlier, not the current camera view. "
                      "If a detail is unreadable, say so. " + effective_question)
        else:
            input_image = image
            prompt = question
            if not image:
                return {"answer": "The camera is not ready for a new view. You can still ask about an earlier saved object.",
                        "model": "session-memory", "error": True}
        result = llm.ask(question=prompt, image_bytes=input_image, model_override=model,
                         history=session.context(selected))
        if result.get("error"):
            return result
        if not session.active:
            return {"answer": "This conversation was cleared. Please ask again in the new session.",
                    "model": "session-memory", "error": True}
        if selected:
            result["answer"] = f"From your earlier view (#{selected.id}): " + result["answer"]
            session.focus = selected.id
        else:
            extracted = ""
            if words(question + " " + result["answer"]) & {"book", "poster", "document", "sign", "notice", "text", "title"}:
                try:
                    extracted = ocr.extract_text(image).get("text", "")
                except Exception:
                    # Visual memory remains useful even when optional OCR fails.
                    pass
            view = session.remember_view(image, question, result["answer"], extracted)
            result["view_id"] = view.id
        session.pending = []
        session.pending_question = ""
        session.remember_turn(question, result["answer"])
        result.update(source=source, memory_count=len(session.views))
        if selected:
            result["view_id"] = selected.id
        return result

def remember_scan(session, image, result):
    if not result.get("text", "").strip():
        return result
    with session.lock:
        if session.active:
            view = session.remember_view(image, "Scanned document poster or book text", "Scanned text", result["text"])
            session.remember_turn("Scan the text in this view.", result["text"])
            result.update(view_id=view.id, memory_count=len(session.views))
    return result
