#!/usr/bin/env node
// Explicit CI publication only. Local/default execution prints a read-only release plan.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { ROOT, git } from './cli.mjs';
import { assertPublicationContext, canonicalVersion, validateConfig } from './core.mjs';

const config = validateConfig(JSON.parse(readFileSync(resolve(ROOT, '.versioning.json'), 'utf8')));
const sha = git(['rev-parse', 'HEAD']);
const version = canonicalVersion(git(['show', `HEAD:${config.files[0].path}`], { raw: true }), config.files[0]);
const tag = `${config.tagPrefix ?? ''}${version}`;
const publish = process.argv.includes('--publish');
if (!publish) console.log(JSON.stringify({ version, tag, sha, publish: false, note: 'Read-only plan. Publication is restricted to the gated CI workflow.' }, null, 2));
else {
  assertPublicationContext(process.env, sha, config);
  const repository = process.env.GITHUB_REPOSITORY;
  const gh = args => spawnSync('gh', args, { cwd: ROOT, encoding: 'utf8' });
  const ref = gh(['api', `repos/${repository}/git/ref/tags/${encodeURIComponent(tag)}`]);
  if (ref.status === 0) {
    let object = JSON.parse(ref.stdout).object;
    for (let depth = 0; object.type === 'tag' && depth < 5; depth++) {
      const nested = gh(['api', `repos/${repository}/git/tags/${object.sha}`]);
      if (nested.status !== 0) throw new Error(nested.stderr);
      object = JSON.parse(nested.stdout).object;
    }
    if (object.type !== 'commit' || object.sha !== sha) throw new Error(`Immutable tag collision: ${tag} already identifies another commit. It will not be moved or deleted.`);
  } else if (!/HTTP 404|Not Found/i.test(ref.stderr)) throw new Error(ref.stderr || 'Could not verify the remote tag.');
  const existing = gh(['release', 'view', tag, '--repo', repository, '--json', 'tagName']);
  if (existing.status === 0) console.log(`Release ${tag} already exists at the verified commit; left unchanged.`);
  else {
    if (!/not found|HTTP 404/i.test(existing.stderr)) throw new Error(existing.stderr || 'Could not verify the remote release.');
    const result = gh(['release', 'create', tag, '--repo', repository, '--target', sha, '--title', `${config.productName ?? repository.split('/')[1]} ${version}`, '--generate-notes']);
    if (result.status !== 0) throw new Error(result.stderr || 'Release creation failed.');
    console.log(result.stdout.trim());
  }
}
