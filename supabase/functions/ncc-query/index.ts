import { createClient } from "npm:@supabase/supabase-js@2";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const GEMINI_EMBED_URL =
  "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";
const GEMINI_LLM_URL =
  "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

interface NccChunk {
  id: number;
  content: string;
  section: string;
  title: string;
  part: string;
  volume: number;
  applicable_classes: number[];
  similarity: number;
}

interface ChatMessage {
  role: "user" | "assistant";
  content: string;
}

interface RequestBody {
  question: string;
  context: {
    building_class: string;
    state: string;
    construction_type: string;
  };
  chat_history?: ChatMessage[];
}

// Volume 2: Classes 1, 1a, 10a, 10b, 10c. Volume 1: Classes 2-9.
function parseClassInfo(raw: string): { volume: number; classInt: number } {
  const lower = raw.toLowerCase().trim();
  if (lower.startsWith("10")) return { volume: 2, classInt: 10 };
  if (lower.startsWith("1")) return { volume: 2, classInt: 1 };
  const num = parseInt(lower, 10);
  if (num >= 2 && num <= 9) return { volume: 1, classInt: num };
  return { volume: 2, classInt: 1 };
}

function validate(body: unknown): RequestBody {
  const b = body as Record<string, unknown>;
  if (!b?.question || typeof b.question !== "string") {
    throw new Error("Missing required field: question");
  }
  const ctx = b.context as Record<string, unknown> | undefined;
  if (!ctx?.building_class || !ctx?.state || !ctx?.construction_type) {
    throw new Error(
      "Missing required field: context (building_class, state, construction_type)"
    );
  }
  return b as unknown as RequestBody;
}

async function embedQuery(
  text: string,
  apiKey: string
): Promise<number[]> {
  const res = await fetch(GEMINI_EMBED_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-goog-api-key": apiKey,
    },
    body: JSON.stringify({
      content: { parts: [{ text }] },
      taskType: "RETRIEVAL_QUERY",
      outputDimensionality: 768,
    }),
  });
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`Embedding failed (${res.status}): ${err}`);
  }
  const data = await res.json();
  return data.embedding.values;
}

async function searchChunks(
  supabase: ReturnType<typeof createClient>,
  embedding: number[],
  volume: number,
  classInt: number
): Promise<NccChunk[]> {
  // PostgREST requires vector as a string literal
  const vectorStr = `[${embedding.join(",")}]`;
  const { data, error } = await supabase.rpc("match_ncc_chunks", {
    query_embedding: vectorStr,
    filter_volume: volume,
    filter_class: classInt,
    match_threshold: 0.5,
    match_count: 8,
  });
  if (error) throw new Error(`Vector search failed: ${error.message}`);
  return (data ?? []) as NccChunk[];
}

function buildSystemPrompt(
  ctx: RequestBody["context"],
  chunks: NccChunk[]
): string {
  const excerpts = chunks
    .map((c) => `[${c.section}] ${c.title}\n${c.content}`)
    .join("\n\n---\n\n");

  return `You are an NCC (National Construction Code) compliance assistant for Australian buildings.

Project context:
- Building class: ${ctx.building_class}
- State/Territory: ${ctx.state}
- Type of construction: ${ctx.construction_type}

Instructions:
- Answer questions about NCC compliance specific to the project context above
- Always cite NCC sections using bold formatting: **SectionID**
- At the end of your answer, include a "References:" section listing each cited section as: **SectionID** - Section Title
- If the provided NCC excerpts do not contain enough information, say so clearly
- Do not make up section numbers -- only cite sections from the provided excerpts
- Be concise and practical

NCC excerpts:
${excerpts}`;
}

function buildGeminiContents(
  question: string,
  history?: ChatMessage[]
): Array<{ role: string; parts: Array<{ text: string }> }> {
  const contents: Array<{ role: string; parts: Array<{ text: string }> }> = [];
  if (history) {
    for (const msg of history) {
      contents.push({
        role: msg.role === "assistant" ? "model" : "user",
        parts: [{ text: msg.content }],
      });
    }
  }
  contents.push({ role: "user", parts: [{ text: question }] });
  return contents;
}

async function callGemini(
  systemPrompt: string,
  contents: Array<{ role: string; parts: Array<{ text: string }> }>,
  apiKey: string
): Promise<string> {
  const res = await fetch(GEMINI_LLM_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-goog-api-key": apiKey,
    },
    body: JSON.stringify({
      system_instruction: { parts: [{ text: systemPrompt }] },
      contents,
    }),
  });
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`Gemini LLM failed (${res.status}): ${err}`);
  }
  const data = await res.json();
  return data.candidates[0].content.parts[0].text;
}

// Extract section references from the LLM answer by matching bold patterns
// and cross-referencing with chunks that were provided as context.
function extractReferences(
  answer: string,
  chunks: NccChunk[]
): Array<{ section: string; title: string }> {
  const boldRefs = answer.match(/\*\*([A-Z0-9][A-Za-z0-9.]*)\*\*/g) ?? [];
  const cited = new Set(boldRefs.map((r) => r.replace(/\*\*/g, "")));
  const chunkMap = new Map(chunks.map((c) => [c.section, c.title]));

  const refs: Array<{ section: string; title: string }> = [];
  const seen = new Set<string>();
  for (const sec of cited) {
    if (seen.has(sec)) continue;
    seen.add(sec);
    const title = chunkMap.get(sec);
    if (title) refs.push({ section: sec, title });
  }
  return refs;
}

function jsonResponse(
  body: Record<string, unknown>,
  status = 200
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }
  if (req.method !== "POST") {
    return jsonResponse({ error: "Method not allowed" }, 405);
  }

  try {
    const body = await req.json();
    const { question, context, chat_history } = validate(body);

    const geminiKey = Deno.env.get("GEMINI_API_KEY");
    if (!geminiKey) throw new Error("GEMINI_API_KEY not configured");

    const supabase = createClient(
      Deno.env.get("SUPABASE_URL")!,
      Deno.env.get("SUPABASE_ANON_KEY")!,
      {
        global: {
          headers: { Authorization: req.headers.get("Authorization")! },
        },
      }
    );

    const { volume, classInt } = parseClassInfo(context.building_class);
    const embedding = await embedQuery(question, geminiKey);
    const chunks = await searchChunks(supabase, embedding, volume, classInt);

    if (chunks.length === 0) {
      return jsonResponse({
        answer:
          "No relevant NCC sections found for this building class and query. " +
          "This may be because the requested volume has not been indexed yet.",
        references: [],
      });
    }

    const systemPrompt = buildSystemPrompt(context, chunks);
    const contents = buildGeminiContents(question, chat_history);
    const answer = await callGemini(systemPrompt, contents, geminiKey);
    const references = extractReferences(answer, chunks);

    return jsonResponse({ answer, references });
  } catch (err) {
    const message =
      err instanceof Error ? err.message : "Internal server error";
    const status = message.startsWith("Missing required field") ? 400 : 500;
    return jsonResponse({ error: message }, status);
  }
});
