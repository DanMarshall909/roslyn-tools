import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, rmSync, writeFileSync, chmodSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const repoRoot = fileURLToPath(new URL('../', import.meta.url));

function runGate(mode, args = [], shell = 'bash') {
  const root = mkdtempSync(join(tmpdir(), 'rt-check-local-test-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  const dotnet = join(bin, 'dotnet');
  writeFileSync(dotnet, [
    '#!/usr/bin/env bash',
    'set -euo pipefail',
    'case "$1" in',
    '  build) exit 0 ;;',
    '  format) [[ "${RT_FAKE_MODE}" != format-fail ]]; exit $? ;;',
    '  sln) printf "Project(s)\\n----------\\ntests/RoslynTools.Tests/RoslynTools.Tests.csproj\\ntests/RoslynTools.Tests.Mcp/RoslynTools.Tests.Mcp.csproj\\n"; exit 0 ;;',
    '  test)',
    '    results=""',
    '    next=0',
    '    filter=""',
    '    filter_next=0',
    '    for arg in "$@"; do',
    '      if [[ "$filter_next" == 1 ]]; then filter="$arg"; filter_next=0',
    '      elif [[ "$arg" == --filter ]]; then filter_next=1',
    '      fi',
    '      if [[ "$next" == 1 ]]; then results="$arg"; next=0',
    '      elif [[ "$arg" == --results-directory ]]; then next=1',
    '      fi',
    '    done',
    '    printf "test-filter: %s\\n" "$filter"',
    '    [[ -n "$results" ]] || exit 0',
    '    [[ "${RT_FAKE_MODE}" != missing ]] || exit 0',
    '    [[ "${RT_FAKE_MODE}" != second-missing || "$2" != *Tests.Mcp* ]] || exit 0',
    '    mkdir -p "$results"',
    '    if [[ "${RT_FAKE_MODE}" == truncated ]]; then',
    '      printf "<TestRun><ResultSummary" > "$results/results.trx"',
    '      exit 0',
    '    fi',
    '    outcome=Completed; total=1; executed=1; passed=1; failed=0; aborted=0',
    '    if [[ "${RT_FAKE_MODE}" == zero ]]; then total=0; executed=0; passed=0; fi',
    '    if [[ "${RT_FAKE_MODE}" == aborted ]]; then outcome=Aborted; aborted=1; fi',
    '    if [[ "${RT_FAKE_MODE}" == failed ]]; then total=2; executed=2; failed=1; fi',
    '    printf "<TestRun><ResultSummary outcome=\\"%s\\"><Counters total=\\"%s\\" executed=\\"%s\\" passed=\\"%s\\" failed=\\"%s\\" error=\\"0\\" timeout=\\"0\\" aborted=\\"%s\\" passedButRunAborted=\\"0\\" disconnected=\\"0\\" /></ResultSummary></TestRun>\\n" "$outcome" "$total" "$executed" "$passed" "$failed" "$aborted" > "$results/results.trx"',
    '    exit 0 ;;',
    '  *) exit 2 ;;',
    'esac',
  ].join('\n'));
  chmodSync(dotnet, 0o755);

  try {
    const command = shell === 'pwsh' ? 'pwsh'
      : shell === 'direct' ? join(repoRoot, 'scripts/check-local.sh') : 'bash';
    const commandArgs = shell === 'pwsh'
      ? ['-NoProfile', '-File', 'scripts/check-local.ps1', ...args]
      : shell === 'direct' ? args : ['scripts/check-local.sh', ...args];
    const result = spawnSync(command, commandArgs, {
      cwd: repoRoot,
      env: { ...process.env, PATH: bin + delimiter + process.env.PATH, TMPDIR: root, RT_FAKE_MODE: mode },
      encoding: 'utf8',
      timeout: 20_000,
    });
    const output = (result.stdout ?? '') + (result.stderr ?? '');
    const reportPaths = [...output.matchAll(/report: ([^\s)]+)/g)].map((match) => match[1]);
    const existingReports = reportPaths.filter((report) => existsSync(report));
    const filters = [...output.matchAll(/^test-filter: (.*)$/gm)].map(match => match[1]);
    return { status: result.status, output, error: result.error, existingReports, filters };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const hasPowerShell = spawnSync('pwsh', ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()'], {
  encoding: 'utf8',
}).status === 0;

test('Bash gate can be invoked directly by the documented CI command', { skip: process.platform === 'win32' }, () => {
  const result = runGate('complete', [], 'direct');
  assert.equal(result.status, 0, String(result.error ?? result.output));
});

for (const shell of ['bash', 'pwsh']) {
test(`${shell} routine feedback excludes slow groups even with a user filter`, { skip: shell === 'pwsh' && !hasPowerShell }, () => {
  const args = shell === 'pwsh'
    ? ['-Full', '-Filter', 'FullyQualifiedName~RequiredBehavior']
    : ['--full', '--filter', 'FullyQualifiedName~RequiredBehavior'];
  const result = runGate('complete', args, shell);

  assert.equal(result.status, 0, result.output);
  assert.deepEqual(result.filters, [
    '(FullyQualifiedName~RequiredBehavior)&(FullyQualifiedName!~RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.&FullyQualifiedName!~RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.)',
    '(FullyQualifiedName~RequiredBehavior)&(FullyQualifiedName!~RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.&FullyQualifiedName!~RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.)',
  ]);
});

test(`${shell} slow lane selects its owner and ticket lane runs every project once`, { skip: shell === 'pwsh' && !hasPowerShell }, () => {
  const args = shell === 'pwsh' ? ['-Full', '-Lane'] : ['--full', '--lane'];
  const slow = runGate('complete', [...args, 'slow'], shell);
  assert.equal(slow.status, 0, slow.output);
  assert.deepEqual(slow.filters, [
    'FullyQualifiedName~RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.|FullyQualifiedName~RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.',
  ]);
  assert.equal((slow.output.match(/check-local: completed /g) ?? []).length, 1);

  const all = runGate('complete', [...args, 'all'], shell);
  assert.equal(all.status, 0, all.output);
  assert.deepEqual(all.filters, ['', '']);
  assert.equal((all.output.match(/check-local: completed /g) ?? []).length, 2);
});
}

test('an unknown lane or a project without slow tests cannot produce a passing gate', () => {
  const invalid = runGate('complete', ['--lane', 'unknown']);
  assert.notEqual(invalid.status, 0);
  assert.match(invalid.output, /Unknown test lane/);
  assert.deepEqual(invalid.filters, []);
  const unavailable = runGate('complete', ['--lane', 'slow', '--test-project', 'tests/RoslynTools.Tests.Mcp/RoslynTools.Tests.Mcp.csproj']);
  assert.notEqual(unavailable.status, 0);
  assert.match(unavailable.output, /no test projects own the slow lane/);
  assert.deepEqual(unavailable.filters, []);
});

test('a zero-exit test command without a completed report cannot pass the local gate', () => {
  const result = runGate('missing');

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.output, /check-local: OK/);
});

for (const mode of ['truncated', 'zero', 'aborted', 'failed']) {
  test(`a test report with ${mode} results cannot pass the local gate`, () => {
    const result = runGate(mode);

    assert.equal(result.error, undefined);
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.output, /check-local: OK/);
  });
}

test('the full gate requires a completed report from every test project', () => {
  const result = runGate('second-missing', ['--full']);

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.output, /check-local: OK/);
});

test('completed reports from both projects let the full gate pass', () => {
  const result = runGate('complete', ['--full']);

  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.output);
  assert.equal((result.output.match(/check-local: completed /g) ?? []).length, 2);
  assert.match(result.output, /check-local: OK/);
});

test('a later format failure preserves the completed report for diagnosis', () => {
  const result = runGate('format-fail');

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.equal(result.existingReports.length, 1);
  assert.doesNotMatch(result.output, /check-local: OK/);
});

test('PowerShell gate rejects a zero-exit test command without a report', { skip: !hasPowerShell }, () => {
  const result = runGate('missing', [], 'pwsh');

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.output, /check-local: OK/);
});

test('PowerShell full gate requires both project reports', { skip: !hasPowerShell }, () => {
  const result = runGate('second-missing', ['-Full'], 'pwsh');

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.output, /check-local: OK/);
});

test('PowerShell full gate accepts both completed project reports', { skip: !hasPowerShell }, () => {
  const result = runGate('complete', ['-Full'], 'pwsh');

  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.output);
  assert.equal((result.output.match(/check-local: completed /g) ?? []).length, 2);
  assert.match(result.output, /check-local: OK/);
});

test('PowerShell preserves reports when formatting fails', { skip: !hasPowerShell }, () => {
  const result = runGate('format-fail', [], 'pwsh');

  assert.equal(result.error, undefined);
  assert.notEqual(result.status, 0);
  assert.equal(result.existingReports.length, 1);
  assert.doesNotMatch(result.output, /check-local: OK/);
});
