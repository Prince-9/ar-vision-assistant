import logging
from io import BytesIO
import numpy as np
from PIL import Image
from ultralytics import YOLO

logger = logging.getLogger("gateway.detection")
MODEL_NAME = "yolov8n.pt"
_model = None

def get_model():
    global _model
    if _model is None:
        _model = YOLO(MODEL_NAME)
    return _model

def detect_objects(image_bytes: bytes, confidence_threshold: float = 0.4) -> dict:
    model = get_model()
    image = Image.open(BytesIO(image_bytes)).convert("RGB")
    image_np = np.array(image)
    results = model(image_np, conf=confidence_threshold, verbose=False)[0]
    objects = []
    for box in results.boxes:
        cls_id = int(box.cls[0])
        label = model.names[cls_id]
        confidence = float(box.conf[0])
        x1, y1, x2, y2 = box.xyxy[0].tolist()
        objects.append({
            "label": label, "confidence": round(confidence, 3),
            "box": {"x": round(x1, 1), "y": round(y1, 1),
                    "width": round(x2 - x1, 1), "height": round(y2 - y1, 1)},
        })
    return {"objects": objects}
