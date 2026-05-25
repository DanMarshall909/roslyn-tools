// Phase 0 of the "drive backlog to architectural zero" strategy (plan:
// C:\Users\DanMarshall\.claude\plans\please-summarise-all-unfinsihed-toasty-waterfall.md).
// Applies dispositions to stories that are superseded, folded, reclassified as
// trackers, or deferred. Idempotent: re-running the script is a no-op once each
// story's description carries its disposition marker.

const fs = require("fs");
const path = require("path");

const repo = path.resolve(__dirname, "..");
const files = [
    path.join(repo, "ralph", "backlog-prd.json"),
    path.join(repo, "ralph", "prd.json"),
];

const SUPERSEDED = {
    "US-085": {
        by: "US-114",
        reason:
            "Factory-only direction. US-114's central catalog + capability interfaces + RtCommandFlagBinder is the winning shape per docs/target-command-surface-architecture.md.",
    },
};

const FOLDED = {
    "US-094": {
        into: "US-115",
        reason:
            "One architectural rule. Becomes a single Skip'd Fact in McpAdapterContracts.cs citing the existing allow-list. US-107's McpHandlerNameContract is the template.",
    },
};

const PARTIAL_FOLDED = {
    "US-088": {
        into: "US-084",
        portion: "schema",
        reason:
            "RtCommandInfo.SeeAlso lives with US-084's catalog schema. US-088 retains only the rendering + `rt suggest` integration + bidirectionality lint.",
    },
};

const TRACKER = {
    "US-082": "EP-007 JSON-output rollout — eaten by the per-command sweep.",
    "US-089": "SeeAlso seed data — populated per-command during the sweep.",
    "US-095": "Inspect → native MCP handler — done per-command during the sweep.",
    "US-096": "Analyze → native MCP handler — done per-command during the sweep.",
    "US-104": "change-signature native MCP handler — done in its sweep slot.",
    "US-105": "move native MCP handler — done in its sweep slot.",
    "US-106": "suggest naming alignment — done in its sweep slot.",
};

const DEFERRED = {
    "US-067": "Workflow skill policy — not architectural; revisit after architectural zero.",
    "US-071": "extract-interface dogfood bug — schedule after the sweep; touches one command.",
    "US-073": ".NET 10 migration — defer until .NET 10 ships and stabilises.",
    "US-080": "Synonym thesaurus — defer until US-087 measures actual need.",
    "US-081": "convert-static-class triple-fix — schedule after the sweep.",
    "US-083": "Stryker scope to Cli/Mcp — defer; current pilot still informative.",
    "US-091": "Syntax highlighting polish — defer; ergonomics, not architecture.",
    "US-101": "SolidAnalyzer split — defer; orthogonal to consistency.",
    "US-103": "Parameterless service ctors — defer; testability cleanup.",
    "US-111": "add-base-type tooling — defer; new feature, not consistency.",
    "US-113": "rt analyze conventions / EP-014 exploratory — defer; research-flagged.",
};

const MARKER_PREFIX = "[DISPOSITION";

function hasMarker(description) {
    return description.startsWith(MARKER_PREFIX);
}

function prepend(description, marker) {
    return marker + "\n\n" + description;
}

function applyOne(story, marker, passesValue) {
    if (hasMarker(story.description)) return false;
    story.description = prepend(story.description, marker);
    if (passesValue !== undefined) story.passes = passesValue;
    return true;
}

function processFile(file) {
    const json = JSON.parse(fs.readFileSync(file, "utf8"));
    const stories = json.userStories || [];
    let touched = 0;
    const log = [];

    for (const story of stories) {
        const id = story.id;
        if (SUPERSEDED[id]) {
            const e = SUPERSEDED[id];
            const marker = `[DISPOSITION: SUPERSEDED-BY ${e.by}] ${e.reason}`;
            if (applyOne(story, marker, true)) {
                touched++;
                log.push(`  ${id}  SUPERSEDED-BY ${e.by} (passes -> true)`);
            }
        } else if (FOLDED[id]) {
            const e = FOLDED[id];
            const marker = `[DISPOSITION: FOLDED-INTO ${e.into}] ${e.reason}`;
            if (applyOne(story, marker, true)) {
                touched++;
                log.push(`  ${id}  FOLDED-INTO ${e.into} (passes -> true)`);
            }
        } else if (PARTIAL_FOLDED[id]) {
            const e = PARTIAL_FOLDED[id];
            const marker = `[DISPOSITION: ${e.portion.toUpperCase()}-PORTION-FOLDED-INTO ${e.into}, REMAINDER ACTIVE] ${e.reason}`;
            if (applyOne(story, marker, undefined)) {
                touched++;
                log.push(`  ${id}  PARTIAL-FOLDED (${e.portion}) -> ${e.into}, remainder still passes:${story.passes}`);
            }
        } else if (TRACKER[id]) {
            const marker = `[DISPOSITION: TRACKER-ONLY] ${TRACKER[id]} Close when the sweep removes all commands from US-115's Skip allow-list.`;
            if (applyOne(story, marker, undefined)) {
                touched++;
                log.push(`  ${id}  TRACKER-ONLY (passes stays ${story.passes})`);
            }
        } else if (DEFERRED[id]) {
            const marker = `[DISPOSITION: DEFERRED] ${DEFERRED[id]}`;
            if (applyOne(story, marker, undefined)) {
                touched++;
                log.push(`  ${id}  DEFERRED (passes stays ${story.passes})`);
            }
        }
    }

    if (touched > 0) {
        fs.writeFileSync(file, JSON.stringify(json, null, 4) + "\n");
    }
    return { touched, log };
}

let total = 0;
for (const file of files) {
    if (!fs.existsSync(file)) continue;
    const { touched, log } = processFile(file);
    console.log(`\n${path.relative(repo, file)}: ${touched} edits`);
    for (const line of log) console.log(line);
    total += touched;
}
console.log(`\nTotal: ${total} story dispositions applied.`);
