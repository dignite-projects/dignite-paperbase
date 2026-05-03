import io
import os
from typing import Annotated, Optional

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

_VL_MODELS = {"PaddleOCR-VL-1.5", "PaddleOCR-VL-2.0"}

_readers: dict[tuple[str, str], PaddleOCR] = {}


def _to_paddle_lang(bcp47: str) -> str:
    return _LANG_MAP.get(bcp47.lower(), bcp47)


def _is_vl(model_name: str) -> bool:
    return model_name in _VL_MODELS


def _get_reader(lang_code: str, model_name: str) -> PaddleOCR:
    key = (lang_code, model_name)
    if key not in _readers:
        use_gpu = os.environ.get("PADDLEOCR_USE_GPU", "false").lower() == "true"
        if _is_vl(model_name):
            _readers[key] = PaddleOCR(use_vl=True, lang=lang_code)
        else:
            _readers[key] = PaddleOCR(use_angle_cls=True, lang=lang_code, use_gpu=use_gpu)
    return _readers[key]


def _process_image(
    image_bytes: bytes,
    reader: PaddleOCR,
    page: int,
    include_bboxes: bool,
    use_vl: bool,
) -> tuple[list[dict], str, Optional[str]]:
    """
    Returns (blocks, plain_text, page_markdown).
    page_markdown is None for non-VL mode.
    """
    if use_vl:
        # VL 模型直接产 Markdown：reader.ocr 返回的 result 里有 'markdown' 字段
        raw_results = reader.ocr(image_bytes)
        page_results = raw_results[0] if raw_results else None
        if not page_results:
            return [], "", ""

        markdown_text = ""
        plain_text = ""
        if isinstance(page_results, dict):
            markdown_text = page_results.get("markdown") or page_results.get("md") or ""
            plain_text = page_results.get("text") or markdown_text
        else:
            # 不同 paddleocr 版本可能返回 (markdown, text) 元组
            try:
                markdown_text, plain_text = page_results
            except Exception:
                markdown_text = str(page_results)
                plain_text = markdown_text

        # VL 模式不产 line-level bbox（产页面级 markdown）
        block: dict = {
            "text": plain_text,
            "confidence": 1.0,
            "page": page,
            "bbox": [0, 0, 0, 0],
        }
        return [block], plain_text, markdown_text

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
    return blocks, "\n".join(texts), None


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
    use_vl = _is_vl(model_name)
    reader = _get_reader(lang_code, model_name)

    file_bytes = await file.read()
    filename = (file.filename or "").lower()
    all_blocks: list[dict] = []
    all_texts: list[str] = []
    all_markdown_pages: list[str] = []

    if filename.endswith(".pdf") or file.content_type == "application/pdf":
        from pdf2image import convert_from_bytes
        images = convert_from_bytes(file_bytes)
        for page_num, image in enumerate(images, start=1):
            buf = io.BytesIO()
            image.save(buf, format="PNG")
            blocks, text, page_md = _process_image(
                buf.getvalue(), reader, page_num, include_bbox, use_vl
            )
            all_blocks.extend(blocks)
            all_texts.append(text)
            if use_vl and page_md is not None:
                all_markdown_pages.append(page_md)
        page_count = len(images)
    else:
        blocks, text, page_md = _process_image(
            file_bytes, reader, 1, include_bbox, use_vl
        )
        all_blocks.extend(blocks)
        all_texts.append(text)
        if use_vl and page_md is not None:
            all_markdown_pages.append(page_md)
        page_count = 1

    confidences = [b["confidence"] for b in all_blocks]
    avg_confidence = sum(confidences) / len(confidences) if confidences else 0.0

    # VL 模型按页拼接 Markdown，多页用 \n\n--- 分隔以保留页边界
    markdown_payload = "\n\n---\n\n".join(p for p in all_markdown_pages if p) if use_vl else None

    return JSONResponse({
        "raw_text": "\n".join(all_texts),
        "markdown": markdown_payload,
        "blocks": all_blocks,
        "confidence": avg_confidence,
        "detected_language": lang_list[0] if lang_list else None,
        "page_count": page_count,
    })


@app.get("/health")
def health():
    return {"status": "ok"}
