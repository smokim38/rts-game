#!/usr/bin/env node
'use strict';
// PreToolUse guardrail for the rules in AGENTS.md §5 and for each Claude role's lane.
// This file is the reviewed source. The active copy is installed by the owner as a managed
// hook (tools/install-claude-guard.ps1), so editing this file never changes the rules in force.
// It applies to every Claude Code session on the PC: a session opened anywhere can reach the
// studio repository by path, so there is no scope check to get around.
// Claude Code sends the tool call as JSON on stdin. Exit code 2 blocks the call and shows
// stderr to Claude. Errors also exit 2, and the managed hook sets onFailure "block".
// Two Producer commands need GitHub as well: the merge form and the Codex launcher form. For
// those the guard queries GitHub read-only and blocks if any lookup fails or is incomplete.
// Tests: node --test .claude/hooks/guard.test.js

const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');

const OWNER = 'smokim38';
const REPO = 'smokim38/rts-game';

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
  'get-itemproperty', 'gp', 'get-acl', 'whoami',
]);
// Programs that take `git` or `gh` as plain data and never run it, e.g. `gitleaks git`.
// Anything else (xargs, find -exec, unknown programs) may run its arguments, so git and gh
// are checked wherever they appear there.
const NON_RUNNERS = new Set([
  ...READERS, 'gitleaks', 'echo', 'printf', 'write-output', 'write-host', 'man', 'help', 'get-help', 'which',
  'where', 'get-command', 'gcm',
]);
// Redirection targets that discard output instead of writing a file.
const NULL_TARGET = /^(\/dev\/null|\$null|nul)$/i;
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
  const braces = []; // PowerShell: 'hash' for @{, 'block' for any other {
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
    // PowerShell reads `{…}` inside a word as a script block, which splits the word. Inside a
    // hashtable, `key={…}` is a script-block value (e.g. Select-Object @{n='x';e={…}}).
    if (!bash && c === '{') {
      const hashValue = braces.at(-1) === 'hash' && text?.endsWith('=');
      if (text !== null && text !== '@' && text !== '$' && !hashValue) wordBrace = true;
      braces.push(text === '@' ? 'hash' : 'block');
    }
    if (!bash && c === '}') braces.pop();
    // Bash `&>` sends both output streams to a file: a redirection, not a separator.
    if (bash && c === '&' && src[i + 1] === '>') {
      endToken();
      i++;
      if (src[i + 1] === '>') i++;
      tokens.push({ text: '>', redirect: true });
      continue;
    }
    if (';&|(){}'.includes(c)) {
      if (c === '(' && text === null && tokens.at(-1)?.redirect) hasSubst = true;
      endSegment(c);
      continue;
    }
    if (c === '<' || c === '>') {
      // A stream number (`2>`) or PowerShell's `*>` belongs to the redirection.
      if (c === '>' && text !== null && /^(\d+|\*)$/.test(text)) {
        text = null;
        dynamic = false;
      } else endToken();
      let op = c;
      if (c === '>' && src[i + 1] === '>') op += src[++i];
      // `>&1`, `2>&1`, `>&-`: duplicates or closes a stream; no file is involved.
      if (src[i + 1] === '&' && /[\d-]/.test(src[i + 2] ?? '')) {
        i += 2;
        while (/\d/.test(src[i + 1] ?? '')) i++;
        tokens.push({ text: op, redirect: true, dup: true });
        continue;
      }
      tokens.push({ text: op, redirect: true });
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
  const parts = [...top.parts];
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
    parts.push(...inner.parts);
    hasSubst ||= inner.hasSubst;
  }
  return { segments, parts, hasSubst };
}

// git, gh or a downloader run as a command: the name stands alone, optionally with a path in
// front or an executable extension, and ends at a space, quote, separator or the end.
// Names inside other words (`setup-git-hooks.md`, `gitleaks`) don't count.
const RUNS_TOOL = /(^|[^\w.-])(git(-[\w-]+)?|gh|curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod|codex|claude)(\.(exe|cmd|bat|ps1|js))?(?=$|[\s'"`;&|()])/i;

function checkInterpreterCode(text, depth) {
  // Also test the text without quotes and escapes, so `g\it` or `"g"it` still read as git.
  const flat = text.replace(/["'`^\\]/g, '');
  if (RUNS_TOOL.test(text) || RUNS_TOOL.test(flat)) {
    block('run git, gh and downloads directly, not inside code passed to another shell or interpreter (Codex and Claude never run from code)');
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
      if (t.text !== '<<' && !t.dup) j++;
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
  // Rewriting refs could make a local `origin/main` lie about what was merged.
  const symbolicRefWrite = sub === 'symbolic-ref' && (rest.some((t) => /^(-d|--delete|-m)$/.test(t)) || rest.filter((t) => !t.startsWith('-')).length > 1);
  if (sub === 'update-ref' || sub === 'replace' || symbolicRefWrite) block(`\`git ${sub}\` rewrites refs; the guard never lets refs be rewritten by hand`);
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
  'pr comment': 'bFR',
  'issue comment': 'bFR',
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

const approvesPr = (e) => e.some((t) => t === '-a' || /^--approve(=|$)/.test(t));

// The Producer's two gated commands, each exact and alone in its call (AGENTS.md §6, §5).
const MERGE_FORM = '`gh pr merge <n> --repo smokim38/rts-game --squash --delete-branch --match-head-commit <sha>`';
const MERGE_COMMAND = /^gh pr merge (\d+) --repo smokim38\/rts-game --squash --delete-branch --match-head-commit ([0-9a-f]{40})$/;
const LAUNCH_FORM = '`node tools/codex-launch.js <role> <issue> [<pr>]`';
const LAUNCH_COMMAND = /^node tools\/codex-launch\.js (?:(gameplay-programmer|tech-artist) (\d+)|(qa-test-engineer|reviewer) (\d+) (\d+))$/;

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

// The first three words of a gh command, skipping the repository option.
function ghWords(a) {
  const words = [];
  for (let i = 0; i < a.length && words.length < 3; i++) {
    const t = a[i];
    if (t === '-R' || t === '--repo') i++;
    else if (!t.startsWith('-')) words.push(t.toLowerCase());
  }
  return words;
}

function checkGh(a, allowMerge) {
  const [group, action, third] = ghWords(a);
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
    const why = group === 'label' ? 'the label set is managed by the owner' : 'login, secrets, settings and installs are owner-only';
    block(`\`gh ${group}${forbidden[group] ? ` ${action}` : ''}\` is forbidden: ${why} (AGENTS.md §5)`);
  }
  if (group === 'repo' && action === 'deploy-key' && ['add', 'delete'].includes(third)) block('deploy keys are repository settings (AGENTS.md §5)');
  const e = expandFlags(a, GH_SHORT_VALUES[`${group} ${action}`] ?? GH_SHORT_VALUES[group] ?? 'R');
  if (group === 'pr' && action === 'merge' && !allowMerge) block(`only the Producer merges, with exactly ${MERGE_FORM} as a call of its own (AGENTS.md §0)`);
  if (group === 'pr' && action === 'review' && approvesPr(e)) {
    block('agents never approve on GitHub; post the review as a comment (AGENTS.md §0, §6)');
  }
  if ((group === 'issue' || group === 'pr') && action === 'comment' && e.some((t) => /^--(edit-last|delete-last)/.test(t))) {
    block('agents share the owner’s account, so they never edit or delete existing comments');
  }
  if (group === 'api') checkGhApi(e);
}

// ---------- shell commands ----------

// Output redirected into a file (not a stream duplicate like 2>&1, and not a null device).
function writesFile(seg) {
  return seg.tokens.some((t, i) => t.redirect && t.text.startsWith('>') && !t.dup && !NULL_TARGET.test(seg.tokens[i + 1]?.text ?? ''));
}

function isReadSegment(seg) {
  if (writesFile(seg)) return false;
  const i = commandIndex(seg);
  if (i < 0) return false;
  const name = cmdName(seg.tokens[i].text);
  // icacls only reads when its switches are limited to listing options.
  if (name === 'icacls') return argsAfter(seg.tokens, i).every((a) => !a.startsWith('/') || /^\/[tclq]$/i.test(a));
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
// LESSOPEN and LESSCLOSE make `less` run a program on each file it reads.
const ENV_NAME = /(?<![$\w{:])(GIT_[A-Z0-9_]+|GITLEAKS_[A-Z0-9_]*|PATH|PATHEXT|LESSOPEN|LESSCLOSE)(?!\w)/;

function checkEnvironment(seg, command) {
  const why = 'setting GIT_*, GITLEAKS_*, PATH or LESSOPEN changes what git, the secret scan or `less` run (AGENTS.md §5)';
  const overrides = /^(\$?env:)?(git_\w*|gitleaks_\w*|path|pathext|lessopen|lessclose)(\+?=|$)/;
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

// Codex, nested Claude sessions and the launcher, by any path or extension. Codex runs only
// through the launcher, and only in the Producer's exact launcher form (AGENTS.md §5).
const AGENT_PROGRAMS = new Set(['codex', 'claude', 'codex-launch', 'codex-launch.js']);

function checkSegment(seg, input, dialect, command, total, gate) {
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
    // Read commands with options that run another program on what they read.
    if (name === 'rg' && rest.some((t) => /^--pre(-glob)?(=|$)/.test(t.text))) block('`rg --pre` runs another program on each file, which the guard can’t check');
    if (name === 'find' && rest.some((t) => /^-(exec|execdir|ok|okdir)$/i.test(t.text))) block('`find -exec` and `-ok` run other programs, which the guard can’t check');
  }
  const headName = ci >= 0 ? cmdName(seg.tokens[ci].text) : '';
  seg.tokens.forEach((t, i) => {
    if (!isCommandToken(t)) return;
    const name = cmdName(t.text);
    // As data for a program that doesn't run it (`rg codex`, `git commit -m claude`), the name is harmless.
    const asData = i !== ci && (NON_RUNNERS.has(headName) || headName === 'git' || headName === 'gh');
    if (AGENT_PROGRAMS.has(name) && !asData && gate?.kind !== 'launch') {
      block(`Codex runs only through the Producer’s ${LAUNCH_FORM}, and Claude sessions are never started from a shell (AGENTS.md §5)`);
    }
    if (name !== 'git' && name !== 'gh') return;
    if (i !== ci && NON_RUNNERS.has(headName)) return; // only data for a program that doesn't run it
    const after = seg.tokens.slice(i + 1);
    const discards = (x, k) => after[k - 1]?.redirect && NULL_TARGET.test(x.text); // 2>$null
    if (after.some((x, k) => x.dynamic && !discards(x, k))) block(`\`${name}\` arguments built from variables or expansions can’t be checked; write them out`);
    if (name === 'git') checkGit(argsAfter(seg.tokens, i), total === 1 && i === 0);
    else checkGh(argsAfter(seg.tokens, i), gate?.kind === 'merge');
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

// ---------- records ----------
// Review, QA and owner-approval evidence: a PR comment whose first line is the marker
// (docs/studio/workflow.md). The merge gate reads them; these rules decide who may post which.

const RECORD = /studio-record/i;
const MARKER = /^<!-- studio-record v1 type=(verdict|qa|owner-approval) pr=(\d+) sha=([0-9a-f]{40}) result=([a-z-]+)(?: vendor=(claude|codex))? -->$/;
const RESULTS = { verdict: ['approve', 'request-changes', 'escalate'], qa: ['pass', 'fail'], 'owner-approval': ['approved'] };

// The record a comment body holds, or null if it is malformed: one marker, on the first line.
function parseRecord(body) {
  const text = body.replace(/\r\n/g, '\n');
  if ((text.match(/studio-record/gi) ?? []).length !== 1) return null;
  const m = MARKER.exec(text.split('\n')[0]);
  if (!m) return null;
  const [, type, pr, sha, result, vendor] = m;
  if (!RESULTS[type].includes(result) || (type === 'verdict') !== Boolean(vendor)) return null;
  return { type, pr: Number(pr), sha, result, vendor: vendor ?? '' };
}

// A comment's text when the guard can read all of it: a literal --body, or `--body-file -`
// fed by the call's one quoted here-document. Otherwise null.
function commentBody(seg, e, parts) {
  let body = null;
  let stdin = false;
  for (let i = 0; i < e.length; i++) {
    const { name, value, next } = flagValue(e, i);
    if (name === '--body' || name === '-b') {
      if (body !== null) return null;
      body = value;
    } else if (name === '--body-file' || name === '-F') {
      if (value !== '-') return null;
      stdin = true;
    } else if (['--editor', '-e', '--web', '-w'].includes(name)) return null;
    else continue;
    i = next;
  }
  const heredocs = seg.tokens.filter((t) => t.redirect && t.text === '<<');
  if (!stdin) return heredocs.length === 0 ? body : null;
  const docs = parts.filter((p) => p.heredoc);
  return body === null && heredocs.length === 1 && heredocs[0].literal && docs.length === 1 ? docs[0].text : null;
}

// The Producer posts only owner-approval records and the code-reviewer only its own verdicts
// (vendor=claude). The launcher posts Codex verdicts and QA records itself. Other sessions post none.
function checkRecordComments(segments, parts, role) {
  const strict = role === 'producer' || role === 'code-reviewer';
  for (const seg of segments) {
    const i = commandIndex(seg);
    if (i < 0 || cmdName(seg.tokens[i].text) !== 'gh') continue;
    const a = argsAfter(seg.tokens, i);
    const [group, action, n] = ghWords(a);
    if ((group !== 'pr' && group !== 'issue') || action !== 'comment') continue;
    const body = commentBody(seg, expandFlags(a, GH_SHORT_VALUES[`${group} comment`]), parts);
    if (strict && (body === null || segments.length !== 1)) {
      block(`${role} comments must be a literal --body or \`--body-file -\` with a quoted here-document (<<'EOF'), as a call of their own, so the guard can read them`);
    }
    const text = body ?? [...seg.tokens.map((t) => t.text), ...parts.map((p) => p.text)].join('\n');
    if (!RECORD.test(text)) continue;
    const record = body === null ? null : parseRecord(body);
    const mine = record && group === 'pr' && record.pr === Number(n) &&
      ((role === 'producer' && record.type === 'owner-approval') || (role === 'code-reviewer' && record.type === 'verdict' && record.vendor === 'claude'));
    if (!mine) {
      block(
        'only the code-reviewer posts verdict records (vendor=claude), only the Producer posts owner-approval records, and the launcher posts the rest; ' +
          'one well-formed marker on the first line, naming this PR (docs/studio/workflow.md)',
      );
    }
  }
}

// ---------- the Producer ----------

// Git Bash writes drive paths as /c/dev/...
const fromMsys = (p) => (process.platform === 'win32' ? p.replace(/^\/([a-z])(?=\/|$)/i, '$1:') : p);
const samePath = (a, b) => norm(path.resolve(a)).replace(/\/$/, '') === norm(path.resolve(b)).replace(/\/$/, '');
const projectRoot = (input) => path.resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || '.');

// The merge form or the launcher form, exact and alone in the call, from the Producer only.
function producerGate(command, input) {
  if (input.agent_type !== 'producer') return null;
  const c = command.trim();
  let m = MERGE_COMMAND.exec(c);
  if (m) return { kind: 'merge', pr: Number(m[1]), sha: m[2] };
  m = LAUNCH_COMMAND.exec(c);
  if (m) return { kind: 'launch', role: m[1] ?? m[3], issue: Number(m[2] ?? m[4]), pr: m[5] ? Number(m[5]) : null };
  return null;
}

// Strict: only git, gh and `cd` to the repository root, run directly, with no file redirects.
// Everything else (interpreters, scripts, programs given by path, read commands) is blocked, so
// the launcher form is the only way the Producer's shell can start Codex.
function checkProducerShell(segments, hasSubst, input) {
  const allowed = `the Producer’s shell runs only git, gh, ${LAUNCH_FORM} and \`cd\` to the repository root; read files with Read, Grep and Glob (.claude/agents/producer.md)`;
  if (hasSubst) block('the Producer’s commands may not use command substitution');
  const root = projectRoot(input);
  for (const seg of segments) {
    if (commandIndex(seg) !== 0 || !/^(git|gh|cd)$/i.test(seg.tokens[0].text)) block(allowed);
    seg.tokens.forEach((t, k) => {
      if (!t.redirect || t.dup || t.text === '<<') return;
      if (t.text.startsWith('>') && NULL_TARGET.test(seg.tokens[k + 1]?.text ?? '')) return;
      block('the Producer’s commands may not redirect to or from files');
    });
    if (seg.tokens[0].text.toLowerCase() === 'cd') {
      const args = argsAfter(seg.tokens, 0);
      const target = args.length === 1 && !seg.tokens.some((t) => t.dynamic) ? path.resolve(input.cwd || root, fromMsys(args[0])) : null;
      if (target === null || !samePath(target, root)) block('the Producer changes directory only to the repository root; use `git -C <worktree>` for a launcher worktree');
    }
  }
}

// Returns the gate to check against GitHub for the Producer's merge and launcher forms.
function checkShell(command, dialect, input) {
  const { segments, parts, hasSubst } = parse(command, dialect);
  const role = input.agent_type;
  const gate = producerGate(command, input);
  for (const seg of segments) checkSegment(seg, input, dialect, command, segments.length, gate);
  checkDownloadPipe(segments, command);
  checkRecordComments(segments, parts, role);
  if (role === 'producer' && !gate) checkProducerShell(segments, hasSubst, input);
  if (Object.hasOwn(LANES, role ?? '')) {
    if (role !== 'code-reviewer') block(`the ${role} role has no shell access (.claude/agents/${role}.md)`);
    checkReviewerShell(segments, hasSubst);
  }
  return gate;
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

// The checks that need no network. Returns the gate for a Producer merge or launch, else null.
function check(input) {
  if (!input || typeof input !== 'object') block('the hook input is not a JSON object');
  const tool = input.tool_name;
  if (!SHELL_TOOLS.has(tool) && !FILE_TOOLS.has(tool)) return null;
  if (FILE_TOOLS.has(tool)) {
    checkFile(input);
    return null;
  }
  const command = input.tool_input?.command;
  if (typeof command !== 'string') block('the tool input has no command');
  return checkShell(command, tool, input);
}

// ---------- GitHub lookups ----------
// Read-only queries for the fixed repository, never the local remote or local refs. Every list
// is read in full: a count that doesn't add up, an error or a timeout blocks.

const REPO_API = `repos/${REPO}`;
const LOOKUP_MS = 12000; // inside the hook's 15-second timeout

function runGh(args) {
  return new Promise((resolve, reject) => {
    execFile('gh', args, { timeout: LOOKUP_MS, maxBuffer: 64 * 1024 * 1024, windowsHide: true }, (err, stdout) => {
      if (err) reject(new Error(`gh api ${args[1]} failed`));
      else resolve(stdout);
    });
  });
}

const PR_QUERY = `query($n:Int!){repository(owner:"smokim38",name:"rts-game"){pullRequest(number:$n){
  state baseRefName headRefOid body changedFiles commits{totalCount}
  closingIssuesReferences(first:10){totalCount nodes{number}}}}}`;
const COMMENTS_QUERY = `query($n:Int!,$endCursor:String){repository(owner:"smokim38",name:"rts-game"){pullRequest(number:$n){
  comments(first:100,after:$endCursor){totalCount pageInfo{hasNextPage endCursor}
  nodes{author{login} body createdAt lastEditedAt includesCreatedEdit}}}}}`;
const ISSUE_QUERY = `query($n:Int!){repository(owner:"smokim38",name:"rts-game"){issue(number:$n){
  state author{login} body labels(first:100){totalCount nodes{name}}
  userContentEdits(first:100){totalCount nodes{editor{login}}}}}}`;

// `run` takes gh's arguments and resolves to its output; tests pass a stub.
function makeGitHub(run = runGh) {
  const api = async (endpoint, ...args) => JSON.parse(await run(['api', endpoint, '--hostname', 'github.com', ...args]));
  const graphql = (query, n, paginate = false) => api('graphql', ...(paginate ? ['--paginate', '--slurp'] : []), '-F', `n=${n}`, '-f', `query=${query}`);
  const pages = async (endpoint) => {
    const list = await api(endpoint, '--paginate', '--slurp');
    if (!Array.isArray(list) || list.length === 0) throw new Error(`no pages from ${endpoint}`);
    return list;
  };
  const complete = (what, got, total, max = Infinity) => {
    if (got !== total || got > max) throw new Error(`${what}: read ${got} of ${total}`);
  };
  return {
    async pullRequest(n) {
      const pr = (await graphql(PR_QUERY, n))?.data?.repository?.pullRequest;
      if (!pr) throw new Error(`PR #${n} not found`);
      const refs = pr.closingIssuesReferences;
      complete('closing issues', refs.nodes.length, refs.totalCount);
      return {
        state: pr.state,
        base: pr.baseRefName,
        head: pr.headRefOid,
        body: pr.body ?? '',
        changedFiles: pr.changedFiles,
        commitCount: pr.commits.totalCount,
        closes: refs.nodes.map((x) => x.number),
      };
    },
    async comments(n) {
      const conns = (await graphql(COMMENTS_QUERY, n, true)).map((p) => p?.data?.repository?.pullRequest?.comments);
      if (conns.length === 0 || conns.some((c) => !c) || conns.at(-1).pageInfo.hasNextPage) throw new Error('PR comments incomplete');
      const nodes = conns.flatMap((c) => c.nodes);
      complete('PR comments', nodes.length, conns[0].totalCount);
      return nodes.map((c) => ({ author: c.author?.login ?? '', body: c.body ?? '', createdAt: c.createdAt, edited: Boolean(c.lastEditedAt || c.includesCreatedEdit) }));
    },
    async commits(n, expected) {
      const list = (await pages(`${REPO_API}/pulls/${n}/commits?per_page=100`)).flat();
      complete('PR commits', list.length, expected, 250);
      return list.map((c) => ({ sha: c.sha, message: c.commit?.message ?? '', parents: (c.parents ?? []).map((p) => p.sha) }));
    },
    async files(n, expected) {
      const list = (await pages(`${REPO_API}/pulls/${n}/files?per_page=100`)).flat();
      complete('PR files', list.length, expected, 3000);
      return list.map((f) => ({ path: f.filename, previous: f.previous_filename ?? null }));
    },
    async checkRuns(sha) {
      const list = await pages(`${REPO_API}/commits/${sha}/check-runs?filter=latest&per_page=100`);
      const runs = list.flatMap((p) => p.check_runs);
      complete('check runs', runs.length, list[0].total_count);
      return runs.map((r) => ({ id: r.id, name: r.name, status: r.status, conclusion: r.conclusion, app: r.app?.slug ?? '' }));
    },
    async statuses(sha) {
      const list = await pages(`${REPO_API}/commits/${sha}/status?per_page=100`);
      const statuses = list.flatMap((p) => p.statuses);
      complete('commit statuses', statuses.length, list[0].total_count);
      return statuses.map((s) => ({ context: s.context, state: s.state }));
    },
    async issue(n) {
      const issue = (await graphql(ISSUE_QUERY, n))?.data?.repository?.issue;
      if (!issue) throw new Error(`Issue #${n} not found`);
      complete('Issue labels', issue.labels.nodes.length, issue.labels.totalCount);
      complete('Issue edits', issue.userContentEdits.nodes.length, issue.userContentEdits.totalCount);
      return {
        state: issue.state,
        author: issue.author?.login ?? '',
        body: issue.body ?? '',
        labels: issue.labels.nodes.map((l) => l.name),
        editors: issue.userContentEdits.nodes.map((e) => e.editor?.login ?? ''),
      };
    },
    async isAncestorOfMain(sha) {
      const { status } = await api(`${REPO_API}/compare/${sha}...main`);
      return status === 'ahead' || status === 'identical';
    },
    async launcherBlob() {
      const { sha } = await api(`${REPO_API}/contents/tools/codex-launch.js?ref=main`);
      if (!/^[0-9a-f]{40}$/.test(sha ?? '')) throw new Error('no blob for tools/codex-launch.js on main');
      return sha;
    },
  };
}

// ---------- merge gate (AGENTS.md §6) ----------

// An authorised Issue (AGENTS.md §3). Same rule as the launcher's check.
function ownerInstruction(body) {
  const lines = (body ?? '').split(/\r?\n/);
  const start = lines.findIndex((l) => /^###\s+Owner instruction\s*$/i.test(l.trim()));
  if (start < 0) return false;
  const section = [];
  for (const line of lines.slice(start + 1)) {
    if (/^#{1,3}\s/.test(line)) break;
    section.push(line);
  }
  return section.some((l) => /^>\s*\S/.test(l)) && /\b20\d\d-[01]\d-[0-3]\d\b/.test(section.join('\n'));
}

function authorityProblem(issue) {
  if (issue.state !== 'OPEN') return 'is not open';
  if (issue.author !== OWNER) return `was not opened by ${OWNER}`;
  if (!ownerInstruction(issue.body)) return 'has no "### Owner instruction" section with a quote and a date';
  if (issue.editors.some((e) => e !== OWNER)) return 'was edited by another account';
  return null;
}

const TIERS = ['light', 'owner', 'standard', 'critical'];
// The security perimeter (AGENTS.md §6). Paths are compared in lower case.
const PERIMETER = /^(\.claude|\.codex|\.githooks|\.github)\/|^(agents\.md|claude\.md|\.gitattributes|\.gitleaksignore)$|(^|\/)(\.gitignore|\.mcp\.json|\.?gitleaks\.toml)$|^tools\/(install-claude-guard\.ps1|codex-launch(\.test)?\.js)$|^game\/packages\/(manifest|packages-lock)\.json$|^game\/projectsettings\/projectversion\.txt$/;
const CODE_FILE = /\.(cs|js|mjs|cjs|ts|py|ps1|psm1|sh|bat|cmd|shader|hlsl|cginc|compute|glsl|shadergraph|shadersubgraph)$/;
const DOC_FILE = /^docs\/|\.md$/;

// The tier that counts: the highest of the Issue's label, the PR's `Tier:` line and the path minimums.
function requiredTier(issue, body, paths) {
  const labels = issue.labels.filter((l) => /^tier:/i.test(l)).map((l) => l.slice(5).toLowerCase());
  if (labels.length !== 1 || !TIERS.includes(labels[0])) block('the linked Issue needs exactly one tier label');
  const line = /^Tier:[ \t]*(light|owner|standard|critical)[ \t]*$/im.exec(body);
  if (!line) block('the PR body needs a `Tier:` line naming one tier (AGENTS.md §8)');
  const p = paths.map((x) => x.toLowerCase());
  const perimeter = p.some((x) => PERIMETER.test(x));
  let floor = 'light';
  if (perimeter || p.some((x) => x.startsWith('docs/adr/'))) floor = 'critical';
  else if (p.some((x) => /^(game|art|tools)\//.test(x) || CODE_FILE.test(x) || !DOC_FILE.test(x))) floor = 'standard';
  const declared = [labels[0], line[1].toLowerCase()];
  const tier = [...declared, floor].reduce((a, b) => (TIERS.indexOf(b) > TIERS.indexOf(a) ? b : a));
  return { tier, owner: tier === 'owner' || declared.includes('owner') || perimeter, qa: p.some((x) => x.startsWith('game/')) };
}

// Only the latest run of each check counts; gitleaks must have succeeded (AGENTS.md §5).
function checkChecks(runs, statuses) {
  const latest = new Map();
  for (const r of runs) {
    const key = `${r.app}\0${r.name}`;
    if (!latest.has(key) || r.id > latest.get(key).id) latest.set(key, r);
  }
  const gitleaks = latest.get('github-actions\0gitleaks');
  if (gitleaks?.status !== 'completed' || gitleaks.conclusion !== 'success') block('the latest gitleaks check on this commit has not succeeded (AGENTS.md §5)');
  for (const r of latest.values()) {
    if (r.status !== 'completed' || !['success', 'neutral', 'skipped'].includes(r.conclusion)) {
      block(`the check ${JSON.stringify(String(r.name).slice(0, 60))} is ${r.conclusion ?? r.status}`);
    }
  }
  for (const s of statuses) if (s.state !== 'success') block(`the status ${JSON.stringify(String(s.context).slice(0, 60))} is ${s.state}`);
}

// Who wrote the PR: `Codex-Run:` trailers mark launcher output, every other commit is Claude
// work. Merges of `main` into the branch don't count.
async function vendorsOf(commits, gh) {
  const vendors = new Set();
  for (const c of commits) {
    if (c.parents.length > 1 && (await Promise.all(c.parents.slice(1).map((p) => gh.isAncestorOfMain(p)))).every(Boolean)) continue;
    vendors.add(/^Codex-Run:[ \t]*\S+[ \t]*$/m.test(c.message) ? 'codex' : 'claude');
  }
  if (vendors.size === 0) vendors.add('claude');
  return vendors;
}

// The newest record of each type (and vendor) for the SHA. Only the owner account's comments count.
function recordsFor(comments, n, sha) {
  const latest = new Map();
  for (const c of comments) {
    if (c.author !== OWNER || !RECORD.test(c.body)) continue;
    const r = parseRecord(c.body);
    if (!r || r.pr !== n) block('a record comment on this PR is malformed or names another PR; post a new, well-formed record (docs/studio/workflow.md)');
    if (c.edited) block('a record comment on this PR was edited after posting; records are never edited (docs/studio/workflow.md)');
    if (r.sha !== sha) continue;
    const key = `${r.type}:${r.vendor}`;
    if (!latest.has(key) || c.createdAt >= latest.get(key).at) latest.set(key, { ...r, at: c.createdAt });
  }
  return latest;
}

async function checkMerge({ pr: n, sha }, gh) {
  const pr = await gh.pullRequest(n);
  if (pr.state !== 'OPEN' || pr.base !== 'main') block(`PR #${n} is not an open pull request on main`);
  if (pr.head !== sha) block(`PR #${n}'s head is ${pr.head}, not ${sha}; review the new head first`);
  const bodyCloses = [...pr.body.matchAll(/\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)[ \t]+#(\d+)\b/gi)].map((m) => Number(m[1]));
  const linked = new Set([...bodyCloses, ...pr.closes]);
  if (linked.size !== 1 || pr.closes.length !== 1 || bodyCloses.length === 0) block(`PR #${n} must close exactly one Issue, with \`Closes #<n>\` in its body`);
  const [number] = linked;
  const [issue, comments, commits, files, runs, statuses] = await Promise.all([
    gh.issue(number),
    gh.comments(n),
    gh.commits(n, pr.commitCount),
    gh.files(n, pr.changedFiles),
    gh.checkRuns(sha),
    gh.statuses(sha),
  ]);
  const problem = authorityProblem(issue);
  if (problem) block(`Issue #${number} ${problem}, so it doesn't authorise work (AGENTS.md §3)`);
  checkChecks(runs, statuses);
  // A rename counts by its old and its new path.
  const need = requiredTier(issue, pr.body, files.flatMap((f) => (f.previous ? [f.path, f.previous] : [f.path])));
  const records = recordsFor(comments, n, sha);
  for (const r of records.values()) {
    if (['request-changes', 'escalate', 'fail'].includes(r.result)) block(`the newest ${r.type}${r.vendor ? ` (${r.vendor})` : ''} record for ${sha} is ${r.result}`);
  }
  const missing = [];
  if (need.tier === 'light' && !/self-check/i.test(pr.body)) missing.push('the author’s self-check in the PR body');
  if (TIERS.indexOf(need.tier) >= TIERS.indexOf('standard')) {
    for (const vendor of await vendorsOf(commits, gh)) {
      const reviewer = vendor === 'codex' ? 'claude' : 'codex';
      if (!records.has(`verdict:${reviewer}`)) missing.push(`an Approve verdict from ${reviewer} for ${vendor}-written work`);
    }
  }
  if (need.owner && !records.has('owner-approval:')) missing.push('an owner-approval record');
  if (need.qa && !records.has('qa:')) missing.push('a QA: pass record (it changes Game/)');
  if (missing.length) block(`PR #${n} at ${sha} (${need.tier} tier) is missing ${missing.join(', ')} (AGENTS.md §6)`);
}

// ---------- launcher check (AGENTS.md §5) ----------

const blobSha = (buf) => crypto.createHash('sha1').update(`blob ${buf.length}\0`).update(buf).digest('hex');

// Only the launcher merged on GitHub's main may run: the file under the project root, with no
// symlink or junction on the way, must match main's blob, as checked out or with LF line endings.
async function checkLauncher(input, gh) {
  const root = projectRoot(input);
  if (!samePath(input.cwd || root, root)) block('run the launcher from the repository root');
  const file = path.join(root, 'tools', 'codex-launch.js');
  let content;
  try {
    const expected = path.join(fs.realpathSync.native(root), 'tools', 'codex-launch.js');
    const linked = [file, path.dirname(file)].some((p) => fs.lstatSync(p).isSymbolicLink());
    if (linked || !samePath(fs.realpathSync.native(file), expected)) block('tools/codex-launch.js must be a plain file in the repository, not reached through a link');
    content = fs.readFileSync(expected);
  } catch (err) {
    if (err instanceof Blocked) throw err;
    block('tools/codex-launch.js can’t be read');
  }
  const lf = Buffer.from(content.toString('latin1').replace(/\r\n/g, '\n'), 'latin1');
  const main = await gh.launcherBlob();
  if (blobSha(content) !== main && blobSha(lf) !== main) block('tools/codex-launch.js differs from the reviewed copy on GitHub main; only the merged launcher may run');
}

// Everything: the local checks, then GitHub for a Producer merge or launch.
async function checkAsync(input, gh = makeGitHub()) {
  const gate = check(input);
  if (!gate) return;
  let timer;
  const deadline = new Promise((resolve, reject) => {
    timer = setTimeout(() => reject(new Error('timed out')), LOOKUP_MS);
  });
  try {
    await Promise.race([gate.kind === 'merge' ? checkMerge(gate, gh) : checkLauncher(input, gh), deadline]);
  } catch (err) {
    if (err instanceof Blocked) throw err;
    block(`a GitHub lookup failed, so the ${gate.kind} can’t be checked (${err.message})`);
  } finally {
    clearTimeout(timer);
  }
}

async function main() {
  try {
    await checkAsync(JSON.parse(fs.readFileSync(0, 'utf8')));
  } catch (err) {
    const why = err instanceof Blocked ? err.message : `the guard could not check this call (${err.message})`;
    process.stderr.write(`Blocked by the studio guard: ${why}.\nDon't retry in another form. If it is really needed, stop and ask the owner.\n`);
    process.exit(2);
  }
  process.exit(0);
}

if (require.main === module) main();

module.exports = { check, checkAsync, makeGitHub, blobSha, Blocked };
