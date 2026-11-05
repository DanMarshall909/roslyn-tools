import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, delimiter, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const root = resolve(import.meta.dirname, '..');
const psProbe = spawnSync('pwsh', ['-NoProfile', '-Command', '(Get-Process -Id $PID).Path'], { encoding: 'utf8', timeout: 5000 });
const pwsh = psProbe.status === 0 ? psProbe.stdout.trim() : null;

function invoke(t, command, args) {
    const temporary = mkdtempSync(join(tmpdir(), 'rt-mutation-disabled-'));
    t.after(() => rmSync(temporary, { recursive: true, force: true }));
    const marker = join(temporary, 'external-tool-called');
    for (const name of ['dotnet', 'pwsh', 'powershell.exe']) {
        writeFileSync(join(temporary, name), '#!/bin/sh\ntouch "$RT_DISABLED_MARKER"\nexit 93\n', { mode: 0o755 });
    }
    const result = spawnSync(command, args, {
        cwd: root,
        env: { ...process.env, PATH: temporary + delimiter + process.env.PATH, RT_DISABLED_MARKER: marker },
        encoding: 'utf8', timeout: 10000,
    });
    assert.equal(result.error, undefined);
    return { ...result, externalToolCalled: existsSync(marker) };
}

for (const args of [[], ['--full'], ['--full', '--update-baseline']]) {
    test(`mutation supervisor rejects disabled execution ${args.join(' ')}`, t => {
        const result = invoke(t, process.execPath, ['scripts/mutation.mjs', ...args]);
        assert.equal(result.status, 2);
        assert.equal(JSON.parse(result.stdout.trim().split('\n').at(-1)).code, 'mutation_disabled');
        assert.equal(result.externalToolCalled, false);
    });
}

test('explicit local mutation gate fails before build or native tooling', { skip: process.platform === 'win32' }, t => {
    const result = invoke(t, 'bash', ['scripts/check-local.sh', '--mutation']);
    assert.equal(result.status, 2);
    assert.match(result.stderr, /mutation testing is disabled/i);
    assert.equal(result.externalToolCalled, false);
});

test('commit hook explicitly skips disabled mutation without requiring PowerShell', { skip: process.platform === 'win32' }, t => {
    const result = invoke(t, 'sh', ['scripts/git-hooks/pre-commit']);
    assert.equal(result.status, 0);
    assert.match(result.stderr, /mutation testing is disabled/i);
    assert.equal(result.externalToolCalled, false);
});

for (const [script, args, expected] of [
    ['scripts/check-local.ps1', ['-Mutation'], 2],
    ['scripts/pre-commit-gates.ps1', [], 0],
]) {
    test(`PowerShell disabled policy stops native work: ${script}`, { skip: !pwsh }, t => {
        const result = invoke(t, pwsh, ['-NoProfile', '-File', script, ...args]);
        assert.equal(result.status, expected);
        assert.match(result.stderr, /mutation testing is disabled/i);
        assert.equal(result.externalToolCalled, false);
    });
}
