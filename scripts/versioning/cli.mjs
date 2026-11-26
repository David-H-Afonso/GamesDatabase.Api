#!/usr/bin/env node
import { accessSync, chmodSync, constants, existsSync, mkdirSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';
import { assertImmutableTagUpdate, canonicalVersion, classifyMessage, plannedVersion, rewriteTarget, validateConfig, verifyCommit } from './core.mjs';

export const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const CONFIG = '.versioning.json';
const HOOK_PATH = '.githooks';
const SETUP_FILES = [CONFIG, '.githooks/prepare-commit-msg', '.githooks/commit-msg', '.githooks/pre-push', 'scripts/versioning/core.mjs', 'scripts/versioning/cli.mjs'];

export function git(args, options = {}) {
  const { raw = false, ...spawnOptions } = options;
  const result = spawnSync('git', ['--no-optional-locks', ...args], { cwd: ROOT, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024, ...spawnOptions });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr.trim() || `git ${args[0]} failed`);
  return raw ? result.stdout : result.stdout.trimEnd();
}
const blob = (ref, path) => git(['show', `${ref}:${path}`], { raw: true });
const head = () => git(['rev-parse', 'HEAD']);
const config = staged => validateConfig(JSON.parse(staged ? blob('', CONFIG) : readFileSync(resolve(ROOT, CONFIG), 'utf8')));
const gitPath = name => { const path = git(['rev-parse', '--git-path', `gdb-versioning/${name}`]); return isAbsolute(path) ? path : resolve(ROOT, path); };
const readRecord = name => { try { return JSON.parse(readFileSync(gitPath(name), 'utf8')); } catch { return null; } };
const record = (name, value) => { const path = gitPath(name); mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, JSON.stringify(value, null, 2) + '\n'); };

function assertUnpublishedAmend() {
  const refs = git(['for-each-ref', '--format=%(refname)', '--contains', 'HEAD', 'refs/remotes', 'refs/tags']);
  if (refs.trim()) throw new Error('Refusing to amend a commit already referenced by a tag or remote-tracking branch. No published history is rewritten. Create a new commit instead.');
}

export function makePlan(message, { amend = false, staged = false } = {}) {
  const settings = config(staged);
  if (amend) assertUnpublishedAmend();
  const baseRef = amend ? 'HEAD^1' : 'HEAD';
  const previous = canonicalVersion(blob(baseRef, settings.files[0].path), settings.files[0]);
  const classification = classifyMessage(message, settings.allowedTypes);
  return { settings, baseRef, from: previous, version: plannedVersion(previous, classification), ...classification, files: settings.files.map(file => file.path) };
}

function prepareWorking(plan) {
  const changes = plan.settings.files.map(target => {
    const path = resolve(ROOT, target.path), before = readFileSync(path, 'utf8');
    accessSync(path, constants.W_OK);
    return { path, before, after: rewriteTarget(before, target, plan.version) };
  });
  const written = [];
  try { for (const change of changes) if (change.before !== change.after) { writeFileSync(change.path, change.after); written.push(change); } }
  catch (error) { for (const change of written) if (readFileSync(change.path, 'utf8') === change.after) writeFileSync(change.path, change.before); throw error; }
}

// Only version fields enter the index. Other unstaged edits in the same file remain unstaged.
export function prepareIndex(plan) {
  const changes = plan.settings.files.map(target => {
    const path = resolve(ROOT, target.path), indexed = blob('', target.path), working = readFileSync(path, 'utf8');
    const entry = /^(\d+) ([0-9a-f]+) 0\t/m.exec(git(['ls-files', '--stage', '--', target.path]));
    if (!entry) throw new Error(`${target.path} must have a resolved tracked index entry.`);
    accessSync(path, constants.W_OK);
    return { target, path, indexed, working, indexAfter: rewriteTarget(indexed, target, plan.version), workingAfter: rewriteTarget(working, target, plan.version), mode: entry[1], oldOid: entry[2] };
  });
  const applied = [];
  try {
    for (const change of changes) {
      applied.push(change);
      if (change.working !== change.workingAfter) writeFileSync(change.path, change.workingAfter);
      if (change.indexed !== change.indexAfter) {
        const oid = git(['hash-object', '-w', '--stdin'], { input: change.indexAfter });
        git(['update-index', '--cacheinfo', change.mode, oid, change.target.path]);
      }
    }
  } catch (error) {
    for (const change of applied.reverse()) {
      git(['update-index', '--cacheinfo', change.mode, change.oldOid, change.target.path]);
      if (readFileSync(change.path, 'utf8') === change.workingAfter) writeFileSync(change.path, change.working);
    }
    throw error;
  }
}

function checkRange(settings, from, to = 'HEAD') {
  const end = git(['rev-parse', '--verify', '--end-of-options', `${to}^{commit}`]);
  git(['merge-base', '--is-ancestor', settings.baselineCommit, end]);
  let start = settings.baselineCommit;
  if (from && !/^0+$/.test(from)) {
    const possible = git(['rev-parse', '--verify', '--end-of-options', `${from}^{commit}`]);
    try { git(['merge-base', '--is-ancestor', possible, end]); }
    catch { throw new Error('The proposed range rewrites published history. Version validation requires a fast-forward range.'); }
    try { git(['merge-base', '--is-ancestor', settings.baselineCommit, possible]); start = possible; } catch { /* Older public history is an explicit legacy baseline. */ }
  }
  const commits = git(['rev-list', '--first-parent', '--reverse', `${start}..${end}`]).split(/\r?\n/).filter(Boolean);
  const versions = new Set(), results = [];
  for (const sha of commits) {
    if (git(['rev-list', '--parents', '-n', '1', sha]).split(/\s+/).length !== 2) throw new Error('Automatic versions require linear history. Merge commits must be reviewed explicitly; no side-branch history is silently skipped.');
    const parent = git(['rev-parse', `${sha}^1`]);
    const previousVersion = canonicalVersion(blob(parent, settings.files[0].path), settings.files[0]);
    const message = git(['show', '-s', '--format=%B', sha]);
    const files = Object.fromEntries(settings.files.map(file => [file.path, blob(sha, file.path)]));
    const result = verifyCommit({ previousVersion, message, files }, settings);
    if (versions.has(result.version)) throw new Error(`Duplicate version ${result.version} in the outgoing first-parent history.`);
    versions.add(result.version); results.push({ sha, version: result.version, bump: result.bump });
  }
  return results;
}

function install() {
  config(false);
  let current = null;
  try { current = git(['config', '--local', '--get', 'core.hooksPath']); } catch { /* Unset is normal. */ }
  if (current && current !== HOOK_PATH) throw new Error(`Existing core.hooksPath=${current}; not overwritten. Integrate/chain the existing hook manager explicitly.`);
  if (!current) {
    const defaultPath = git(['rev-parse', '--git-path', 'hooks']);
    const location = isAbsolute(defaultPath) ? defaultPath : resolve(ROOT, defaultPath);
    for (const name of ['prepare-commit-msg', 'commit-msg', 'pre-push']) if (existsSync(resolve(location, name))) throw new Error(`An existing ${name} hook was found; not overwritten.`);
  }
  if (!readRecord('installation.json')) record('installation.json', { schemaVersion: 1, previousHookPath: current, installedHookPath: HOOK_PATH });
  for (const file of SETUP_FILES.filter(file => file.startsWith('.githooks/'))) chmodSync(resolve(ROOT, file), 0o755);
  git(['config', '--local', 'core.hooksPath', HOOK_PATH]);
  console.log('Version hooks installed locally. No commit, tag, push, release or deployment was created.');
}

function uninstall() {
  const previous = readRecord('installation.json');
  if (!previous) throw new Error('No installation record exists. Git configuration was not changed.');
  const current = git(['config', '--local', '--get', 'core.hooksPath']);
  if (current !== HOOK_PATH) throw new Error('Hook configuration has changed since installation; it was not overwritten.');
  if (previous.previousHookPath) git(['config', '--local', 'core.hooksPath', previous.previousHookPath]);
  else git(['config', '--local', '--unset', 'core.hooksPath']);
  unlinkSync(gitPath('installation.json'));
  console.log('Previous local hook configuration restored. Repository files and history were not changed.');
}

export function main(args = process.argv.slice(2)) {
  const command = args[0];
  const option = name => { const at = args.indexOf(name); return at < 0 ? undefined : args[at + 1]; };
  if (command === 'install') return install();
  if (command === 'uninstall') return uninstall();
  if (command === 'context') {
    const source = args[2], sha = args[3];
    record('commit-context.json', { head: head(), amend: source === 'commit' && sha === head(), source: source ?? null });
    return;
  }
  if (command === 'commit-msg') {
    const message = readFileSync(args[1], 'utf8');
    for (const file of SETUP_FILES) { try { blob('', file); } catch { throw new Error(`Stage the versioning setup file ${file} before its first commit. No unrelated file is staged automatically.`); } }
    const context = readRecord('commit-context.json');
    const amend = context?.head === head() && context.amend;
    const plan = makePlan(message, { amend, staged: true });
    prepareIndex(plan);
    console.log(`Version ${plan.from} → ${plan.version} (${plan.bump}). Version fields only; no extra commit.`);
    return;
  }
  if (command === 'plan' || command === 'prepare') {
    if (!option('--message')) throw new Error('Supply --message "feat(scope): summary"; the number is calculated automatically.');
    const plan = makePlan(option('--message'), { amend: args.includes('--amend') });
    if (command === 'prepare') prepareWorking(plan);
    console.log(JSON.stringify({ from: plan.from, version: plan.version, bump: plan.bump, files: plan.files, staged: false, committed: false }, null, 2));
    return;
  }
  if (command === 'check' || command === 'check-range') { console.log(JSON.stringify({ checked: checkRange(config(false), option('--from'), option('--to')) }, null, 2)); return; }
  if (command === 'pre-push') {
    const settings = config(false), lines = readFileSync(0, 'utf8').trim().split(/\r?\n/).filter(Boolean);
    for (const line of lines) {
      const [_localRef, localSha, remoteRef, remoteSha] = line.split(/\s+/);
      if (remoteRef.startsWith('refs/tags/')) {
        assertImmutableTagUpdate(localSha, remoteSha);
        const tag = remoteRef.slice('refs/tags/'.length);
        const prefix = settings.tagPrefix ?? '';
        if (tag.startsWith(prefix) && /^\d+\.\d+\.\d+$/.test(tag.slice(prefix.length))) {
          const commit = git(['rev-parse', `${localSha}^{commit}`]);
          const version = canonicalVersion(blob(commit, settings.files[0].path), settings.files[0]);
          if (tag !== `${prefix}${version}`) throw new Error(`Tag ${tag} does not match the committed version ${version}.`);
        }
        continue;
      }
      if (/^0+$/.test(localSha)) continue;
      if (!remoteRef.startsWith('refs/heads/')) continue;
      if (!/^0+$/.test(remoteSha)) {
        try { git(['merge-base', '--is-ancestor', remoteSha, localSha]); }
        catch { throw new Error('Non-fast-forward or unknown remote base. Fetch and inspect the branch; this hook never rewrites published history or force-pushes.'); }
      }
      const checked = checkRange(settings, remoteSha, localSha);
      console.log(`Version pre-push: ${checked.length} commit(s) validated for ${remoteRef}.`);
    }
    return;
  }
  if (command === 'release-info') {
    const settings = config(false), sha = head(), version = canonicalVersion(blob('HEAD', settings.files[0].path), settings.files[0]);
    const tag = `${settings.tagPrefix ?? ''}${version}`;
    let taggedCommit = null;
    try { taggedCommit = git(['rev-parse', '--verify', `refs/tags/${tag}^{commit}`]); } catch { /* New version, no existing tag. */ }
    if (taggedCommit && taggedCommit !== sha && sha !== settings.baselineCommit) throw new Error(`Immutable tag collision: ${tag} already identifies another commit.`);
    console.log(JSON.stringify({ sha, version, tag, baseline: sha === settings.baselineCommit, alreadyPublished: taggedCommit === sha }));
    return;
  }
  throw new Error('Commands: install, uninstall, plan, prepare, check, check-range, release-info. Hooks also use context, commit-msg and pre-push.');
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  try { main(); } catch (error) { console.error(`Versioning: ${error.message}\nNo history was rewritten. Review the diff before committing; never force-push to repair a published version.`); process.exitCode = 1; }
}
