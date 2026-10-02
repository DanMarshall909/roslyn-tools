import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const issuePath = 'docs/issues/resolved/architecture/rt-007-shared-command-descriptors.md';
const manifestPath = 'docs/command-port-migration-groups.tsv';
const hardeningPath = 'openspec/changes/critical-platform-hardening/tasks.md';
const outputPath = 'docs/command-contract-status.md';

function checkboxes(markdown, heading) {
  const lines = markdown.split(/\r?\n/);
  const matches = lines.flatMap((line, index) => line === heading ? [index] : []);
  if (matches.length !== 1) throw new Error(`Expected one authority section: ${heading}`);
  const after = lines.slice(matches[0] + 1);
  const end = after.findIndex(line => /^#{1,2} /.test(line));
  const items = [];
  for (const line of end < 0 ? after : after.slice(0, end)) {
    const item = line.match(/^\s*- \[([ xX])\] (.+)$/);
    if (item) {
      items.push({ done: item[1].toLowerCase() === 'x', text: item[2] });
    } else if (/^\s*[-*]\s+\[/.test(line)) {
      throw new Error(`Unsupported authority checkbox: ${line.trim()}`);
    } else if (/^\s{2,}\S/.test(line) && items.length) {
      items.at(-1).text += ` ${line.trim()}`;
    }
  }
  if (!items.length) throw new Error(`No authority checkboxes: ${heading}`);
  return items;
}

export function renderStatus({ issue, manifest, hardening }) {
  const archive = issue.match(/^resolved: (\d{4}-\d{2}-\d{2})$/m)?.[1];
  if (!archive) throw new Error('RT-007 archive date is missing');
  const accepted = checkboxes(issue, '## Acceptance Criteria');
  const spine = checkboxes(hardening, '## P1.1 five-command canonical spine');
  const lines = manifest.trim().split(/\r?\n/);
  if (lines.shift() !== 'command\tcategory\tgroup\twork\texecution\tsafety')
    throw new Error('Unexpected migration manifest header');
  const names = new Set();
  const categories = new Map();
  for (const line of lines) {
    const values = line.split('\t');
    if (values.length !== 6 || values.some(value => !value))
      throw new Error('Incomplete migration manifest row');
    const [name, category, , work] = values;
    if (!['inspect', 'analyze', 'refactor', 'ci', 'control'].includes(category))
      throw new Error(`Unknown migration category: ${category}`);
    if (names.has(name)) throw new Error(`Duplicate migration command: ${name}`);
    if (!['complete', 'reconcile', 'convert'].includes(work))
      throw new Error(`Unknown migration work state: ${work}`);
    names.add(name);
    const counts = categories.get(category) ?? { complete: 0, reconcile: 0, convert: 0 };
    counts[work]++;
    categories.set(category, counts);
  }
  if (!names.size) throw new Error('Empty migration manifest');
  const table = [...categories].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    .map(([name, counts]) => `| ${name} | ${counts.complete + counts.reconcile + counts.convert} | ${counts.complete} | ${counts.reconcile} | ${counts.convert} |`);
  return [
    '# Command contract status',
    '',
    '<!-- GENERATED — node scripts/generate-command-contract-status.mjs; do not edit by hand. -->',
    '',
    'This view reads authored authority records. It is not a fresh runtime gate,',
    'live GitHub ticket status, or preview admission verdict.',
    '',
    '## Typed-port migration record',
    '',
    `[RT-007](issues/resolved/architecture/rt-007-shared-command-descriptors.md#acceptance-criteria) records its archive date as ${archive} and ${accepted.filter(item => item.done).length}/${accepted.length} acceptance criteria checked.`,
    '',
    `The [migration manifest](command-port-migration-groups.tsv) records ${names.size} category/control command rows:`,
    '',
    '| Category | Total | Complete | Reconcile | Convert |',
    '| --- | ---: | ---: | ---: | ---: |',
    ...table,
    '',
    'These are manifest work states, not supported-preview counts. RT-007 owns the',
    'archived typed-port and host-decoupling evidence, including bridge deletion.',
    '',
    '## Canonical-contract admission record',
    '',
    `[Platform hardening P1.1](../${hardeningPath}#p11-five-command-canonical-spine) records ${spine.filter(item => item.done).length}/${spine.length} tasks checked:`,
    '',
    ...spine.map(item => `- [${item.done ? 'x' : ' '}] ${item.text}`),
    '',
    'Typed dispatch and bridge deletion do not prove the broader request/result,',
    'provenance, safety, cancellation, completeness, resource, and host-parity',
    'admission matrix. An unchecked P1.1 bridge task is a broader admission record;',
    'it is not evidence that the archived CLI bridge still exists. Its owner must',
    'reconcile that task with fresh canonical-contract evidence before closing it.',
    '',
    '## Evidence owners',
    '',
    '- [Descriptor contracts](../tests/RoslynTools.Tests.Mcp/Architecture/CommandDescriptorContracts.cs): discoverable typed ports, bridge absence, generic dispatch, and schemas.',
    '- [Host dependency ratchets](../tests/RoslynTools.Tests.Mcp/Architecture/HostDependencyRatchetTests.cs): source/assembly boundaries and shared typed contracts.',
    '- [Manifest contracts](../tests/RoslynTools.Tests/Architecture/CommandMigrationManifestContracts.cs): command inventory and discovered-port agreement.',
    '- [Product-spine tasks](../openspec/changes/shape-pre-release-product-spine/tasks.md): current product shaping and preview admission.',
    '- [RT-016](issues/open/architecture/rt-016-pre-release-product-spine.md): execution record and links to the owning OpenSpec.',
    '',
    'The linked test files describe verification owners; this generator does not',
    'execute them. Regenerate after an authority changes; check for stale output',
    'with `node scripts/generate-command-contract-status.mjs --check`.',
    '',
  ].join('\n');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    if (args.some(arg => arg !== '--check') || args.length > 1)
      throw new Error('Usage: node scripts/generate-command-contract-status.mjs [--check]');
    const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
    const read = path => readFileSync(resolve(root, path), 'utf8');
    const output = renderStatus({ issue: read(issuePath), manifest: read(manifestPath), hardening: read(hardeningPath) });
    if (args.includes('--check')) {
      if (read(outputPath) !== output) throw new Error('Command contract status is stale; regenerate it');
    } else {
      writeFileSync(resolve(root, outputPath), output);
    }
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}
