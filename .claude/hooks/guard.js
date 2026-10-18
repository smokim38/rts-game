#!/usr/bin/env node
'use strict';
// PreToolUse guardrail for the rules in AGENTS.md §5 and for each Claude role's lane.
// Claude Code sends the tool call as JSON on stdin. Exit code 2 blocks the call and
// shows stderr to Claude. Any error also exits 2, so the guard fails closed.
// Tests: node --test .claude/hooks/guard.test.js

const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const SHELL_TOOLS = new Set(['Bash', 'PowerShell']);
const FILE_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);
const MAX_DEPTH = 4;

// Programs that run a string or stdin as code. When one appears, quoted text is checked as a command too.
const INTERPRETERS = new Set([
  'sh', 'bash', 'zsh', 'dash', 'ksh', 'fish', 'csh', 'tcsh', 'pwsh', 'powershell', 'cmd', 'wsl',
  'iex', 'invoke-expression', 'invoke-command', 'icm', 'start-process', 'eval', 'source', 'exec',
  'xargs', 'watch', 'python', 'python3', 'py', 'node', 'perl', 'ruby', 'php',
]);
const WRAPPERS = new Set(['sudo', 'env', 'command', 'nohup', 'time', 'nice']);
const DOWNLOADERS = new Set(['curl', 'wget', 'iwr', 'irm', 'invoke-webrequest', 'invoke-restmethod', 'start-bitstransfer']);
const READERS = new Set([
  'cat', 'type', 'get-content', 'gc', 'more', 'less', 'head', 'tail', 'grep', 'egrep', 'rg', 'select-string',
  'sls', 'findstr', 'ls', 'dir', 'get-item', 'get-childitem', 'gci', 'test-path', 'wc', 'stat', 'file',
]);
const GIT_READS = new Set(['diff', 'log', 'show', 'status', 'blame', 'ls-files', 'check-ignore', 'cat-file']);
const PROTECTED_FILE = /(^|\/)(\.gitleaksignore$|\.git\/hooks(\/|$)|\.git\/(worktrees\/[^/]+\/)?config(\.worktree)?$)/;
const PROTECTED_MENTION = /\.gitleaksignore|\.git\/hooks|\.git\/(worktrees\/[^/]+\/)?config/;

const LANES = {
  'game-designer': { writes: 'docs/design/' },
  'tech-architect': { writes: 'docs/adr/' },
  'code-reviewer': { writes: null },
};

class Blocked extends Error {}
const block = (reason) => {
  throw new Blocked(reason);
};

const norm = (p) => p.replace(/\\/g, '/').toLowerCase();
const cmdName = (text) => norm(text).split('/').pop().replace(/\.(exe|cmd|bat|ps1)$/, '');
const isWord = (t) => !t.redirect && !/\s/.test(t.text);
const isCommandToken = (t) => !t.redirect;

// ---------- Lexer ----------
// Splits a command line into segments (simple commands) the way a shell would, closely
// enough to see what runs. Quoting is honored, so a commit message stays one token.

function lexLine(src, dialect) {
  const esc = dialect === 'PowerShell' ? '`' : '\\';
  const segments = [];
  const parts = []; // quoted text and here-document bodies that may run as code
  const heredocs = [];
  let hasSubst = false;
  let tokens = [];
  let text = null;
  let sep = '';

  const add = (s) => {
    text = (text ?? '') + s;
  };
  const endToken = () => {
    if (text !== null) tokens.push({ text });
    text = null;
  };
  const endSegment = (next) => {
    endToken();
    if (tokens.length) segments.push({ tokens, sep });
    tokens = [];
    sep = next;
  };

  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (c === esc) {
      if (i + 1 < src.length) {
        const n = src[++i];
        if (n === '\r' && src[i + 1] === '\n') i++;
        else if (n !== '\n') add(n);
      }
      continue;
    }
    if (c === "'") {
      const end = src.indexOf("'", i + 1);
      const body = src.slice(i + 1, end < 0 ? src.length : end);
      add(body);
      parts.push({ text: body, subst: false });
      i = end < 0 ? src.length : end;
      continue;
    }
    if (c === '"') {
      let body = '';
      let j = i + 1;
      for (; j < src.length && src[j] !== '"'; j++) {
        const escapes = dialect === 'PowerShell' || '$`"\\\n'.includes(src[j + 1]);
        if (src[j] === esc && j + 1 < src.length && escapes) body += src[++j];
        else body += src[j];
      }
      const subst = body.includes('$(') || (dialect !== 'PowerShell' && body.includes('`'));
      hasSubst ||= subst;
      add(body);
      parts.push({ text: body, subst });
      i = j;
      continue;
    }
    if (c === '<' && dialect !== 'PowerShell' && src[i + 1] === '<' && src[i + 2] !== '<') {
      let j = i + 2;
      if (src[j] === '-') j++;
      while (src[j] === ' ' || src[j] === '\t') j++;
      const m = /^(['"])(.*?)\1|^([^\s;&|<>()]+)/.exec(src.slice(j));
      if (m) {
        endToken();
        tokens.push({ text: '<<', redirect: true });
        heredocs.push({ delim: m[2] ?? m[3], literal: Boolean(m[1]) });
        i = j + m[0].length - 1;
        continue;
      }
    }
    if (c === '\n') {
      endSegment('\n');
      // Here-document bodies start on the line after the operator.
      for (const h of heredocs.splice(0)) {
        const lines = [];
        let k = i + 1;
        while (k < src.length) {
          let e = src.indexOf('\n', k);
          if (e < 0) e = src.length;
          const line = src.slice(k, e).replace(/\r$/, '');
          k = e + 1;
          if (line.replace(/^\t+/, '') === h.delim) break;
          lines.push(line);
        }
        i = k - 1;
        const body = lines.join('\n');
        const subst = !h.literal && (body.includes('$(') || body.includes('`'));
        hasSubst ||= subst;
        parts.push({ text: body, subst });
      }
      continue;
    }
    if (c === ' ' || c === '\t' || c === '\r') {
      endToken();
      continue;
    }
    if (c === '`' && dialect !== 'PowerShell') {
      hasSubst = true;
      endSegment('`');
      continue;
    }
    if (';&|(){}'.includes(c)) {
      if (c === '(' && (text?.endsWith('$') || (text === null && tokens.at(-1)?.redirect))) hasSubst = true;
      endSegment(c);
      continue;
    }
    if (c === '<' || c === '>') {
      endToken();
      tokens.push({ text: c, redirect: true });
      continue;
    }
    add(c);
  }
  endSegment('');
  return { segments, parts, hasSubst };
}

function parse(src, dialect, depth = 0) {
  if (depth > MAX_DEPTH) block('the command nests quoted commands too deeply to check');
  const { segments, parts, hasSubst } = lexLine(src, dialect);
  const runsCode = segments.some((s) =>
    s.tokens.some((t, i) => isCommandToken(t) && (INTERPRETERS.has(cmdName(t.text)) || (i === 0 && t.text === '.'))),
  );
  const all = segments.map((s) => ({ ...s, depth }));
  let subst = hasSubst;
  for (const p of parts) {
    if (!runsCode && !p.subst) continue;
    const inner = parse(p.text, dialect, depth + 1);
    all.push(...inner.segments);
    subst ||= inner.hasSubst;
  }
  return { segments: all, hasSubst: subst };
}

// Arguments after token i, without redirections and their targets.
function argsAfter(tokens, i) {
  const out = [];
  for (let j = i + 1; j < tokens.length; j++) {
    const t = tokens[j];
    if (t.redirect) {
      if (t.text !== '<<') j++;
      continue;
    }
    out.push(t.text);
  }
  return out;
}

function commandIndex(seg) {
  let i = 0;
  while (i < seg.tokens.length) {
    const t = seg.tokens[i].text;
    if (/^(\$env:)?[A-Za-z_][A-Za-z0-9_]*=/.test(t) || WRAPPERS.has(cmdName(t))) i++;
    else break;
  }
  return i < seg.tokens.length && !seg.tokens[i].redirect ? i : -1;
}

// ---------- git ----------

function parseGitArgs(a) {
  let i = 0;
  let dir = null;
  const configs = [];
  while (i < a.length && a[i].startsWith('-')) {
    const t = a[i];
    if (t === '-c' || t === '--config-env') {
      configs.push(a[i + 1] ?? '');
      i += 2;
    } else if (t === '-C') {
      dir = path.join(dir ?? '', a[i + 1] ?? '');
      i += 2;
    } else if (['--git-dir', '--work-tree', '--namespace', '--super-prefix'].includes(t)) {
      i += 2;
    } else {
      if (t.startsWith('--config-env=')) configs.push(t.slice('--config-env='.length));
      i++;
    }
  }
  return { sub: (a[i] ?? '').toLowerCase(), rest: a.slice(i + 1), configs, dir };
}

function isConfigRead(rest) {
  if (rest.some((t) => /^(--unset|--add$|--replace-all|--edit$|-e$|--rename-section|--remove-section)/.test(t))) return false;
  const first = rest.find((t) => !t.startsWith('-'));
  if (first === 'get' || first === 'list') return true;
  return rest.some((t) => /^(--get(-all|-regexp|-urlmatch|-color|-colorbool)?|--list|-l)$/.test(t));
}

function checkCommit(rest) {
  const valueLong = /^--(message|file|author|date|template|reuse-message|reedit-message|fixup|squash|trailer|cleanup|pathspec-from-file)$/;
  for (let i = 0; i < rest.length; i++) {
    const t = rest[i];
    if (t === '--') break;
    if (t.startsWith('--')) {
      if (valueLong.test(t)) i++;
      continue;
    }
    if (!/^-[A-Za-z]/.test(t)) continue;
    for (const ch of t.slice(1)) {
      if (ch === 'n') block('`git commit -n` skips the git hooks, including the gitleaks secret scan (AGENTS.md §5)');
      if ('mFcCt'.includes(ch)) {
        if (t.endsWith(ch)) i++;
        break;
      }
    }
  }
}

function currentBranch(cwd, dir) {
  try {
    const out = execFileSync('git', ['symbolic-ref', '--short', '-q', 'HEAD'], {
      cwd: path.resolve(cwd || '.', dir || '.'),
      encoding: 'utf8',
      timeout: 5000,
      stdio: ['ignore', 'pipe', 'ignore'],
      windowsHide: true,
    });
    return out.trim() || null;
  } catch {
    return null;
  }
}

function checkPush(rest, cwd, dir) {
  const positional = [];
  let hasRepo = false;
  let endOfOptions = false;
  for (let i = 0; i < rest.length; i++) {
    const t = rest[i];
    if (endOfOptions || !t.startsWith('-') || t === '-') {
      positional.push(t);
      continue;
    }
    if (t === '--') {
      endOfOptions = true;
      continue;
    }
    if (t.startsWith('--')) {
      const name = t.split('=')[0];
      // git accepts unique prefixes of long options, so match on prefixes.
      if (/^--(for|mir|al|bra|del|pru)/.test(name)) {
        block(`\`git push ${name}\` can overwrite or delete remote branches (AGENTS.md §5)`);
      }
      if (name === '--repo') hasRepo = true;
      if (!t.includes('=') && ['--repo', '--receive-pack', '--exec', '--upload-pack', '--push-option'].includes(name)) i++;
      continue;
    }
    for (const ch of t.slice(1)) {
      if (ch === 'f') block('`git push -f` is a force push (AGENTS.md §5)');
      if (ch === 'd') block('`git push -d` deletes a remote branch (AGENTS.md §5)');
      if (ch === 'o') {
        if (t.endsWith('o')) i++;
        break;
      }
    }
  }
  const refspecs = hasRepo ? positional : positional.slice(1);
  const branch = () => {
    const b = currentBranch(cwd, dir);
    if (b === null) block('could not tell which branch `git push` would push; name your own branch explicitly');
    return b;
  };
  if (refspecs.length === 0 && branch() === 'main') {
    block('this push would go to `main` (the current branch). Never push to main: push your own branch and open a PR (AGENTS.md §0)');
  }
  for (const r of refspecs) {
    if (r.startsWith('+')) block(`\`${r}\` is a force push (AGENTS.md §5)`);
    if (r.startsWith(':')) block(`\`${r}\` deletes a remote branch (AGENTS.md §5)`);
    const colon = r.indexOf(':');
    let dst = colon > 0 ? r.slice(colon + 1) : r;
    if (dst.includes('*')) block(`the wildcard refspec \`${r}\` can push to main (AGENTS.md §0)`);
    if (dst === 'HEAD' || dst === '@') dst = branch();
    if (dst.replace(/^refs\/heads\//, '').toLowerCase() === 'main') {
      block('never push to `main`: push your own branch and open a PR (AGENTS.md §0, §5)');
    }
  }
}

function checkGit(a, cwd) {
  const { sub, rest, configs, dir } = parseGitArgs(a);
  for (const kv of configs) {
    const key = kv.split('=')[0].toLowerCase();
    if (/^(core\.hookspath|alias\.|include\.|includeif\.|user\.|author\.|committer\.)/.test(key)) {
      block(`\`git -c ${key}\` changes hooks, aliases or identity; git config changes are forbidden (AGENTS.md §5)`);
    }
  }
  if (sub === 'commit') checkCommit(rest);
  if (sub === 'push') checkPush(rest, cwd, dir);
  if (sub === 'config' && !isConfigRead(rest)) {
    block('changing git config is forbidden (AGENTS.md §5). To read a value, use `git config --get <key>` or `git config --list`');
  }
  if (['diff', 'log', 'show'].includes(sub) && rest.some((t) => /^--output(=|$)/.test(t))) {
    block(`\`git ${sub} --output\` writes a file; print to the terminal instead`);
  }
}

function gitSub(seg, i) {
  return parseGitArgs(argsAfter(seg.tokens, i)).sub;
}

// ---------- gh ----------

function hasApprovedLabel(a) {
  const flags = new Set(['--label', '-l', '--add-label', '--remove-label']);
  for (let i = 0; i < a.length; i++) {
    const t = a[i];
    let name = t;
    let value;
    if (t.startsWith('--') && t.includes('=')) [name, value] = [t.slice(0, t.indexOf('=')), t.slice(t.indexOf('=') + 1)];
    else if (flags.has(t)) value = a[i + 1];
    else if (/^-l./.test(t)) [name, value] = ['-l', t.slice(2)];
    if (flags.has(name) && (value ?? '').split(',').some((v) => v.trim().toLowerCase() === 'approved')) return true;
  }
  return false;
}

function approvesPr(a) {
  for (const t of a) {
    if (t === '--approve' || t === '--approve=true') return true;
    if (!/^-[A-Za-z]+$/.test(t)) continue;
    for (const ch of t.slice(1)) {
      if (ch === 'a') return true;
      if ('bF'.includes(ch)) break;
    }
  }
  return false;
}

function checkGhApi(a) {
  const valueFlags = new Set(['-H', '--header', '-q', '--jq', '-t', '--template', '--hostname', '--cache', '-p', '--preview']);
  let method = null;
  let fields = false;
  let endpoint = null;
  for (let i = a.indexOf('api') + 1; i < a.length; i++) {
    const t = a[i];
    if (t === '-X' || t === '--method') method = a[++i] ?? '';
    else if (t.startsWith('--method=')) method = t.slice('--method='.length);
    else if (/^-X./.test(t)) method = t.slice(2);
    else if (['-f', '-F', '--field', '--raw-field', '--input'].includes(t)) {
      fields = true;
      i++;
    } else if (/^(--field=|--raw-field=|--input=|-f.|-F.)/.test(t)) fields = true;
    else if (valueFlags.has(t)) i++;
    else if (!t.startsWith('-') && endpoint === null) endpoint = t;
  }
  if (endpoint === 'graphql') {
    if (a.some((t) => /\bmutation\b/i.test(t) || /=@/.test(t))) block('GraphQL mutations through `gh api` write to GitHub');
    return;
  }
  method = method?.toUpperCase();
  if (method ? method !== 'GET' : fields) {
    block(`\`gh api\` ${method ?? 'POST'} writes to GitHub; only GET reads are allowed. Labels, merges, auth, secrets and repo settings are off limits (AGENTS.md §5)`);
  }
}

function checkGh(a) {
  const words = [];
  for (let i = 0; i < a.length && words.length < 3; i++) {
    const t = a[i];
    if (t === '-R' || t === '--repo') i++;
    else if (!t.startsWith('-')) words.push(t.toLowerCase());
  }
  const [group, action, third] = words;
  const forbidden = {
    auth: null,
    secret: null,
    label: null,
    variable: ['set', 'delete'],
    'ssh-key': ['add', 'delete'],
    'gpg-key': ['add', 'delete'],
    alias: ['set', 'import', 'delete'],
    repo: ['edit', 'delete', 'rename', 'archive', 'unarchive', 'set-default'],
  };
  if (Object.hasOwn(forbidden, group) && (forbidden[group] === null || forbidden[group].includes(action))) {
    const why = group === 'label' ? 'labels are managed by the owner; `approved` is owner-only' : 'login, secrets and repository settings are owner-only';
    block(`\`gh ${group}${forbidden[group] ? ` ${action}` : ''}\` is forbidden: ${why} (AGENTS.md §5)`);
  }
  if (group === 'repo' && action === 'deploy-key' && ['add', 'delete'].includes(third)) block('deploy keys are repository settings (AGENTS.md §5)');
  if (group === 'pr' && action === 'merge') block('only the owner merges pull requests (AGENTS.md §0)');
  if (group === 'pr' && action === 'review' && approvesPr(a)) {
    block('agents never approve on GitHub; post the review as a comment (AGENTS.md §0, §6)');
  }
  if ((group === 'issue' || group === 'pr') && (action === 'create' || action === 'edit') && hasApprovedLabel(a)) {
    block('only the owner adds or removes the `approved` label (AGENTS.md §3)');
  }
  if (group === 'api') checkGhApi(a);
}

// ---------- shell commands ----------

function isReadSegment(seg) {
  if (seg.tokens.some((t) => t.redirect && t.text === '>')) return false;
  const i = commandIndex(seg);
  if (i < 0) return false;
  const name = cmdName(seg.tokens[i].text);
  return READERS.has(name) || (name === 'git' && GIT_READS.has(gitSub(seg, i)));
}

function isGitConfigRead(seg) {
  const i = seg.tokens.findIndex((t) => isCommandToken(t) && cmdName(t.text) === 'git');
  if (i < 0) return false;
  const { sub, rest } = parseGitArgs(argsAfter(seg.tokens, i));
  return sub === 'config' && isConfigRead(rest);
}

function checkSegment(seg, input, dialect) {
  const isPwsh = seg.tokens.some((t) => isCommandToken(t) && ['pwsh', 'powershell'].includes(cmdName(t.text)));
  for (const t of seg.tokens.filter(isWord)) {
    const lower = norm(t.text);
    if (lower.includes('--no-veri')) block('`--no-verify` skips the git hooks, including the gitleaks secret scan (AGENTS.md §5)');
    if (/git_config_(parameters|count|key_|value_|global|system)/.test(lower)) {
      block('GIT_CONFIG_* variables change git config for the command (AGENTS.md §5)');
    }
    if (isPwsh && /^-(e|ec|ea|en\w*)$/.test(lower)) block('an encoded PowerShell command cannot be checked');
    if (PROTECTED_MENTION.test(lower) && !isReadSegment(seg)) {
      block(`\`${t.text}\` is protected (.gitleaksignore is owner-only; .git/config and .git/hooks are git config). Only read it (AGENTS.md §5, §6)`);
    }
    if (lower.includes('hookspath') && !isGitConfigRead(seg)) block('core.hooksPath must stay `.githooks`; git config changes are forbidden (AGENTS.md §5)');
  }
  seg.tokens.forEach((t, i) => {
    if (!isCommandToken(t)) return;
    const name = cmdName(t.text);
    if (name === 'git') checkGit(argsAfter(seg.tokens, i), input.cwd);
    if (name === 'gh') checkGh(argsAfter(seg.tokens, i));
  });
  if (dialect === 'PowerShell' && seg.tokens.some((t) => t.text === '--%')) block('the PowerShell stop-parsing token `--%` hides arguments from the guard');
}

function checkDownloadPipe(segments, command) {
  const hasCmd = (seg, set) => seg.tokens.some((t) => isCommandToken(t) && set.has(cmdName(t.text)));
  const isDownload = (s) => hasCmd(s, DOWNLOADERS) || s.tokens.some((t) => /downloadstring|downloaddata|downloadfile|net\.webclient/i.test(t.text));
  const isShell = (s) => hasCmd(s, INTERPRETERS);
  if (/frombase64string/i.test(command) && /\b(iex|invoke-expression|invoke-command|scriptblock)\b/i.test(command)) {
    block('running decoded base64 cannot be checked');
  }
  if (!segments.some(isDownload) || !segments.some(isShell)) return;
  const piped = segments.some(
    (s) => (isShell(s) && s.sep === '|') || (isDownload(s) && (['(', '`'].includes(s.sep) || s.depth > 0)),
  );
  if (piped) block('piping a download into a shell is forbidden (AGENTS.md §5). Download, read it, then ask the owner');
}

function checkReviewerShell(segments, hasSubst) {
  if (hasSubst) block('code-reviewer commands may not use command substitution');
  for (const seg of segments.filter((s) => s.depth === 0)) {
    if (seg.tokens.some((t) => t.redirect && t.text !== '<<')) block('code-reviewer commands may not redirect output or input');
    const name = cmdName(seg.tokens[0].text);
    const a = argsAfter(seg.tokens, 0);
    const ok =
      (name === 'git' && ['diff', 'log', 'show'].includes(a[0])) ||
      (name === 'gh' && a[0] === 'pr' && ['view', 'diff', 'checks', 'comment'].includes(a[1])) ||
      (name === 'gh' && a[0] === 'issue' && a[1] === 'view');
    if (!ok) {
      block('code-reviewer may run only `gh pr view|diff|checks|comment`, `gh issue view` and `git diff|log|show` (.claude/agents/code-reviewer.md)');
    }
    // A comment body must come from the command itself, never from a local file.
    a.forEach((t, i) => {
      const file = t === '--body-file' || t === '-F' ? a[i + 1] : t.startsWith('--body-file=') ? t.slice('--body-file='.length) : null;
      if (file !== null && file !== '-') block('code-reviewer posts with `--body-file -` and a quoted here-document, never a local file');
    });
  }
}

function checkShell(command, dialect, input) {
  const { segments, hasSubst } = parse(command, dialect);
  for (const seg of segments) checkSegment(seg, input, dialect);
  checkDownloadPipe(segments, command);
  const role = input.agent_type;
  if (Object.hasOwn(LANES, role ?? '')) {
    if (role !== 'code-reviewer') block(`the ${role} role has no shell access (.claude/agents/${role}.md)`);
    checkReviewerShell(segments, hasSubst);
  }
}

// ---------- file edits ----------

function checkFile(input) {
  const ti = input.tool_input ?? {};
  const target = input.tool_name === 'NotebookEdit' ? ti.notebook_path : ti.file_path;
  if (typeof target !== 'string' || target === '') block('the tool input has no file path');
  const root = path.resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || '.');
  const abs = path.resolve(input.cwd || root, target);
  if (PROTECTED_FILE.test(norm(abs))) {
    block(`\`${target}\` is protected (.gitleaksignore is owner-only; .git/config and .git/hooks are git config) (AGENTS.md §5, §6)`);
  }
  const role = input.agent_type;
  if (!Object.hasOwn(LANES, role ?? '')) return;
  const lane = LANES[role];
  if (lane.writes === null) block('code-reviewer is read-only and never edits files (AGENTS.md §6)');
  const rel = norm(path.relative(root, abs));
  if (path.isAbsolute(rel) || !rel.startsWith(lane.writes)) {
    block(`${role} may write only under ${lane.writes} (.claude/agents/${role}.md)`);
  }
}

function check(input) {
  if (!input || typeof input !== 'object') block('the hook input is not a JSON object');
  const tool = input.tool_name;
  if (SHELL_TOOLS.has(tool)) {
    const command = input.tool_input?.command;
    if (typeof command !== 'string') block('the tool input has no command');
    checkShell(command, tool, input);
  } else if (FILE_TOOLS.has(tool)) {
    checkFile(input);
  }
}

function main() {
  try {
    check(JSON.parse(fs.readFileSync(0, 'utf8')));
  } catch (err) {
    const why = err instanceof Blocked ? err.message : `the guard could not check this call (${err.message})`;
    process.stderr.write(`Blocked by .claude/hooks/guard.js: ${why}.\nDon't retry in another form. If it is really needed, stop and ask the owner.\n`);
    process.exit(2);
  }
  process.exit(0);
}

if (require.main === module) main();

module.exports = { check };
