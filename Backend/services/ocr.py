import logging
import os
import shutil
from io import BytesIO
from PIL import Image
import pytesseract

logger = logging.getLogger("gateway.ocr")
# On Windows, if tesseract isn't on PATH, uncomment and set:
# pytesseract.pytesseract.tesseract_cmd = r"C:\Program Files\Tesseract-OCR\tesseract.exe"

def extract_text(image_bytes: bytes) -> dict:
    executable = os.environ.get("TESSERACT_CMD") or shutil.which("tesseract")
    if not executable:
        candidate = os.path.join(os.environ.get("ProgramFiles", "C:/Program Files"), "Tesseract-OCR", "tesseract.exe")
        executable = candidate if os.path.isfile(candidate) else None
    if not executable:
        raise RuntimeError("Text scanning needs Tesseract OCR installed on the gateway PC. Install it or set TESSERACT_CMD to tesseract.exe.")
    pytesseract.pytesseract.tesseract_cmd = executable
    image = Image.open(BytesIO(image_bytes))
    raw_text = pytesseract.image_to_string(image, timeout=20).strip()
    data = pytesseract.image_to_data(image, output_type=pytesseract.Output.DICT, timeout=20)
    words = []
    for i in range(len(data["text"])):
        text = data["text"][i].strip()
        conf = float(data["conf"][i])
        if text and conf > 0:
            words.append({
                "text": text, "confidence": conf,
                "box": {"x": data["left"][i], "y": data["top"][i],
                        "width": data["width"][i], "height": data["height"][i]},
            })
    return {"text": raw_text, "words": words}
