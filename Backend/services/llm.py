"""
LLM service - talks to a locally-running Ollama server.
"""
import base64
import logging
import os
import requests

logger = logging.getLogger("gateway.llm")

DEFAULT_OLLAMA_BASE = "http://127.0.0.1:11434"


def get_ollama_urls() -> list[str]:
    configured = os.environ.get("OLLAMA_URL")
    if configured:
        return [configured.rstrip("/")]

    explicit_host = os.environ.get("OLLAMA_HOST")
    if explicit_host:
        host = explicit_host.rstrip("/")
        if host.startswith("http://") or host.startswith("https://"):
            return [host.rstrip("/") + "/api/chat"]
        return [f"http://{host.rstrip('/')}" + "/api/chat"]

    # Prefer 127.0.0.1 over localhost because some Windows setups resolve
    # localhost to IPv6 first, while Ollama may only be listening on IPv4.
    return [
        f"{DEFAULT_OLLAMA_BASE}/api/chat",
        "http://localhost:11434/api/chat",
    ]


OLLAMA_URL = get_ollama_urls()[0]

TEXT_MODEL = "qwen3.5:9b"
VISION_MODEL = "moondream"  # confirmed correct for 6GB VRAM (RTX 4050)

SYSTEM_PROMPT = (
    "You are a helpful voice assistant embedded in an AR/MR headset. "
    "Keep answers concise and conversational since they may be read aloud. "
    "When an image is provided, use it as the primary evidence for the answer. "
    "Help with general image understanding: describe scenes, objects, actions, "
    "visual relationships, diagrams, colors, and visible text as relevant to the "
    "question. Do not invent details; say when something is unclear, too small, "
    "or outside the image. Use conversation history only as context, not as "
    "evidence about what is currently visible."
)

VISION_PROMPT = (
    "Answer as a general-purpose visual assistant. Carefully inspect the attached "
    "image and use visible evidence to answer the user's question. You can explain "
    "the overall scene, identify and describe objects, summarize visible activity, "
    "explain spatial relationships, interpret simple diagrams, compare visible "
    "items, and read text that is clearly legible. Give the useful visual details "
    "the question calls for. Be concise and natural for a spoken AR conversation. "
    "Do not guess hidden, tiny, blurry, or unsupported details; clearly say when "
    "you cannot tell. Treat earlier conversation as context only, never as proof "
    "of what is in the current image."
)


def ask(question: str, image_bytes: bytes | None = None, model_override: str | None = None, history: list[dict] | None = None) -> dict:
    use_vision = image_bytes is not None
    model = model_override or (VISION_MODEL if use_vision else TEXT_MODEL)

    messages = [{"role": "system", "content": SYSTEM_PROMPT}]
    messages.extend(history or [])
    user_message = {"role": "user", "content": question}
    if use_vision:
        user_message["images"] = [base64.b64encode(image_bytes).decode("utf-8")]
    messages.append(user_message)

    # The installed Moondream model uses a single Question/Answer template,
    # rather than a system + multi-turn chat template. Put the visual guidance
    # and recent text context in its one user question, keeping the image attached.
    if model.split(":", 1)[0] == "moondream":
        context = "\n".join(f"{m['role'].capitalize()}: {m['content']}" for m in (history or []))
        parts = [VISION_PROMPT]
        if context:
            parts.append(f"Previous conversation (context only):\n{context}")
        parts.append(f"Question: {question}")
        user_message["content"] = "\n\n".join(parts)
        messages = [user_message]

    payload = {"model": model, "messages": messages, "stream": False}
    last_error = None

    for endpoint in get_ollama_urls():
        try:
            response = requests.post(endpoint, json=payload, timeout=90)
            response.raise_for_status()
            data = response.json()
            answer = data.get("message", {}).get("content", "").strip()
            if not answer and use_vision and model.split(":", 1)[0] == "moondream":
                # Some Moondream prompts immediately emit EOS. Offer a clearly
                # labeled description rather than silently substituting an answer.
                fallback = {"model": model, "stream": False, "messages": [{
                    "role": "user", "content": "Describe this image.",
                    "images": user_message["images"]}]}
                retry = requests.post(endpoint, json=fallback, timeout=90)
                retry.raise_for_status()
                description = retry.json().get("message", {}).get("content", "").strip()
                if description:
                    answer = "I couldn't answer that exact question. Here is what I can describe: " + description
            if not answer:
                return {"answer": "The model returned no answer. Try a shorter question or a clearer image.", "model": model, "error": True}
            return {"answer": answer, "model": model}
        except requests.exceptions.ConnectionError as exc:
            last_error = exc
            logger.warning("Ollama connection failed at %s; trying fallback", endpoint)
            continue
        except Exception as e:
            logger.exception("LLM request failed")
            return {"answer": f"Something went wrong asking the model: {e}", "model": model, "error": True}

    logger.error("Could not reach Ollama at %s - is it running? (`ollama serve`)", get_ollama_urls())
    if last_error is not None:
        logger.debug("Last Ollama connection failure: %s", last_error)
    return {"answer": "I can't reach the local language model right now. Check that Ollama is running and reachable via the configured URL.", "model": model, "error": True}
