'use strict';
// Run after the last Unity run: node tools/unity-tree-check.js [--allow <repo-relative-file>]...
const { execFileSync } = require('node:child_process');
const path = require('node:path');

const ROOTS = ['Game/Assets/', 'Game/Packages/', 'Game/ProjectSettings/'];
// ADR 0010, "Each file"; all five settled files were committed in #125.
const REWRITES = new Map([
  ['Game/Assets/Settings/Mobile_RPAsset.asset', true],
  ['Game/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset', true],
  ['Game/Assets/Settings/DefaultVolumeProfile.asset', true],
  ['Game/Assets/Settings/PC_RPAsset.asset', true],
  ['Game/ProjectSettings/ProjectSettings.asset', true],
  ['Game/ProjectSettings/UnityConnectSettings.asset', false],
]);

function runGit(args, cwd) {
  try {
    return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (error) {
    // Git stderr can include local paths containing a user name; keep it out of reports.
    throw Error(`git ${args[0]} failed (${error.code || error.status}); no workaround attempted.`);
  }
}

function allowedPaths(argv) {
  const allowed = new Set();
  for (let i = 0; i < argv.length; i += 2) {
    const value = argv[i + 1]?.replace(/\\/g, '/').replace(/^\.\//, '');
    if (argv[i] !== '--allow' || !value || value.startsWith('-') ||
        path.posix.isAbsolute(value) || /^[A-Za-z]:/.test(value) ||
        value.split('/').some(part => !part || part === '.' || part === '..')) {
      throw Error('Usage: node tools/unity-tree-check.js [--allow <repo-relative-file>]... (exact paths only)');
    }
    allowed.add(value);
  }
  return allowed;
}

function status(git, cwd, paths) {
  // NUL delimiters preserve spaces and Unicode. Split renames so both endpoints are checked.
  return git(['status', '--porcelain=v1', '-z', '--untracked-files=all', '--no-renames', '--', ...paths], cwd)
    .split('\0').filter(Boolean).map(record => ({ code: record.slice(0, 2), file: record.slice(3) }));
}

const staged = entry => entry.code !== '??' && entry.code[0] !== ' ';

function main(argv = process.argv.slice(2), { cwd = process.cwd(), git = runGit, log = console.log } = {}) {
  try {
    const allowed = allowedPaths(argv);
    const root = git(['rev-parse', '--show-toplevel'], cwd).trim();
    const entries = status(git, root, ROOTS);
    const stagedFiles = new Set(entries.filter(staged).map(entry => entry.file));
    const seen = new Set();
    let failed = false;
    let restoreFailed = false;
    for (const { file, code } of entries) {
      if (seen.has(file)) continue;
      seen.add(file);
      if (file.startsWith('Game/Packages/')) {
        log(`Unexpected package change: ${file} (${code}); left untouched.`);
        failed = true;
      } else if (REWRITES.has(file) && stagedFiles.has(file)) {
        log(`Unexpected staged change: ${file}; left untouched.`);
        failed = true;
      } else if (allowed.has(file)) {
        log(`Allowed: ${file} (${code}); left untouched.`);
      } else if (REWRITES.has(file)) {
        if (restoreFailed) {
          log(`Not restored: ${file}; an earlier restore failed.`);
          continue;
        }
        // Recheck immediately before touching the index; a newly staged file stays untouched.
        if (status(git, root, [file]).some(staged)) {
          log(`Unexpected staged change: ${file}; left untouched.`);
          failed = true;
          continue;
        }
        try {
          git(['restore', '--source=HEAD', '--staged', '--worktree', '--', file], root);
          if (status(git, root, [file]).length) throw Error('git status is still nonempty after restore.');
          log(`Restored: ${file}${REWRITES.get(file) ? ' (unexpected: settled in #125)' : ''}`);
        } catch (error) {
          log(`Restore failed: ${file}: ${error.message}`);
          failed = true;
          restoreFailed = true;
        }
      } else {
        log(`Unexpected: ${file} (${code}); left untouched.`);
        failed = true;
      }
    }
    if (!failed) log('No unexpected changes remain.');
    return failed ? 1 : 0;
  } catch (error) {
    log(error.message);
    return 1;
  }
}

if (require.main === module) process.exitCode = main();
module.exports = { main };
