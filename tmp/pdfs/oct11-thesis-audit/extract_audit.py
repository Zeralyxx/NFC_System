from pathlib import Path
from hashlib import sha256
import json
import re

from pypdf import PdfReader

SOURCE = Path(r"C:\Users\justine mendoza\Documents\NFC Thesis Chapter 1-5 (1).pdf")
OUT = Path(__file__).resolve().parent
reader = PdfReader(SOURCE)
pages = []
for i, page in enumerate(reader.pages, 1):
    text = page.extract_text() or ""
    normalized = re.sub(r"\s+", " ", text).strip()
    pages.append({"pdf_page": i, "text": text, "normalized": normalized})
(OUT / "pages.json").write_text(json.dumps(pages, ensure_ascii=False, indent=2), encoding="utf-8")
(OUT / "full-text.txt").write_text("\n\n".join(f"===== PDF PAGE {p['pdf_page']} =====\n{p['normalized']}" for p in pages), encoding="utf-8")
print(json.dumps({"source": str(SOURCE), "sha256": sha256(SOURCE.read_bytes()).hexdigest(),
                  "pages": len(pages), "bytes": SOURCE.stat().st_size}, indent=2))
for p in pages:
    t = p["normalized"]
    if re.search(r"CHAPTER [1-5]|4\.1 Prototype|4\.2 System Metrics|4\.6 Independent|BIBLIOGRAPHY|^\d+ TAGUIG CITY UNIVERSITY Abstract", t):
        print(p["pdf_page"], t[:320])
