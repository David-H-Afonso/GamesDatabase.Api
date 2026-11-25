#!/usr/bin/env node
// Explicit export only: never scans or modifies the user's other projects automatically.
import { copyFileSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const args = process.argv.slice(2), at = args.indexOf('--to');
if (at < 0 || !args[at + 1]) throw new Error('Use node scripts/versioning/export.mjs --to <explicit-target-folder> [--apply].');
const target = resolve(args[at + 1]);
if (target === root) throw new Error('Source and destination are the same.');
const files = ['scripts/versioning/core.mjs', 'scripts/versioning/cli.mjs', 'scripts/versioning/core.test.mjs', 'scripts/versioning/export.mjs', 'scripts/versioning/release.mjs', 'scripts/versioning/README.md', '.githooks/prepare-commit-msg', '.githooks/commit-msg', '.githooks/pre-push'];
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
for (const file of files) if (existsSync(resolve(target, file)) && hash(resolve(root, file)) !== hash(resolve(target, file))) throw new Error(`Not overwriting an existing different file: ${file}`);
if (args.includes('--apply')) for (const file of files) { const destination = resolve(target, file); mkdirSync(dirname(destination), { recursive: true }); copyFileSync(resolve(root, file), destination); }
console.log(JSON.stringify({ source: root, target, files, copied: args.includes('--apply'), note: 'Configure .versioning.json explicitly for the target repository, then install. No target versions, Git config or history are changed by exporting.' }, null, 2));
