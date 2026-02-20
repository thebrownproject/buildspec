# Feature: BuildScope POC

**Goal:** Ship a Revit add-in with AI-powered NCC compliance Q&A using RAG over Supabase pgvector and Gemini, ported from the Archie Copilot side panel pattern.

## Overview

BuildScope is a dockable Revit side panel that lets architects ask natural language questions about the National Construction Code and get answers with specific section references. Users define project context (building class, state, construction type) which filters the RAG pipeline to return relevant NCC sections only.

The system has three major subsystems:
1. **Supabase backend** — Postgres with pgvector for NCC embeddings + Edge Function for RAG queries
2. **Python ingestion** — One-time script to parse NCC PDFs into section-based chunks with metadata
3. **Revit add-in** — C#/.NET 8 WPF dockable pane with project management and chat UI

Phase 1 (Mac) builds the backend. Phase 2 (Windows) builds the Revit add-in.

## Tasks

### Task: Set up Supabase project and database schema

**Goal:** Create the Supabase project, enable pgvector, create the ncc_chunks table with metadata columns, and deploy the similarity search function.
**Files:** Supabase migrations (applied via MCP or dashboard)
**Depends on:** None

**Steps:**
1. Create Supabase project (or use existing one)
2. Enable pgvector extension: `create extension if not exists vector with schema extensions;`
3. Create `ncc_chunks` table with columns: id (bigint identity), content (text), embedding (vector(768)), volume (smallint), part (text), section (text), title (text), applicable_classes (int array), state_specific (boolean), created_at (timestamptz)
4. Create HNSW index on embedding column for cosine similarity
5. Create GIN index on applicable_classes for array containment queries
6. Create `match_ncc_chunks` RPC function that accepts query_embedding, filter_volume, filter_class, match_threshold (default 0.7), match_count (default 8) and returns matching chunks ordered by similarity
7. Set GEMINI_API_KEY as Supabase secret
8. Verify by inserting a test row with a dummy 768-dim vector and querying it back

**Tests:**
- [ ] pgvector extension is enabled
- [ ] ncc_chunks table exists with correct columns and types
- [ ] match_ncc_chunks function accepts embedding + filters and returns results
- [ ] Can insert a test row and retrieve it with similarity of 1.0
- [ ] GEMINI_API_KEY secret is set

### Task: Build Python NCC ingestion script

**Goal:** Create a Python script that parses NCC PDFs into section-based chunks with rich metadata, generates embeddings via Gemini text-embedding-004, and uploads to Supabase pgvector.
**Files:** Create ingestion/ingest.py, ingestion/requirements.txt, ingestion/.env.example
**Depends on:** Set up Supabase project and database schema

**Steps:**
1. Create `ingestion/` directory with requirements.txt (pymupdf, supabase, google-genai, python-dotenv, tqdm)
2. Download NCC PDFs and place in `ingestion/pdfs/`
3. Implement PDF text extraction using PyMuPDF (fitz)
4. Implement section-based chunking: detect Part headers (e.g. "Part D2 Access and egress") and Section headers (e.g. "D2D17 Exit travel distances") using regex, split text at section boundaries
5. Split long sections (>2000 chars) at paragraph boundaries
6. Detect applicable building classes per chunk: Volume 2 sections default to classes [1, 10], Volume 1 sections parse class mentions or default to [2-9]
7. Generate embeddings in batches of 20 using Gemini text-embedding-004 (768 dims) with rate limiting (0.5s between batches)
8. Upload to Supabase ncc_chunks table in batches of 50
9. Add dry-run mode that prints chunks without calling APIs
10. Run ingestion and verify data in Supabase

**Tests:**
- [ ] Script runs without errors on NCC PDFs
- [ ] Chunks have correct volume (1 or 2) based on PDF source
- [ ] Section IDs match NCC format (e.g. "D2D17", "H1V3")
- [ ] Each chunk has a 768-dimension embedding
- [ ] Dry-run mode prints chunks without calling APIs
- [ ] Data appears in Supabase ncc_chunks table
- [ ] match_ncc_chunks returns relevant results for a test query

### Task: Deploy Supabase Edge Function for RAG queries

**Goal:** Create and deploy a TypeScript/Deno Edge Function that receives a question + project context, runs vector search with metadata filtering, calls Gemini LLM with retrieved chunks, and returns an answer with NCC section references.
**Files:** Supabase Edge Function `ncc-query` (deployed via MCP)
**Depends on:** Set up Supabase project and database schema (needs schema); Build Python NCC ingestion script (needs data to test)

**Steps:**
1. Implement Edge Function entry point with POST handler and input validation
2. Implement query embedding via Gemini text-embedding-004 REST API
3. Implement pgvector similarity search via supabase.rpc("match_ncc_chunks") with volume filtering (class 1/1a/10a-c -> volume 2, class 2-9 -> volume 1) and building class filtering
4. Build system prompt that injects project context (class, state, construction type) and instructs NCC citation format with bold section references and a References footer
5. Implement LLM call to Gemini 2.0 Flash with retrieved chunks, system prompt, and optional chat history (last 10 messages)
6. Extract structured references from the answer text (parse bold section patterns)
7. Return response as `{ answer: string, references: [{ section, title }] }`
8. Deploy with verify_jwt: true (anon key works as Bearer token)
9. Test with curl using different building classes to verify volume filtering
10. Document the API contract (request/response JSON shapes) for the C# service

**Tests:**
- [ ] Edge Function deploys without errors
- [ ] POST with valid body returns 200 with { answer, references } shape
- [ ] POST with missing fields returns 400 with error message
- [ ] Answer contains inline bold NCC section references
- [ ] References array has section and title for each cited section
- [ ] Class 1 query retrieves only Volume 2 chunks
- [ ] Class 3 query retrieves only Volume 1 chunks
- [ ] Response time under 10 seconds
- [ ] Handles empty search results gracefully

### Task: Scaffold Revit add-in project from Archie Copilot

**Goal:** Create the BuildScope.sln and .csproj targeting net8.0-windows with WPF, port App.cs from Archie Copilot with new GUID and branding, remove IronPython dependencies, verify it builds and loads in Revit.
**Files:** Create BuildScope.sln, BuildScope.csproj, BuildScope.addin, App.cs
**Depends on:** None (but requires Windows environment)

**Steps:**
1. Create BuildScope.sln and BuildScope.csproj targeting net8.0-windows with UseWPF=true, x64 platform
2. Reference RevitAPI.dll and RevitAPIUI.dll from Revit 2025 install path (Private=False)
3. Add Newtonsoft.Json NuGet reference (keep from Archie). Do NOT add IronPython
4. Port App.cs from Archie Copilot: implement IExternalApplication, rename namespace to BuildScope, change pane title to "BuildScope", generate a new unique GUID for DockablePaneId, create ribbon tab "BuildScope" with toggle button
5. Remove RevitCommandHandler.cs and all IronPython/ExternalEvent code (not needed)
6. Create BuildScope.addin manifest with unique AddInId GUID
7. Create placeholder ChatPanel.xaml/cs (empty WPF Page implementing IDockablePaneProvider) so the project builds
8. Build with dotnet build and verify no errors
9. Copy to Revit addins folder and verify it loads

**Tests:**
- [ ] Project builds with dotnet build (no errors)
- [ ] .addin manifest has a unique GUID (not Archie's B5F5C9A2-7D3E-4A1B-9C8F-2E6D4A3B1C0D)
- [ ] No IronPython dependency in csproj
- [ ] App.cs registers a dockable pane titled "BuildScope"
- [ ] Ribbon tab shows "BuildScope" with toggle button
- [ ] Side panel opens in Revit (even if empty)

### Task: Build models and services layer

**Goal:** Create all C# models (ChatMessage, ProjectContext) and services (Config, BuildScopeService, ProjectManager) that power the add-in's data and API layers.
**Files:** Create Models/ChatMessage.cs, Models/ProjectContext.cs, Services/Config.cs, Services/BuildScopeService.cs, Services/ProjectManager.cs
**Depends on:** Scaffold Revit add-in project from Archie Copilot; Deploy Supabase Edge Function for RAG queries (needs API contract)

**Steps:**
1. Create Models/ChatMessage.cs with MessageType enum (User, Assistant, Welcome, Loading), properties: Type, Content, References (list of {Section, Title}), Timestamp
2. Create Models/ProjectContext.cs with properties: Name, BuildingClass (string), State (string), ConstructionType (string)
3. Port Config.cs from Archie: store Supabase URL and API key (instead of Anthropic key). Read from env vars BUILDSCOPE_SUPABASE_URL and BUILDSCOPE_API_KEY, fallback to config.json next to DLL. Support Save/Load
4. Create BuildScopeService.cs: HTTP POST to `{supabaseUrl}/functions/v1/ncc-query` with `{ question, context: { building_class, state, construction_type }, chat_history }`. Deserialize response into `{ answer, references[] }`. Set Authorization header with Bearer anon key
5. Create ProjectManager.cs: save/load ProjectContext as JSON files in a local directory (AppData/BuildScope/Projects/). Methods: CreateProject, ListProjects, LoadProject, DeleteProject, GetCurrentProject, SetCurrentProject. Current project tracked in memory

**Tests:**
- [ ] Config reads Supabase URL and API key from env vars or config.json
- [ ] Config.Save persists values, Config.Load reads them back
- [ ] BuildScopeService sends correctly formatted POST matching Edge Function contract
- [ ] BuildScopeService deserializes answer text and references array
- [ ] HTTP errors produce clear error messages
- [ ] ProjectManager creates/lists/loads/deletes projects as JSON files
- [ ] Can switch current project and retrieve it
- [ ] Missing/corrupted project files handled gracefully

### Task: Build all views (ChatPanel, ProjectForm, SettingsPanel)

**Goal:** Create the complete WPF UI: ChatPanel (main chat with markdown rendering and references footer), ProjectForm (create project with dropdowns for class, state, construction type), SettingsPanel (Supabase URL and API key config), and the project selector dropdown.
**Files:** Create/modify Views/ChatPanel.xaml, Views/ChatPanel.xaml.cs, Views/ProjectForm.xaml, Views/ProjectForm.xaml.cs, Views/SettingsPanel.xaml, Views/SettingsPanel.xaml.cs
**Depends on:** Build models and services layer

**Steps:**
1. Port ChatPanel.xaml from Archie Copilot with these changes:
   - Header: "BuildScope" branding with settings gear icon button
   - Project dropdown bar below header: ComboBox bound to ProjectManager.ListProjects, showing "Name | State | Class | Type"
   - Remove Code and Result DataTemplates (not needed)
   - Keep User, Assistant, Loading, Welcome templates
   - Welcome message: "Ask me anything about NCC compliance for your [Class] building in [State]." with New Project button if no projects exist
   - Input placeholder: "Ask about NCC..."
2. Add basic markdown rendering for assistant messages: bold (**text**) via Run with FontWeight.Bold, bullet lists via TextBlock with bullet prefix, section headers via larger font size
3. Add References footer to assistant messages: separator line + "References:" header + list of "§ Section - Title" items
4. Create ProjectForm.xaml: Project Name TextBox, Building Class ComboBox (1, 1a, 2, 3, 4, 5, 6, 7a, 7b, 8, 9a, 9b, 9c, 10a, 10b, 10c), State/Territory ComboBox (NSW, VIC, QLD, SA, WA, TAS, NT, ACT), Type of Construction ComboBox (Type A, Type B, Type C), Cancel and Create buttons
5. Create SettingsPanel.xaml: Supabase URL TextBox, API Key PasswordBox, Save button, back arrow to return to chat
6. Wire navigation: gear icon -> SettingsPanel, back arrow -> ChatPanel, "+ New Project" in dropdown -> ProjectForm, Create button -> back to ChatPanel with new project selected

**Tests:**
- [ ] Panel renders with BuildScope header and settings gear icon
- [ ] Project dropdown shows projects with name, class, state, type
- [ ] Welcome screen shows on first run with New Project button
- [ ] ProjectForm has all required dropdowns with correct options
- [ ] Creating a project navigates to chat with project context visible
- [ ] Empty project name validation prevents creation
- [ ] SettingsPanel loads and saves Supabase URL and API key
- [ ] Navigation between views works (gear -> settings -> back, dropdown -> new project -> chat)
- [ ] Assistant messages render bold text and bullet lists
- [ ] References footer displays section numbers and titles

### Task: Wire up end-to-end chat flow

**Goal:** Connect all pieces: ChatPanel sends questions with project context to BuildScopeService, renders responses with markdown and references, manages per-project session memory (~20 messages), and handles project switching, errors, and missing config gracefully.
**Files:** Modify Views/ChatPanel.xaml.cs, Services/BuildScopeService.cs
**Depends on:** All previous tasks

**Steps:**
1. Wire send button: on click, add user message to current project's ObservableCollection, show loading message, call BuildScopeService.QueryAsync(question, currentProject, chatHistory)
2. On response: remove loading, add assistant message with parsed answer + references to collection, auto-scroll to bottom
3. Implement per-project chat memory: Dictionary<string, ObservableCollection<ChatMessage>> keyed by project name, cap at 20 messages per project (FIFO eviction), clear on project switch
4. Project switching: dropdown selection change loads that project's chat history (or shows welcome if empty), updates context bar
5. Error handling: missing config -> show "Configure Supabase URL and API key in Settings" message in chat, network errors -> show error as assistant message, empty results -> show "No relevant NCC sections found" message
6. Send chat history with each request (last 10 user/assistant messages as chat_history array)
7. Test complete flow: create project -> ask NCC question -> get answer with section references -> ask follow-up -> see contextual answer

**Tests:**
- [ ] Typing a question sends it to Edge Function with correct project context
- [ ] Response renders in chat with answer text and references footer
- [ ] Session holds up to ~20 messages per project with FIFO eviction
- [ ] Switching projects loads that project's chat or shows welcome
- [ ] Missing config shows helpful error directing to settings
- [ ] Network errors display gracefully as assistant messages
- [ ] Chat history is sent with requests for multi-turn context
- [ ] Full loop works: create project, ask NCC question, get cited answer, ask follow-up

## Sequence

1. Set up Supabase project and database schema (no dependencies)
2. Build Python NCC ingestion script (depends on 1)
3. Deploy Supabase Edge Function for RAG queries (depends on 1, 2)
4. Scaffold Revit add-in project from Archie Copilot (no dependencies, but requires Windows)
5. Build models and services layer (depends on 4; uses API contract from 3)
6. Build all views (depends on 5)
7. Wire up end-to-end chat flow (depends on all)

**Platform switch between tasks 3 and 4** (Mac -> Windows)

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
