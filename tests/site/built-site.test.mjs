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

test('the homepage presents the semantic CLI and shared host model', () => {
  const html = readFileSync(join(outputRoot, 'index.html'), 'utf8');

  assert.match(html, /Work with the code the compiler sees/);
  assert.match(html, /rt suggest/);
  assert.match(html, /rt inspect describe/);
  assert.match(html, /CLI and MCP/);
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
