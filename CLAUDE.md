# BuildScope

AI-powered compliance assistant for Autodesk Revit. Side panel that lets users query the National Construction Code (NCC) without leaving the model.

## Concept

BuildScope is a Revit add-in that provides an AI assistant for NCC compliance checking. Users define their project context (building class, state, etc.) and can ask natural language questions about code compliance — getting answers with specific NCC section references, inline in Revit.

## Architecture

- **Platform:** Autodesk Revit add-in (C# / .NET)
- **Base:** Extends Archie Copilot side panel pattern (github.com/thebrownproject/archie-copilot)
- **Vector DB:** Pinecone — NCC PDFs chunked and embedded
- **LLM:** Google Gemini (free tier) — RAG query agent
- **Project Context:** Users define project metadata (building class, state, climate zone, etc.) stored per-project and injected into every query

## How It Works

1. User creates a project: e.g. Class 3 building, Victoria
2. Project context is stored and injected into all queries
3. User asks: "what are the egress requirements for this building type?"
4. BuildScope retrieves relevant NCC sections from Pinecone using project context
5. Returns answer with NCC section references, displayed in Revit side panel

## Build Approach

Ship fast. POC first, iterate later.

- Pinecone free tier handles NCC volume
- Gemini free tier covers LLM queries
- Extend Archie Copilot side panel with compliance tab
- Demo video when working → send to contacts

## Key Differentiator

- Stays in Revit — zero context switching for architects/BIM managers
- NCC is public, downloadable — no licensing issues
- Project context makes answers specific, not generic
- Rare combo: Revit API + RAG + AI — most BIM managers can't build this

## References

`references/` contains cloned repos for porting code from. These are local-only (gitignored) and not part of BuildScope's repo.

- `references/archie-copilot/` — Archie Copilot source. The base side panel pattern to extend for BuildScope.

## Related

- Archie Copilot: github.com/thebrownproject/archie-copilot
- NCC: publicly available PDF documents
