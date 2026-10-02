import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { renderStatus } from '../scripts/generate-command-contract-status.mjs';

const fixture = {
  issue: '---\nresolved: 2026-09-29\n---\n\n## Acceptance Criteria\n\n- [x] Typed ports\n- [x] Bridge deleted\n\n## Archive decision\n\nHistorical gate evidence.\n',
  manifest: 'command\tcategory\tgroup\twork\texecution\tsafety\noutline\tinspect\tR0\tcomplete\tdaemon-preferred\tread-only\nrename\trefactor\tM0\treconcile\tin-process\treview-default\ncheck\tci\tR2\tconvert\tin-process\tread-only\n',
  hardening: '## P1.1 five-command canonical spine\n\n- [ ] Prove cancellation and partial parity.\n- [ ] Delete compatibility paths after admission.\n\n## P1.2 correctness\n\n- [x] An unrelated task.\n',
};

test('archived migration and incomplete canonical admission remain distinct', () => {
  const result = renderStatus(fixture);
  assert.match(result, /archive date as 2026-09-29 and 2\/2 acceptance criteria checked/);
  assert.match(result, /records 3 category\/control command rows/);
  assert.match(result, /\| inspect \| 1 \| 1 \| 0 \| 0 \|/);
  assert.match(result, /\| refactor \| 1 \| 0 \| 1 \| 0 \|/);
  assert.match(result, /\| ci \| 1 \| 0 \| 0 \| 1 \|/);
  assert.match(result, /records 0\/2 tasks checked/);
  assert.match(result, /- \[ \] Prove cancellation and partial parity\./);
  assert.doesNotMatch(result, /An unrelated task/);
  assert.match(result, /not a fresh runtime gate/);
});

test('authority changes update status without a second maintained count', () => {
  const changed = { ...fixture, hardening: fixture.hardening.replace('[ ] Prove', '[x] Prove') };
  assert.match(renderStatus(changed), /records 1\/2 tasks checked/);
  assert.equal(renderStatus(fixture), renderStatus({ ...fixture, manifest: fixture.manifest.trim().split('\n').slice(0, 1).concat(fixture.manifest.trim().split('\n').slice(1).reverse()).join('\n') }));
});

test('missing, empty, duplicate and unknown authority inputs fail visibly', () => {
  assert.throws(() => renderStatus({ ...fixture, issue: fixture.issue.replace('## Acceptance Criteria', '## Retired heading') }), /Expected one authority section/);
  assert.throws(() => renderStatus({ ...fixture, hardening: '## P1.1 five-command canonical spine\n' }), /No authority checkboxes/);
  assert.throws(() => renderStatus({ ...fixture, issue: fixture.issue.replace('resolved:', 'reported:') }), /archive date is missing/);
  assert.throws(() => renderStatus({ ...fixture, manifest: fixture.manifest + fixture.manifest.split('\n')[1] + '\n' }), /Duplicate migration command/);
  assert.throws(() => renderStatus({ ...fixture, manifest: fixture.manifest.replace('\treconcile\t', '\tunknown\t') }), /Unknown migration work state/);
  assert.throws(() => renderStatus({ ...fixture, manifest: fixture.manifest.split('\n')[0] + '\n' }), /Empty migration manifest/);
});

test('wrapped authority criteria retain their qualifying clauses', () => {
  const changed = { ...fixture, hardening: fixture.hardening.replace('Prove cancellation and partial parity.', 'Prove host parity,\n  including cancellation and partial workspaces.') };
  assert.match(renderStatus(changed), /Prove host parity, including cancellation and partial workspaces\./);
});

test('uppercase completion marks remain counted and visible', () => {
  const changed = { ...fixture, hardening: fixture.hardening.replace('[ ] Prove', '[X] Prove') };
  assert.match(renderStatus(changed), /records 1\/2 tasks checked/);
  assert.match(renderStatus(changed), /- \[x\] Prove cancellation and partial parity\./);
});

test('unknown categories cannot corrupt the generated table', () => {
  assert.throws(() => renderStatus({ ...fixture, manifest: fixture.manifest.replace('\tinspect\t', '\tinspect | malformed\t') }), /Unknown migration category/);
});


test('the CLI rejects stale generated output without rewriting it', () => {
  const root = mkdtempSync(join(tmpdir(), 'rt-contract-status-'));
  try {
    const script = join(root, 'scripts/generate-command-contract-status.mjs');
    const files = {
      'docs/issues/resolved/architecture/rt-007-shared-command-descriptors.md': fixture.issue,
      'docs/command-port-migration-groups.tsv': fixture.manifest,
      'openspec/changes/critical-platform-hardening/tasks.md': fixture.hardening,
    };
    mkdirSync(dirname(script), { recursive: true });
    copyFileSync(new URL('../scripts/generate-command-contract-status.mjs', import.meta.url), script);
    for (const [path, text] of Object.entries(files)) {
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), text);
    }
    const run = (...args) => spawnSync(process.execPath, [script, ...args], { cwd: root, encoding: 'utf8' });
    assert.equal(run().status, 0);
    const output = join(root, 'docs/command-contract-status.md');
    const good = readFileSync(output, 'utf8');
    assert.match(good, /records 0\/2 tasks checked/);
    assert.equal(run('--check').status, 0);
    const stale = good + '\nstale authority snapshot\n';
    writeFileSync(output, stale);
    const rejected = run('--check');
    assert.equal(rejected.status, 1);
    assert.match(rejected.stderr, /status is stale/);
    assert.equal(readFileSync(output, 'utf8'), stale);
    assert.equal(run().status, 0);
    assert.equal(readFileSync(output, 'utf8'), good);
    assert.equal(run('--check').status, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
