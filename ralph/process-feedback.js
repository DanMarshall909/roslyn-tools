#!/usr/bin/env node
// process-feedback.js — AI agent that triages rt feedback items into backlog user stories.
//
// Usage:  node ralph/process-feedback.js [--dry-run] [--repo-root <path>]
// Env:    ANTHROPIC_API_KEY=<key>
//
// Requires: node 18+ (uses native fetch), @anthropic-ai/sdk
//   npm install --prefix ralph
//   # or: npm install -g @anthropic-ai/sdk

"use strict";

const fs = require("fs");
const path = require("path");

// ---------------------------------------------------------------------------
// Args
// ---------------------------------------------------------------------------

const args = process.argv.slice(2);
const dryRun = args.includes("--dry-run");

function argValue(flag) {
    const idx = args.indexOf(flag);
    return idx !== -1 && idx + 1 < args.length ? args[idx + 1] : null;
}

// ---------------------------------------------------------------------------
// Repo root resolution
// ---------------------------------------------------------------------------

function findRepoRoot(startDir) {
    let dir = startDir || process.cwd();
    while (dir) {
        if (fs.existsSync(path.join(dir, "ralph"))) return dir;
        const parent = path.dirname(dir);
        if (parent === dir) break;
        dir = parent;
    }
    return null;
}

const repoRoot = argValue("--repo-root") || findRepoRoot(path.resolve(__dirname, ".."));

if (!repoRoot) {
    console.error("Error: could not locate repo root (expected a 'ralph/' directory). Pass --repo-root <path>.");
    process.exit(1);
}

const feedbackPath = path.join(repoRoot, "ralph", "feedback.json");
const backlogPath = path.join(repoRoot, "ralph", "backlog-prd.json");

// ---------------------------------------------------------------------------
// API key check
// ---------------------------------------------------------------------------

const apiKey = process.env.ANTHROPIC_API_KEY;
if (!apiKey) {
    console.error("Error: ANTHROPIC_API_KEY environment variable is not set.");
    process.exit(1);
}

// ---------------------------------------------------------------------------
// File I/O helpers
// ---------------------------------------------------------------------------

function readFeedback() {
    if (!fs.existsSync(feedbackPath)) return [];
    try {
        const raw = JSON.parse(fs.readFileSync(feedbackPath, "utf8"));
        return raw.feedbackItems || [];
    } catch (e) {
        console.error(`Warning: could not parse ${feedbackPath}: ${e.message}`);
        return [];
    }
}

function writeFeedback(items) {
    const tmp = feedbackPath + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify({ feedbackItems: items }, null, 2));
    fs.renameSync(tmp, feedbackPath);
}

function readBacklog() {
    if (!fs.existsSync(backlogPath)) {
        return {
            project: "roslyn-tools",
            branchName: "backlog",
            description: "Triaged backlog",
            epics: [],
            userStories: []
        };
    }
    return JSON.parse(fs.readFileSync(backlogPath, "utf8"));
}

function writeBacklog(backlog) {
    const tmp = backlogPath + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(backlog, null, 4));
    fs.renameSync(tmp, backlogPath);
}

function nextStoryId(backlog) {
    const ids = (backlog.userStories || [])
        .map(s => s.id)
        .filter(id => /^US-\d+$/.test(id))
        .map(id => parseInt(id.slice(3), 10));
    const max = ids.length ? Math.max(...ids) : 0;
    return `US-${String(max + 1).padStart(3, "0")}`;
}

// ---------------------------------------------------------------------------
// Anthropic API call
// ---------------------------------------------------------------------------

async function callClaude(prompt) {
    let Anthropic;
    try {
        Anthropic = require("@anthropic-ai/sdk");
    } catch {
        console.error(
            "Error: @anthropic-ai/sdk not found. Run: npm install --prefix ralph\n" +
            "       or:  npm install -g @anthropic-ai/sdk"
        );
        process.exit(1);
    }

    const client = new Anthropic.default({ apiKey });

    const message = await client.messages.create({
        model: "claude-sonnet-4-6",
        max_tokens: 1024,
        messages: [{ role: "user", content: prompt }]
    });

    return message.content[0].text;
}

function extractJson(text) {
    const match = text.match(/\{[\s\S]*\}/);
    if (!match) throw new Error("No JSON object found in Claude response");
    return JSON.parse(match[0]);
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
    const allItems = readFeedback();
    const pending = allItems.filter(e => e.status === "pending");

    if (pending.length === 0) {
        console.log("No pending feedback items. Nothing to process.");
        return;
    }

    console.log(`Found ${pending.length} pending feedback item(s).`);

    const backlog = readBacklog();
    const existingTitles = new Set(
        (backlog.userStories || []).map(s => (s.title || "").toLowerCase())
    );

    let added = 0;
    let skipped = 0;

    for (const item of pending) {
        process.stdout.write(`  Processing ${item.id} (${item.type})... `);

        const storyId = nextStoryId(backlog);
        const epicSummary = (backlog.epics || [])
            .map(e => `  ${e.id}: ${e.title}`)
            .join("\n");

        const prompt = `You are a product manager for "roslyn-tools", a CLI refactoring tool for C# using Roslyn.
Convert the following user feedback into a user story for the backlog.

Feedback item:
- ID: ${item.id}
- Type: ${item.type}
- Description: ${item.description || "(none)"}
- Sample Code: ${item.sampleCode || "(none)"}
- Expected Result: ${item.expectedResult || "(none)"}
- Actual Result: ${item.actualResult || "(none)"}
- Command Line: ${item.commandLine || "(none)"}
- Stack Trace: ${item.stackTrace ? item.stackTrace.slice(0, 500) + "..." : "(none)"}

Available epics:
${epicSummary || "  (none defined)"}

Return ONLY a JSON object with these exact fields (no markdown, no explanation):
{
  "valid": true or false,
  "reason": "one-line explanation of whether it is actionable and why",
  "story": {
    "epicId": "EP-NNN or null if no epic fits",
    "title": "short imperative title describing the fix or feature",
    "description": "as-a / i-want / so-that user story description",
    "acceptanceCriteria": ["criterion 1", "criterion 2"],
    "notes": "implementation hints or repro steps"
  }
}

Set valid=false (and story=null) if the feedback is: a duplicate of an existing known issue, too vague to act on, not related to roslyn-tools, or is not a real bug/feature.
Set valid=true and provide a story for genuine bugs (especially stack traces) and concrete feature requests.`;

        let result;
        try {
            const raw = await callClaude(prompt);
            result = extractJson(raw);
        } catch (e) {
            console.log(`FAILED (${e.message})`);
            continue;
        }

        const resolution = result.reason || "";

        if (!result.valid || !result.story) {
            console.log(`skipped — ${resolution}`);
            item.status = "processed";
            item.processedAt = new Date().toISOString();
            item.resolution = `Skipped: ${resolution}`;
            skipped++;
            continue;
        }

        const titleLower = (result.story.title || "").toLowerCase();
        if (existingTitles.has(titleLower)) {
            console.log(`skipped — duplicate title`);
            item.status = "processed";
            item.processedAt = new Date().toISOString();
            item.resolution = "Skipped: duplicate story title already in backlog";
            skipped++;
            continue;
        }

        const newStory = {
            id: storyId,
            epicId: result.story.epicId || undefined,
            title: result.story.title,
            description: result.story.description,
            acceptanceCriteria: result.story.acceptanceCriteria || [],
            priority: parseInt(storyId.slice(3), 10),
            passes: false,
            notes: `Generated from feedback ${item.id}. ${result.story.notes || ""}`.trim()
        };

        if (dryRun) {
            console.log(`would add → ${storyId}: ${newStory.title}`);
        } else {
            backlog.userStories.push(newStory);
            existingTitles.add(titleLower);
            item.status = "processed";
            item.processedAt = new Date().toISOString();
            item.resolution = `Added as ${storyId}: ${resolution}`;
            console.log(`added → ${storyId}: ${newStory.title}`);
            added++;
        }
    }

    if (!dryRun) {
        writeBacklog(backlog);
        writeFeedback(allItems);
    }

    console.log(`\nDone. ${pending.length} item(s) processed: ${added} added to backlog, ${skipped} skipped.`);
    if (dryRun) console.log("(dry-run — no files were written)");
}

main().catch(e => {
    console.error("Fatal error:", e.message);
    process.exit(1);
});
