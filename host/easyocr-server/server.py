import io
import os
from typing import Annotated

import easyocr
from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import JSONResponse

app = FastAPI(title="EasyOCR Server")

_readers: dict[str, easyocr.Reader] = {}


def _get_reader(languages: list[str]) -> easyocr.Reader:
    key = ",".join(sorted(languages))
    if key not in _readers:
        use_gpu = os.environ.get("EASYOCR_USE_GPU", "false").lower() == "true"
        _readers[key] = easyocr.Reader(languages, gpu=use_gpu)
    return _readers[key]


def _process_image(image_bytes: bytes, reader: easyocr.Reader, page: int, include_bboxes: bool) -> tuple[list[dict], str]:
    raw_results = reader.readtext(image_bytes)
    blocks = []
    texts = []
    for bbox_points, text, confidence in raw_results:
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
    include_bboxes: Annotated[str, Form()] = "false",
):
    lang_list = [l.strip() for l in languages.split(",") if l.strip()]
    include_bbox = include_bboxes.lower() == "true"
    reader = _get_reader(lang_list)

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
