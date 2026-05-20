const fs = require('fs');
const path = process.argv[2];
const r = JSON.parse(fs.readFileSync(path, 'utf8'));
const scoped = ['DependencyInjector', 'InstanceReachabilityAnalyzer', 'NamespaceMoveRewriter', 'MethodExtractorBuilder', 'MakeSealedCommand', 'DebugTraceAnalyzerService', 'DebugReport'];
for (const [file, data] of Object.entries(r.files)) {
    if (!scoped.some(s => file.includes(s))) continue;
    const all = data.mutants;
    const survived = all.filter(m => m.status === 'Survived');
    const noCov = all.filter(m => m.status === 'NoCoverage');
    const killed = all.filter(m => m.status === 'Killed');
    const total = all.length;
    const name = file.split(/[\\/]/).pop();
    console.log(`=== ${name}  killed=${killed.length} survived=${survived.length} noCov=${noCov.length} total=${total} ===`);
    for (const m of [...survived, ...noCov]) {
        const repl = (m.replacement || '').substring(0, 120).replace(/\n/g, ' ');
        console.log(`  L${m.location.start.line}  [${m.status}][${m.mutatorName}] ${repl}`);
    }
}
