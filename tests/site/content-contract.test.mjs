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
  'ci.md',
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
    'scripts/generate-site-command-reference.mjs',
    'site/components/CommandCatalogue.astro',
    'site/components/CommandWallpaper.astro',
    'site/lib/commandCatalogue.ts',
    'site/pages/docs/[...slug].astro',
  ];

  for (const path of requiredTrackedFiles) {
    assert.ok(
      trackedFiles.has(path),
      `${path} must be committed so CI can build the site`,
    );
  }
});

test('the public examples cover CLI, MCP and CI with canonical commands', () => {
  const homepage = read('site/pages/index.astro');
  const mcp = read('site/content/docs/automation-and-mcp.md');
  const gettingStarted = read('site/content/docs/getting-started.md');
  const discovery = read('site/content/docs/discovery.md');

  assert.match(homepage, /Semantic C# tools for developers, agents, and CI/);
  assert.match(homepage, /roslyn-tools-mcp/);
  assert.match(homepage, /rt inspect describe/);
  assert.match(homepage, /rt ci check/);
  assert.match(homepage, /relationships/);
  assert.match(mcp, /title: Connect with MCP/);
  assert.match(mcp, /order: 1/);
  assert.match(mcp, /"command": "roslyn-tools-mcp"/);
  assert.match(gettingStarted, /rt inspect doctor/);
  assert.match(discovery, /rt dump-help --compact/);
});

test('the website command catalogue is derived from the generated command reference', () => {
  const component = read('site/components/CommandCatalogue.astro');
  const catalogue = read('site/lib/commandCatalogue.ts');
  const page = read('site/pages/docs/[...slug].astro');
  const packageJson = JSON.parse(read('package.json'));
  const generator = read('scripts/generate-site-command-reference.mjs');

  assert.match(component, /lib\/commandCatalogue/);
  assert.match(catalogue, /docs\/COMMANDS\.md/);
  assert.match(catalogue, /commands\.length !== declaredCount/);
  assert.match(page, /slug === 'command-reference'/);
  assert.match(page, /<CommandCatalogue \/>/);
  assert.equal(packageJson.scripts.prebuild, 'npm run generate:commands');
  assert.match(generator, /\[cliAssembly, 'dump-help'\]/);
  assert.match(generator, /renameSync\(temporaryPath, referencePath\)/);
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
  assert.match(
    css,
    /@media \(max-width:\s*48rem\)[\s\S]*?\.docs-sidebar::-webkit-scrollbar\s*{[^}]*display:\s*none;/,
  );
  assert.match(css, /\.docs-sidebar \.docs-subnav\s*{[^}]*border-left:/s);
  assert.match(
    css,
    /@media \(max-width:\s*48rem\)[\s\S]*?\.docs-sidebar \.docs-subnav\s*{[^}]*display:\s*none;/,
  );
  assert.match(css, /\.doc-article,[\s\S]*?\.prose\s*{\s*min-width:\s*0;/);
  assert.match(css, /\.prose pre\s*{[^}]*overflow-x:\s*hidden;/s);
  assert.match(css, /\.prose pre code\s*{[^}]*white-space:\s*pre-wrap;/s);
  assert.match(css, /@keyframes command-wallpaper-drift/);
  assert.match(css, /font-size:\s*var\(--wallpaper-font-size\)/);
  assert.match(css, /0%\s*{[^}]*translate3d\(100vw, 0, 0\)/s);
  assert.match(css, /7\.14%,[\s\S]*?translate3d\(-200vw, 0, 0\)/);
  assert.match(css, /animation:\s*command-wallpaper-drift var\(--wallpaper-cycle\)/);
  assert.match(
    css,
    /@media \(prefers-reduced-motion:\s*reduce\)[\s\S]*?\.command-wallpaper span\s*{[^}]*animation:\s*none;/,
  );
});
