import assert from 'node:assert/strict';
import { readFileSync, readdirSync, mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { delimiter, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const root = resolve(import.meta.dirname, '..');

// A test-only evaluator for the FQN expressions used by the maintained configs.
// Calibrate this projection against real VSTest discovery when selection changes.
function selects(filter, fqn) {
    if (!filter) return true;
    const tokens = filter.match(/\(|\)|[&|]|FullyQualifiedName(?:!~|!=|~|=)[^&|()]+/g);
    let index = 0;
    function atom() {
        const token = tokens[index++];
        if (token === '(') {
            const result = or();
            assert.equal(tokens[index++], ')');
            return result;
        }
        const match = /^FullyQualifiedName(!~|!=|~|=)(.+)$/.exec(token);
        assert.ok(match, `Unsupported filter term: ${token}`);
        const [, operator, value] = match;
        const name = fqn.toLowerCase(), expected = value.toLowerCase();
        return operator === '~' ? name.includes(expected)
            : operator === '!~' ? !name.includes(expected)
                : operator === '=' ? name === expected : name !== expected;
    }
    function and() {
        let result = atom();
        while (tokens[index] === '&') {
            index++;
            const next = atom();
            result = result && next;
        }
        return result;
    }
    function or() {
        let result = and();
        while (tokens[index] === '|') {
            index++;
            const next = and();
            result = result || next;
        }
        return result;
    }
    const result = or();
    assert.equal(index, tokens.length);
    return result;
}

test('every maintained native mutation configuration excludes both slow test groups', () => {
    const configs = readdirSync(root).filter(name => /^stryker.*config\.json$/.test(name));
    assert.ok(configs.length > 0);
    for (const name of configs) {
        const config = JSON.parse(readFileSync(resolve(root, name)))['stryker-config'];
        for (const fqn of [
            'RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.unknown_command_hints_keep_lexical_lookup',
            'RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.child_process_cleanup_crash_probe',
        ]) {
            assert.equal(selects(config['test-case-filter'], fqn), false, `${name} selects slow test ${fqn}`);
        }
    }
});

const hasPowerShell = spawnSync('pwsh', ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()']).status === 0;

test('the staged mutation gate intersects its scoped configuration with slow exclusions', { skip: !hasPowerShell || process.platform !== 'linux' }, t => {
    const scratch = mkdtempSync(join(tmpdir(), 'rt-staged-lane-'));
    t.after(() => rmSync(scratch, { recursive: true, force: true }));
    mkdirSync(join(scratch, 'src'));
    mkdirSync(join(scratch, 'bin'));
    writeFileSync(join(scratch, 'src/Owner.cs'), 'class Owner;');
    writeFileSync(join(scratch, 'stryker-config.json'), JSON.stringify({
        'stryker-config': { project: 'src/Owner.csproj', mutate: ['**/Owner.cs'],
            'test-case-filter': 'FullyQualifiedName~Wanted|FullyQualifiedName~JournaledApplyAuthorityTests' },
    }));
    writeFileSync(join(scratch, 'bin/dotnet'), `#!/usr/bin/env node
const fs = require('node:fs');
const args = process.argv.slice(2);
if (args[0] === 'tool') process.exit(0);
if (args[0] !== 'stryker') process.exit(27);
fs.copyFileSync(args[args.indexOf('--config-file') + 1], 'captured-config.json');
`, { mode: 0o755 });
    assert.equal(spawnSync('git', ['init', '-q', '-b', 'main', scratch]).status, 0);
    assert.equal(spawnSync('git', ['add', 'src/Owner.cs'], { cwd: scratch }).status, 0);
    const result = spawnSync('pwsh', ['-NoProfile', '-File', join(root, 'scripts/pre-commit-gates.ps1')], {
        cwd: scratch, env: { ...process.env, PATH: join(scratch, 'bin') + delimiter + process.env.PATH },
        encoding: 'utf8', timeout: 20_000,
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const config = JSON.parse(readFileSync(join(scratch, 'captured-config.json')))['stryker-config'];
    assert.deepEqual(config.mutate, ['**/Owner.cs']);
    assert.equal(config['test-case-filter'], '(FullyQualifiedName~Wanted|FullyQualifiedName~JournaledApplyAuthorityTests)&(FullyQualifiedName!~RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.&FullyQualifiedName!~RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.)');
});
