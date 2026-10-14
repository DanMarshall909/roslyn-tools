import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmod, copyFile, mkdtemp, mkdir, rm, utimes, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { inspectPhase, trackedOutputPaths } from '../scripts/check-build-purity.mjs';

async function fixture(action) {
  const root = await mkdtemp(join(tmpdir(), 'rt-build-purity-test-'));
  const checkout = join(root, 'checkout');
  const profile = join(root, 'profile');
  await mkdir(join(checkout, 'docs'), { recursive: true });
  await mkdir(profile);
  try {
    await action({ checkout, profile });
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

test('a same-content rewrite of an ignored command reference fails its phase', async () => {
  await fixture(async ({ checkout, profile }) => {
    const reference = join(checkout, 'docs', 'COMMANDS.md');
    await writeFile(reference, 'unchanged\n');
    const changes = await inspectPhase('build', checkout, profile, async () => {
      await writeFile(reference, 'unchanged\n');
      await utimes(reference, new Date(0), new Date(0));
    });
    assert.deepEqual(changes, ['build: checkout/docs/COMMANDS.md modified']);
  });
});

test('a user-profile skill write fails its phase', async () => {
  await fixture(async ({ checkout, profile }) => {
    const skill = join(profile, '.claude', 'skills', 'use-rt', 'SKILL.md');
    await mkdir(join(profile, '.claude', 'skills', 'use-rt'), { recursive: true });
    await writeFile(skill, 'canary\n');
    const changes = await inspectPhase('test', checkout, profile,
      () => writeFile(skill, 'deployed\n'));
    assert.deepEqual(changes, ['test: profile/.claude/skills/use-rt/SKILL.md modified']);
  });
});

test('only the SDK revocation-list cache is exempt within the profile', async () => {
  await fixture(async ({ checkout, profile }) => {
    const crls = join(profile, '.dotnet', 'corefx', 'cryptography', 'crls');
    await mkdir(crls, { recursive: true });
    const changes = await inspectPhase('restore', checkout, profile, async () => {
      await writeFile(join(crls, 'issuer.crl'), 'cached\n');
      await writeFile(join(profile, '.dotnet', 'corefx', 'cryptography', 'unexpected'),
        'new file\n');
    });
    assert.deepEqual(changes,
      ['restore: profile/.dotnet/corefx/cryptography/unexpected created']);
  });
});

test('intermediate obj output is allowed while authored files stay unchanged', async () => {
  await fixture(async ({ checkout, profile }) => {
    await mkdir(join(checkout, 'src', 'Project'), { recursive: true });
    await writeFile(join(checkout, 'src', 'Project', 'Project.csproj'), '<Project />\n');
    const changes = await inspectPhase('build', checkout, profile, async () => {
      const output = join(checkout, 'src', 'Project', 'obj');
      await mkdir(output, { recursive: true });
      await writeFile(join(output, 'assets.json'), '{}');
    });
    assert.deepEqual(changes, []);
  });
});

test('an unrelated bin or artifacts directory remains monitored', async () => {
  await fixture(async ({ checkout, profile }) => {
    await mkdir(join(checkout, 'docs', 'bin'), { recursive: true });
    await mkdir(join(checkout, 'docs', 'artifacts'), { recursive: true });
    const changes = await inspectPhase('build', checkout, profile, async () => {
      await writeFile(join(checkout, 'docs', 'bin', 'readme.md'), 'authored\n');
      await writeFile(join(checkout, 'docs', 'artifacts', 'notes.md'), 'authored\n');
    });
    assert.deepEqual(changes, [
      'build: checkout/docs/artifacts/notes.md created',
      'build: checkout/docs/bin/readme.md created',
    ]);
  });
});

test('a failed phase still reports the checkout writes it made', async () => {
  await fixture(async ({ checkout, profile }) => {
    await assert.rejects(
      inspectPhase('restore', checkout, profile, async () => {
        await writeFile(join(checkout, 'docs', 'COMMANDS.md'), 'written before failure\n');
        throw new Error('restore failed');
      }),
      error => {
        assert.equal(error.message, 'restore failed');
        assert.deepEqual(error.changes, ['restore: checkout/docs/COMMANDS.md created']);
        return true;
      });
  });
});

test('tracked files beneath exempt output directories invalidate the exemption', () => {
  assert.deepEqual(trackedOutputPaths([
    'Root.csproj',
    'bin/valuable.txt',
    'src/Project/Project.csproj',
    'src/Project/obj/important.cs',
    'artifacts/site/index.html',
    'docs/artifacts/notes.md',
    'src/Project/Program.cs',
  ]), [
    'bin/valuable.txt',
    'src/Project/obj/important.cs',
    'artifacts/site/index.html',
  ]);
});

test('the executable exits nonzero and names writes made before a failed command',
  { skip: process.platform === 'win32' }, async () => {
    await fixture(async ({ checkout }) => {
      const scripts = join(checkout, 'scripts');
      const fakeBin = join(checkout, 'fake-bin');
      await mkdir(scripts);
      await mkdir(fakeBin);
      await copyFile(fileURLToPath(new URL('../scripts/check-build-purity.mjs', import.meta.url)),
        join(scripts, 'check-build-purity.mjs'));
      await copyFile(fileURLToPath(new URL('../scripts/test-lanes.mjs', import.meta.url)),
        join(scripts, 'test-lanes.mjs'));
      const fakeDotnet = join(fakeBin, 'dotnet');
      await writeFile(fakeDotnet, `#!/usr/bin/env node
const { writeFileSync } = require('node:fs');
if (process.argv[3]?.endsWith('bootstrap.csproj')) process.exit(0);
writeFileSync('docs/COMMANDS.md', 'changed\\n');
process.exit(17);
`);
      await chmod(fakeDotnet, 0o755);
      const init = spawnSync('git', ['init', '-q', checkout], { encoding: 'utf8' });
      assert.equal(init.status, 0, init.stderr);
      const result = spawnSync(process.execPath,
        [join(scripts, 'check-build-purity.mjs')], {
          cwd: checkout,
          env: { ...process.env, PATH: `${fakeBin}${delimiter}${process.env.PATH}` },
          encoding: 'utf8',
          timeout: 10_000,
        });
      assert.equal(result.status, 1, result.stderr);
      assert.match(result.stderr, /restore: checkout\/docs\/COMMANDS\.md created/);
      assert.match(result.stderr, /exited 17/);
    });
  });

test('the routine purity test phase excludes the ticket-only slow groups',
  { skip: process.platform === 'win32' }, async () => {
    await fixture(async ({ checkout }) => {
      const scripts = join(checkout, 'scripts');
      const fakeBin = join(checkout, 'fake-bin');
      await mkdir(scripts);
      await mkdir(fakeBin);
      for (const name of ['check-build-purity.mjs', 'test-lanes.mjs']) {
        await copyFile(fileURLToPath(new URL(`../scripts/${name}`, import.meta.url)),
          join(scripts, name));
      }
      const fakeDotnet = join(fakeBin, 'dotnet');
      await writeFile(fakeDotnet, `#!/usr/bin/env node
const args = process.argv.slice(2);
if (args[0] === 'test') {
  const filter = args[args.indexOf('--filter') + 1];
  if (filter !== 'FullyQualifiedName!~RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests.&FullyQualifiedName!~RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests.') {
    console.error('routine purity selected the slow lane');
    process.exit(47);
  }
}
`);
      await chmod(fakeDotnet, 0o755);
      assert.equal(spawnSync('git', ['init', '-q', checkout]).status, 0);
      const result = spawnSync(process.execPath,
        [join(scripts, 'check-build-purity.mjs')], {
          cwd: checkout,
          env: { ...process.env, PATH: `${fakeBin}${delimiter}${process.env.PATH}` },
          encoding: 'utf8', timeout: 10_000,
        });
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /^build-purity: test clean$/m);
    });
  });
