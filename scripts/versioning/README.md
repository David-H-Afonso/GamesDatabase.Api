# Automatic versions before commit

Portable Node 22+ tooling, no npm dependency and no AI/network classification.

## Rules

- `feat(scope): ...` → MINOR.
- `fix`, `refactor`, `test`, `docs`, `chore`, `build`, `ci` → PATCH, so **every allowed commit has a version**, including documentation/tooling.
- `!` after type/scope or a `BREAKING CHANGE:` footer → MAJOR.
- Vague/untyped messages stop before commit rather than guessing compatibility from file count.
- Existing numeric tags are retained (`tagPrefix: ""`). Old public history before the explicit baseline is not rewritten or retrospectively versioned.

The number is derived from the **parent's committed version**, not the working version. Preparing twice is idempotent. Front/API are independent repositories.

## Install / undo

```sh
node scripts/versioning/cli.mjs install
node scripts/versioning/cli.mjs uninstall
```

Installation is local per clone, never global. Existing hook managers or active default hooks are not overwritten. The previous setting is recorded under the repository's Git directory, not in source control. Linux users should stage the executable mode of the three hooks after installation. No commit, tag, push, release, migration or deployment is performed by installing.

## Normal work

Stage your intended code and create a normal Conventional Commit from Git/VS Code. `commit-msg` calculates the version and adds only its version-field changes to that **same commit**, including root lock metadata or .NET numeric assembly fields. Other unstaged edits in the same file remain unstaged. The setup files must be staged in the first setup commit.

For agent work, prepare the **exact proposed message before asking for commit approval**:

```sh
node scripts/versioning/cli.mjs plan --message "feat(home): add configurable shelves"
node scripts/versioning/cli.mjs prepare --message "feat(home): add configurable shelves"
```

`plan` is read-only. `prepare` updates working version fields only; it does not stage, commit or push. Inspect/stage the exact diff and obtain fresh approval. Using the same message for the eventual commit does not bump twice.

## Safety before push

The pre-push hook checks every new first-parent commit after the baseline and verifies its increment and synchronized files. CI repeats the check so `--no-verify` cannot bypass the remote gate. It never amends, rebases, force-pushes, tags or publishes. A failed check blocks with diagnostics.

An explicitly requested amend is calculated from the original parent; commits already referenced by a tag or remote-tracking branch are refused. Do not use `git commit -C HEAD` as a new-commit shortcut (Git reports the same hook context as amend); use a normal message. Rebase/cherry-pick/merge workflows require revalidation; the supported initial workflow is the owner's direct, linear main/master work. Never rewrite public history to fix a number. Repairing an unpublished commit still requires explicit approval when an agent is operating.

## CI / release identity

Each commit contains its own number. When several commits are pushed together, CI validates all, but only the tested/build HEAD gets a published Docker artifact and GitHub release. Artifact/release numbers come from that committed HEAD. Publication is not production deployment. GitHub-token-created tags must not be expected to trigger a second workflow; release/build steps must be explicitly chained in the workflow.

## Export to another project

```sh
node scripts/versioning/export.mjs --to /explicit/project/path
node scripts/versioning/export.mjs --to /explicit/project/path --apply
```

The first command is a plan. The exporter only copies this kit and refuses to overwrite different files. It does **not** copy a baseline/config, bump a target version or install hooks. Add the target `.versioning.json` with an explicit baseline SHA and files: JSON property paths (including `packages[""].version`) or XML tags (`semver`/`assembly`). Then run its tests and install in that clone. No other workspace project is scanned or modified.

## Verification

```sh
node --test scripts/versioning/core.test.mjs
node scripts/versioning/cli.mjs check
```

Unit tests and read-only real-repository plans do not create real commits or pushes. Git-history rewrites and publication are deliberately not exercised on application repositories.
