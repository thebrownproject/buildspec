# BuildSpec

AI-powered compliance assistant for Autodesk Revit. Side panel that lets users query the National Construction Code (NCC) without leaving the model.

## Concept

BuildSpec is a Revit add-in that provides an AI assistant for NCC compliance checking. Users define their project context (building class, state, etc.) and can ask natural language questions about code compliance, getting answers with specific NCC section references, inline in Revit.

## Architecture

- **Platform:** Autodesk Revit add-in (C# / .NET 8)
- **Base:** Ported from Archie Copilot side panel pattern (github.com/thebrownproject/archie-copilot)
- **Vector DB:** Supabase PostgreSQL + pgvector
- **LLM:** Google Gemini 2.0 Flash (free tier)
- **Embeddings:** gemini-embedding-001 (768 dimensions)
- **Backend:** Supabase Edge Function (TypeScript/Deno)
- **Project Context:** Users define project metadata (building class, state, construction type) stored per-project and injected into every query

## How It Works

1. User creates a project: e.g. Class 3 building, Victoria
2. Project context is stored and injected into all queries
3. User asks: "what are the egress requirements for this building type?"
4. BuildSpec embeds the query, searches pgvector with class/volume filtering
5. Returns answer with NCC section references, displayed in Revit side panel

## Build Approach

Ship fast. POC first, iterate later.

- Supabase free tier handles database + Edge Functions
- Gemini free tier covers embeddings + LLM queries
- Ported Archie Copilot side panel pattern for the Revit add-in
- Demo video when working on Windows -> send to contacts

## Key Differentiator

- Stays in Revit -- zero context switching for architects/BIM managers
- NCC is public, downloadable -- no licensing issues
- Project context makes answers specific, not generic
- Rare combo: Revit API + RAG + AI -- most BIM managers can't build this

## References

`references/` contains cloned repos for porting code from. These are local-only (gitignored) and not part of BuildSpec's repo.

- `references/archie-copilot/` -- Archie Copilot source. The base side panel pattern ported for BuildSpec.

## Related

- Archie Copilot: github.com/thebrownproject/archie-copilot
- NCC: publicly available PDF documents
