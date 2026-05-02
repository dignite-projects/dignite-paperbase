import io
import os
from typing import Annotated

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import JSONResponse
from paddleocr import PaddleOCR

app = FastAPI(title="PaddleOCR Server")

_LANG_MAP = {
    "ja": "japan",
    "zh": "ch",
    "zh-hans": "ch",
    "zh-hant": "chinese_cht",
    "ko": "korean",
}

_readers: dict[tuple[str, str], PaddleOCR] = {}


def _to_paddle_lang(bcp47: str) -> str:
    return _LANG_MAP.get(bcp47.lower(), bcp47)


def _get_reader(lang_code: str, model_name: str) -> PaddleOCR:
    key = (lang_code, model_name)
    if key not in _readers:
        use_gpu = os.environ.get("PADDLEOCR_USE_GPU", "false").lower() == "true"
        if model_name == "PaddleOCR-VL-1.5":
            _readers[key] = PaddleOCR(use_vl=True, lang=lang_code)
        else:
            _readers[key] = PaddleOCR(use_angle_cls=True, lang=lang_code, use_gpu=use_gpu)
    return _readers[key]


def _process_image(image_bytes: bytes, reader: PaddleOCR, page: int, include_bboxes: bool) -> tuple[list[dict], str]:
    raw_results = reader.ocr(image_bytes, cls=True)
    blocks = []
    texts = []
    page_results = raw_results[0] if raw_results else []
    for item in (page_results or []):
        bbox_points, (text, confidence) = item
        texts.append(text)
        block: dict = {"text": text, "confidence": confidence, "page": page}
        if include_bboxes:
            xs = [p[0] for p in bbox_points]
            ys = [p[1] for p in bbox_points]
            x, y = min(xs), min(ys)
            block["bbox"] = [x, y, max(xs) - x, max(ys) - y]
        else:
            block["bbox"] = [0, 0, 0, 0]
        blocks.append(block)
    return blocks, "\n".join(texts)


@app.post("/ocr")
async def ocr(
    file: Annotated[UploadFile, File()],
    languages: Annotated[str, Form()] = "ja,en",
    model_name: Annotated[str, Form()] = "PP-OCRv4",
    include_bboxes: Annotated[str, Form()] = "false",
):
    lang_list = [l.strip() for l in languages.split(",") if l.strip()]
    lang_code = _to_paddle_lang(lang_list[0]) if lang_list else "japan"
    include_bbox = include_bboxes.lower() == "true"
    reader = _get_reader(lang_code, model_name)

    file_bytes = await file.read()
    filename = (file.filename or "").lower()
    all_blocks: list[dict] = []
    all_texts: list[str] = []

    if filename.endswith(".pdf") or file.content_type == "application/pdf":
        from pdf2image import convert_from_bytes
        images = convert_from_bytes(file_bytes)
        for page_num, image in enumerate(images, start=1):
            buf = io.BytesIO()
            image.save(buf, format="PNG")
            blocks, text = _process_image(buf.getvalue(), reader, page_num, include_bbox)
            all_blocks.extend(blocks)
            all_texts.append(text)
        page_count = len(images)
    else:
        blocks, text = _process_image(file_bytes, reader, 1, include_bbox)
        all_blocks.extend(blocks)
        all_texts.append(text)
        page_count = 1

    confidences = [b["confidence"] for b in all_blocks]
    avg_confidence = sum(confidences) / len(confidences) if confidences else 0.0

    return JSONResponse({
        "raw_text": "\n".join(all_texts),
        "blocks": all_blocks,
        "confidence": avg_confidence,
        "detected_language": lang_list[0] if lang_list else None,
        "page_count": page_count,
    })


@app.get("/health")
def health():
    return {"status": "ok"}
