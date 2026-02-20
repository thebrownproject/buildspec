"""NCC PDF ingestion: chunk by TOC sections, embed via Gemini, upload to Supabase."""

import argparse
import os
import re
import sys
import time

import fitz  # pymupdf
from dotenv import load_dotenv
from google import genai
from google.genai import types
from supabase import create_client
from tqdm import tqdm

VOLUME_HEADERS = {
    1: re.compile(
        r"^.+\nNCC 2022 Volume One - Building Code of Australia\nPage \d+\n",
        re.MULTILINE,
    ),
    2: re.compile(
        r"^.+\nNCC 2022 Volume Two - Building Code of Australia\nPage \d+\n",
        re.MULTILINE,
    ),
}
VOLUME_CLASSES = {
    1: [2, 3, 4, 5, 6, 7, 8, 9],
    2: [1, 10],
}
FOOTER_RE = re.compile(r"\(1 May 2023\)\s*$", re.MULTILINE)
VERSION_ANNO_RE = re.compile(
    r"^\[(?:2019|New for 2022|New For 2022):.+\]\s*$", re.MULTILINE
)
SECTION_ID_RE = re.compile(r"^[A-Z]\d+[A-Z]+\d+$")
SPEC_ID_RE = re.compile(r"^S\d+C\d+$")

STATE_PREFIXES = {"ACT", "NSW", "NT", "QLD", "SA", "TAS", "VIC", "WA"}

EMBED_MODEL = "gemini-embedding-001"
EMBED_DIMS = 768
EMBED_BATCH = 20
EMBED_DELAY = 0.5
UPLOAD_BATCH = 50
MAX_CHUNK_CHARS = 2000
MIN_CHUNK_CHARS = 50


def load_env():
    env_path = os.path.join(os.path.dirname(__file__), "..", ".env")
    load_dotenv(env_path)
    required = ["SUPABASE_URL", "SUPABASE_SERVICE_ROLE_KEY", "GEMINI_API_KEY"]
    missing = [k for k in required if not os.getenv(k)]
    if missing:
        sys.exit(f"Missing env vars: {', '.join(missing)}")


def clean_page_text(text: str, header_re: re.Pattern) -> str:
    text = header_re.sub("", text)
    text = FOOTER_RE.sub("", text)
    text = VERSION_ANNO_RE.sub("", text)
    return text.strip()


def extract_section_id(title: str) -> str | None:
    for word in title.split():
        if SECTION_ID_RE.match(word) or SPEC_ID_RE.match(word):
            return word
    return None


def is_state_appendix(title: str, level: int) -> bool:
    if level > 2:
        return False
    first_word = title.split()[0] if title.split() else ""
    if first_word.upper() in STATE_PREFIXES:
        return True
    if "Schedule" in title and any(s in title for s in STATE_PREFIXES):
        return True
    return False


def get_part_from_toc(toc: list, idx: int) -> str | None:
    for i in range(idx - 1, -1, -1):
        level, title, _ = toc[i]
        if level <= 2:
            return title
    return None


def extract_section_text(full_text: str, section_id: str, next_section_id: str | None) -> str:
    """Extract text for a specific section from concatenated page text.

    Uses the hair-space-wrapped section ID markers that PyMuPDF extracts
    to find precise boundaries between sections sharing the same page.
    """
    marker = f"\u200a{section_id}\u200a"
    start = full_text.find(marker)
    if start == -1:
        return full_text  # fallback: return everything

    if next_section_id:
        next_marker = f"\u200a{next_section_id}\u200a"
        end = full_text.find(next_marker, start + len(marker))
        if end != -1:
            return full_text[start:end].strip()

    return full_text[start:].strip()


def chunk_pdf(pdf_path: str, volume: int = 2) -> list[dict]:
    header_re = VOLUME_HEADERS[volume]
    applicable_classes = VOLUME_CLASSES[volume]

    doc = fitz.open(pdf_path)
    toc = doc.get_toc()
    total_pages = len(doc)

    # Build cleaned text per page (0-indexed)
    page_cache: dict[int, str] = {}

    def get_page_text(pg_0idx: int) -> str:
        if pg_0idx not in page_cache:
            page_cache[pg_0idx] = clean_page_text(doc[pg_0idx].get_text(), header_re)
        return page_cache[pg_0idx]

    # Filter to L3 entries, skip state appendices
    state_appendix_started = False
    l3_entries = []
    for i, (level, title, page) in enumerate(toc):
        if level <= 2:
            state_appendix_started = is_state_appendix(title, level)
        if state_appendix_started:
            continue
        if level == 3:
            l3_entries.append((i, title, page))

    # Find content boundary: first L1 entry after the last L3 entry
    # (Schedules 1-3 are definitions/references, not clause content)
    last_l3_toc_idx = l3_entries[-1][0] if l3_entries else 0
    content_end_page = total_pages
    for i in range(last_l3_toc_idx + 1, len(toc)):
        level, _, page = toc[i]
        if level <= 1:
            content_end_page = page
            break

    chunks = []
    for entry_idx, (toc_idx, title, start_page) in enumerate(l3_entries):
        section_id = extract_section_id(title)

        # Determine page range
        if entry_idx + 1 < len(l3_entries):
            end_page = l3_entries[entry_idx + 1][2]
        else:
            end_page = content_end_page

        # Concatenate cleaned page text for this range
        page_texts = []
        for pg in range(start_page - 1, min(end_page, total_pages)):
            cleaned = get_page_text(pg)
            if cleaned:
                page_texts.append(cleaned)
        full_text = "\n".join(page_texts)

        if not full_text.strip():
            continue

        # Extract just this section's content using section ID markers
        next_section_id = None
        if entry_idx + 1 < len(l3_entries):
            next_section_id = extract_section_id(l3_entries[entry_idx + 1][1])

        content = extract_section_text(full_text, section_id, next_section_id) if section_id else full_text
        if not content.strip():
            continue

        # Clean up hair spaces and non-breaking spaces
        content = content.replace("\u200a", "").replace("\xa0", " ")
        # Collapse whitespace-only lines into real blank lines
        content = re.sub(r"\n[ \t]+\n", "\n\n", content)

        part = get_part_from_toc(toc, toc_idx)

        sub_chunks = split_long_chunk(content)
        for i, sub in enumerate(sub_chunks):
            chunk_title = title if len(sub_chunks) == 1 else f"{title} (part {i + 1})"
            chunks.append({
                "content": sub,
                "volume": volume,
                "part": part,
                "section": section_id,
                "title": chunk_title,
                "applicable_classes": applicable_classes,
                "state_specific": False,
            })

    doc.close()
    return chunks


def split_long_chunk(text: str) -> list[str]:
    if len(text) <= MAX_CHUNK_CHARS:
        return [text]

    # Split on blank lines (including lines with only whitespace)
    paragraphs = re.split(r"\n\s*\n", text)
    result = []
    current = ""

    for para in paragraphs:
        if current and len(current) + len(para) + 2 > MAX_CHUNK_CHARS:
            result.append(current.strip())
            current = para
        else:
            current = f"{current}\n\n{para}" if current else para

    if current.strip():
        result.append(current.strip())

    # Merge short chunks (headers, stubs) into their neighbor
    merged = []
    for chunk in result:
        if merged and len(merged[-1]) < MIN_CHUNK_CHARS:
            merged[-1] = f"{merged[-1]}\n\n{chunk}"
        elif merged and len(chunk) < MIN_CHUNK_CHARS:
            merged[-1] = f"{merged[-1]}\n\n{chunk}"
        else:
            merged.append(chunk)

    # Force-split any remaining oversized chunks at sentence boundaries
    final = []
    for chunk in merged:
        if len(chunk) <= MAX_CHUNK_CHARS:
            final.append(chunk)
        else:
            final.extend(split_at_sentences(chunk))
    return final


def split_at_sentences(text: str) -> list[str]:
    # Split on period or semicolon followed by whitespace, or on newlines
    sentences = re.split(r"(?<=[.;])\s+|\n", text)
    result = []
    current = ""
    for sent in sentences:
        if not sent.strip():
            continue
        if current and len(current) + len(sent) + 1 > MAX_CHUNK_CHARS:
            result.append(current.strip())
            current = sent
        else:
            current = f"{current} {sent}" if current else sent
    if current.strip():
        result.append(current.strip())
    return result if result else [text]


def generate_embeddings(texts: list[str], client) -> list[list[float]]:
    all_embeddings = []
    for i in tqdm(range(0, len(texts), EMBED_BATCH), desc="Embedding"):
        batch = texts[i : i + EMBED_BATCH]
        for attempt in range(5):
            try:
                response = client.models.embed_content(
                    model=EMBED_MODEL,
                    contents=batch,
                    config=types.EmbedContentConfig(output_dimensionality=EMBED_DIMS),
                )
                all_embeddings.extend([e.values for e in response.embeddings])
                break
            except Exception as e:
                err = str(e)
                retryable = (
                    "429" in err
                    or "RESOURCE_EXHAUSTED" in err
                    or "ConnectError" in type(e).__name__
                    or "Connection reset" in err
                    or "ConnectionError" in type(e).__name__
                )
                if retryable and attempt < 4:
                    wait = (attempt + 1) * 15
                    tqdm.write(f"  {type(e).__name__}, waiting {wait}s...")
                    time.sleep(wait)
                else:
                    raise
        if i + EMBED_BATCH < len(texts):
            time.sleep(EMBED_DELAY)
    return all_embeddings


def upload_to_supabase(chunks: list[dict], supabase_client):
    for i in tqdm(range(0, len(chunks), UPLOAD_BATCH), desc="Uploading"):
        batch = chunks[i : i + UPLOAD_BATCH]
        supabase_client.table("ncc_chunks").insert(batch).execute()


def print_dry_run(chunks: list[dict]):
    print(f"\n{'=' * 60}")
    print(f"DRY RUN: {len(chunks)} chunks extracted")
    print(f"{'=' * 60}\n")

    for i, chunk in enumerate(chunks):
        content_preview = chunk["content"][:120].replace("\n", " ")
        print(
            f"[{i + 1:3d}] section={chunk['section'] or 'N/A':10s} "
            f"title={chunk['title'][:50]:50s} "
            f"chars={len(chunk['content']):5d}"
        )
        print(f"      part={chunk['part'] or 'N/A'}")
        print(f"      preview: {content_preview}...")
        print()

    total_chars = sum(len(c["content"]) for c in chunks)
    sections = set(c["section"] for c in chunks if c["section"])
    long_chunks = [c for c in chunks if len(c["content"]) > MAX_CHUNK_CHARS]
    print(f"{'=' * 60}")
    print(f"Total chunks:    {len(chunks)}")
    print(f"Unique sections: {len(sections)}")
    print(f"Total chars:     {total_chars:,}")
    print(f"Avg chunk size:  {total_chars // max(len(chunks), 1):,} chars")
    print(f"Chunks > {MAX_CHUNK_CHARS}:   {len(long_chunks)}")
    if long_chunks:
        for c in long_chunks:
            print(f"  WARNING: {c['section']} '{c['title']}' = {len(c['content'])} chars")


def main():
    parser = argparse.ArgumentParser(description="Ingest NCC PDFs into Supabase")
    parser.add_argument("pdf_path", help="Path to NCC PDF file")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print chunks without calling APIs",
    )
    parser.add_argument(
        "--volume",
        type=int,
        choices=[1, 2],
        default=2,
        help="NCC volume number (default: 2)",
    )
    parser.add_argument(
        "--skip",
        type=int,
        default=0,
        help="Skip first N chunks (resume after partial ingestion)",
    )
    args = parser.parse_args()

    if not os.path.exists(args.pdf_path):
        sys.exit(f"PDF not found: {args.pdf_path}")

    print(f"Parsing PDF: {args.pdf_path} (Volume {args.volume})")
    chunks = chunk_pdf(args.pdf_path, volume=args.volume)
    print(f"Extracted {len(chunks)} chunks")

    if args.dry_run:
        print_dry_run(chunks)
        return

    if args.skip > 0:
        print(f"Skipping first {args.skip} chunks (resuming from index {args.skip})")
        chunks = chunks[args.skip:]
        print(f"Remaining chunks to process: {len(chunks)}")

    load_env()

    print("Generating embeddings via Gemini...")
    gemini = genai.Client(api_key=os.getenv("GEMINI_API_KEY"))
    texts = [c["content"] for c in chunks]
    embeddings = generate_embeddings(texts, gemini)
    print(f"Generated {len(embeddings)} embeddings ({EMBED_DIMS} dims each)")

    for chunk, emb in zip(chunks, embeddings):
        chunk["embedding"] = emb

    print("Uploading to Supabase...")
    sb = create_client(
        os.getenv("SUPABASE_URL"),
        os.getenv("SUPABASE_SERVICE_ROLE_KEY"),
    )
    upload_to_supabase(chunks, sb)
    print(f"Uploaded {len(chunks)} chunks to ncc_chunks")


if __name__ == "__main__":
    main()
