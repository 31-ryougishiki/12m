"""Read burned-in captions from extracted frames with Windows OCR.

Input frames must already be cropped to the narrator's caption strip; do not
mix game dialogue, contact lists and narrator captions. Output is unverified
OCR, with approximate timestamps, for local research only.
"""
import argparse
import asyncio
import difflib
import json
from pathlib import Path

import numpy as np
from PIL import Image
from winrt.windows.globalization import Language
from winrt.windows.graphics.imaging import BitmapDecoder
from winrt.windows.media.ocr import OcrEngine
from winrt.windows.storage import FileAccessMode, StorageFile


async def run(args):
    engine = OcrEngine.try_create_from_language(Language("zh-Hans-CN"))
    if engine is None:
        raise RuntimeError("Windows Simplified Chinese OCR is unavailable")
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    temp = output.with_suffix(".ocr-temp.png")
    previous, last_mask, count = "", None, 0
    try:
        with output.open("w", encoding="utf-8") as target:
            for index, frame in enumerate(sorted(Path(args.frames).glob("*.jpg"))):
                with Image.open(frame) as source:
                    mask = np.asarray(source.convert("L")) > 190
                if last_mask is not None and np.mean(mask != last_mask) < 0.006:
                    continue
                last_mask = mask
                image = Image.fromarray((~mask).astype(np.uint8) * 255)
                image.resize((image.width * 2, image.height * 2)).save(temp)
                file = await StorageFile.get_file_from_path_async(str(temp))
                stream = await file.open_async(FileAccessMode.READ)
                try:
                    decoder = await BitmapDecoder.create_async(stream)
                    bitmap = await decoder.get_software_bitmap_async()
                    try:
                        result = await engine.recognize_async(bitmap)
                        text = " ".join(line.text.replace(" ", "") for line in result.lines).strip()
                    finally:
                        bitmap.close()
                finally:
                    stream.close()
                # BitmapDecoder retains a COM reference to the file stream.
                # Release it before overwriting the scratch image on Windows.
                del decoder, bitmap, stream, file
                if not text or difflib.SequenceMatcher(None, previous, text).ratio() > 0.94:
                    continue
                previous = text
                target.write(json.dumps({"seconds": index / args.fps, "text": text}, ensure_ascii=False) + "\n")
                count += 1
                if count % 50 == 0:
                    target.flush()
                    print(f"{output.stem}: {count} caption changes; frame {index}", flush=True)
        print(f"{output}: {count} unverified caption changes", flush=True)
    finally:
        temp.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames")
    parser.add_argument("output")
    parser.add_argument("--fps", type=float, default=2)
    options = parser.parse_args()
    if options.fps <= 0:
        parser.error("--fps must be positive")
    asyncio.run(run(options))
