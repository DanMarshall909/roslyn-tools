// Tests for .claude/scripts/rt-first-gate.sh — the PreToolUse Read/Edit gate
// that enforces "inspect .cs files via rt first". Run: node --test .claude/scripts/rt-first-gate.test.cjs
//
// Regression cover for FB-364 / FB-365: the gate matched the Read target against
// the inspect log by the FULL path, case-sensitively, so a drive-letter case
// difference (lowercase `c:` inspected, uppercase `C:` read) wrongly denied. The
// fix matches on the bare filename, case-insensitively, and handles backslash
// (Windows) path separators.

const { test } = require("node:test");
const assert = require("node:assert");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const GATE = path.join(__dirname, "rt-first-gate.sh");

// Invoke the gate with a given hook payload + inspect-log contents.
// Returns { stdout, exitCode, decision } where decision is the parsed
// permissionDecision ("deny" | undefined).
function runGate(payload, logLines) {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rt-gate-"));
    const logPath = path.join(dir, "rt-inspected.log");
    if (logLines !== null) fs.writeFileSync(logPath, logLines.join("\n") + "\n");
    try {
        const res = spawnSync("bash", [GATE], {
            input: JSON.stringify(payload),
            encoding: "utf8",
            env: {
                ...process.env,
                RT_INSPECTED_LOG: logPath,
                // Point the build-status marker at a path that does not exist so the
                // "broken build" bypass never fires during tests.
                RT_BUILD_STATUS_MARKER: path.join(dir, "no-such-marker"),
            },
        });
        let decision;
        const out = (res.stdout || "").trim();
        if (out) {
            try {
                decision = JSON.parse(out).hookSpecificOutput?.permissionDecision;
            } catch {
                /* non-JSON output (e.g. empty) */
            }
        }
        return { stdout: out, exitCode: res.status, decision };
    } finally {
        fs.rmSync(dir, { recursive: true, force: true });
    }
}

const read = (filePath) => ({ tool_name: "Read", tool_input: { file_path: filePath } });

test("FB-365: drive-letter case mismatch still satisfies the gate", () => {
    // Inspected with lowercase c:, read with uppercase C: — same file.
    const log = ["1700000000 rt inspect outline c:\\code\\proj\\McpServiceRegistration.cs --solution x.slnx"];
    const { decision } = runGate(read("C:\\code\\proj\\McpServiceRegistration.cs"), log);
    assert.notStrictEqual(decision, "deny", "uppercase-drive Read should be allowed after lowercase-drive inspect");
});

test("backslash path: gate matches on the bare filename, not the full path", () => {
    // Inspected one dir, read another — same filename. Bare-name match should allow.
    const log = ["1700000000 rt inspect outline C:\\a\\b\\Widget.cs --solution x.slnx"];
    const { decision } = runGate(read("C:\\other\\dir\\Widget.cs"), log);
    assert.notStrictEqual(decision, "deny", "bare-filename match should allow regardless of directory");
});

test("forward-slash path is also matched", () => {
    const log = ["1700000000 rt inspect outline /c/code/proj/Gadget.cs --solution x.slnx"];
    const { decision } = runGate(read("/c/code/proj/Gadget.cs"), log);
    assert.notStrictEqual(decision, "deny");
});

test("an uninspected .cs file is denied", () => {
    const { decision } = runGate(read("C:\\code\\proj\\NeverSeen.cs"), ["1700000000 rt inspect outline Other.cs"]);
    assert.strictEqual(decision, "deny", "a file never inspected must be denied");
});

test("non-.cs files pass silently (no decision, exit 0)", () => {
    const { decision, exitCode, stdout } = runGate(read("C:\\code\\proj\\notes.md"), null);
    assert.strictEqual(decision, undefined);
    assert.strictEqual(exitCode, 0);
    assert.strictEqual(stdout, "");
});

test("Write to a .cs file is always allowed (and logs the basename)", () => {
    const { exitCode } = runGate({ tool_name: "Write", tool_input: { file_path: "C:\\code\\proj\\Fresh.cs" } }, []);
    assert.strictEqual(exitCode, 0);
});
