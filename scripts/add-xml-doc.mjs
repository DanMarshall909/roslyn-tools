#!/usr/bin/env node
/**
 * Bulk XML doc generator.
 *
 * Usage:
 *   node scripts/add-xml-doc.mjs --solution RoslynTools.slnx [--scope RoslynTools.Core] [--members] [--dry-run]
 *
 * Steps:
 *   1. rt inspect find-undocumented --json  → list of {fqn, signature, kind}
 *   2. Batch 25 at a time → POST to local LLM → JSON {fqn: summary} map
 *   3. rt refactor add-xml-doc --from-json docs.json
 */

import { spawnSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import path from "node:path";
import { parseArgs } from "node:util";

if (process.argv.includes("--help") || process.argv.includes("-h")) {
  console.log(`
Usage: node scripts/add-xml-doc.mjs [options]

Options:
  --solution <path>   .slnx/.sln file (default: RoslynTools.slnx)
  --scope <ns>        Namespace prefix filter (e.g. RoslynTools.Core)
  --members           Include undocumented methods/properties (not just types)
  --level <levels>    Comma-separated accessibility levels: public, internal, protected, private, all (default: public)
  --dry-run           Preview changes without writing files
  --llm-url <url>     LLM endpoint (default: http://localhost:8080/v1/chat/completions)
  --model <name>      Model name (default: Bonsai-8B.gguf)
  --batch <n>         Symbols per LLM call (default: 25)
  --out <file>        Output JSON path (default: docs.json)
`);
  process.exit(0);
}

const { values: args } = parseArgs({
  options: {
    solution: { type: "string", default: "RoslynTools.slnx" },
    scope:    { type: "string" },
    members:  { type: "boolean", default: false },
    level:    { type: "string",  default: "public" },
    "dry-run":{ type: "boolean", default: false },
    "llm-url":{ type: "string",  default: "http://localhost:8080/v1/chat/completions" },
    "model":  { type: "string",  default: "Bonsai-8B.gguf" },
    "batch":  { type: "string",  default: "25" },
    "out":    { type: "string",  default: "docs.json" },
  },
  strict: true,
});

const BATCH  = parseInt(args["batch"], 10);
const LLM    = args["llm-url"];
const MODEL  = args["model"];
const OUT    = args["out"];
const DRY    = args["dry-run"];

// ── 1. Discover undocumented symbols ─────────────────────────────────────────

const rtArgs = [
  "inspect", "find-undocumented",
  "--solution", args.solution,
  "--json",
  ...(args.members ? ["--members"] : []),
  ...(args.scope   ? ["--scope", args.scope] : []),
  "--level", args.level,
];

console.error(`[1/3] Running: rt ${rtArgs.join(" ")}`);
// On Windows, .cmd files need cmd.exe to launch; on POSIX, invoke rt directly.
const [RT, rtPrefix] = process.platform === "win32"
  ? ["cmd", ["/c", "rt"]]
  : ["rt", []];

const result = spawnSync(RT, [...rtPrefix, ...rtArgs], {
  encoding: "utf8",
  stdio: ["inherit", "pipe", "inherit"],  // capture stdout only; stderr flows to terminal
});

if (result.status !== 0) {
  process.exit(1);
}

const raw = (result.stdout ?? "").trim();
let parsed;
try {
  parsed = JSON.parse(raw);
} catch {
  console.error("ERROR: Could not parse rt output as JSON:");
  console.error(raw.slice(0, 500));
  process.exit(1);
}
const allSymbols = parsed.symbols;
const symbols = allSymbols.filter(s =>
  !s.filePath.toLowerCase().includes(`${path.sep}tests${path.sep}`)
);
const skippedTests = allSymbols.length - symbols.length;
console.error(`      Found ${allSymbols.length} undocumented symbols (${skippedTests} in test projects skipped).`);

if (symbols.length === 0) {
  console.error("      Nothing to document. Exiting.");
  process.exit(0);
}

// ── 2. Batch → LLM → accumulate docs map ─────────────────────────────────────

const docs = {};
const batches = [];
for (let i = 0; i < symbols.length; i += BATCH) {
  batches.push(symbols.slice(i, i + BATCH));
}

console.error(`[2/3] Sending ${batches.length} batch(es) to ${MODEL} at ${LLM}`);

for (let b = 0; b < batches.length; b++) {
  const batch = batches[b];
  const symbolList = batch
    .map(s => `- ${s.kind} \`${s.fqn}\` — signature: \`${s.signature}\``)
    .join("\n");

  const prompt = `You are a C# technical writer. For each symbol below, write a concise one-sentence XML <summary> comment (plain text only — no XML tags, no line breaks). Return ONLY a JSON object mapping each fully-qualified name to its summary string. No markdown fences, no extra keys.

Symbols:
${symbolList}`;

  const body = JSON.stringify({
    model: MODEL,
    messages: [{ role: "user", content: prompt }],
    temperature: 0.2,
    max_tokens: 1024,
  });

  process.stderr.write(`      Batch ${b + 1}/${batches.length} (${batch.length} symbols)... `);

  let resp;
  try {
    resp = await fetch(LLM, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
    });
  } catch (e) {
    console.error(`\nERROR: Could not reach LLM at ${LLM}: ${e.message}`);
    process.exit(1);
  }

  if (!resp.ok) {
    console.error(`\nERROR: LLM returned HTTP ${resp.status}`);
    process.exit(1);
  }

  const json = await resp.json();
  const text = json.choices?.[0]?.message?.content ?? "";

  let parsed;
  try {
    // Strip markdown fences if the model wrapped them anyway
    const clean = text.replace(/^```(?:json)?\n?/m, "").replace(/\n?```$/m, "").trim();
    parsed = JSON.parse(clean);
  } catch {
    console.error(`\nWARN: Could not parse LLM response for batch ${b + 1}. Skipping.`);
    console.error("      Raw response:", text.slice(0, 300));
    continue;
  }

  let count = 0;
  for (const [fqn, summary] of Object.entries(parsed)) {
    if (typeof summary === "string" && summary.trim()) {
      docs[fqn] = summary.trim();
      count++;
    }
  }
  console.error(`OK (${count} summaries)`);
}

console.error(`      Total summaries collected: ${Object.keys(docs).length}`);

writeFileSync(OUT, JSON.stringify(docs, null, 2));
console.error(`      Written to ${OUT}`);

// ── 3. Apply via rt refactor add-xml-doc ─────────────────────────────────────

if (Object.keys(docs).length === 0) {
  console.error("[3/3] No summaries to apply. Done.");
  process.exit(0);
}

const applyArgs = [
  "refactor", "add-xml-doc",
  "--from-json", OUT,
  "--solution", args.solution,
  ...(DRY ? ["--dry-run"] : []),
];

console.error(`[3/3] Running: rt ${applyArgs.join(" ")}`);
const apply = spawnSync(RT, [...rtPrefix, ...applyArgs], { encoding: "utf8", stdio: "inherit" });
process.exit(apply.status ?? 0);
