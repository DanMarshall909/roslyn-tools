import { createHash } from 'node:crypto';
import { execFile, spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { testFilter } from './test-lanes.mjs';
import {
  lstat,
  mkdir,
  mkdtemp,
  readFile,
  readdir,
  readlink,
  rm,
  writeFile,
} from 'node:fs/promises';

const repoRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const execFileAsync = promisify(execFile);
const rootOutputDirectories = new Set([
  '.git', '.astro', 'artifacts', 'coverage', 'node_modules',
  'StrykerOutput', 'TestOutput', 'TestResults',
]);
const projectOutputDirectories = new Set([
  'bin', 'obj', 'coverage', 'StrykerOutput', 'TestOutput', 'TestResults',
]);
const siteOutputDirectories = new Set(['.astro', 'node_modules']);
const profileCacheDirectories = new Set([
  '.dotnet/corefx/cryptography/crls',
]);

async function isDeclaredOutputDirectory(root, directory, entry) {
  if (!entry.isDirectory()) return false;
  if (directory === root && rootOutputDirectories.has(entry.name)) return true;
  if (directory === join(root, 'site') && siteOutputDirectories.has(entry.name))
    return true;
  if (!projectOutputDirectories.has(entry.name)) return false;
  const siblings = await readdir(directory, { withFileTypes: true });
  return siblings.some(sibling =>
    sibling.isFile() && sibling.name.endsWith('.csproj'));
}

export async function snapshotTree(root, checkout = false) {
  const files = new Map();

  async function walk(directory) {
    const entries = await readdir(directory, { withFileTypes: true });
    for (const entry of entries) {
      if (checkout && await isDeclaredOutputDirectory(root, directory, entry))
        continue;
      const path = join(directory, entry.name);
      const name = relative(root, path).split(sep).join('/');
      if (!checkout && profileCacheDirectories.has(name)) continue;
      const state = await lstat(path, { bigint: true });
      if (state.isSymbolicLink()) {
        files.set(name, `link:${await readlink(path)}:${state.mtimeNs}:${state.ctimeNs}`);
      } else if (state.isDirectory()) {
        files.set(name, 'directory');
        await walk(path);
      } else if (state.isFile()) {
        const hash = createHash('sha256').update(await readFile(path)).digest('hex');
        files.set(name, `file:${state.mode}:${state.size}:${state.mtimeNs}:${state.ctimeNs}:${hash}`);
      } else {
        files.set(name, `other:${state.mode}:${state.mtimeNs}:${state.ctimeNs}`);
      }
    }
  }

  await walk(root);
  return files;
}

export async function inspectPhase(phase, checkoutRoot, profileRoot, action) {
  const beforeCheckout = await snapshotTree(checkoutRoot, true);
  const beforeProfile = await snapshotTree(profileRoot);
  let actionError;
  try {
    await action();
  } catch (error) {
    actionError = error;
  }
  const afterCheckout = await snapshotTree(checkoutRoot, true);
  const afterProfile = await snapshotTree(profileRoot);
  const changes = [];
  for (const [scope, before, after] of [
    ['checkout', beforeCheckout, afterCheckout],
    ['profile', beforeProfile, afterProfile],
  ]) {
    for (const name of new Set([...before.keys(), ...after.keys()])) {
      if (before.get(name) !== after.get(name)) {
        changes.push(`${phase}: ${scope}/${name} ${before.has(name) ? after.has(name) ? 'modified' : 'removed' : 'created'}`);
      }
    }
  }
  changes.sort();
  if (actionError) {
    actionError.changes = changes;
    throw actionError;
  }
  return changes;
}

export function trackedOutputPaths(paths) {
  const projects = paths.filter(path => path.endsWith('.csproj'))
    .map(path => path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '');
  return paths.filter(path => {
    const parts = path.split('/');
    if (parts.length > 1 && rootOutputDirectories.has(parts[0])) return true;
    if (parts.length > 2 && parts[0] === 'site'
        && siteOutputDirectories.has(parts[1])) return true;
    return projects.some(project => {
      const prefix = project ? `${project}/` : '';
      if (!path.startsWith(prefix)) return false;
      const remainder = path.slice(prefix.length).split('/');
      return remainder.length > 1 && projectOutputDirectories.has(remainder[0]);
    });
  });
}

async function verifyOutputExclusions() {
  const { stdout } = await execFileAsync('git', ['ls-files', '-z'], {
    cwd: repoRoot,
    encoding: 'buffer',
    maxBuffer: 16 * 1024 * 1024,
  });
  const tracked = stdout.toString('utf8').split('\0').filter(Boolean);
  const excluded = trackedOutputPaths(tracked);
  if (excluded.length > 0) {
    throw new Error(`Tracked files under excluded output directories: ${excluded.slice(0, 10).join(', ')}`);
  }
}

function reportChanges(changes) {
  for (const change of changes.slice(0, 20)) console.error(`build-purity: ${change}`);
  if (changes.length > 20) console.error(`build-purity: ${changes.length - 20} more changes`);
}

function run(command, args, environment) {
  return new Promise((accept, reject) => {
    const child = spawn(command, args, {
      cwd: repoRoot,
      env: environment,
      stdio: 'inherit',
    });
    child.on('error', reject);
    child.on('exit', (code, signal) => {
      if (code === 0) accept();
      else reject(new Error(`${command} ${args.join(' ')} exited ${code ?? signal}`));
    });
  });
}

async function main() {
  const scratch = await mkdtemp(join(tmpdir(), 'rt-build-purity-'));
  const profile = join(scratch, 'profile');
  const cache = join(scratch, 'cache');
  const temporary = join(cache, 'tmp');
  const bootstrap = join(cache, 'bootstrap', 'bootstrap.csproj');
  await mkdir(temporary, { recursive: true });
  await mkdir(dirname(bootstrap), { recursive: true });
  await writeFile(bootstrap,
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n');
  await mkdir(join(profile, '.claude', 'skills', 'use-rt'), { recursive: true });
  await writeFile(join(profile, '.claude', 'skills', 'use-rt', 'SKILL.md'), 'build-purity canary\n');
  await mkdir(join(profile, '.dotnet', 'corefx', 'cryptography', 'crls'),
    { recursive: true });
  const environment = {
    ...process.env,
    HOME: profile,
    USERPROFILE: profile,
    APPDATA: join(profile, 'AppData', 'Roaming'),
    LOCALAPPDATA: join(profile, 'AppData', 'Local'),
    XDG_CONFIG_HOME: join(profile, '.config'),
    XDG_DATA_HOME: join(profile, '.local', 'share'),
    XDG_CACHE_HOME: join(cache, 'xdg'),
    DOTNET_CLI_HOME: join(cache, 'dotnet'),
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
    NUGET_PACKAGES: join(cache, 'nuget-packages'),
    NUGET_HTTP_CACHE_PATH: join(cache, 'nuget-http'),
    NUGET_PLUGINS_CACHE_PATH: join(cache, 'nuget-plugins'),
    TMP: temporary,
    TEMP: temporary,
    TMPDIR: temporary,
  };
  const phases = [
    ['restore', ['restore', 'RoslynTools.slnx', '-v', 'minimal']],
    ['build', ['build', 'RoslynTools.slnx', '-c', 'Debug', '--no-restore', '-v', 'minimal']],
    ['test', ['test', 'RoslynTools.slnx', '-c', 'Debug', '--no-build', '-v', 'minimal', '--filter', testFilter('fast')]],
  ];
  try {
    await verifyOutputExclusions();
    // Prime .NET's one-time certificate and NuGet migration state in the
    // disposable profile before measuring this repository's commands.
    await run('dotnet', ['restore', bootstrap, '-v', 'quiet'], environment);
    for (const [phase, args] of phases) {
      const changes = await inspectPhase(phase, repoRoot, profile,
        () => run('dotnet', args, environment));
      if (changes.length > 0) {
        reportChanges(changes);
        process.exitCode = 1;
        return;
      }
      process.stdout.write(`build-purity: ${phase} clean\n`);
    }
  } finally {
    await rm(scratch, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    if (error.changes?.length > 0) reportChanges(error.changes);
    console.error(`build-purity: ${error.message}`);
    process.exitCode = 1;
  });
}
