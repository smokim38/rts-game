#!/usr/bin/env node
'use strict';
// PreToolUse guardrail for the rules in AGENTS.md §5 and for each Claude role's lane.
// This file is the reviewed source. The active copy is installed by the owner as a managed
// hook (tools/install-claude-guard.ps1), so editing this file never changes the rules in force.
// It applies to every Claude Code session on the PC: a session opened anywhere can reach the
// studio repository by path, so there is no scope check to get around.
// Claude Code sends the tool call as JSON on stdin. Exit code 2 blocks the call and shows
// stderr to Claude. Errors also exit 2, and the managed hook sets onFailure "block".
// Tests: node --test .claude/hooks/guard.test.js

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const SHELL_TOOLS = new Set(['Bash', 'PowerShell']);
const FILE_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);
const MAX_DEPTH = 4;

// Programs that run a string or stdin as code.
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
  'get-itemproperty', 'gp',
]);
const DIRECTORY_CHANGERS = new Set(['cd', 'pushd', 'chdir', 'set-location', 'sl', 'push-location']);
const GIT_READS = new Set(['diff', 'log', 'show', 'status', 'blame', 'ls-files', 'check-ignore', 'cat-file']);
// Built-in git commands. Anything else (an alias or a git-* program) is blocked, because the guard can't see what it runs.
const GIT_COMMANDS = new Set([
  'add', 'am', 'apply', 'archive', 'bisect', 'blame', 'branch', 'bundle', 'cat-file', 'check-attr', 'check-ignore',
  'check-ref-format', 'checkout', 'cherry', 'cherry-pick', 'clean', 'clone', 'commit', 'commit-graph', 'config',
  'count-objects', 'describe', 'diff', 'diff-files', 'diff-index', 'diff-tree', 'difftool', 'fetch', 'for-each-ref',
  'format-patch', 'fsck', 'gc', 'grep', 'hash-object', 'help', 'init', 'interpret-trailers', 'lfs', 'log', 'ls-files',
  'ls-remote', 'ls-tree', 'maintenance', 'merge', 'merge-base', 'merge-file', 'mergetool', 'mv', 'name-rev', 'notes',
  'pull', 'push', 'range-diff', 'read-tree', 'rebase', 'reflog', 'remote', 'repack', 'replace', 'rerere', 'reset',
  'restore', 'rev-list', 'rev-parse', 'revert', 'rm', 'shortlog', 'show', 'show-branch', 'show-ref', 'sparse-checkout',
  'stash', 'status', 'submodule', 'switch', 'symbolic-ref', 'tag', 'update-index', 'update-ref', 'var', 'verify-commit',
  'verify-tag', 'version', 'whatchanged', 'worktree', 'write-tree',
]);
// Built-in gh commands, including the built-in `co` alias. Other names are aliases or extensions.
const GH_COMMANDS = new Set([
  'agent-task', 'alias', 'api', 'attestation', 'auth', 'browse', 'cache', 'co', 'codespace', 'completion', 'config',
  'copilot', 'extension', 'gist', 'gpg-key', 'help', 'issue', 'label', 'org', 'pr', 'preview', 'project', 'release',
  'repo', 'ruleset', 'run', 'search', 'secret', 'ssh-key', 'status', 'variable', 'workflow',
]);

// Files that hold the rules in force or run code: owner-only. Shell profiles and Claude's
// session environment file set the environment of later tool calls, so they are included.
const PROTECTED_FILE = /(^|\/)(\.gitleaksignore|\.mcp\.json|\.claude\/settings(\.local)?\.json)$|(^|\/)\.githooks(\/|$)|(^|\/)\.git\/(hooks(\/|$)|(worktrees\/[^/]+\/)?config(\.worktree)?$)|^[a-z]:\/(program files|programdata)\/claudecode(\/|$)|(^|\/)\.(bashrc|bash_profile|bash_login|bash_aliases|profile|zshrc|zshenv|zprofile)$|(^|\/)(windows)?powershell\/[^/]*profile\.ps1$|(^|\/)\.claude\/session-env(\/|$)/;
const PROTECTED_MENTION = /\.gitleaksignore|\.githooks|\.git\/hooks|\.git\/(worktrees\/[^/]+\/)?config|\.mcp\.json|\.claude\/settings|\.claude\.json|\.gitconfig|\.config\/git|claudecode|managed-settings\.json|(^|\/)settings(\.local)?\.json$|\.(bashrc|bash_profile|bash_login|bash_aliases|zshrc|zshenv|zprofile)\b|(^|\/)\.profile$|profile\.ps1|\$profile\b|session-env|claude_env_file/;
const PROTECTED_DIRECTORY = /(^|\/)(\.git|\.githooks|\.claude|\.config)(\/|$)|claudecode/;

const LANES = {
  'game-designer': { file: /^docs\/design\/([a-z0-9._-]+\/)*[a-z0-9._-]+\.md$/, where: 'Markdown files under docs/design/' },
  'tech-architect': { file: /^docs\/adr\/(?!0000-)\d{4}-[a-z0-9-]+\.md$/, where: 'proposed ADRs (docs/adr/NNNN-title.md)' },
  'code-reviewer': { file: null, where: 'nothing' },
};

class Blocked extends Error {}
const block = (reason) => {
  throw new Blocked(reason);
};

const norm = (p) => p.replace(/\\/g, '/').toLowerCase();
const cmdName = (text) => norm(text).split('/').pop().replace(/\.(exe|cmd|bat|ps1)$/, '');
const isWord = (t) => !t.redirect && !/\s/.test(t.text);
const isCommandToken = (t) => !t.redirect;
const hasCommand = (seg, names) => seg.tokens.some((t) => isCommandToken(t) && names.has(cmdName(t.text)));

// ---------- Lexer ----------
// Splits a command line into segments (simple commands) the way a shell would, closely
// enough to see what runs. Quoting is honored, so a commit message stays one token.
// Tokens built from variables or expansions are marked dynamic: their value is unknown here.

function lexLine(src, dialect) {
  const bash = dialect !== 'PowerShell';
  const esc = bash ? '\\' : '`';
  const segments = [];
  const parts = []; // quoted text and here-document bodies that may run as code
  const heredocs = [];
  let hasSubst = false;
  let ansi = false;
  let wordBrace = false;
  let tokens = [];
  let text = null;
  let dynamic = false;
  let sep = '';

  const add = (s) => {
    text = (text ?? '') + s;
  };
  const endToken = () => {
    if (text !== null) tokens.push({ text, dynamic });
    text = null;
    dynamic = false;
  };
  const endSegment = (next) => {
    endToken();
    if (tokens.length) segments.push({ tokens, sep });
    tokens = [];
    sep = next;
  };
  const expands = (next) => (bash ? /[A-Za-z_{(0-9@*#?!$-]/ : /[A-Za-z_{(?^$]/).test(next ?? '');

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
    if (c === '$' && bash && (src[i + 1] === "'" || src[i + 1] === '"')) {
      ansi = true;
      continue;
    }
    if (c === '$' && expands(src[i + 1])) {
      dynamic = true;
      if (src[i + 1] === '(') hasSubst = true;
    }
    if (c === '@' && !bash && text === null && /[A-Za-z_(]/.test(src[i + 1] ?? '')) dynamic = true;
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
        const escapes = !bash || '$`"\\\n'.includes(src[j + 1]);
        if (src[j] === esc && j + 1 < src.length && escapes) body += src[++j];
        else {
          if (src[j] === '$' && expands(src[j + 1])) dynamic = true;
          body += src[j];
        }
      }
      const subst = body.includes('$(') || (bash && body.includes('`'));
      hasSubst ||= subst;
      add(body);
      parts.push({ text: body, subst });
      i = j;
      continue;
    }
    if (c === '<' && bash && src[i + 1] === '<' && src[i + 2] !== '<') {
      let j = i + 2;
      if (src[j] === '-') j++;
      while (src[j] === ' ' || src[j] === '\t') j++;
      const m = /^(['"])(.*?)\1|^([^\s;&|<>()]+)/.exec(src.slice(j));
      if (m) {
        endToken();
        tokens.push({ text: '<<', redirect: true, literal: Boolean(m[1]) });
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
        const subst = !h.literal && (body.includes('$') || body.includes('`'));
        hasSubst ||= subst;
        parts.push({ text: body, subst, heredoc: true });
      }
      continue;
    }
    if (c === ' ' || c === '\t' || c === '\r') {
      endToken();
      continue;
    }
    if (c === '`' && bash) {
      hasSubst = true;
      endSegment('`');
      continue;
    }
    // Bash: `{` and `}` group commands only as words of their own. Inside a word a simple
    // `{name}` (a gh placeholder) is literal; any other braces are a brace expansion.
    if (bash && (c === '{' || c === '}')) {
      const next = src[i + 1] ?? ' ';
      if (text === null && (c === '{' ? /\s/.test(next) : /[\s;&|)]/.test(next))) {
        endSegment(c);
        continue;
      }
      if (c === '{' && !/^[A-Za-z0-9_\-/:@]*\}/.test(src.slice(i + 1))) dynamic = true;
      add(c);
      continue;
    }
    // Unquoted wildcards expand to file names the guard can't know.
    if (bash && (c === '*' || c === '?' || (c === '[' && /^[^\s\]]+\]/.test(src.slice(i + 1))))) dynamic = true;
    // PowerShell reads `{…}` inside a word as a script block, which splits the word.
    if (!bash && c === '{' && text !== null && text !== '@' && text !== '$') wordBrace = true;
    if (';&|(){}'.includes(c)) {
      if (c === '(' && text === null && tokens.at(-1)?.redirect) hasSubst = true;
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
  return { segments, parts, hasSubst, ansi, wordBrace };
}

// `strict` is false only for code passed to an interpreter, which is parsed in both dialects
// because its language is unknown.
function parse(src, dialect, depth = 0, strict = true) {
  if (depth > MAX_DEPTH) block('the command nests quoted commands too deeply to check');
  const top = lexLine(src, dialect);
  if (top.ansi) block("ANSI-C quoting ($'…') can't be checked; write the argument out plainly");
  if (strict && top.wordBrace) block("PowerShell reads `{…}` inside a word as a script block; quote arguments that contain braces, e.g. 'repos/{owner}/{repo}'");
  const runsCode = top.segments.some((s) =>
    s.tokens.some((t, i) => isCommandToken(t) && (INTERPRETERS.has(cmdName(t.text)) || (i === 0 && t.text === '.'))),
  );
  const segments = top.segments.map((s) => ({ ...s, depth }));
  let hasSubst = top.hasSubst;
  if (runsCode) {
    // Code handed to another shell or interpreter. git, gh and downloads must run at the top level,
    // where the guard can read them, so any sign of them in that code blocks the call.
    const code = [...top.parts.map((p) => p.text)];
    for (const s of top.segments) for (const t of s.tokens) if (!t.redirect && /[\s;&|]/.test(t.text)) code.push(t.text);
    for (const text of code) checkInterpreterCode(text, depth);
  }
  for (const p of top.parts) {
    if (!p.subst) continue;
    const inner = parse(p.text, dialect, depth + 1, strict);
    segments.push(...inner.segments);
    hasSubst ||= inner.hasSubst;
  }
  return { segments, hasSubst };
}

function checkInterpreterCode(text, depth) {
  const flat = text.replace(/["'`^\\]/g, '');
  if (/\b(git|gh|curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod)(\.exe)?\b/i.test(flat)) {
    block('run git, gh and downloads directly, not inside code passed to another shell or interpreter');
  }
  for (const dialect of ['Bash', 'PowerShell']) {
    const inner = parse(text, dialect, depth + 1, false);
    for (const seg of inner.segments) checkEnvironment(seg, text);
  }
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
    else if (/^\$[A-Za-z_]\w*$/.test(t) && seg.tokens[i + 1]?.text === '=') i += 2; // PowerShell `$x = <command>`
    else break;
  }
  return i < seg.tokens.length && !seg.tokens[i].redirect ? i : -1;
}

// ---------- git ----------

function parseGitArgs(a) {
  let i = 0;
  while (i < a.length && a[i].startsWith('-')) {
    const t = a[i];
    if (t === '-c' || t === '--config-env' || t.startsWith('--config-env=')) {
      block('`git -c` and `--config-env` change git config for the command; git config changes are forbidden (AGENTS.md §5)');
    }
    if (t === '--exec-path' || t.startsWith('--exec-path=')) block('`git --exec-path` changes which programs git runs');
    i += ['-C', '--git-dir', '--work-tree', '--namespace', '--super-prefix'].includes(t) ? 2 : 1;
  }
  return { sub: (a[i] ?? '').toLowerCase(), rest: a.slice(i + 1), hasSub: i < a.length };
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

const PUSH_FORM = '`git push [-u] origin <branch>:refs/heads/<branch>` as a command of its own';
const BRANCH = /^(?!-)(?!.*\.\.)[A-Za-z0-9._/-]+$/;

function checkPush(rest) {
  const options = new Set(['-u', '--set-upstream', '-q', '--quiet', '-v', '--verbose', '--dry-run', '--porcelain']);
  const positional = [];
  for (const t of rest) {
    if (options.has(t)) continue;
    if (t.startsWith('-')) block(`\`git push ${t}\` is not allowed; use ${PUSH_FORM}`);
    positional.push(t);
  }
  const m = positional.length === 2 && positional[0] === 'origin' ? /^([^:]+):refs\/heads\/(.+)$/.exec(positional[1]) : null;
  if (!m || !(m[1] === 'HEAD' || BRANCH.test(m[1])) || !BRANCH.test(m[2])) {
    block(`pushes must name the remote and the full destination: ${PUSH_FORM} (AGENTS.md §5)`);
  }
  if (m[2].toLowerCase() === 'main') block('never push to `main`: push your own branch and open a PR (AGENTS.md §0, §5)');
}

// Only reads: the remote list, `get-url` and `show`. Options may come before the action.
function isRemoteRead(rest) {
  const words = rest.filter((t) => t !== '-v' && t !== '--verbose');
  if (words.length === 0) return true;
  const [action, ...args] = words;
  const flags = { 'get-url': ['--push', '--all'], show: ['-n'] }[action];
  return flags !== undefined && args.every((t) => flags.includes(t) || !t.startsWith('-'));
}

// Commands that run the git hooks, including the gitleaks scan. Tool calls don't keep
// environment changes, so when one of these is the only command in its call, nothing can
// change the environment git and gitleaks start with.
const HOOKED = new Set(['commit', 'merge', 'pull', 'push']);

function checkGit(a, alone) {
  const { sub, rest, hasSub } = parseGitArgs(a);
  if (!hasSub) {
    if (a.every((t) => ['--version', '--help', '-h'].includes(t))) return;
    block('git needs a command');
  }
  if (!GIT_COMMANDS.has(sub)) block(`\`git ${sub}\` is not a built-in git command; aliases and git-* programs can't be checked`);
  if (rest.some((t) => /^--(upload-pack|receive-pack|exec)(=|$)/.test(t))) block('git options that run another program are not allowed');
  if (HOOKED.has(sub) && !alone) {
    block(`run \`git ${sub}\` as the only command in its call, with nothing before it, so nothing can change what the git hooks run with`);
  }
  if (sub === 'commit') checkCommit(rest);
  if (sub === 'push') checkPush(rest);
  if (sub === 'config' && !isConfigRead(rest)) {
    block('changing git config is forbidden (AGENTS.md §5). To read a value, use `git config --get <key>` or `git config --list`');
  }
  if (sub === 'remote' && !isRemoteRead(rest)) {
    block('changing remotes is a git config change (AGENTS.md §5). Allowed: `git remote -v`, `git remote get-url <name>`, `git remote show <name>`');
  }
  if (sub === 'clone' && rest.some((t) => /^(-c|--config|-u)(=|$)|^--config=/.test(t))) block('`git clone` config options are not allowed');
  if (['diff', 'log', 'show'].includes(sub) && rest.some((t) => /^--(output|ext-diff|textconv)(=|$)/.test(t))) {
    block(`\`git ${sub}\` may not write files or run external diff programs`);
  }
  const runsCommands =
    (sub === 'rebase' && rest.some((t) => /^(-x|--exec)(=|$)/.test(t) || /^-[A-Za-z]*x/.test(t))) ||
    (sub === 'bisect' && rest.includes('run')) ||
    (sub === 'submodule' && rest.includes('foreach')) ||
    (sub === 'difftool' && rest.some((t) => /^(-x|--extcmd)(=|$)/.test(t))) ||
    (sub === 'grep' && rest.some((t) => /^(-O|--open-files-in-pager)/.test(t)));
  if (runsCommands) block(`this \`git ${sub}\` form runs other commands, which the guard can't check`);
}

// ---------- gh ----------

// Short options that take a value, per gh command. Every other letter is read as a switch,
// which is the safe direction: a flag hidden in a cluster is found, never read as a value.
const GH_SHORT_VALUES = {
  'issue create': 'abFlmpRtT',
  'issue edit': 'bFRt',
  'pr create': 'aBbFHlmprRtT',
  'pr edit': 'BbFRt',
  'pr review': 'bFR',
  api: 'FfHpqtX',
};
const GH_LONG_VALUES = new Set([
  '--title', '--body', '--body-file', '--label', '--add-label', '--remove-label', '--assignee', '--milestone', '--project',
  '--base', '--head', '--template', '--reviewer', '--repo', '--field', '--raw-field', '--header', '--method', '--jq',
  '--hostname', '--cache', '--preview', '--input',
]);

// Splits short-option clusters the way gh's flag parser reads them: `-iX PUT` is `-i -X PUT`,
// and `-XPUT` or `-X=PUT` is `-X PUT`.
function expandFlags(a, shortValues) {
  const out = [];
  for (let i = 0; i < a.length; i++) {
    const t = a[i];
    if (t === '--') {
      out.push(...a.slice(i));
      break;
    }
    if (t.startsWith('--') || !/^-./.test(t)) {
      out.push(t);
      if (GH_LONG_VALUES.has(t) && i + 1 < a.length) out.push(a[++i]);
      continue;
    }
    for (let k = 1; k < t.length; k++) {
      out.push(`-${t[k]}`);
      if (!shortValues.includes(t[k])) continue;
      const attached = t.slice(k + 1);
      if (attached) out.push(attached.replace(/^=/, ''));
      else if (i + 1 < a.length) out.push(a[++i]);
      break;
    }
  }
  return out;
}

// `--name=value` or `--name value` / `-x value`, on expanded flags.
function flagValue(e, i) {
  const t = e[i];
  const eq = t.startsWith('--') ? t.indexOf('=') : -1;
  return eq > 0 ? { name: t.slice(0, eq), value: t.slice(eq + 1), next: i } : { name: t, value: e[i + 1] ?? '', next: i + 1 };
}

function hasApprovedLabel(e) {
  const flags = new Set(['--label', '-l', '--add-label', '--remove-label']);
  return e.some((t, i) => {
    const { name, value } = flagValue(e, i);
    return flags.has(name) && value.split(',').some((v) => v.trim().toLowerCase() === 'approved');
  });
}

const approvesPr = (e) => e.some((t) => t === '-a' || /^--approve(=|$)/.test(t));

function checkGhApi(e) {
  const valueFlags = new Set(['-H', '--header', '-q', '--jq', '-t', '--template', '--hostname', '--cache', '-p', '--preview']);
  let method = null;
  let endpoint = null;
  const fields = [];
  for (let i = e.indexOf('api') + 1; i < e.length; i++) {
    const t = e[i];
    const { name, value, next } = flagValue(e, i);
    if (name === '--input') block('`gh api --input` sends a request body the guard can’t read');
    if (name === '-X' || name === '--method') method = value;
    else if (['-f', '-F', '--field', '--raw-field'].includes(name)) fields.push({ flag: name, value });
    else if (!valueFlags.has(name)) {
      if (!t.startsWith('-') && endpoint === null) endpoint = t;
      continue;
    }
    i = next;
  }
  if (fields.some((f) => f.flag !== '-f' && f.flag !== '--raw-field' && f.value.includes('=@'))) {
    block('`gh api` fields read from files (`=@file`) can’t be checked');
  }
  if (endpoint === 'graphql') {
    const query = fields.find((f) => /^query=/.test(f.value));
    const body = query ? query.value.slice('query='.length).trim() : '';
    if (!/^(query\b|\{)/i.test(body) || /\b(mutation|subscription)\b/i.test(body) || method && method.toUpperCase() !== 'POST') {
      block('only literal GraphQL read queries (`-f query=\'query { … }\'`) are allowed through `gh api`');
    }
    return;
  }
  method = method?.toUpperCase();
  if (method ? method !== 'GET' : fields.length > 0) {
    block(`\`gh api\` ${method ?? 'POST'} writes to GitHub; only GET reads are allowed (AGENTS.md §5)`);
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
  if (group === undefined) return;
  if (!GH_COMMANDS.has(group)) block(`\`gh ${group}\` is not a built-in gh command; aliases and extensions can't be checked`);
  const forbidden = {
    auth: null,
    secret: null,
    label: null,
    variable: ['set', 'delete'],
    'ssh-key': ['add', 'delete'],
    'gpg-key': ['add', 'delete'],
    alias: ['set', 'import', 'delete'],
    config: ['set'],
    extension: ['install', 'upgrade', 'remove', 'exec'],
    repo: ['edit', 'delete', 'rename', 'archive', 'unarchive', 'set-default', 'sync'],
  };
  if (Object.hasOwn(forbidden, group) && (forbidden[group] === null || forbidden[group].includes(action))) {
    const why = group === 'label' ? 'labels are managed by the owner; `approved` is owner-only' : 'login, secrets, settings and installs are owner-only';
    block(`\`gh ${group}${forbidden[group] ? ` ${action}` : ''}\` is forbidden: ${why} (AGENTS.md §5)`);
  }
  if (group === 'repo' && action === 'deploy-key' && ['add', 'delete'].includes(third)) block('deploy keys are repository settings (AGENTS.md §5)');
  const e = expandFlags(a, GH_SHORT_VALUES[`${group} ${action}`] ?? GH_SHORT_VALUES[group] ?? 'R');
  if (group === 'pr' && action === 'merge') block('only the owner merges pull requests (AGENTS.md §0)');
  if (group === 'pr' && action === 'review' && approvesPr(e)) {
    block('agents never approve on GitHub; post the review as a comment (AGENTS.md §0, §6)');
  }
  if ((group === 'issue' || group === 'pr') && (action === 'create' || action === 'edit') && hasApprovedLabel(e)) {
    block('only the owner adds or removes the `approved` label (AGENTS.md §3)');
  }
  if ((group === 'issue' || group === 'pr') && action === 'comment' && e.some((t) => /^--(edit-last|delete-last)/.test(t))) {
    block('agents share the owner’s account, so they never edit or delete existing comments');
  }
  if (group === 'api') checkGhApi(e);
}

// ---------- shell commands ----------

function isReadSegment(seg) {
  if (seg.tokens.some((t) => t.redirect && t.text === '>')) return false;
  const i = commandIndex(seg);
  if (i < 0) return false;
  const name = cmdName(seg.tokens[i].text);
  return READERS.has(name) || (name === 'git' && GIT_READS.has(parseGitArgs(argsAfter(seg.tokens, i)).sub));
}

function isGitConfigRead(seg) {
  const i = seg.tokens.findIndex((t) => isCommandToken(t) && cmdName(t.text) === 'git');
  if (i < 0) return false;
  const { sub, rest } = parseGitArgs(argsAfter(seg.tokens, i));
  return sub === 'config' && isConfigRead(rest);
}

function isProseSegment(seg) {
  const i = commandIndex(seg);
  if (i < 0) return false;
  const name = cmdName(seg.tokens[i].text);
  return name === 'gh' || (name === 'git' && ['commit', 'tag', 'notes'].includes(parseGitArgs(argsAfter(seg.tokens, i)).sub));
}

// PowerShell's Env: drive and Environment provider, and the registry keys that hold user and
// system environment variables.
const ENV_STORE = /(^|[:{])env:|environment::|\b(hkcu|hkey_current_user|hklm|hkey_local_machine)\b.*[\\/]environment\b/;
// A variable named as a word, not read through `$NAME` or `${NAME}` (shell names are case-sensitive).
const ENV_NAME = /(?<![$\w{:])(GIT_[A-Z0-9_]+|GITLEAKS_[A-Z0-9_]*|PATH|PATHEXT)(?!\w)/;

function checkEnvironment(seg, command) {
  const why = 'setting GIT_*, GITLEAKS_* or PATH changes how git and the secret scan run (AGENTS.md §5)';
  const overrides = /^(\$?env:)?(git_\w*|gitleaks_\w*|path|pathext)(\+?=|$)/;
  const quiet = isReadSegment(seg) || isProseSegment(seg);
  seg.tokens.forEach((t, i) => {
    if (t.redirect) return;
    const lower = t.text.toLowerCase();
    const assigned = /\+?=/.test(lower) || /^\+?=/.test(seg.tokens[i + 1]?.text ?? '');
    if (overrides.test(lower) && assigned) block(why);
    if (quiet) return;
    if (ENV_STORE.test(lower)) block('environment variables are owner settings: the Env: drive, the Environment provider and the registry are off limits');
    if (ENV_NAME.test(t.text)) block(why);
  });
  if (/setenvironmentvariable/i.test(command) && /(git_|gitleaks_|\bpath\b)/i.test(command)) block(why);
}

function checkSegment(seg, input, dialect, command, total) {
  const isPwsh = hasCommand(seg, new Set(['pwsh', 'powershell']));
  checkEnvironment(seg, command);
  for (const t of seg.tokens.filter(isWord)) {
    const lower = norm(t.text);
    if (lower.includes('--no-veri')) block('`--no-verify` skips the git hooks, including the gitleaks secret scan (AGENTS.md §5)');
    if (isPwsh && /^-(e|ec|ea|en\w*)$/.test(lower)) block('an encoded PowerShell command cannot be checked');
    if (lower.includes('hookspath') && !isGitConfigRead(seg)) block('core.hooksPath must stay `.githooks`; git config changes are forbidden (AGENTS.md §5)');
  }
  // Quoted paths can contain spaces, so multi-word tokens are checked piece by piece, except
  // where they are prose: commit messages and gh titles, bodies and comments.
  const prose = isProseSegment(seg);
  for (const t of seg.tokens.filter((x) => !x.redirect)) {
    const pieces = isWord(t) ? [t.text] : prose ? [] : t.text.split(/\s+/);
    const hit = pieces.find((p) => PROTECTED_MENTION.test(norm(p)));
    if (hit !== undefined && !isReadSegment(seg)) {
      block(`\`${hit}\` holds the rules in force or runs code, so only the owner changes it. Reading is fine (AGENTS.md §5, §6)`);
    }
  }
  const ci = commandIndex(seg);
  if (ci >= 0) {
    const head = seg.tokens[ci];
    const name = cmdName(head.text);
    const rest = seg.tokens.slice(ci + 1).filter((t) => !t.redirect);
    // In PowerShell a variable runs as a command only after the call operator `&`.
    const runsVariable = dialect !== 'PowerShell' || seg.sep === '&';
    if (head.dynamic && runsVariable && rest.length && !/^\+?=/.test(rest[0].text)) block('a command name built from a variable can’t be checked');
    if (DIRECTORY_CHANGERS.has(name) && rest.some((t) => PROTECTED_DIRECTORY.test(norm(t.text)))) {
      block('don’t change into .git, .githooks, .claude or config folders; use paths from the repo root');
    }
    const runsInstaller = [head, ...rest].some((t) => cmdName(t.text) === 'install-claude-guard');
    if (runsInstaller && ['install-claude-guard', 'pwsh', 'powershell', 'start-process'].includes(name)) {
      block('only the owner installs the guard, as administrator');
    }
    if (name === 'cmd' && rest.some((t) => /^\/[ck]$/i.test(t.text))) block('`cmd /c` code can’t be checked');
    if (name === 'wsl' && rest.length) block('commands run through WSL can’t be checked');
    if (name === 'setx' || (name === 'reg' && !/^query$/i.test(rest[0]?.text ?? ''))) {
      block('`setx` and registry changes set environment variables for later sessions; they are owner settings');
    }
    if (['new-psdrive', 'ndr', 'mount'].includes(name)) block('a new drive can hide a path from the guard');
  }
  seg.tokens.forEach((t, i) => {
    if (!isCommandToken(t)) return;
    const name = cmdName(t.text);
    if (name !== 'git' && name !== 'gh') return;
    if (seg.tokens.slice(i + 1).some((x) => x.dynamic)) block(`\`${name}\` arguments built from variables or expansions can’t be checked; write them out`);
    if (name === 'git') checkGit(argsAfter(seg.tokens, i), total === 1 && i === 0);
    else checkGh(argsAfter(seg.tokens, i));
  });
  if (dialect === 'PowerShell' && seg.tokens.some((t) => t.text === '--%')) block('the PowerShell stop-parsing token `--%` hides arguments from the guard');
}

function checkDownloadPipe(segments, command) {
  const isDownload = (s) => hasCommand(s, DOWNLOADERS) || s.tokens.some((t) => /downloadstring|downloaddata|downloadfile|net\.webclient/i.test(t.text));
  const isShell = (s) => hasCommand(s, INTERPRETERS);
  if (/frombase64string/i.test(command) && /\b(iex|invoke-expression|invoke-command|scriptblock)\b/i.test(command)) {
    block('running decoded base64 cannot be checked');
  }
  if (!segments.some(isDownload) || !segments.some(isShell)) return;
  const piped = segments.some((s) => (isShell(s) && s.sep === '|') || (isDownload(s) && (['(', '`'].includes(s.sep) || s.depth > 0)));
  if (piped) block('piping a download into a shell is forbidden (AGENTS.md §5). Download, read it, then ask the owner');
}

// The reviewer gets an exact list of read commands and one way to post a comment.
const NUMBER = /^\d+$/;
const FIELDS = /^[A-Za-z,]+$/;
const SAFE_GIT_ARG = /^[A-Za-z0-9_.,:/@^~=+%-]+$/;

function reviewerCommandOk(a, seg, hasSubst) {
  const [group, action, n, ...flags] = a;
  const only = (allowed) => flags.every((t, i) => allowed(t, flags[i - 1]));
  const jsonFields = (t, prev) => t === '--json' || (prev === '--json' && FIELDS.test(t));
  if (group === 'pr' && action === 'view' && NUMBER.test(n)) return only((t, p) => t === '--comments' || jsonFields(t, p));
  if (group === 'issue' && action === 'view' && NUMBER.test(n)) return only((t, p) => t === '--comments' || jsonFields(t, p));
  if (group === 'pr' && action === 'diff' && NUMBER.test(n)) return only((t) => ['--name-only', '--patch'].includes(t));
  if (group === 'pr' && action === 'checks' && NUMBER.test(n)) return only((t, p) => t === '--required' || jsonFields(t, p));
  if (group === 'pr' && action === 'comment' && NUMBER.test(n)) {
    const stdin = flags.length === 2 && flags[0] === '--body-file' && flags[1] === '-';
    const heredoc = seg.tokens.filter((t) => t.text === '<<');
    if (stdin) return heredoc.length === 1 && heredoc[0].literal && !hasSubst;
    return flags.length === 2 && ['--body', '-b'].includes(flags[0]) && heredoc.length === 0 && !hasSubst;
  }
  return false;
}

function checkReviewerShell(segments, hasSubst) {
  const top = segments.filter((s) => s.depth === 0);
  if (top.length !== 1 || segments.length !== 1) block('code-reviewer runs one command per call, with no pipes, chains or substitutions');
  const seg = top[0];
  if (seg.tokens.some((t) => t.dynamic)) block('code-reviewer commands may not use variables or expansions');
  if (seg.tokens.some((t) => t.redirect && t.text !== '<<')) block('code-reviewer commands may not redirect output or input');
  const name = cmdName(seg.tokens[0].text);
  const a = argsAfter(seg.tokens, 0);
  const gitOk =
    name === 'git' &&
    ['diff', 'log', 'show'].includes(a[0]) &&
    a.slice(1).every((t) => (SAFE_GIT_ARG.test(t) || /^--(format|pretty)=[^\n$`]*$/.test(t)) && !/^--(output|ext-diff|textconv|no-index)/.test(t) && !/^-[cC]$/.test(t));
  if (!gitOk && !(name === 'gh' && reviewerCommandOk(a, seg, hasSubst))) {
    block(
      'code-reviewer may run only `gh pr view|diff|checks <n>`, `gh issue view <n>`, `git diff|log|show` and ' +
        "`gh pr comment <n> --body-file - <<'EOF'` (.claude/agents/code-reviewer.md)",
    );
  }
}

function checkShell(command, dialect, input) {
  const { segments, hasSubst } = parse(command, dialect);
  for (const seg of segments) checkSegment(seg, input, dialect, command, segments.length);
  checkDownloadPipe(segments, command);
  const role = input.agent_type;
  if (Object.hasOwn(LANES, role ?? '')) {
    if (role !== 'code-reviewer') block(`the ${role} role has no shell access (.claude/agents/${role}.md)`);
    checkReviewerShell(segments, hasSubst);
  }
}

// ---------- file edits ----------

function isProtectedPath(abs) {
  const p = norm(abs);
  const home = norm(os.homedir());
  const userFiles = ['/.claude/settings.json', '/.claude/settings.local.json', '/.claude.json', '/.gitconfig'].map((f) => home + f);
  return PROTECTED_FILE.test(p) || userFiles.includes(p) || p.startsWith(`${home}/.config/git/`);
}

function adrStatus(abs) {
  let text;
  try {
    text = fs.readFileSync(abs, 'utf8');
  } catch {
    return null; // a new ADR
  }
  const m = /^##\s*Status\s*$([\s\S]*?)(^##\s|$(?![\s\S]))/im.exec(text);
  const line = m ? m[1].split(/\r?\n/).map((l) => l.trim()).find((l) => l !== '') : '';
  return (line ?? '').toLowerCase();
}

function checkFile(input) {
  const ti = input.tool_input ?? {};
  const target = input.tool_name === 'NotebookEdit' ? ti.notebook_path : ti.file_path;
  if (typeof target !== 'string' || target === '') block('the tool input has no file path');
  const root = path.resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || '.');
  const abs = path.resolve(input.cwd || root, target);
  if (isProtectedPath(abs)) {
    block(`\`${target}\` holds the rules in force or runs code, so only the owner changes it (AGENTS.md §5, §6)`);
  }
  const role = input.agent_type;
  if (!Object.hasOwn(LANES, role ?? '')) return;
  const lane = LANES[role];
  if (lane.file === null) block('code-reviewer is read-only and never edits files (AGENTS.md §6)');
  const rel = norm(path.relative(root, abs));
  if (!lane.file.test(rel)) block(`${role} may write only ${lane.where} (.claude/agents/${role}.md)`);
  if (role === 'tech-architect') {
    const status = adrStatus(abs);
    if (status !== null && !status.startsWith('proposed')) {
      block('only Proposed ADRs can be edited; supersede an accepted ADR with a new one (AGENTS.md §9)');
    }
  }
}

function check(input) {
  if (!input || typeof input !== 'object') block('the hook input is not a JSON object');
  const tool = input.tool_name;
  if (!SHELL_TOOLS.has(tool) && !FILE_TOOLS.has(tool)) return;
  if (SHELL_TOOLS.has(tool)) {
    const command = input.tool_input?.command;
    if (typeof command !== 'string') block('the tool input has no command');
    checkShell(command, tool, input);
  } else {
    checkFile(input);
  }
}

function main() {
  try {
    check(JSON.parse(fs.readFileSync(0, 'utf8')));
  } catch (err) {
    const why = err instanceof Blocked ? err.message : `the guard could not check this call (${err.message})`;
    process.stderr.write(`Blocked by the studio guard: ${why}.\nDon't retry in another form. If it is really needed, stop and ask the owner.\n`);
    process.exit(2);
  }
  process.exit(0);
}

if (require.main === module) main();

module.exports = { check };
