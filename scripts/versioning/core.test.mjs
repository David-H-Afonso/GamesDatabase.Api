import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { assertImmutableTagUpdate, assertPublicationContext, canonicalVersion, classifyMessage, locateJson, nextVersion, parseVersion, rewriteTarget, validateConfig, verifyCommit } from './core.mjs';

const json = { path: 'package.json', format: 'json', pointers: [['version']] };
const lock = { path: 'package-lock.json', format: 'json', pointers: [['version'], ['packages', '', 'version']] };
const xml = { path: 'Api.csproj', format: 'xml', tags: { Version: 'semver', AssemblyVersion: 'assembly', FileVersion: 'assembly' } };
const settings = { schemaVersion: 1, baselineCommit: 'a'.repeat(40), files: [json] };

test('fix and every non-feature allowed type produce patch, including docs/ci/test', () => {
  for (const type of ['fix', 'docs', 'ci', 'test', 'refactor', 'chore', 'build']) assert.equal(classifyMessage(`${type}(area): meaningful change`).bump, 'patch');
});
test('features produce minor and explicit incompatible changes produce major', () => {
  assert.equal(classifyMessage('feat(home): add configurable shelves').bump, 'minor');
  assert.equal(classifyMessage('feat(api)!: remove an incompatible contract').bump, 'major');
  assert.equal(classifyMessage('fix(api): change contract\n\nBREAKING CHANGE: old callers must migrate').bump, 'major');
  assert.equal(classifyMessage('refactor!: change supported runtime').bump, 'major');
});
test('unknown or vague messages are rejected, not assigned an invented major/minor', () => {
  for (const value of ['Update files', '', 'add: feature', 'feat: ', 'fix(): missing scope', 'feat(x) missing colon']) assert.throws(() => classifyMessage(value));
});
test('custom project types can be explicitly configured without changing the engine', () => {
  assert.equal(classifyMessage('perf(query): optimize', ['perf']).bump, 'patch');
  assert.throws(() => classifyMessage('perf(query): optimize'));
});
test('semver resets lower positions, accepts zero and rejects malformed/unsafe versions', () => {
  assert.equal(nextVersion('2.3.4', 'patch'), '2.3.5');
  assert.equal(nextVersion('2.3.4', 'minor'), '2.4.0');
  assert.equal(nextVersion('2.3.4', 'major'), '3.0.0');
  assert.equal(nextVersion('0.0.0', 'patch'), '0.0.1');
  for (const value of ['v2.0.0', '02.0.0', '2.0', '2.0.0-beta.1', '2.0.0+sha', '-1.0.0', '9007199254740992.0.0']) assert.throws(() => parseVersion(value));
});
test('each step in a commit sequence has its own calculated version', () => {
  let version = '2.0.0';
  const results = ['fix: one', 'feat: two', 'fix: three', 'docs: four', 'feat!: five'].map(message => version = nextVersion(version, classifyMessage(message).bump));
  assert.deepEqual(results, ['2.0.1', '2.1.0', '2.1.1', '2.1.2', '3.0.0']);
});
test('JSON editing changes only the requested value and preserves indentation and EOL', () => {
  const source = '{\r\n\t"name": "fixture",\r\n\t"version": "2.0.0",\r\n\t"dependencies": { "x": "3.4.5" }\r\n}\r\n';
  assert.equal(rewriteTarget(source, json, '2.1.0'), source.replace('"2.0.0"', '"2.1.0"'));
  assert.equal(canonicalVersion(source, json), '2.0.0');
});
test('lock root and empty-package root versions update without changing dependency versions', () => {
  const source = '{"version":"2.0.0","packages":{"":{"version":"2.0.0"},"node_modules/x":{"version":"2.0.0"}}}\n';
  const result = rewriteTarget(source, lock, '2.1.0');
  assert.equal(JSON.parse(result).version, '2.1.0');
  assert.equal(JSON.parse(result).packages[''].version, '2.1.0');
  assert.equal(JSON.parse(result).packages['node_modules/x'].version, '2.0.0');
  assert.ok(result.endsWith('\n'));
});
test('JSON locator handles escaped strings, nested arrays and an empty key', () => {
  const source = '{"text":"a\\\"b","x":[{"":"2.0.0"}],"version":"2.0.0"}';
  assert.equal(locateJson(source, ['x', '0', '']).value, '2.0.0');
  assert.throws(() => locateJson(source, ['missing']));
});
test('XML uses stable app semver and numeric four-part assembly/file versions', () => {
  const source = '<Project>\r\n  <Version>2.0.0</Version>\r\n  <AssemblyVersion>2.0.0.0</AssemblyVersion>\r\n  <FileVersion>2.0.0.0</FileVersion>\r\n</Project>\r\n';
  const result = rewriteTarget(source, xml, '2.1.0');
  assert.equal(result, source.replaceAll('2.0.0', '2.1.0'));
  assert.equal(canonicalVersion(source, xml), '2.0.0');
  assert.throws(() => rewriteTarget(source, xml, '2.1.65536'));
});
test('already prepared files are idempotent and do not bump twice', () => {
  const source = '{"version":"2.1.0"}\n';
  assert.equal(rewriteTarget(source, json, '2.1.0'), source);
});
test('staged and working texts preserve different unrelated partial edits', () => {
  const indexed = '{"version":"2.0.0","dependency":"staged"}\n';
  const working = '{"version":"2.0.0","dependency":"unstaged","extra":true}\n';
  const a = rewriteTarget(indexed, json, '2.1.0'), b = rewriteTarget(working, json, '2.1.0');
  assert.equal(JSON.parse(a).dependency, 'staged');
  assert.equal(JSON.parse(a).extra, undefined);
  assert.equal(JSON.parse(b).dependency, 'unstaged');
  assert.equal(JSON.parse(b).extra, true);
});
test('configuration requires an explicit baseline and safe version paths', () => {
  assert.equal(validateConfig(settings), settings);
  for (const path of ['../other/package.json', '/secret.json', '.git/config', '.env', 'C:\\secret.json']) assert.throws(() => validateConfig({ ...settings, files: [{ ...json, path }] }));
  assert.throws(() => validateConfig({ ...settings, baselineCommit: 'HEAD' }));
});
test('a mismatched committed version or unsynchronized lock is rejected before push', () => {
  assert.equal(verifyCommit({ previousVersion: '2.0.0', message: 'feat: home', files: { 'package.json': '{"version":"2.1.0"}' } }, settings).version, '2.1.0');
  assert.throws(() => verifyCommit({ previousVersion: '2.0.0', message: 'feat: home', files: { 'package.json': '{"version":"2.0.1"}' } }, settings));
});
test('CLI does not create commits, tags, pushes, rebases, resets, or broad git-add operations', () => {
  const code = readFileSync(new URL('./cli.mjs', import.meta.url), 'utf8');
  assert.doesNotMatch(code, /git\(\[\s*['"](?:commit|tag|push|rebase|reset|checkout|add)['"]/);
  assert.match(code, /update-index/);
  assert.match(code, /Refusing to amend/);
});
test('planning is read-only against the actual repository and writes no version fields', () => {
  const root = fileURLToPath(new URL('../..', import.meta.url));
  const localConfig = JSON.parse(readFileSync(resolve(root, '.versioning.json'), 'utf8'));
  const digest = () => localConfig.files.map(file => createHash('sha256').update(readFileSync(resolve(root, file.path))).digest('hex'));
  const before = digest();
  const result = spawnSync(process.execPath, [fileURLToPath(new URL('./cli.mjs', import.meta.url)), 'plan', '--message', 'feat(versioning): verify a read-only plan'], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const plan = JSON.parse(result.stdout);
  assert.equal(plan.staged, false);
  assert.equal(plan.committed, false);
  assert.equal(plan.version, nextVersion(plan.from, 'minor'));
  assert.deepEqual(digest(), before);
});

test('publication context fails closed without executing any publication command', () => {
  const sha = 'b'.repeat(40), config = { ...settings, releaseBranches: ['main'] };
  const context = { GITHUB_ACTIONS: 'true', VERSION_PUBLICATION_ALLOWED: 'true', GITHUB_SHA: sha, GITHUB_REF_TYPE: 'branch', GITHUB_REF_NAME: 'main', GITHUB_EVENT_NAME: 'push', GITHUB_REPOSITORY: 'owner/repository' };
  assert.equal(assertPublicationContext(context, sha, config), true);
  for (const key of Object.keys(context)) { const invalid = { ...context }; delete invalid[key]; assert.throws(() => assertPublicationContext(invalid, sha, config), key); }
  for (const change of [{ GITHUB_SHA: 'c'.repeat(40) }, { GITHUB_REF_NAME: 'feature' }, { GITHUB_EVENT_NAME: 'pull_request' }, { GITHUB_REF_TYPE: 'tag' }, { GITHUB_REPOSITORY: '../other' }]) assert.throws(() => assertPublicationContext({ ...context, ...change }, sha, config));
  assert.throws(() => assertPublicationContext(context, sha, { ...config, baselineCommit: sha }));
});

test('existing remote tags cannot be replaced or deleted, even with a matching version string', () => {
  const zero = '0'.repeat(40), sha = 'a'.repeat(40), other = 'b'.repeat(40);
  assert.equal(assertImmutableTagUpdate(sha, zero), true);
  assert.equal(assertImmutableTagUpdate(sha, sha), true);
  assert.throws(() => assertImmutableTagUpdate(other, sha));
  assert.throws(() => assertImmutableTagUpdate(zero, sha));
});
