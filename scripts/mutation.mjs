import * as fs from 'node:fs';
import { resolve, relative, join, dirname, sep } from 'node:path';
import { homedir } from 'node:os';
import { createHash, randomUUID } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { isSlowTest, testFilter } from './test-lanes.mjs';
import { mutationEnabled } from './mutation-policy.mjs';

const hash = x => createHash('sha256').update(x).digest('hex');
const stable = value => JSON.stringify(value, (_, x) => x && !Array.isArray(x) && typeof x === 'object' ? Object.fromEntries(Object.entries(x).sort(([a], [b]) => a.localeCompare(b))) : x);
const delay = ms => new Promise(r => setTimeout(r, ms));
const env = { ...process.env, MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0' };
// Normalize shell bookkeeping so the verified native environment is repeatable.
for (const key of ['_', 'SHLVL', 'PWD', 'OLDPWD']) delete env[key];
let stdoutBytes = 0;
let pageSizeKiB = 0;
function emit(value) {
    for (let i = 0; i < 4; i++) value.driverStdoutBytes = stdoutBytes + Buffer.byteLength(JSON.stringify(value) + '\n');
    const line = JSON.stringify(value) + '\n'; stdoutBytes += Buffer.byteLength(line); process.stdout.write(line);
}
function fail(code, message, recovery) { throw Object.assign(new Error(message), { code, recovery }); }
function command(file, args, optional = false) {
    const r = spawnSync(file, args, { encoding: 'utf8', env, maxBuffer: 16 * 1024 * 1024 });
    if (r.status !== 0 && !optional) fail('preflight_failed', `${file} ${args[0]} failed.`, 'Restore the solution and verify its pinned SDK and local tool manifest.');
    return r;
}
const git = (...args) => command('git', args).stdout.trim();

function options() {
    const o = { config: 'stryker-config.json' }, args = process.argv.slice(2);
    for (let i = 0; i < args.length; i++) {
        const name = args[i].replace(/^--/, '');
        if (['full', 'update-baseline', 'show-baseline', 'reset-baseline'].includes(name) && args[i].startsWith('--')) o[name] = true;
        else if (['config', 'concurrency'].includes(name) && args[i].startsWith('--') && args[i + 1] && !args[i + 1].startsWith('--')) o[name] = args[++i];
        else fail('invalid_arguments', `Unknown or incomplete option: ${args[i]}`);
    }
    if (o['update-baseline'] && !o.full) fail('seed_requires_full', 'Updating the baseline requires a full run.', '--full --update-baseline');
    if ((o['show-baseline'] || o['reset-baseline']) && (o.full || o['update-baseline']) || o['show-baseline'] && o['reset-baseline']) fail('invalid_arguments', 'Choose one run or state operation.');
    if (o.concurrency && !/^[1-9]\d*$/.test(o.concurrency)) fail('invalid_arguments', 'Concurrency must be a positive integer.');
    return o;
}

function context(o) {
    const root = git('rev-parse', '--show-toplevel'); process.chdir(root);
    const configPath = resolve(root, o.config), configRelative = relative(root, configPath).split(sep).join('/');
    if (configRelative === '..' || configRelative.startsWith('../')) fail('invalid_config', 'The configuration must belong to this checkout.');
    let config;
    try { config = JSON.parse(fs.readFileSync(configPath, 'utf8'))['stryker-config']; } catch { fail('invalid_config', 'Cannot read the Stryker configuration.'); }
    if (!config?.project || !Array.isArray(config.mutate) || !config.mutate.length || config.since || config['with-baseline']) fail('invalid_config', 'Use explicit mutation scope with native since/baseline modes disabled.');
    const solution = config.solution || 'RoslynTools.slnx';
    if (!fs.existsSync(resolve(root, solution))) fail('invalid_config', 'The configured or repository-default solution is missing.');
    const branch = git('symbolic-ref', '--quiet', '--short', 'HEAD');
    const origin = command('git', ['config', '--get', 'remote.origin.url'], true).stdout.trim() || resolve(root, git('rev-parse', '--git-common-dir'));
    const namespace = hash(stable({ originHash: hash(origin), checkout: fs.realpathSync(root), branch, config: configRelative, project: config.project }));
    const cacheRoot = resolve(process.env.RT_MUTATION_CACHE_ROOT || join(process.env.XDG_CACHE_HOME || join(homedir(), '.cache'), 'MutationBaselines', 'roslyn-tools'));
    return { root, configPath, configRelative, config, solution, branch, namespace, cache: join(cacheRoot, namespace, 'baseline.json'), concurrency: o.concurrency };
}

function snapshot(c) {
    const paths = new Set(command('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard']).stdout.split('\0').filter(Boolean));
    paths.add(c.configRelative);
    for (const p of command('git', ['ls-files', '-z', '--others', '--ignored', '--exclude-standard', '--', 'src', 'tests']).stdout.split('\0')) if (p) paths.add(p);
    const outputs = new Set(['bin', 'obj', 'artifacts', 'node_modules', 'StrykerOutput', 'TestResults']);
    const relevant = p => p === c.configRelative || ['global.json', '.config/dotnet-tools.json', '.editorconfig'].includes(p) || /\.(props|targets|slnx?|lock\.json)$/.test(p) || /^nuget\.config$/i.test(p) || /^(src|tests)\//.test(p) && !p.split('/').some(x => outputs.has(x));
    const files = {}, sourceHashes = {};
    for (const p of [...paths].filter(relevant).sort()) {
        try {
            const bytes = fs.readFileSync(p); files[p] = hash(bytes);
            if (p.endsWith('.cs')) sourceHashes[p] = hash(bytes.toString('utf8').replace(/^\uFEFF/, ''));
        } catch (e) { if (e.code === 'ENOENT') files[p] = 'missing'; else throw e; }
    }
    let tool;
    try { tool = JSON.parse(fs.readFileSync('.config/dotnet-tools.json', 'utf8')).tools['dotnet-stryker'].version; } catch { fail('tool_manifest_invalid', 'Cannot resolve the pinned local Stryker version.'); }
    const sdk = command('dotnet', ['--version']).stdout.trim();
    let packages;
    try { packages = JSON.parse(command('dotnet', ['list', c.solution, 'package', '--include-transitive', '--format', 'json', '--no-restore']).stdout); } catch (e) { if (e.code) throw e; fail('dependency_graph_unavailable', 'The resolved dependency graph is unavailable.', `dotnet restore ${c.solution}`); }
    if (!Array.isArray(packages.projects) || !packages.projects.length) fail('dependency_graph_unavailable', 'No resolved projects were reported.');
    const graph = packages.projects.map(p => ({ ...p, path: relative(c.root, resolve(p.path)).split(sep).join('/') })).sort((a, b) => a.path.localeCompare(b.path));
    const binding = { files, sourceHashes, tool, sdk, graph, solution: c.solution, concurrency: c.concurrency || c.config.concurrency || null, wrapper: hash(fs.readFileSync(new URL(import.meta.url))), lanePolicy: hash(fs.readFileSync(new URL('./test-lanes.mjs', import.meta.url))), testCaseFilter: testFilter('fast', c.config['test-case-filter']), node: process.version, platform: process.platform, arch: process.arch, environmentHash: hash(stable(env)) };
    return { binding, fingerprint: hash(stable(binding)) };
}

function reportSummary(raw, binding, root) {
    const r = JSON.parse(raw), counts = {}, findings = [];
    if (String(r.schemaVersion) !== '2' || !r.files || !r.testFiles) throw new Error('Pinned report schema or source/test files are missing.');
    const source = (file, text) => {
        const p = relative(root, resolve(root, file)).split(sep).join('/');
        if (typeof text !== 'string' || binding.sourceHashes[p] !== hash(text)) throw new Error('Report source is not bound to the checkout inputs.');
        return text;
    };
    const span = (where, text) => {
        const s = where?.start, e = where?.end, lines = text.split('\n');
        if (![s?.line, s?.column, e?.line, e?.column].every(x => Number.isInteger(x) && x > 0) || s.line > e.line || s.line === e.line && s.column >= e.column || e.line > lines.length || s.column > lines[s.line - 1].length + 1 || e.column > lines[e.line - 1].length + 1) throw new Error('Invalid source location.');
    };
    const testIds = new Set(), mutantIds = new Set();
    for (const [file, data] of Object.entries(r.testFiles)) {
        if (!Array.isArray(data.tests)) throw new Error('Test catalog is invalid.');
        if (!data.tests.length) continue;
        const text = source(file, data.source);
        for (const t of data.tests) {
            if (isSlowTest(t.name, file)) throw new Error(`Mutation catalogue contains a slow-lane test: ${t.name}`);
            if (typeof t.id !== 'string' || !t.id || !t.name || testIds.has(t.id)) throw new Error('Named test identity is invalid.');
            span(t.location, text); testIds.add(t.id);
        }
    }
    if (!testIds.size) throw new Error('No source-resolved test catalog.');
    const allowed = new Set(['Killed', 'Survived', 'Timeout', 'NoCoverage', 'Ignored', 'CompileError']);
    for (const [file, data] of Object.entries(r.files)) {
        if (!Array.isArray(data.mutants)) throw new Error('Report mutant lists are missing.');
        for (const m of data.mutants) {
            if (!allowed.has(m.status)) throw new Error('Nonterminal or unknown outcomes.');
            if (['Ignored', 'CompileError'].includes(m.status)) continue;
            if (typeof m.id !== 'string' || !m.id || mutantIds.has(m.id) || !m.mutatorName || typeof m.replacement !== 'string') throw new Error('Source-resolved mutant identity is missing or duplicated.');
            mutantIds.add(m.id); span(m.location, source(file, data.source));
            for (const key of ['coveredBy', 'killedBy']) if (m[key] !== undefined && (!Array.isArray(m[key]) || m[key].some(id => !testIds.has(id)))) throw new Error('Test attribution does not resolve to the source catalog.');
            if (m.status === 'Killed' && !m.killedBy?.length) throw new Error('Killed outcome has no named source-resolved test.');
            counts[m.status] = (counts[m.status] || 0) + 1;
            if (m.status !== 'Killed') findings.push({ file, location: m.location, mutator: m.mutatorName, replacement: m.replacement, status: m.status, id: m.id });
        }
    }
    const eligibleMutants = Object.values(counts).reduce((a, b) => a + b, 0);
    if (!eligibleMutants) throw new Error('No eligible terminal mutant outcomes.');
    return { counts, eligibleMutants, testableMutants: eligibleMutants - (counts.NoCoverage || 0), findings };
}

function load(c) {
    if (!fs.existsSync(c.cache)) fail('baseline_missing', 'No verified baseline exists for this checkout, branch and target.', `node scripts/mutation.mjs --config ${c.configRelative} --full --update-baseline`);
    let state;
    try {
        state = JSON.parse(fs.readFileSync(c.cache, 'utf8'));
        if (state.schema !== 1 || state.namespace !== c.namespace || !state.complete || state.nativeExitCode !== 0 || hash(state.reportRaw) !== state.reportHash) throw new Error('Invalid state binding.');
        const v = state.verification;
        if (!v?.complete || v.nativeExitCode !== 0 || v.cancelled || !v.streamsDrained || !v.inputsUnchanged || v.commit !== state.commit || v.fingerprint !== state.fingerprint || hash(stable(v.binding)) !== state.fingerprint || v.remainingProcesses.some(p => p.state !== 'Z')) throw new Error('Complete durable verification is missing.');
        const summary = reportSummary(state.reportRaw, v.binding, c.root);
        const findings = { schema: 1, report: v.report, reportHash: state.reportHash, counts: summary.counts, eligibleMutants: summary.eligibleMutants, findings: summary.findings };
        if (stable(summary.counts) !== stable(state.counts) || summary.eligibleMutants !== state.eligibleMutants || stable(findings) !== stable(state.findings)) throw new Error('Invalid report summary.');
        if (v.scheduledMutants !== summary.testableMutants || v.nativeVersion !== v.binding.tool) throw new Error('Native terminal binding is invalid.');
    } catch { fail('baseline_corrupt', 'The saved baseline is invalid.', `node scripts/mutation.mjs --config ${c.configRelative} --full --update-baseline`); }
    if (!/^[a-f0-9]{40,64}$/.test(state.commit) || command('git', ['cat-file', '-e', `${state.commit}^{commit}`], true).status !== 0) fail('baseline_unreachable', 'The baseline commit is not reachable in this checkout.', 'Restore the exact commit or create a full seed.');
    return state;
}

function processGroup(pgid) {
    const rows = [];
    for (const pid of fs.readdirSync('/proc').filter(x => /^\d+$/.test(x))) {
        try {
            const raw = fs.readFileSync(`/proc/${pid}/stat`, 'utf8'), p = raw.slice(raw.lastIndexOf(')') + 2).trim().split(/\s+/);
            if (Number(p[2]) === pgid) rows.push({ pid: Number(pid), startTicks: p[19], state: p[0], rssKiB: Number(p[21]) * pageSizeKiB });
        } catch (e) { if (!['ENOENT', 'ESRCH', 'EACCES'].includes(e.code)) throw e; }
    }
    return rows;
}

async function full(c, o) {
    if (process.platform !== 'linux') fail('unsupported_platform', 'Owned-process cancellation is currently verified only on Linux.');
    pageSizeKiB = Number(command('getconf', ['PAGESIZE']).stdout.trim()) / 1024;
    if (!Number.isFinite(pageSizeKiB) || pageSizeKiB <= 0) fail('unsupported_platform', 'Cannot determine the system page size.');
    const before = snapshot(c), commit = git('rev-parse', 'HEAD');
    const run = join(c.root, 'artifacts', 'mutation', c.namespace.slice(0, 16), new Date().toISOString().replace(/[:.]/g, '-') + '-' + randomUUID().slice(0, 8));
    fs.mkdirSync(run, { recursive: true });
    const log = join(run, 'native.log'), receiptPath = join(run, 'receipt.json'), findingsPath = join(run, 'findings.json');
    const nativeConfigPath = join(run, 'stryker-config.json');
    fs.writeFileSync(nativeConfigPath, JSON.stringify({ 'stryker-config': { ...c.config, 'test-case-filter': before.binding.testCaseFilter } }, null, 2) + '\n');
    const argv = ['stryker', '--config-file', nativeConfigPath, '--solution', c.solution, '--output', join(run, 'native'), '--reporter', 'json', '--reporter', 'cleartext'];
    if (c.concurrency) argv.push('--concurrency', c.concurrency);
    const start = performance.now(), fd = fs.openSync(log, 'w'), identities = new Map(), signals = [], milestones = [];
    let cancelled = false, cleanupRequired = false, peak = 0, samples = 0, timer, escalation, force, buffer = '', scheduled;
    const diskFree = () => { const s = fs.statfsSync(c.root); return s.bavail * s.bsize; };
    const receipt = { schema: 1, mode: 'full', complete: false, namespace: c.namespace, root: c.root, branch: c.branch, commit, statusBefore: git('status', '--porcelain=v1'), fingerprint: before.fingerprint, binding: before.binding, startedUtc: new Date().toISOString(), argv: ['dotnet', ...argv], log, diskFreeBeforeBytes: diskFree(), agentAttribution: { toolCalls: null, responseBytes: null, tokens: null } };
    const native = spawn('dotnet', argv, { env, detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
    const closed = new Promise(r => native.once('close', () => r(true)));
    const capture = () => {
        const rows = native.pid ? processGroup(native.pid) : []; samples++;
        for (const p of rows) identities.set(`${p.pid}:${p.startTicks}`, p);
        peak = Math.max(peak, rows.reduce((sum, p) => sum + p.rssKiB, 0)); return rows;
    };
    const send = signal => {
        const rows = native.pid ? processGroup(native.pid) : [];
        if (!rows.some(p => identities.has(`${p.pid}:${p.startTicks}`) && p.state !== 'Z')) return;
        signals.push({ signal, elapsedSeconds: (performance.now() - start) / 1000, processes: rows });
        try { process.kill(-native.pid, signal); } catch (e) { if (e.code !== 'ESRCH') throw e; }
    };
    const stop = () => {
        send('SIGINT');
        if (!escalation) escalation = setTimeout(() => send('SIGTERM'), 10000);
        if (!force) force = setTimeout(() => send('SIGKILL'), 15000);
    };
    const cancel = () => { cancelled = true; capture(); stop(); };
    process.on('SIGINT', cancel); process.on('SIGTERM', cancel);
    const data = chunk => {
        fs.writeSync(fd, chunk); buffer += chunk.toString();
        const lines = buffer.split(/[\r\n]/); buffer = lines.pop();
        for (const line of lines) {
            const record = line.match(/^\[\d{2}:\d{2}:\d{2} INF\] (.*)$/);
            if (!record) continue;
            const match = record[1].match(/^(\d+)\s+total mutants will be tested$/); if (match) scheduled = Number(match[1]);
            if (match || /Initial test|coverage|Time Elapsed/.test(record[1])) milestones.push({ elapsedSeconds: (performance.now() - start) / 1000, message: line.slice(0, 240) });
        }
        if (buffer.length > 65536) buffer = buffer.slice(-65536);
    };
    native.stdout.on('data', data); native.stderr.on('data', data);
    native.once('spawn', () => { capture(); timer = setInterval(capture, 1000); if (cancelled) stop(); emit({ event: 'mutation_started', pid: native.pid, run }); });
    let code, signal, error, streamsDrained;
    try {
        ({ code, signal, error } = await new Promise(r => { native.once('exit', (code, signal) => r({ code, signal })); native.once('error', error => r({ error })); }));
        for (let i = 0; i < 20 && capture().some(p => p.state !== 'Z'); i++) await delay(100);
        if (capture().some(p => p.state !== 'Z')) {
            cleanupRequired = !cancelled; stop();
            for (let i = 0; i < 170 && capture().some(p => p.state !== 'Z'); i++) await delay(100);
        }
        streamsDrained = await Promise.race([closed, delay(2000).then(() => false)]);
    } finally {
        clearInterval(timer); clearTimeout(escalation); clearTimeout(force);
        process.removeListener('SIGINT', cancel); process.removeListener('SIGTERM', cancel);
        native.stdout.destroy(); native.stderr.destroy(); fs.closeSync(fd);
    }
    Object.assign(receipt, { nativeExitCode: code ?? null, nativeSignal: signal ?? null, launchError: error?.message ?? null, cancelled, cleanupRequired, streamsDrained, signals, remainingProcesses: native.pid ? processGroup(native.pid) : [], processIdentities: [...identities.values()], pageSizeKiB, peakSampledGroupRssKiB: peak, resourceSamples: samples, elapsedSeconds: (performance.now() - start) / 1000, milestones, scheduledMutants: scheduled ?? null, finishedUtc: new Date().toISOString(), diskFreeAfterBytes: diskFree() });
    const reportPath = join(run, 'native', 'reports', 'mutation-report.json'); receipt.report = fs.existsSync(reportPath) ? reportPath : null;
    let result, exit = code || 1;
    try {
        if (cancelled) fail('cancelled', 'Mutation execution was cancelled; retained evidence is incomplete.');
        if (error || signal || code !== 0) fail('native_failed', 'Native mutation execution did not succeed.');
        if (receipt.remainingProcesses.some(p => p.state !== 'Z')) fail('owned_workers_remained', 'Owned workers remained after native terminal exit and cleanup.');
        if (!streamsDrained) fail('diagnostics_incomplete', 'Native diagnostic streams did not drain.');
        const raw = fs.readFileSync(reportPath, 'utf8'), summary = reportSummary(raw, before.binding, c.root);
        const nativeVersion = fs.readFileSync(log, 'utf8').match(/^Version:\s*(\S+)\s*$/m)?.[1];
        receipt.nativeVersion = nativeVersion ?? null;
        if (scheduled !== summary.testableMutants || nativeVersion !== before.binding.tool) fail('incomplete_report', 'Outcomes do not match the scheduled mutants or pinned tool.');
        const after = snapshot(c); receipt.finalFingerprint = after.fingerprint;
        receipt.inputsUnchanged = before.fingerprint === after.fingerprint && commit === git('rev-parse', 'HEAD');
        if (!receipt.inputsUnchanged) fail('inputs_changed', 'Relevant inputs or HEAD changed during native execution.');
        const findings = { schema: 1, report: reportPath, reportHash: hash(raw), counts: summary.counts, eligibleMutants: summary.eligibleMutants, findings: summary.findings };
        fs.writeFileSync(findingsPath, JSON.stringify(findings, null, 2) + '\n');
        receipt.complete = true;
        fs.writeFileSync(receiptPath, JSON.stringify(receipt, null, 2) + '\n');
        if (o['update-baseline']) {
            const state = { schema: 1, namespace: c.namespace, complete: true, nativeExitCode: 0, commit, branch: c.branch, fingerprint: before.fingerprint, counts: summary.counts, eligibleMutants: summary.eligibleMutants, reportRaw: raw, reportHash: hash(raw), verification: receipt, findings, originalReceipt: receiptPath, originalFindings: findingsPath, updatedUtc: new Date().toISOString() };
            fs.mkdirSync(dirname(c.cache), { recursive: true }); const temp = c.cache + '.' + randomUUID() + '.tmp';
            try { fs.writeFileSync(temp, JSON.stringify(state)); fs.renameSync(temp, c.cache); } finally { fs.rmSync(temp, { force: true }); }
            receipt.baselineUpdated = true;
        }
        receipt.complete = true; exit = 0;
        result = { status: 'complete', mode: 'full', nativeExecuted: true, counts: summary.counts, eligibleMutants: summary.eligibleMutants, findings: findingsPath, baseline: o['update-baseline'] ? c.cache : null };
    } catch (e) {
        receipt.complete = false;
        result = { status: 'incomplete', mode: 'full', code: e.code || 'incomplete_report', message: e.message, nativeExecuted: true };
        exit = cancelled ? 130 : code && code > 0 ? code : 1;
    }
    fs.writeFileSync(receiptPath, JSON.stringify(receipt, null, 2) + '\n');
    emit({ ...result, receipt: receiptPath, log, elapsedSeconds: receipt.elapsedSeconds }); process.exitCode = exit;
}

try {
    if (process.argv.includes('--help')) process.stdout.write('node scripts/mutation.mjs [--config stryker-config.json] [--concurrency N]\n  default: exact verified-state reuse; no implicit full run\n  --full: native whole-config verification, without updating state\n  --full --update-baseline: verify and atomically seed durable state\n  --show-baseline: inspect compatibility\n  --reset-baseline: remove only this checkout/branch/target state\nFull execution and owned-process cancellation are currently verified on Linux.\n');
    else {
        if (!mutationEnabled()) fail('mutation_disabled', 'Mutation testing is disabled by repository policy. No native process was started.');
        const o = options(), c = context(o);
        if (o.full) await full(c, o);
        else if (o['reset-baseline']) { fs.rmSync(c.cache, { force: true }); emit({ status: 'reset', baseline: c.cache }); }
        else if (o['show-baseline'] && !fs.existsSync(c.cache)) emit({ exists: false, baseline: c.cache });
        else {
            const state = load(c), current = snapshot(c), compatible = state.fingerprint === current.fingerprint;
            if (o['show-baseline']) emit({ exists: true, compatible, recordedFingerprint: state.fingerprint, currentFingerprint: current.fingerprint, commit: state.commit, counts: state.counts, baseline: c.cache });
            else {
                if (!compatible) fail('baseline_incompatible', 'Source, tests, tool, configuration or environment changed.', `node scripts/mutation.mjs --config ${c.configRelative} --full --update-baseline`);
                emit({ status: 'complete', mode: 'reused', nativeExecuted: false, counts: state.counts, eligibleMutants: state.eligibleMutants, sourceCommit: state.commit, establishedAt: state.updatedUtc, receipt: c.cache, findings: c.cache, baseline: c.cache, originalReceipt: state.originalReceipt, originalFindings: state.originalFindings });
            }
        }
    }
} catch (e) { emit({ status: 'error', code: e.code || 'mutation_error', message: e.message, recovery: e.recovery }); process.exitCode = 2; }
