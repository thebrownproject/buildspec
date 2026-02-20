# Exploration: BuildScope POC

**Date:** 2026-02-20
**Status:** Ready for planning

---

## Problem

Architects and BIM managers working in Autodesk Revit need to regularly check their designs against the National Construction Code (NCC). Currently this requires leaving Revit, opening PDFs, manually searching through hundreds of pages, and cross-referencing section numbers with their building's specific classification.

This context-switching is slow, error-prone, and frustrating. The NCC is dense regulatory text with complex cross-references between sections, and the applicable requirements change depending on building class, state/territory, and construction type.

---

## Solution

BuildScope is a Revit add-in that provides an AI-powered NCC compliance assistant as a dockable side panel. Users define their project context (building class, state, construction type) and ask natural language questions about NCC compliance. The system retrieves relevant NCC sections using RAG and returns answers with specific section references, all without leaving Revit.

---

## Requirements

- [ ] Revit dockable side panel (WPF, right-docked) extending the Archie Copilot pattern
- [ ] Project management: create, select, and switch between projects via dropdown
- [ ] Project context fields: project name, building class, state/territory, type of construction (A/B/C)
- [ ] Project context stored as local JSON files
- [ ] Per-project chat with ~20 message session memory (clears on Revit close)
- [ ] Natural language NCC queries sent to Supabase Edge Function
- [ ] RAG pipeline: pgvector similarity search with metadata filtering by volume and building class
- [ ] Gemini LLM generates answers using retrieved NCC chunks + project context
- [ ] Answers include inline NCC section references (bold) and a grouped References footer
- [ ] Basic markdown rendering in chat (bold, lists, headers)
- [ ] Settings screen for Supabase URL and API key configuration
- [ ] Python ingestion script: NCC PDFs parsed into section-based chunks with metadata, embedded via Gemini text-embedding-004, stored in Supabase pgvector
- [ ] Welcome screen on first run with project creation prompt

---

## Non-Requirements

- No Revit model interaction (reading building data from the model is a future feature)
- No chat history persistence across Revit sessions (session-only for POC)
- No user authentication or multi-user support
- No cloud sync of project context (local-only)
- No IronPython code execution (carried over from Archie Copilot but not needed)
- No Pinecone (replaced by Supabase pgvector)
- No paid API tiers (everything runs on free tiers)

---

## Architecture

### System Components

```
+------------------+       HTTPS        +------------------------+
|  Revit Add-in    | -----------------> |  Supabase Edge Function |
|  (C# / .NET 8)  |                    |  (TypeScript / Deno)    |
|                  | <----------------- |                         |
|  - WPF Side Panel|    JSON response   |  1. Receive query +     |
|  - Project Context                    |     project context     |
|  - Chat UI       |                    |  2. Embed query via     |
|  - Local JSON    |                    |     Gemini embedding API|
+------------------+                    |  3. pgvector similarity |
                                        |     search + metadata   |
                                        |     filtering           |
                                        |  4. Send chunks + query |
                                        |     to Gemini LLM       |
                                        |  5. Return answer with  |
                                        |     NCC section refs    |
                                        +------------------------+
                                                   |
                                        +------------------------+
                                        |  Supabase Postgres     |
                                        |  + pgvector extension  |
                                        |                        |
                                        |  ncc_chunks table:     |
                                        |  - id                  |
                                        |  - content (text)      |
                                        |  - embedding (vector)  |
                                        |  - volume (1 or 2)     |
                                        |  - part (e.g. "D2")    |
                                        |  - section (e.g.       |
                                        |    "D2D17")            |
                                        |  - title               |
                                        |  - applicable_classes  |
                                        |    (int array)         |
                                        |  - state_specific      |
                                        |    (boolean)           |
                                        +------------------------+

+------------------+
|  Python Script   |  (one-time ingestion)
|  - Parse NCC PDFs|
|  - Extract sections with structure
|  - Generate metadata per section
|  - Embed via Gemini text-embedding-004 (768 dims)
|  - Upload to Supabase pgvector
+------------------+
```

### Query Flow

1. User types a question in the chat panel
2. Add-in sends HTTP POST to Supabase Edge Function with: `{ question, context: { class, state, construction_type } }`
3. Edge Function embeds the question using Gemini text-embedding-004
4. pgvector similarity search runs with metadata filters (volume determined by building class, applicable_classes filter)
5. Top-k relevant NCC chunks retrieved
6. Chunks + question + project context sent to Gemini LLM with system prompt instructing citation format
7. Gemini returns answer with inline NCC section references
8. Edge Function returns structured response: `{ answer, references: [{ section, title }] }`
9. Add-in renders answer with markdown formatting and references footer

### Revit Add-in Structure (ported from Archie Copilot)

```
BuildScope/
  BuildScope.sln
  BuildScope.csproj          # .NET 8, WPF, x64
  BuildScope.addin           # Revit manifest (unique GUID)
  App.cs                     # Entry point, ribbon, pane registration
  Views/
    ChatPanel.xaml            # Main chat UI
    ChatPanel.xaml.cs         # Chat logic (code-behind)
    ProjectForm.xaml          # Project create/edit form
    ProjectForm.xaml.cs
    SettingsPanel.xaml         # API configuration
    SettingsPanel.xaml.cs
  Models/
    ChatMessage.cs            # Message types (User, Assistant, Welcome)
    ProjectContext.cs          # Building class, state, construction type
  Services/
    BuildScopeService.cs      # HTTP calls to Supabase Edge Function
    ProjectManager.cs          # Load/save/switch project JSON files
    Config.cs                  # Supabase URL + API key management
```

### Key Technical Decisions

| Decision | Choice | Reasoning |
|----------|--------|-----------|
| Vector DB | Supabase pgvector | Free tier, no extra service, already using Supabase |
| LLM | Google Gemini (free tier) | Free, good quality, same ecosystem as embeddings |
| Embeddings | Gemini text-embedding-004 | Free, 768 dims, same ecosystem as LLM |
| Backend | Supabase Edge Functions | Free hosting, zero infra, TypeScript/Deno |
| Chunking | Section-based with metadata | Preserves NCC structure, enables filtering, citable |
| Project context storage | Local JSON | Simple, works offline, no auth needed for POC |
| UI pattern | Code-behind (no MVVM) | Matches Archie Copilot pattern, fast to build for POC |
| Chat memory | Session-only, ~20 messages | Matches Archie pattern, persistence is a future add |

### NCC Chunk Metadata Schema

Each chunk in pgvector carries metadata for filtering:

```json
{
  "volume": 1,
  "part": "D2",
  "section": "D2D17",
  "title": "Exit travel distances",
  "applicable_classes": [2, 3, 5, 6, 7, 8, 9],
  "state_specific": false
}
```

Filtering logic:
- Building classes 1, 1a, 10a, 10b, 10c → Volume 2
- Building classes 2-9 → Volume 1
- `applicable_classes` filter narrows to relevant sections
- State-specific variations flagged for future state filtering

---

## Constraints

- Must be WPF — Revit dockable panes require a WPF `FrameworkElement`
- Must target `net8.0-windows` with `UseWPF=true`
- Revit API is single-threaded — any future model interaction must use `ExternalEvent` pattern
- DLL locked by Revit — must close Revit to rebuild during development
- `.addin` manifest deployed to `%AppData%\Autodesk\Revit\Addins\2025\` with unique GUID
- Revit add-in development requires Windows (Mac for backend/ingestion work only)
- All services must operate within free tiers (Supabase, Gemini)
- NCC is publicly available but content should be treated respectfully (chunked, not republished)

---

## Wireframes

### Screen 1: First Run (no projects)

```
┌────────────────────────────┐
│ BuildScope              [⚙] │
├────────────────────────────┤
│                            │
│     Welcome to             │
│     BuildScope             │
│                            │
│  Your NCC compliance       │
│  assistant. Ask questions  │
│  about the National        │
│  Construction Code and get │
│  answers with section      │
│  references.               │
│                            │
│  Create a project to get   │
│  started.                  │
│                            │
│     [ + New Project ]      │
│                            │
└────────────────────────────┘
```

### Screen 2: Create Project

```
┌────────────────────────────┐
│ BuildScope              [⚙] │
├────────────────────────────┤
│                            │
│  New Project               │
│                            │
│  Project Name              │
│  [______________________]  │
│                            │
│  Building Class            │
│  [▼ Select class ]         │
│  (1, 1a, 2, 3, 4, 5,      │
│   6, 7a, 7b, 8, 9a,       │
│   9b, 9c, 10a, 10b, 10c)  │
│                            │
│  State / Territory         │
│  [▼ Select state ]         │
│                            │
│  Type of Construction      │
│  [▼ Select type ]          │
│  (Type A, Type B, Type C)  │
│                            │
│  [Cancel]  [Create Project]│
│                            │
└────────────────────────────┘
```

### Screen 3: Chat (main view)

```
┌────────────────────────────┐
│ BuildScope              [⚙] │
├────────────────────────────┤
│ [▼ Smith Residence      ]  │
│   VIC | Class 1 | Type C   │
├────────────────────────────┤
│                            │
│  Ask me anything about NCC │
│  compliance for your Class │
│  1 building in Victoria.   │
│                            │
│ ┌────────────────────────┐ │
│ │ What insulation do I   │ │
│ │ need for external walls?│ │
│ └────────────────────────┘ │
│                            │
│ For a **Class 1** building │
│ in **Victoria**, external  │
│ walls must achieve a       │
│ minimum total R-value of   │
│ **R2.8** per **H1V3**.     │
│                            │
│ The wall system must also  │
│ meet condensation mgmt     │
│ requirements per **H4V2**. │
│                            │
│ ───────────────────────    │
│ References:                │
│ § H1V3 - Thermal values    │
│ § H4V2 - Condensation      │
│ § Spec 22 - Wall systems   │
│                            │
├────────────────────────────┤
│ [Ask about NCC...      ][→] │
└────────────────────────────┘
```

### Screen 4: Project Dropdown Expanded

```
┌────────────────────────────┐
│ BuildScope              [⚙] │
├────────────────────────────┤
│ [▲ Smith Residence      ]  │
│ ┌────────────────────────┐ │
│ │ Smith Res.    VIC  C ✓│ │
│ │ Office Tower  NSW  A   │ │
│ │ Warehouse     QLD  B   │ │
│ ├────────────────────────┤ │
│ │ + New Project          │ │
│ └────────────────────────┘ │
│ ...                        │
└────────────────────────────┘
```

### Screen 5: Settings

```
┌────────────────────────────┐
│ [←] Settings               │
├────────────────────────────┤
│                            │
│  Supabase URL              │
│  [______________________]  │
│                            │
│  API Key                   │
│  [______________________]  │
│                            │
│  [Save]                    │
│                            │
└────────────────────────────┘
```

---

## Success Criteria

- [ ] BuildScope loads as a dockable side panel in Revit 2025
- [ ] User can create a project with name, building class, state, and construction type
- [ ] User can switch between multiple projects via dropdown
- [ ] User can type an NCC compliance question and receive an answer
- [ ] Answers include inline NCC section references and a references footer
- [ ] Answers are contextually relevant to the project's building class and state
- [ ] Chat renders basic markdown (bold, lists, headers)
- [ ] Settings screen allows configuring Supabase URL and API key
- [ ] NCC ingestion script successfully processes PDFs into pgvector
- [ ] Edge Function correctly retrieves relevant chunks and generates answers
- [ ] Entire system runs on free tiers (Supabase + Gemini)

---

## Open Questions

1. **NCC PDF source and format** — Which specific NCC PDF files to ingest? Are they well-structured enough for automated section extraction, or will manual cleanup be needed?
2. **Gemini model version** — Which specific Gemini model for the LLM queries? (e.g., gemini-1.5-flash for speed, gemini-1.5-pro for quality)
3. **Top-k retrieval count** — How many NCC chunks to retrieve per query? Needs tuning after ingestion.
4. **Building class sub-types** — Should the class dropdown include all sub-types (1a, 7a, 7b, 9a, 9b, 9c) or simplify for POC?

---

## Next Steps

1. `/plan` to create implementation tasks from this spec
2. Download NCC PDFs and assess structure for ingestion
3. Set up Supabase project with pgvector extension
4. Begin implementation (Python ingestion script can run on Mac, Revit add-in requires Windows)
