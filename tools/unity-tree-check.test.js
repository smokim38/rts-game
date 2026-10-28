'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync, spawnSync } = require('node:child_process');
const { main } = require('./unity-tree-check.js');

const source = path.resolve(__dirname, '..');
const fixtureRoot = path.join(source, 'Game', '.utmp');
const script = path.join(__dirname, 'unity-tree-check.js');
const settings = [
  'Game/Assets/Settings/Mobile_RPAsset.asset',
  'Game/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset',
  'Game/Assets/Settings/DefaultVolumeProfile.asset',
  'Game/Assets/Settings/PC_RPAsset.asset',
  'Game/ProjectSettings/ProjectSettings.asset',
  'Game/ProjectSettings/UnityConnectSettings.asset',
];
const other = 'Game/Assets/Settings/PC_Renderer.asset';
const git = (cwd, args) => execFileSync('git', args, {
  cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
});

function fixture(t) {
  // Borrow existing HEAD without creating commits or running a checkout of unrelated files.
  // AGENTS.md section 5 keeps writes in this worktree; Game/.utmp/ is already git-ignored.
  fs.mkdirSync(fixtureRoot, { recursive: true });
  const dir = fs.mkdtempSync(path.join(fixtureRoot, '.unity-tree-check-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(dir)), fixtureRoot);
    assert.ok(path.basename(dir).startsWith('.unity-tree-check-'));
    fs.rmSync(dir, { recursive: true, force: true });
  });
  git(source, ['clone', '--quiet', '--shared', '--no-checkout', '--', source, dir]);
  git(dir, ['restore', '--source=HEAD', '--staged', '--worktree', '--',
    '.gitattributes', 'Game/Assets/', 'Game/Packages/', 'Game/ProjectSettings/']);
  const write = (file, content = 'test change\n') => {
    const target = path.join(dir, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content);
  };
  const status = () => git(dir, ['status', '--porcelain', '-z', '--untracked-files=all', '--',
    'Game/Assets/', 'Game/Packages/', 'Game/ProjectSettings/']);
  const run = (...args) => {
    const result = spawnSync(process.execPath, [script, ...args], { cwd: dir, encoding: 'utf8' });
    assert.ifError(result.error);
    assert.equal(result.stderr, '');
    return { code: result.status, output: result.stdout };
  };
  return { dir, write, status, run };
}

test('clean scoped tree succeeds; changes outside the three folders are ignored', t => {
  const f = fixture(t);
  f.write('Game/Other/file.txt');
  f.write('notes.txt');
  const result = f.run();
  assert.equal(result.code, 0);
  assert.equal(result.output, 'No unexpected changes remain.\n');
});

test('restores all six settings to HEAD, marks only the five settled files, then succeeds', t => {
  const f = fixture(t);
  for (const file of settings) f.write(file);
  const result = f.run();
  assert.equal(result.code, 0, result.output);
  for (const [i, file] of settings.entries()) {
    const line = result.output.split('\n').find(line => line.startsWith(`Restored: ${file}`));
    assert.ok(line, file);
    assert.equal(line.includes('unexpected'), i < 5, file);
    assert.deepEqual(fs.readFileSync(path.join(f.dir, file)),
      execFileSync('git', ['show', `HEAD:${file}`], { cwd: f.dir, stdio: ['ignore', 'pipe', 'pipe'] }));
  }
  assert.equal(f.status(), '');
});

test('unstaged deletion of a known setting is restored', t => {
  const f = fixture(t);
  fs.unlinkSync(path.join(f.dir, settings[0]));
  assert.equal(f.run().code, 0);
  assert.equal(f.status(), '');
});

for (const file of settings) {
  test(`staged setting is preserved and fails, even when allowed: ${file}`, t => {
    const f = fixture(t);
    f.write(file, 'staged\n');
    git(f.dir, ['add', '--', file]);
    f.write(file, 'unstaged\n');
    const before = f.status();
    for (const args of [[], ['--allow', file]]) {
      const result = f.run(...args);
      assert.equal(result.code, 1);
      assert.ok(result.output.includes(`Unexpected staged change: ${file}`));
      assert.equal(f.status(), before);
      assert.equal(git(f.dir, ['show', `:${file}`]), 'staged\n');
      assert.equal(fs.readFileSync(path.join(f.dir, file), 'utf8'), 'unstaged\n');
    }
  });
}

test('staged deletion with an untracked replacement of a setting stays untouched', t => {
  const f = fixture(t);
  const file = settings[0];
  git(f.dir, ['rm', '--', file]);
  f.write(file);
  const before = f.status();
  assert.equal(f.run().code, 1);
  assert.equal(f.status(), before);
});

test('repeatable exact allow paths preserve tracked, staged and untracked files', t => {
  const f = fixture(t);
  const files = [...settings, other, 'Game/Assets/New folder/space \u5355\u4f4d.asset'];
  for (const file of files) f.write(file);
  git(f.dir, ['add', '--', other]);
  const before = f.status();
  const result = f.run(...files.flatMap(file => ['--allow', file]));
  assert.equal(result.code, 0, result.output);
  assert.equal(f.status(), before);
  for (const file of files) assert.ok(result.output.includes(`Allowed: ${file}`));
});

test('allow paths are repo-relative even when invoked from a project subdirectory', t => {
  const f = fixture(t);
  f.write(settings[0]);
  const lines = [];
  assert.equal(main(['--allow', settings[0].replaceAll('/', '\\')], {
    cwd: path.join(f.dir, 'Game'), log: line => lines.push(line),
  }), 0);
  assert.ok(lines.some(line => line.startsWith('Allowed:')));
  assert.ok(f.status().includes(settings[0]));
});

test('all package changes fail and remain untouched, including explicitly allowed files', t => {
  const f = fixture(t);
  const files = ['Game/Packages/manifest.json', 'Game/Packages/packages-lock.json', 'Game/Packages/new file.json'];
  for (const file of files) f.write(file);
  git(f.dir, ['add', '--', files[1]]);
  const before = f.status();
  const result = f.run(...files.flatMap(file => ['--allow', file]));
  assert.equal(result.code, 1);
  assert.equal(f.status(), before);
  for (const file of files) assert.ok(result.output.includes(`Unexpected package change: ${file}`));
});

test('other modifications, staged files, deletions and untracked files fail without changing them', t => {
  const f = fixture(t);
  const files = [other, 'Game/ProjectSettings/QualitySettings.asset',
    'Game/Assets/New folder/space \u5355\u4f4d.asset', 'Game/ProjectSettings/new file.asset'];
  for (const file of files) f.write(file);
  git(f.dir, ['add', '--', other]);
  fs.unlinkSync(path.join(f.dir, files[1]));
  const before = f.status();
  const result = f.run('--allow', 'Game/Assets/New folder');
  assert.equal(result.code, 1);
  assert.equal(f.status(), before);
  for (const file of files) assert.ok(result.output.includes(`Unexpected: ${file}`));
});

test('staged rename checks both endpoints, even if the destination is allowed', t => {
  const f = fixture(t);
  const renamed = 'Game/Assets/renamed.asset';
  git(f.dir, ['mv', '--', settings[0], renamed]);
  const before = f.status();
  const result = f.run('--allow', renamed);
  assert.equal(result.code, 1);
  assert.ok(result.output.includes(`Unexpected staged change: ${settings[0]}`));
  assert.equal(f.status(), before);
});

test('unexpected files still fail after a known rewrite has been restored', t => {
  const f = fixture(t);
  f.write(settings[5]);
  f.write(other);
  const result = f.run();
  assert.equal(result.code, 1);
  assert.ok(result.output.includes(`Restored: ${settings[5]}`));
  assert.ok(!f.status().includes(settings[5]));
  assert.ok(f.status().includes(other));
});

test('fails if a successful git restore leaves a dirty path', t => {
  const f = fixture(t);
  f.write(settings[0]);
  const lines = [];
  const result = main([], { cwd: f.dir, log: line => lines.push(line), git: (args, cwd) => {
    const output = git(cwd, args);
    if (args[0] === 'restore') f.write(settings[0], 'rewrite after restore\n');
    return output;
  } });
  assert.equal(result, 1);
  assert.ok(lines.some(line => line.includes('git status is still nonempty')));
  assert.ok(f.status().includes(settings[0]));
});

test('a file staged after the initial scan is never restored', t => {
  const f = fixture(t);
  f.write(settings[0]);
  let scans = 0;
  const result = main([], { cwd: f.dir, log: () => {}, git: (args, cwd) => {
    const output = git(cwd, args);
    if (args[0] === 'status' && ++scans === 1) git(cwd, ['add', '--', settings[0]]);
    assert.notEqual(args[0], 'restore');
    return output;
  } });
  assert.equal(result, 1);
  assert.ok(f.status().startsWith('M '));
});

test('restore command failure stops further restores but names every remaining change', t => {
  const f = fixture(t);
  for (const file of settings) f.write(file);
  const packageFile = 'Game/Packages/manifest.json';
  const untracked = 'Game/ProjectSettings/new file.asset';
  f.write(packageFile);
  f.write(other);
  f.write(untracked);
  const before = f.status();
  const lines = [];
  const restores = [];
  assert.equal(main([], { cwd: f.dir, log: line => lines.push(line), git: (args, cwd) => {
    if (args[0] === 'restore') { restores.push(args.at(-1)); throw Error('test restore failure'); }
    return git(cwd, args);
  } }), 1);
  assert.equal(restores.length, 1);
  assert.ok(lines.some(line => line.startsWith(`Restore failed: ${restores[0]}:`)));
  for (const file of settings.filter(file => file !== restores[0])) {
    assert.ok(lines.includes(`Not restored: ${file}; an earlier restore failed.`), file);
  }
  assert.ok(lines.some(line => line.startsWith(`Unexpected package change: ${packageFile}`)));
  for (const file of [other, untracked]) {
    assert.ok(lines.some(line => line.startsWith(`Unexpected: ${file}`)), file);
  }
  assert.ok(!lines.includes('No unexpected changes remain.'));
  assert.equal(f.status(), before);
});

test('invalid arguments and git failure exit 1 without changing files', t => {
  const f = fixture(t);
  f.write(settings[0]);
  const before = f.status();
  for (const args of [['--allow'], ['--unknown'], ['--allow', '../escape'], ['--allow', '/absolute']]) {
    assert.equal(f.run(...args).code, 1);
    assert.equal(f.status(), before);
  }
  const outsideRepo = path.join(f.dir, 'not-a-repo');
  fs.mkdirSync(outsideRepo);
  const lines = [];
  assert.equal(main([], { cwd: path.join(outsideRepo, 'missing'), log: line => lines.push(line) }), 1);
  assert.ok(lines[0].startsWith('git rev-parse failed'));
});
