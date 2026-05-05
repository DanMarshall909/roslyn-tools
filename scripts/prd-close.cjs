#!/usr/bin/env node
// Close a PRD story by id. Sets passes:true, resolution, resolvedAt (today).
// Preserves the source file's indent and trailing newline.
//
// Usage:
//   node scripts/prd-close.cjs <id> "<resolution>"
//   node scripts/prd-close.cjs <id> "<resolution>" --date 2026-05-05
//   node scripts/prd-close.cjs <id> --reopen                       # inverse: passes:false, strips resolution/resolvedAt
//   node scripts/prd-close.cjs <id> "<note>" --note                # append to story.notes; does not close
//
// Exit 0 on write, 1 on error (story missing, bad args, already in target state).

const fs = require('fs');
const path = require('path');

const PRD = path.resolve(__dirname, '..', 'ralph', 'prd.json');

function today() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function parseArgs(argv) {
    const args = { positional: [], reopen: false, note: false, date: today() };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === '--reopen') args.reopen = true;
        else if (a === '--note') args.note = true;
        else if (a === '--date') args.date = argv[++i];
        else args.positional.push(a);
    }
    return args;
}

function detectIndent(src) {
    const m = src.match(/^( +)"/m);
    return m ? m[1].length : 4;
}

function main() {
    const args = parseArgs(process.argv.slice(2));
    const [id, message] = args.positional;

    if (!id || (!args.reopen && !message)) {
        console.error('usage: node scripts/prd-close.cjs <id> "<resolution>" [--date YYYY-MM-DD]');
        console.error('       node scripts/prd-close.cjs <id> --reopen');
        console.error('       node scripts/prd-close.cjs <id> "<note>" --note');
        process.exit(1);
    }

    const src = fs.readFileSync(PRD, 'utf8');
    const indent = detectIndent(src);
    const trailingNL = src.endsWith('\n');
    const data = JSON.parse(src);

    const story = (data.userStories || []).find(s => s.id === id);
    if (!story) {
        console.error(`error: ${id} not found in ${PRD}`);
        process.exit(1);
    }

    if (args.reopen) {
        if (!story.passes) {
            console.error(`error: ${id} is already open`);
            process.exit(1);
        }
        story.passes = false;
        delete story.resolution;
        delete story.resolvedAt;
        console.log(`reopened ${id}`);
    } else if (args.note) {
        const stamped = `[${args.date}] ${message}`;
        story.notes = story.notes ? `${story.notes}\n\n${stamped}` : stamped;
        console.log(`noted ${id}`);
    } else {
        if (story.passes) {
            console.error(`error: ${id} is already closed (resolvedAt: ${story.resolvedAt || 'unknown'})`);
            process.exit(1);
        }
        story.passes = true;
        story.resolution = message;
        story.resolvedAt = args.date;
        console.log(`closed ${id} (${args.date})`);
    }

    fs.writeFileSync(PRD, JSON.stringify(data, null, indent) + (trailingNL ? '\n' : ''));
}

main();
