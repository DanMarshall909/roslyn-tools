import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

// These whole groups retain their assertions in ticket-completion verification.
export const slowTestClasses = [
    'RoslynTools.Tests.Cli.SuggestWorkflowSurfaceTests',
    'RoslynTools.Tests.Infrastructure.JournaledApplyAuthorityTests',
];

export function hasSlowTests(project) {
    return project.replaceAll('\\', '/').split('/').at(-1) === 'RoslynTools.Tests.csproj';
}

export function isSlowTest(name, file) {
    const normalizedName = String(name).toLowerCase();
    const fileName = file.replaceAll('\\', '/').split('/').at(-1).toLowerCase();
    return slowTestClasses.some(type =>
        normalizedName.startsWith(type.toLowerCase() + '.')
        || fileName === type.split('.').at(-1).toLowerCase() + '.cs');
}

export function testFilter(lane, requestedFilter = '') {
    let selection;
    switch (lane) {
        case 'fast':
            selection = slowTestClasses.map(name => `FullyQualifiedName!~${name}.`).join('&');
            break;
        case 'slow':
            selection = slowTestClasses.map(name => `FullyQualifiedName~${name}.`).join('|');
            break;
        case 'all':
            return requestedFilter;
        default:
            throw new Error(`Unknown test lane: ${lane}. Choose fast, slow or all.`);
    }
    return requestedFilter ? `(${requestedFilter})&(${selection})` : selection;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        const result = process.argv[2] === 'has-slow-tests'
            ? String(hasSlowTests(process.argv[3]))
            : testFilter(process.argv[2], process.argv[3]);
        process.stdout.write(result + '\n');
    } catch (error) {
        process.stderr.write(error.message + '\n');
        process.exitCode = 2;
    }
}
