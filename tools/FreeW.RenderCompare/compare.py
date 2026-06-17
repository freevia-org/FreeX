#!/usr/bin/env python
"""Visual fidelity comparison: FreeW page PNGs vs MS Word PDF renderings.

Rasterizes each Word ground-truth PDF (runs/word/<doc>.pdf) to per-page PNGs at the same DPI the
FreeW harness used (default 150), then for every page present on both sides computes SSIM + mean
absolute pixel delta and writes a side-by-side+heatmap triptych. Aggregates per-doc and overall.

Usage: python compare.py [runsDir] [dpi]
"""
import csv
import os
import sys
import glob

import numpy as np
from PIL import Image
import pypdfium2 as pdfium
from skimage.metrics import structural_similarity as ssim

RUNS = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
DPI = float(sys.argv[2]) if len(sys.argv) > 2 else 150.0

FREEW = os.path.join(RUNS, "freew")
WORD = os.path.join(RUNS, "word")
WORDPNG = os.path.join(WORD, "png")
DIFF = os.path.join(RUNS, "diff")
os.makedirs(WORDPNG, exist_ok=True)
os.makedirs(DIFF, exist_ok=True)


def rasterize_word(pdf_path, base):
    """Render every page of a Word PDF to grayscale PIL images at DPI; cache PNGs under word/png."""
    pages = []
    pdf = pdfium.PdfDocument(pdf_path)
    try:
        scale = DPI / 72.0
        for i in range(len(pdf)):
            page = pdf[i]
            bmp = page.render(scale=scale)
            img = bmp.to_pil().convert("RGB")
            img.save(os.path.join(WORDPNG, f"{base}-p{i+1}.png"))
            pages.append(img)
    finally:
        pdf.close()
    return pages


def to_gray_arr(img, size):
    if img.size != size:
        img = img.resize(size, Image.LANCZOS)
    return np.asarray(img.convert("L"))


def heatmap(a, b):
    """Red-intensity heatmap of |a-b| over a faint gray base."""
    d = np.abs(a.astype(np.int16) - b.astype(np.int16)).astype(np.uint8)
    base = (220 + (255 - 220) * (a.astype(np.float32) / 255.0)).astype(np.uint8)
    rgb = np.stack([base, base - np.minimum(base, d), base - np.minimum(base, d)], axis=-1)
    return Image.fromarray(rgb, "RGB")


def triptych(freew_img, word_img, hm, size):
    pad = 8
    w, h = size
    canvas = Image.new("RGB", (w * 3 + pad * 4, h + pad * 2), (245, 245, 245))
    for idx, im in enumerate([freew_img.resize(size), word_img.resize(size), hm]):
        canvas.paste(im.convert("RGB"), (pad + idx * (w + pad), pad))
    return canvas


def main():
    rows = []
    docs = sorted(glob.glob(os.path.join(WORD, "*.pdf")))
    for pdf_path in docs:
        base = os.path.splitext(os.path.basename(pdf_path))[0]
        freew_pages = sorted(glob.glob(os.path.join(FREEW, f"{base}-p*.png")),
                             key=lambda p: int(p.rsplit("-p", 1)[1].split(".")[0]))
        if not freew_pages:
            rows.append({"doc": base, "page": "-", "ssim": "", "pixdiff_pct": "",
                         "freew_pages": 0, "word_pages": "?", "note": "no FreeW render"})
            continue
        try:
            word_pages = rasterize_word(pdf_path, base)
        except Exception as e:
            rows.append({"doc": base, "page": "-", "ssim": "", "pixdiff_pct": "",
                         "freew_pages": len(freew_pages), "word_pages": "err",
                         "note": f"word raster fail: {type(e).__name__}"})
            continue

        n = min(len(freew_pages), len(word_pages))
        for i in range(n):
            fw = Image.open(freew_pages[i])
            wd = word_pages[i]
            size = fw.size  # compare at FreeW page pixel size
            fa = to_gray_arr(fw, size)
            wa = to_gray_arr(wd, size)
            score = ssim(fa, wa)
            pix = float(np.abs(fa.astype(np.int16) - wa.astype(np.int16)).mean()) / 255.0 * 100.0
            hm = heatmap(fa, wa)
            triptych(fw, wd, hm, size).save(os.path.join(DIFF, f"{base}-p{i+1}.png"))
            rows.append({"doc": base, "page": i + 1, "ssim": round(score, 4),
                         "pixdiff_pct": round(pix, 2), "freew_pages": len(freew_pages),
                         "word_pages": len(word_pages),
                         "note": "" if len(freew_pages) == len(word_pages) else "page-count differs"})

    with open(os.path.join(DIFF, "scores.csv"), "w", newline="") as f:
        wr = csv.DictWriter(f, fieldnames=["doc", "page", "ssim", "pixdiff_pct",
                                           "freew_pages", "word_pages", "note"])
        wr.writeheader()
        wr.writerows(rows)

    # Per-doc + overall summary
    perdoc = {}
    for r in rows:
        if r["ssim"] == "":
            continue
        perdoc.setdefault(r["doc"], []).append(r["ssim"])
    print(f"{'doc':28} pages  mean_SSIM")
    all_ssim = []
    for doc in sorted(perdoc):
        s = perdoc[doc]
        all_ssim += s
        print(f"{doc:28} {len(s):5}  {sum(s)/len(s):.3f}")
    if all_ssim:
        print(f"\nOVERALL  pages={len(all_ssim)}  mean_SSIM={sum(all_ssim)/len(all_ssim):.3f}")
    print(f"diff images + scores.csv -> {DIFF}")


if __name__ == "__main__":
    main()
