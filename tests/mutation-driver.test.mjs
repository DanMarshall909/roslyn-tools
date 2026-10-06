import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, appendFileSync, rmSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync, spawn } from 'node:child_process';
import { test } from 'node:test';

const driver = resolve(import.meta.dirname, '../scripts/mutation.mjs');
const linux = process.platform === 'linux';
const mock = `#!/usr/bin/env node
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const a=process.argv.slice(2),r=process.cwd(),s=JSON.parse(fs.readFileSync(path.join(r,'native-settings.json')));
if(a[0]==='--version'){console.log(s.sdk||'10.0.100');process.exit(0)}
if(a[0]==='list'){console.log(JSON.stringify({version:1,projects:[{path:path.join(r,'src/Owner.csproj'),frameworks:[{framework:'net10.0',topLevelPackages:[{id:'Adapter',resolvedVersion:s.dependency||'1.0.0'}]}]}]}));process.exit(0)}
if(a[0]!=='stryker')process.exit(20);
fs.appendFileSync(path.join(r,'native-calls.txt'),'run\\n');
if(a.some(x=>x.startsWith('--since')||x.startsWith('--with-baseline')))process.exit(21);
console.log('Version: '+(s.tool||'4.14.1'));
if(s.mode==='wait'){
 const worker=cp.spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{stdio:'ignore'});
 fs.writeFileSync(path.join(r,'worker.pid'),String(worker.pid));
 fs.writeFileSync(path.join(r,'native-started'),'yes');
 console.log('diagnostics before cancellation');
 process.on('SIGINT',()=>{worker.kill('SIGINT');worker.on('exit',()=>process.exit(130));});
 setInterval(()=>{},1000);
}else{
 const out=a[a.indexOf('--output')+1];fs.mkdirSync(path.join(out,'reports'),{recursive:true});
 if(s.mode==='change')fs.appendFileSync(path.join(r,'src/Owner.cs'),'// changed during native run');
 const mutants=s.mode==='empty'?[{id:'1',status:'Ignored'}]:[
 {id:'1',status:'Killed',mutatorName:'Boolean',replacement:'false',location:{start:{line:1,column:1},end:{line:1,column:5}},coveredBy:[],killedBy:['t1']},
 {id:'2',status:'Survived',mutatorName:'String',replacement:'""',location:{start:{line:2,column:1},end:{line:2,column:5}},coveredBy:[]}];
 const report={schemaVersion:'2',files:{'src/Owner.cs':{source:'true;\\n"text";',mutants}},testFiles:{'tests/OwnerTests.cs':{source:'// contract',tests:[{id:'t1',name:'owned behavior',location:{start:{line:1,column:1},end:{line:1,column:12}}}]}}};
 if(s.mode==='unattributed')delete mutants[0].killedBy;
 fs.writeFileSync(path.join(out,'reports/mutation-report.json'),s.mode==='malformed'?'broken':JSON.stringify(report));
 console.log('[10:00:02 INF] '+(s.mode==='empty'?0:2)+' total mutants will be tested');
 if(s.mode==='progress'){
  console.log('            // coverage: unreachable guard');
  console.log('[10:00:02 DBG] Initial test run output: .');
  console.log('var note = \"999 total mutants will be tested\";');
  console.log('[10:00:02 DBG] 999 total mutants will be tested');
  console.log('[10:00:03 INF] Number of tests found: 1 for project Owner.csproj. Initial test run started.');
  console.log("[10:00:04 INF] Capture mutant coverage using 'CoverageBasedTest' mode.");
  console.log('[10:00:05 INF] 2 total mutants will be tested');
  console.log('[10:00:05 INF] Source note: 999 total mutants will be tested');
  console.log('[10:00:05 INF] 999 total mutants will be tested in this source example');
  console.log('[10:00:06 INF] Time Elapsed 00:00:04.0000000');
 }
 console.log('All mutants have been tested');
 if(s.mode==='linger'){
  const worker=cp.spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{stdio:['ignore','inherit','inherit']});
  fs.writeFileSync(path.join(r,'worker.pid'),String(worker.pid));
 }
 process.exit(s.mode==='failure'?9:0);
}
`;

function fixture(t) {
    const root = mkdtempSync(join(tmpdir(), 'rt-mutation-contract-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    for (const dir of ['src', 'tests', '.config', 'tools']) mkdirSync(join(root, dir));
    writeFileSync(join(root, 'src/Owner.cs'), 'true;\n"text";');
    writeFileSync(join(root, 'src/Owner.csproj'), '<Project />');
    writeFileSync(join(root, 'tests/OwnerTests.cs'), '// contract');
    writeFileSync(join(root, 'RoslynTools.slnx'), '<Solution />');
    writeFileSync(join(root, '.gitignore'), 'artifacts/\ncache/\ntools/\nnative-*\nworker.pid\n');
    writeFileSync(join(root, '.config/dotnet-tools.json'), JSON.stringify({ tools: { 'dotnet-stryker': { version: '4.14.1', commands: ['dotnet-stryker'] } } }));
    const cfg = { 'stryker-config': { project: 'src/Owner.csproj', solution: 'RoslynTools.slnx', mutate: ['**/Owner.cs'], concurrency: 4, thresholds: { high: 100, low: 95, break: 0 }, reporters: ['json'] } };
    writeFileSync(join(root, 'stryker-config.json'), JSON.stringify(cfg));
    writeFileSync(join(root, 'other-config.json'), JSON.stringify(cfg));
    writeFileSync(join(root, 'tools/dotnet'), mock, { mode: 0o755 });
    writeFileSync(join(root, 'native-settings.json'), '{}');
    for (const args of [['init', '-q', '-b', 'main'], ['add', '.'], ['-c', 'user.name=Tests', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'fixture']]) {
        assert.equal(spawnSync('git', args, { cwd: root }).status, 0);
    }
    const env = { ...process.env, PATH: join(root, 'tools') + ':' + process.env.PATH, RT_MUTATION_CACHE_ROOT: join(root, 'cache') };
    function call(args = []) {
        const r = spawnSync(process.execPath, [driver, ...args], { cwd: root, env, encoding: 'utf8', timeout: 20000 });
        assert.equal(r.error, undefined, String(r.error));
        const lines = r.stdout.trim().split('\n').filter(Boolean);
        let data;
        try { data = JSON.parse(lines.at(-1)); } catch { assert.fail(`Expected compact JSON: ${r.stdout} ${r.stderr}`); }
        return { exit: r.status, data };
    }
    return { root, env, call, settings: value => writeFileSync(join(root, 'native-settings.json'), JSON.stringify(value)), nativeCalls: () => existsSync(join(root, 'native-calls.txt')) ? readFileSync(join(root, 'native-calls.txt'), 'utf8').trim().split('\n').length : 0 };
}

test('missing state and invalid seed requests fail before native execution', { skip: !linux }, t => {
    const f = fixture(t);
    const missing = f.call();
    assert.equal(missing.exit, 2);
    assert.equal(missing.data.code, 'baseline_missing');
    assert.match(missing.data.recovery, /--full --update-baseline/);
    assert.equal(f.call(['--update-baseline']).data.code, 'seed_requires_full');
    assert.equal(f.nativeCalls(), 0);
});

test('a full seed preserves native outcomes and identical state launches no native process', { skip: !linux }, t => {
    const f = fixture(t), full = f.call(['--full', '--update-baseline']);
    assert.equal(full.exit, 0);
    assert.equal(full.data.mode, 'full');
    assert.equal(full.data.eligibleMutants, 2);
    assert.deepEqual(full.data.counts, { Killed: 1, Survived: 1 });
    assert.ok(existsSync(full.data.receipt));
    assert.ok(existsSync(full.data.findings));
    const fast = f.call();
    assert.equal(fast.exit, 0);
    assert.equal(fast.data.mode, 'reused');
    assert.equal(fast.data.nativeExecuted, false);
    assert.deepEqual(fast.data.counts, full.data.counts);
    assert.equal(f.nativeCalls(), 1);
    assert.equal(f.call(['--show-baseline']).data.compatible, true);
    rmSync(join(f.root, 'artifacts'), { recursive: true });
    const durable = f.call();
    assert.equal(durable.exit, 0);
    assert.ok(existsSync(durable.data.receipt));
    assert.equal(JSON.parse(readFileSync(durable.data.receipt)).verification.complete, true);
});

test('progress metadata accepts INFO records and leaves source comments and DEBUG output in the raw log', { skip: !linux }, t => {
    const f = fixture(t); f.settings({ mode: 'progress' });
    const full = f.call(['--full']);
    const receipt = JSON.parse(readFileSync(full.data.receipt, 'utf8'));
    assert.equal(receipt.scheduledMutants, 2, 'Source examples must not overwrite the scheduled count.');
    assert.equal(full.exit, 0);
    assert.deepEqual(receipt.milestones.map(x => x.message), [
        '[10:00:02 INF] 2 total mutants will be tested',
        '[10:00:03 INF] Number of tests found: 1 for project Owner.csproj. Initial test run started.',
        "[10:00:04 INF] Capture mutant coverage using 'CoverageBasedTest' mode.",
        '[10:00:05 INF] 2 total mutants will be tested',
        '[10:00:06 INF] Time Elapsed 00:00:04.0000000'
    ]);
    const raw = readFileSync(receipt.log, 'utf8');
    assert.match(raw, /coverage: unreachable guard/);
    assert.match(raw, /DBG\] Initial test run output/);
    assert.match(raw, /999 total mutants will be tested/);
    assert.match(raw, /INF\] Source note: 999 total mutants will be tested/);
    assert.match(raw, /999 total mutants will be tested in this source example/);
});

test('source, test, shared fixture, configuration, tool, SDK and dependency changes invalidate reuse', { skip: !linux }, t => {
    const f = fixture(t);
    assert.equal(f.call(['--full', '--update-baseline']).exit, 0);
    const changes = [
        ['src/Owner.cs', '// new behavior'], ['tests/OwnerTests.cs', '// weakened assertion'],
        ['tests/shared-fixture.json', '{"mode":"different"}'],
        ['stryker-config.json', ' '], ['.config/dotnet-tools.json', ' ']
    ];
    for (const [path, change] of changes) {
        const p = join(f.root, path), old = existsSync(p) ? readFileSync(p) : null;
        appendFileSync(p, change);
        assert.equal(f.call().data.code, 'baseline_incompatible', path);
        if (old) writeFileSync(p, old); else rmSync(p);
    }
    for (const value of [{ sdk: '10.0.101' }, { dependency: '2.0.0' }]) {
        f.settings(value);
        assert.equal(f.call().data.code, 'baseline_incompatible');
    }
    f.settings({});
    for (const [path, edit] of [
        ['stryker-config.json', x => x['stryker-config'].concurrency = 8],
        ['.config/dotnet-tools.json', x => x.tools['dotnet-stryker'].version = '4.14.2']
    ]) {
        const p = join(f.root, path), old = readFileSync(p, 'utf8'), changed = JSON.parse(old);
        edit(changed); writeFileSync(p, JSON.stringify(changed));
        assert.equal(f.call().data.code, 'baseline_incompatible', path);
        writeFileSync(p, old);
    }
    assert.equal(f.call().exit, 0);
    assert.equal(f.nativeCalls(), 1);
});

test('corrupt evidence and an unreachable anchor fail honestly', { skip: !linux }, t => {
    const f = fixture(t), seeded = f.call(['--full', '--update-baseline']);
    const path = seeded.data.baseline, original = readFileSync(path, 'utf8');
    const state = JSON.parse(original);
    state.reportRaw = 'broken'; writeFileSync(path, JSON.stringify(state));
    assert.equal(f.call().data.code, 'baseline_corrupt');
    writeFileSync(path, original);
    for (const alter of [x => delete x.findings, x => x.findings.findings[0].replacement = 'tampered']) {
        const changed = JSON.parse(original);
        alter(changed);
        writeFileSync(path, JSON.stringify(changed));
        assert.equal(f.call().data.code, 'baseline_corrupt');
    }
    writeFileSync(path, original);
    state.reportRaw = JSON.parse(original).reportRaw; state.commit = '0'.repeat(40); state.verification.commit = state.commit;
    writeFileSync(path, JSON.stringify(state));
    assert.equal(f.call().data.code, 'baseline_unreachable');
    assert.equal(f.nativeCalls(), 1);
});

test('an explicitly selected ignored configuration is bound to verified state', { skip: !linux }, t => {
    const f = fixture(t), config = 'artifacts/frozen-config.json';
    mkdirSync(join(f.root, 'artifacts'));
    const path = join(f.root, config), original = readFileSync(join(f.root, 'stryker-config.json'), 'utf8');
    writeFileSync(path, original);
    assert.equal(f.call(['--config', config, '--full', '--update-baseline']).exit, 0);
    const changed = JSON.parse(original);
    changed['stryker-config'].mutate = ['**/Other.cs'];
    writeFileSync(path, JSON.stringify(changed));
    assert.equal(f.call(['--config', config]).data.code, 'baseline_incompatible');
    writeFileSync(path, original);
    assert.equal(f.call(['--config', config]).exit, 0);
    assert.equal(f.nativeCalls(), 1);
});

test('the repository solution default and inherited behavior environment are bound', { skip: !linux }, t => {
    const f = fixture(t), p = join(f.root, 'stryker-config.json'), cfg = JSON.parse(readFileSync(p));
    delete cfg['stryker-config'].solution; writeFileSync(p, JSON.stringify(cfg));
    assert.equal(f.call(['--full', '--update-baseline']).exit, 0);
    f.env.RT_DISABLE_WORKSPACE_PREBUILD = '1';
    assert.equal(f.call().data.code, 'baseline_incompatible');
    delete f.env.RT_DISABLE_WORKSPACE_PREBUILD;
    assert.equal(f.call().exit, 0);
    assert.equal(f.nativeCalls(), 1);
});

test('complete native outcomes survive verified cleanup of a lingering owned worker', { skip: !linux }, t => {
    const f = fixture(t); f.settings({ mode: 'linger' });
    const r = f.call(['--full', '--update-baseline']);
    assert.equal(r.exit, 0);
    const receipt = JSON.parse(readFileSync(r.data.receipt));
    assert.equal(receipt.nativeExitCode, 0);
    assert.equal(receipt.cleanupRequired, true);
    assert.equal(receipt.cancelled, false);
    assert.equal(receipt.streamsDrained, true);
    assert.equal(receipt.remainingProcesses.filter(x => x.state !== 'Z').length, 0);
    assert.equal(f.call().exit, 0);
});

test('failed, empty and malformed native results never become a baseline', { skip: !linux }, t => {
    const f = fixture(t);
    for (const mode of ['failure', 'empty', 'malformed', 'unattributed', 'change']) {
        f.settings({ mode });
        const r = f.call(['--full', '--update-baseline']);
        assert.notEqual(r.exit, 0, mode);
        assert.ok(existsSync(r.data.receipt), mode);
        assert.equal(f.call().data.code, 'baseline_missing', mode);
    }
    f.settings({ tool: '4.14.10' });
    assert.equal(f.call(['--full', '--update-baseline']).data.code, 'incomplete_report');
    assert.equal(f.call().data.code, 'baseline_missing');
});

test('full verification without update preserves the seed and reset affects only the selected target', { skip: !linux }, t => {
    const f = fixture(t), a = f.call(['--full', '--update-baseline']);
    const original = readFileSync(a.data.baseline, 'utf8');
    const b = f.call(['--config', 'other-config.json', '--full', '--update-baseline']);
    assert.notEqual(a.data.baseline, b.data.baseline);
    assert.equal(f.call(['--full']).exit, 0);
    assert.equal(readFileSync(a.data.baseline, 'utf8'), original);
    assert.equal(f.call(['--reset-baseline']).exit, 0);
    assert.equal(f.call().data.code, 'baseline_missing');
    assert.equal(f.call(['--config', 'other-config.json']).exit, 0);
});

test('cancellation retains diagnostics, stops the owned worker and admits no baseline', { skip: !linux }, async t => {
    const f = fixture(t); f.settings({ mode: 'wait' });
    const child = spawn(process.execPath, [driver, '--full', '--update-baseline'], { cwd: f.root, env: f.env, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = ''; child.stdout.on('data', b => output += b);
    t.after(() => { if (child.exitCode === null) child.kill('SIGINT'); });
    const done = new Promise(resolve => child.on('exit', code => resolve(code)));
    const deadline = Date.now() + 10000;
    while (!existsSync(join(f.root, 'native-started')) && Date.now() < deadline) await new Promise(r => setTimeout(r, 25));
    assert.ok(existsSync(join(f.root, 'native-started')), output);
    const worker = Number(readFileSync(join(f.root, 'worker.pid'), 'utf8'));
    child.kill('SIGINT');
    assert.equal(await done, 130);
    const result = JSON.parse(output.trim().split('\n').at(-1));
    const receipt = JSON.parse(readFileSync(result.receipt));
    assert.equal(receipt.complete, false);
    assert.equal(receipt.cancelled, true);
    assert.equal(receipt.remainingProcesses.filter(x => x.state !== 'Z').length, 0);
    assert.match(readFileSync(receipt.log, 'utf8'), /diagnostics before cancellation/);
    assert.throws(() => process.kill(worker, 0), { code: 'ESRCH' });
    assert.equal(f.call().data.code, 'baseline_missing');
});
