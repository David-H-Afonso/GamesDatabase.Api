/* Portable, dependency-free version rules. Never guesses breaking changes from diff size. */
export const TOOL_VERSION = 1;
export const DEFAULT_TYPES = ['feat', 'fix', 'refactor', 'test', 'docs', 'chore', 'build', 'ci'];

export function parseVersion(value) {
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value ?? '');
  if (!match) throw new Error(`Expected a stable MAJOR.MINOR.PATCH version; received ${JSON.stringify(value)}`);
  const parts = match.slice(1).map(Number);
  if (parts.some(part => !Number.isSafeInteger(part))) throw new Error('Version number exceeds the safe integer range.');
  return parts;
}

export function classifyMessage(message, allowedTypes = DEFAULT_TYPES) {
  const subject = String(message).replace(/^\uFEFF/, '').split(/\r?\n/)[0];
  const header = /^([a-z]+)(?:\(([^\r\n()]+)\))?(!)?:\s+\S.*$/i.exec(subject);
  if (!header || !allowedTypes.includes(header[1].toLowerCase())) {
    throw new Error(`Cannot classify this commit safely. Use type(scope): summary, e.g. fix(games): correct dates or feat(home): add shelves. Allowed types: ${allowedTypes.join(', ')}. No version number is required.`);
  }
  const type = header[1].toLowerCase();
  const breaking = Boolean(header[3]) || /(?:^|\r?\n)BREAKING[ -]CHANGE:\s*\S/.test(message);
  return { type, subject, bump: breaking ? 'major' : type === 'feat' ? 'minor' : 'patch', breaking };
}

export function nextVersion(previous, bump) {
  let [major, minor, patch] = parseVersion(previous);
  if (bump === 'major') { major++; minor = 0; patch = 0; }
  else if (bump === 'minor') { minor++; patch = 0; }
  else if (bump === 'patch') patch++;
  else throw new Error(`Unknown bump ${bump}`);
  const result = `${major}.${minor}.${patch}`;
  parseVersion(result);
  return result;
}

// Locate JSON string values without reformatting lockfiles or unstaged edits.
export function locateJson(text, wanted) {
  JSON.parse(text);
  let index = 0, found = null;
  const whitespace = () => { while (/\s/.test(text[index] ?? '') && index < text.length) index++; };
  const string = () => {
    const start = index++;
    while (index < text.length) {
      const character = text[index++];
      if (character === '\\') index++;
      else if (character === '"') return JSON.parse(text.slice(start, index));
    }
    throw new Error('Unterminated JSON string.');
  };
  function value(path) {
    whitespace();
    const start = index;
    if (text[index] === '{') {
      index++; whitespace();
      if (text[index] !== '}') while (true) {
        const key = string(); whitespace(); index++; value([...path, key]); whitespace();
        if (text[index] !== ',') break;
        index++; whitespace();
      }
      index++;
    } else if (text[index] === '[') {
      index++; whitespace();
      let item = 0;
      if (text[index] !== ']') while (true) {
        value([...path, String(item++)]); whitespace();
        if (text[index] !== ',') break;
        index++; whitespace();
      }
      index++;
    } else if (text[index] === '"') string();
    else while (index < text.length && !/[,}\]\s]/.test(text[index])) index++;
    if (JSON.stringify(path) === JSON.stringify(wanted)) found = { start, end: index, value: JSON.parse(text.slice(start, index)) };
  }
  value([]);
  if (!found) throw new Error(`JSON property ${JSON.stringify(wanted)} was not found.`);
  return found;
}

export function targetValues(text, target) {
  if (target.format === 'json') return target.pointers.map(pointer => locateJson(text, pointer).value);
  if (target.format === 'xml') return Object.keys(target.tags).map(tag => {
    if (!/^[A-Za-z][A-Za-z0-9]*$/.test(tag)) throw new Error('Invalid XML version tag.');
    const match = new RegExp(`<${tag}(?:\\s[^>]*)?>([^<]*)<\\/${tag}>`).exec(text);
    if (!match) throw new Error(`XML property ${tag} was not found.`);
    return match[1].trim();
  });
  throw new Error(`Unsupported version format ${target.format}`);
}

export function rewriteTarget(text, target, version) {
  const parts = parseVersion(version);
  if (target.format === 'json') {
    const ranges = target.pointers.map(pointer => locateJson(text, pointer));
    for (const range of ranges.sort((a, b) => b.start - a.start)) {
      if (typeof range.value !== 'string') throw new Error('A JSON version property must be a string.');
      text = text.slice(0, range.start) + JSON.stringify(version) + text.slice(range.end);
    }
    return text;
  }
  if (target.format === 'xml') {
    for (const [tag, kind] of Object.entries(target.tags)) {
      if (!/^[A-Za-z][A-Za-z0-9]*$/.test(tag)) throw new Error('Invalid XML version tag.');
      if (kind === 'assembly' && parts.some(part => part > 65535)) throw new Error('.NET assembly version fields cannot exceed 65535.');
      const pattern = new RegExp(`(<${tag}(?:\\s[^>]*)?>)(\\s*)([^<]*?)(\\s*)(<\\/${tag}>)`);
      if (!pattern.test(text)) throw new Error(`XML property ${tag} was not found.`);
      text = text.replace(pattern, (_, open, leading, _old, trailing, close) => open + leading + (kind === 'assembly' ? `${version}.0` : version) + trailing + close);
    }
    return text;
  }
  throw new Error(`Unsupported version format ${target.format}`);
}

export function canonicalVersion(text, target) {
  const version = targetValues(text, target)[0];
  parseVersion(version);
  return version;
}

export function checkTarget(text, target, version) {
  if (rewriteTarget(text, target, version) !== text) throw new Error(`${target.path} is not synchronized with ${version}.`);
  return true;
}

export function validateConfig(config) {
  if (config.schemaVersion !== 1 || !/^[0-9a-f]{40}$/i.test(config.baselineCommit ?? '')) throw new Error('A schemaVersion=1 and an explicit baselineCommit SHA are required.');
  if (!Array.isArray(config.files) || !config.files.length) throw new Error('At least one version file is required.');
  const paths = new Set();
  for (const file of config.files) {
    if (typeof file.path !== 'string' || /[\\:\0]/.test(file.path) || file.path.startsWith('/') || file.path.startsWith('-') || file.path.split('/').some(part => !part || part === '..' || part === '.git' || part.startsWith('.env'))) throw new Error('Version files must be explicit, safe repository-relative paths.');
    if (paths.has(file.path)) throw new Error('Duplicate version file.');
    paths.add(file.path);
    if (file.format === 'json' && (!Array.isArray(file.pointers) || !file.pointers.length || file.pointers.some(pointer => !Array.isArray(pointer) || pointer.some(key => typeof key !== 'string')))) throw new Error('JSON pointers must be arrays of property names.');
    else if (file.format === 'xml' && (!file.tags || !Object.keys(file.tags).length || Object.values(file.tags).some(kind => !['semver', 'assembly'].includes(kind)))) throw new Error('XML tags must have semver/assembly roles.');
    else if (!['json', 'xml'].includes(file.format)) throw new Error('Unknown version file format.');
  }
  return config;
}

export function verifyCommit({ previousVersion, message, files }, config) {
  const classification = classifyMessage(message, config.allowedTypes ?? DEFAULT_TYPES);
  const expected = nextVersion(previousVersion, classification.bump);
  for (const target of config.files) checkTarget(files[target.path], target, expected);
  return { version: expected, ...classification };
}

export function assertPublicationContext(context, sha, config) {
  if (context.GITHUB_ACTIONS !== 'true' || context.VERSION_PUBLICATION_ALLOWED !== 'true' || context.GITHUB_SHA !== sha || context.GITHUB_REF_TYPE !== 'branch' || !config.releaseBranches?.includes(context.GITHUB_REF_NAME) || !['push', 'workflow_dispatch'].includes(context.GITHUB_EVENT_NAME)) throw new Error('Release publication is permitted only for the tested release-branch HEAD in the gated GitHub workflow.');
  if (sha === config.baselineCommit) throw new Error('The legacy baseline is not a new release.');
  if (!/^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\/(?!\.{1,2}$)[A-Za-z0-9_.-]+$/.test(context.GITHUB_REPOSITORY ?? '')) throw new Error('Invalid GitHub repository identity.');
  return true;
}

export function assertImmutableTagUpdate(localSha, remoteSha) {
  if (/^0+$/.test(localSha)) throw new Error('Published tags are immutable; deleting a remote tag is refused.');
  if (!/^0+$/.test(remoteSha) && localSha !== remoteSha) throw new Error('Published tags are immutable; replacing a remote tag is refused.');
  return true;
}
