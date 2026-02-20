# BuildSpec

**NCC compliance, without leaving Revit.**

A dockable side panel for Autodesk Revit 2025 that answers National Construction Code questions using RAG. Define your project context, ask a question in plain English, and get answers with specific NCC section references, all inline in Revit.

## How it works

You create a project with your building's classification (e.g. Class 2, Victoria, Type A). When you ask *"What FRL do I need for the walls between units?"*, the system embeds your question, searches the NCC using pgvector similarity filtered by your building class, sends the top matches to Gemini with your project context, and returns a concise answer with inline section references.

The whole conversation is preserved per-project, so follow-up questions have full context. Switch between projects and each keeps its own chat history.

## Demo

*Coming soon. Requires Windows + Revit 2025 for runtime testing.*

## Tech Stack

**Platform:** WPF . .NET 8 . Revit 2025 API <br>
**Backend:** Supabase Edge Functions (TypeScript/Deno) <br>
**Database:** Supabase PostgreSQL . pgvector <br>
**AI:** Gemini 2.0 Flash (LLM) . gemini-embedding-001 (embeddings) <br>
**Ingestion:** Python . PyMuPDF . google-genai

## Architecture

The Revit add-in registers as an `IExternalApplication` on startup, creating a dockable WPF panel and a ribbon tab.

When the user sends a question, the C# client POSTs to a Supabase Edge Function with the question and project context (building class, state, construction type). The Edge Function embeds the query via Gemini, runs a pgvector similarity search filtered by building class and volume, sends the top chunks to Gemini 2.0 Flash with a compliance-focused system prompt, extracts NCC section references from the response, and returns the answer with a references list.

NCC content is pre-processed by a Python ingestion script that parses the PDF using TOC-based chunking with section ID extraction. Each chunk carries metadata (volume, part, section ID, applicable building classes) for filtered retrieval. 363 chunks covering 208 unique sections from NCC 2022 Volume 2.

The entire pipeline runs on free tiers. No paid services required.

## Setup

**Build:**

```bash
cd revit-addin
dotnet build
```

**Deploy:**

1. Copy `BuildSpec.addin` to `%AppData%\Autodesk\Revit\Addins\2025\`
2. Update the `<Assembly>` path in the .addin file to point to your built DLL
3. Configure your Supabase connection. Either set `BUILDSPEC_SUPABASE_URL` and `BUILDSPEC_API_KEY` env vars, or use the Settings panel inside Revit

**Requires:** Revit 2025, .NET 8 SDK, Supabase project with NCC data ingested

## Why this project

This demonstrates building a full AI-powered tool for a domain-specific professional application:

- RAG pipeline from scratch (PDF parsing, chunking strategy, embedding, vector search, LLM generation)
- Integrating AI into Autodesk Revit with WPF dockable panels and threading constraints
- Working with regulatory/compliance content (structured metadata, section-based retrieval, citation accuracy)
- End-to-end system design across multiple platforms (Supabase, Gemini, Revit/.NET, Python)
- Shipping on entirely free tiers with no paid infrastructure
