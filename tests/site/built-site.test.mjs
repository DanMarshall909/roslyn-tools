import assert from 'node:assert/strict';
import {
  existsSync,
  readFileSync,
  readdirSync,
  statSync,
} from 'node:fs';
import { dirname, extname, join, relative, sep } from 'node:path';
import test from 'node:test';

const root = process.cwd();
const outputRoot = join(root, 'artifacts', 'site');

function findFiles(directory, name) {
  const matches = [];
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      matches.push(...findFiles(path, name));
    } else if (entry === name) {
      matches.push(path);
    }
  }
  return matches;
}

function outputPathForHref(fromFile, href) {
  const path = href.split('#')[0].split('?')[0];
  if (!path || /^(?:[a-z]+:|\/\/)/i.test(path)) return null;

  const resolved = path.startsWith('/')
    ? join(outputRoot, path)
    : join(dirname(fromFile), path);

  if (extname(resolved)) return resolved;
  return join(resolved, 'index.html');
}

test('the build emits the custom-domain files expected by GitHub Pages', () => {
  assert.equal(readFileSync(join(outputRoot, 'CNAME'), 'utf8').trim(), 'roslyn-tools.danmarshall.dev');
  assert.ok(existsSync(join(outputRoot, '.nojekyll')));
});

test('the homepage connects CLI, MCP and CI use cases to their guides', () => {
  const html = readFileSync(join(outputRoot, 'index.html'), 'utf8');

  assert.match(html, /Semantic C# tools for developers, agents, and CI/);
  for (const guide of ['getting-started', 'automation-and-mcp', 'ci']) {
    assert.match(html, new RegExp(`href="/docs/${guide}/"`));
  }
  assert.match(html, /roslyn-tools-mcp/);
  assert.match(html, /rt inspect describe/);
  assert.match(html, /rt ci check/);
  assert.match(html, /relationships/);
  assert.match(html, /https:\/\/blog\.danmarshall\.dev/);

  const reference = readFileSync(join(root, 'docs', 'COMMANDS.md'), 'utf8');
  const knownCommands = new Set(
    [...reference.matchAll(/^\| \[`([^`]+)`\]\(#[^)]+\) \|/gm)]
      .map(([, name]) => name),
  );
  const wallpaperCommands = [...html.matchAll(/data-wallpaper-command="([^"]+)"/g)]
    .map(([, name]) => name);

  assert.equal(wallpaperCommands.length, 14);
  assert.ok(wallpaperCommands.every((name) => knownCommands.has(name)));
  assert.match(html, /class="command-wallpaper" aria-hidden="true"/);
});

test('the command reference lists every generated public command', () => {
  const source = readFileSync(join(root, 'docs', 'COMMANDS.md'), 'utf8');
  const declaredCount = Number(source.match(/Total commands: \*\*(\d+)\*\*/)?.[1]);
  const expectedCommands = [...source.matchAll(/^\| \[`([^`]+)`\]\(#[^)]+\) \|/gm)]
    .map(([, name]) => name);
  const html = readFileSync(
    join(outputRoot, 'docs', 'command-reference', 'index.html'),
    'utf8',
  );
  const renderedCommands = [...html.matchAll(/data-command="([^"]+)"/g)]
    .map(([, name]) => name);

  assert.equal(expectedCommands.length, declaredCount);
  assert.equal(renderedCommands.length, declaredCount);
  assert.deepEqual(renderedCommands.sort(), expectedCommands.sort());
  assert.match(html, /class="docs-subnav"[^>]*aria-label="Command reference sections"/);
  assert.match(html, /href="#all-commands"/);
  assert.match(html, /href="#commands-inspect"/);
  assert.match(html, /href="#commands-analyze"/);
  assert.match(html, /href="#commands-refactor"/);
  assert.match(html, /href="#commands-ci"/);
  assert.match(html, /href="#commands-root"/);
  assert.match(html, /data-command="trust"/);
  assert.match(html, /data-command="check"/);
  assert.doesNotMatch(html, /data-command="prepare-operation"/);
  assert.doesNotMatch(html, /id="commands-prepare"/);
});

test('every generated page has production metadata and valid internal links', () => {
  const pages = findFiles(outputRoot, 'index.html');
  assert.ok(pages.length >= 8, `expected at least 8 pages, received ${pages.length}`);

  for (const page of pages) {
    const html = readFileSync(page, 'utf8');
    const label = relative(outputRoot, page).split(sep).join('/');

    assert.match(html, /<html[^>]+lang="en"/i, `${label} has no language`);
    assert.match(html, /<title>[^<]+<\/title>/i, `${label} has no title`);
    assert.match(html, /<meta name="description" content="[^"]{70,}"/i, `${label} has no useful description`);
    assert.match(html, /<link rel="canonical" href="https:\/\/roslyn-tools\.danmarshall\.dev\//i, `${label} has no production canonical URL`);

    for (const [, href] of html.matchAll(/href="([^"]+)"/g)) {
      const target = outputPathForHref(page, href);
      if (target) assert.ok(existsSync(target), `${label} links to missing ${href}`);
    }
  }
});
