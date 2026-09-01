import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';

const root = process.cwd();
const contentRoot = join(root, 'site', 'content', 'docs');
const requiredPages = [
  'getting-started.md',
  'discovery.md',
  'inspecting-code.md',
  'analyzing-solutions.md',
  'refactoring-safely.md',
  'automation-and-mcp.md',
  'command-reference.md',
];

function read(relativePath) {
  return readFileSync(join(root, relativePath), 'utf8');
}

test('the documentation is authored as a complete Markdown collection', () => {
  for (const page of requiredPages) {
    const markdown = readFileSync(join(contentRoot, page), 'utf8');

    assert.match(markdown, /^---\r?\n[\s\S]*?\r?\n---\r?\n/);
    assert.match(markdown, /^title:\s+.+$/m);
    assert.match(markdown, /^description:\s+.{70,}$/m);
    assert.match(markdown, /^order:\s+\d+$/m);
    assert.match(markdown, /^section:\s+.+$/m);
  }
});

test('every required documentation source is tracked by Git', () => {
  const trackedFiles = new Set(
    execFileSync('git', ['ls-files'], { cwd: root, encoding: 'utf8' })
      .split(/\r?\n/)
      .filter(Boolean)
      .map(path => path.replaceAll('\\', '/')),
  );

  const requiredTrackedFiles = [
    ...requiredPages.map(page => `site/content/docs/${page}`),
    'site/pages/docs/[...slug].astro',
  ];

  for (const path of requiredTrackedFiles) {
    assert.ok(
      trackedFiles.has(path),
      `${path} must be committed so CI can build the site`,
    );
  }
});

test('the public examples use canonical commands from the product surface', () => {
  const homepage = read('site/pages/index.astro');
  const gettingStarted = read('site/content/docs/getting-started.md');
  const discovery = read('site/content/docs/discovery.md');

  assert.match(homepage, /rt suggest/);
  assert.match(homepage, /rt inspect describe/);
  assert.match(homepage, /rt refactor rename/);
  assert.match(gettingStarted, /rt inspect doctor/);
  assert.match(discovery, /rt dump-help --compact/);
});

test('custom-domain and deployment configuration follow the sibling-site convention', () => {
  assert.equal(read('CNAME').trim(), 'roslyn-tools.danmarshall.dev');
  assert.equal(read('site/public/CNAME').trim(), 'roslyn-tools.danmarshall.dev');

  const workflow = read('.github/workflows/publish-docs.yml');
  assert.match(workflow, /dotnet test/);
  assert.match(workflow, /npm ci/);
  assert.match(workflow, /npm test/);
  assert.match(workflow, /peaceiris\/actions-gh-pages@v4/);
  assert.match(workflow, /destination_dir:\s*\.\/docs/);
  assert.match(workflow, /force_orphan:\s*true/);
});

test('the homepage and documentation shell have deliberate responsive fallbacks', () => {
  const css = read('site/styles/global.css');

  assert.match(css, /body\s*{[^}]*min-width:\s*0;/s);
  assert.match(
    css,
    /@media \(max-width:\s*62rem\)[\s\S]*?\.hero-grid,[\s\S]*?\.boundary-grid\s*{\s*grid-template-columns:\s*1fr;/,
  );
  assert.match(
    css,
    /@media \(max-width:\s*48rem\)[\s\S]*?\.docs-sidebar\s*{[^}]*overflow-x:\s*auto;/,
  );
  assert.match(css, /\.doc-article,[\s\S]*?\.prose\s*{\s*min-width:\s*0;/);
});
