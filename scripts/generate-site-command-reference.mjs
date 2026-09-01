import { spawnSync } from 'node:child_process';
import { renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const cliProject = join(root, 'src', 'RoslynTools.Cli', 'RoslynTools.Cli.csproj');
const cliAssembly = join(
  root,
  'src',
  'RoslynTools.Cli',
  'bin',
  'Release',
  'net10.0',
  'rt.dll',
);
const referencePath = join(root, 'docs', 'COMMANDS.md');
const temporaryPath = join(root, 'docs', '.COMMANDS.md.tmp');

const build = spawnSync(
  'dotnet',
  [
    'build',
    cliProject,
    '--configuration',
    'Release',
    '--nologo',
    '--verbosity',
    'quiet',
    '-p:GenerateCommandsMd=false',
    '-p:FormatChanged=false',
    '-p:DeployRtSkill=false',
  ],
  { cwd: root, stdio: 'inherit' },
);

if (build.error) throw build.error;
if (build.status !== 0) process.exit(build.status ?? 1);

const dump = spawnSync('dotnet', [cliAssembly, 'dump-help'], {
  cwd: root,
  encoding: 'utf8',
});

if (dump.error) throw dump.error;
if (dump.status !== 0) {
  process.stderr.write(dump.stderr);
  process.exit(dump.status ?? 1);
}

if (!dump.stdout.startsWith('# rt Commands\n')) {
  throw new Error('rt dump-help did not return the generated Markdown command reference.');
}

try {
  writeFileSync(temporaryPath, dump.stdout, 'utf8');
  renameSync(temporaryPath, referencePath);
} finally {
  rmSync(temporaryPath, { force: true });
}

const count = dump.stdout.match(/Total commands: \*\*(\d+)\*\*/)?.[1] ?? 'unknown';
process.stdout.write(`Generated docs/COMMANDS.md with ${count} commands.\n`);
