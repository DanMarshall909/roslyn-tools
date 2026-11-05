import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export function mutationEnabled() {
    try {
        const policy = JSON.parse(readFileSync(new URL('../mutation-testing.json', import.meta.url), 'utf8'));
        if (policy?.schemaVersion !== 1 || typeof policy.enabled !== 'boolean') {
            throw new Error('Require schemaVersion 1 and boolean enabled.');
        }
        return policy.enabled;
    } catch (cause) {
        throw Object.assign(new Error(`Cannot load mutation-testing.json: ${cause.message}`, { cause }), { code: 'mutation_policy_invalid' });
    }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    try { process.stdout.write(`${mutationEnabled()}\n`); }
    catch (error) { process.stderr.write(`${error.message}\n`); process.exitCode = 1; }
}
