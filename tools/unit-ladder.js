'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { execFileSync, spawn } = require('node:child_process');

const STEPS = Object.freeze([10, 50, 100, 200, 300, 400, 500, 1000, 2000]);
const LOW_REGRESSION_CUTOFF_MS = 3.0;
const finitePositive = value => Number.isFinite(value) && value > 0;
const average = values => values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
function median(values) {
  if (values.length !== 3 || !values.every(Number.isFinite)) throw Error('Three finite runs required.');
  return [...values].sort((a, b) => a - b)[1];
}
function nearestRank(values, percentile = 0.99) {
  if (!values.length || !values.every(finitePositive) || !Number.isFinite(percentile) || percentile <= 0 || percentile > 1) throw Error('Invalid percentile input.');
  return [...values].sort((a, b) => a - b)[Math.ceil(percentile * values.length) - 1];
}
function summarizeRun(meta, rows, units) {
  const reasons = [];
  if (!meta.featureEnabled) reasons.push('FrameTimingManager unavailable or timer clocks differ');
  if (meta.focusLost) reasons.push('focus lost');
  if (meta.paused) reasons.push('paused');
  if (meta.bufferInvalid) reasons.push('invalid or overflowing frame buffer');
  if (meta.unmatchedSamples !== 0) reasons.push('unmatched frame timing samples');
  if (meta.matchedSamples !== rows.reduce((count, row) => count + row.samples, 0))
    reasons.push('frame timing sample count mismatch');
  if (!meta.activeUnitsValid) reasons.push('active faction unit counts changed');
  if (meta.development) reasons.push('development Player');
  if (meta.width !== 1920 || meta.height !== 1080 || !meta.borderless || meta.vsync !== 0 || meta.targetFrameRate !== -1)
    reasons.push('incorrect display settings');
  if (meta.units !== units) reasons.push('incorrect unit count');
  if (!(meta.warmupSeconds >= 10) || !(meta.seconds >= 60)) reasons.push('short warm-up or window');
  if (!rows.length || meta.renderedFrames !== rows.length || meta.recordedFrames !== rows.length)
    reasons.push('rendered frame count mismatch');
  const timestamps = new Set();
  let previous = -1n;
  for (const row of rows) {
    if (row.samples !== 1 || !finitePositive(row.frameMs)) reasons.push('missing, duplicate or non-positive frame timing');
    if (!/^\d+$/.test(row.timestamp) || !/^\d+$/.test(row.callback)) {
      reasons.push('malformed frame timestamp'); continue;
    }
    if (BigInt(row.timestamp) === 0n || timestamps.has(row.timestamp))
      reasons.push('missing or duplicate frame timestamp');
    timestamps.add(row.timestamp);
    if (BigInt(row.callback) <= previous || BigInt(row.timestamp) > BigInt(row.callback))
      reasons.push('unmatched frame timestamp');
    if (previous >= 0n && BigInt(row.timestamp) <= previous) reasons.push('sample belongs to another frame');
    previous = BigInt(row.callback);
  }
  if (reasons.length) return { valid: false, reasons: [...new Set(reasons)] };
  const frameTimes = rows.map(row => row.frameMs);
  const main = rows.map(row => row.mainMs).filter(finitePositive);
  const gpu = rows.map(row => row.gpuMs).filter(finitePositive);
  const frameMs = average(frameTimes);
  return { valid: true, frames: rows.length, frameMs, lowMs: nearestRank(frameTimes), maxMs: frameTimes.reduce((a, b) => Math.max(a, b), 0),
    mainMs: average(main), gpuMs: average(gpu), fps: 1000 / frameMs,
    missingMain: rows.length - main.length, missingGpu: rows.length - gpu.length };
}
function regressed(current, before) {
  return current > before * 1.1 && current > before + 0.5;
}
function summarizeStep(units, attempts, before) {
  const valid = attempts.filter(run => run.valid).slice(0, 3);
  if (valid.length < 3) return { units, result: 'invalid', attempts };
  const result = { units, attempts };
  for (const key of ['frameMs', 'lowMs', 'maxMs', 'mainMs', 'gpuMs', 'fps'])
    result[key] = valid.every(run => Number.isFinite(run[key])) ? median(valid.map(run => run[key])) : null;
  result.result = result.frameMs <= 16.67 && result.lowMs <= 16.67 ? 'pass' : 'fail';
  result.regressed = !!before && before.result !== 'invalid' &&
    (regressed(result.frameMs, before.frameMs) ||
      (result.lowMs >= LOW_REGRESSION_CUTOFF_MS && regressed(result.lowMs, before.lowMs)));
  result.previouslyPassing = !!before && before.result === 'pass' && result.result !== 'pass';
  const averages = valid.map(run => run.frameMs);
  result.spreadMs = Math.max(...averages) - Math.min(...averages);
  result.spreadPercent = result.spreadMs / Math.min(...averages) * 100;
  result.spreadExceedsTolerance = regressed(Math.max(...averages), Math.min(...averages));
  return result;
}
async function collectStep(units, run, before) {
  const attempts = [];
  while (attempts.length < 6 && attempts.filter(item => item.valid).length < 3)
    attempts.push(await run(units, attempts.length + 1));
  return summarizeStep(units, attempts, before);
}
function ladderHeight(steps) {
  let height = 0;
  for (let i = 0; i < STEPS.length; i++) {
    if (steps[i]?.units !== STEPS[i] || steps[i]?.result !== 'pass') break;
    height = STEPS[i];
  }
  return height;
}
function parseCsv(text) {
  const lines = text.trim().split(/\r?\n/);
  if (lines.shift() !== 'callback,timestamp,samples,frameMs,mainMs,gpuMs') throw Error('Invalid timing CSV.');
  return lines.filter(Boolean).map(line => {
    const [callback, timestamp, samples, frameMs, mainMs, gpuMs] = line.split(',');
    return { callback, timestamp, samples: Number(samples), frameMs: Number(frameMs), mainMs: Number(mainMs), gpuMs: Number(gpuMs) };
  });
}
function hasPlayerLogFailure(log) {
  return /\b\w*Exception\b|\b(?:error CS\d+|warning CS\d+)\b/.test(log);
}
function git(root, ...args) { return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim(); }
function newestBaseline(entries, hardware, version) {
  return entries.filter(item => item.data.scenarioVersion === version &&
    JSON.stringify(item.data.hardware) === JSON.stringify(hardware)).sort((a, b) => a.order - b.order)[0];
}
function mainBaseline(root, hardware, version) {
  // Addition history, newest first: a hash in the filename is not chronological.
  return newestBaseline([...new Set(git(root, 'log', '--format=', '--name-only', '--diff-filter=A', 'main', '--', 'docs/performance/unit-ladder').split('\n'))]
    .filter(name => /\d{4}-\d{2}-\d{2}-[0-9a-f]{7}(?:-\d+)?\.md$/.test(name)).map((name, order) => {
      const content = git(root, 'show', 'main:' + name);
      const match = content.match(/```json\n([\s\S]*?)\n```/);
      if (!match) throw Error('Unrecognized main ladder entry: ' + name);
      return { name, order, data: JSON.parse(match[1]) };
    }), hardware, version);
}
function safeText(value) {
  if (typeof value !== 'string' || !value || /[\\/\r\n<>|`]|\b(?:\d{1,3}\.){3}\d{1,3}\b/.test(value)) throw Error('Unsafe entry field.');
  return value;
}
function windowsHardware(root) {
  // Fixed, value-only reads work in the sandbox; enumerating this registry class
  // or querying CIM does not. Never request adapter IDs or other machine identity.
  const command = "$ErrorActionPreference='Stop'; $g=@(0..31 | ForEach-Object { " +
    "$p='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\'+('{0:D4}' -f $_); " +
    "Get-ItemProperty -LiteralPath $p -Name DriverDesc,DriverVersion -ErrorAction SilentlyContinue | Select-Object DriverDesc,DriverVersion }); " +
    "$w=Get-ItemProperty -LiteralPath 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion' " +
    "-Name ProductName,CurrentMajorVersionNumber,CurrentMinorVersionNumber,CurrentBuildNumber,UBR | " +
    "Select-Object ProductName,CurrentMajorVersionNumber,CurrentMinorVersionNumber,CurrentBuildNumber,UBR; " +
    "@{gpu=$g;windows=$w}|ConvertTo-Json -Depth 4 -Compress";
  return JSON.parse(execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command],
    { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }));
}
function hardwareInfo(meta, data) {
  if (!Number.isInteger(meta.cores) || !Number.isInteger(meta.threads) || meta.cores <= 0 || meta.threads < meta.cores)
    throw Error('Missing or invalid native CPU core/thread counts; rebuild Players.');
  const gpu = data.gpu.filter(item => typeof item.DriverDesc === 'string' &&
    item.DriverDesc.toLowerCase() === safeText(meta.gpu).toLowerCase());
  if (!gpu.length || gpu.some(item => item.DriverVersion !== gpu[0].DriverVersion))
    throw Error('No unambiguous driver version for the rendered GPU.');
  const w = data.windows;
  for (const value of [w.CurrentMajorVersionNumber, w.CurrentMinorVersionNumber, w.UBR])
    if (!Number.isInteger(value) || value < 0) throw Error('Missing Windows version information.');
  if (!/^\d+$/.test(w.CurrentBuildNumber)) throw Error('Missing Windows build information.');
  // Windows 11 retains the Windows 10 ProductName on some installations.
  const name = Number(w.CurrentBuildNumber) >= 22000 ? safeText(w.ProductName).replace(/^Windows 10\b/, 'Windows 11') : safeText(w.ProductName);
  return { cpu: [{ model: safeText(meta.cpu), cores: meta.cores, threads: meta.threads }],
    gpu: [{ model: safeText(meta.gpu), driver: safeText(gpu[0].DriverVersion) }],
    windows: { name, version: `${w.CurrentMajorVersionNumber}.${w.CurrentMinorVersionNumber}.${w.CurrentBuildNumber}.${w.UBR}`, build: w.CurrentBuildNumber } };
}
function launch(player, args, root) {
  return new Promise(resolve => {
    const child = spawn(player, args, { cwd: root, stdio: 'ignore', windowsHide: false });
    let timedOut = false;
    const timeout = setTimeout(() => { timedOut = true; child.kill(); }, 120000);
    child.once('error', () => { clearTimeout(timeout); resolve('Player launch failed'); });
    child.once('close', code => { clearTimeout(timeout); resolve(timedOut ? 'Player timed out' : code === 0 ? null : 'Player exit failure'); });
  });
}
function renderEntry(data) {
  const number = value => value == null ? 'unavailable' : value.toFixed(3);
  return `# Unit performance ladder — ${data.date}\n\nContext: ${data.context}\n\nCommit: ${data.commit}\n\n` +
    `Reference-class: ${data.referenceClass}\n\nLadder height: ${data.height}\n\nComparison: ${data.comparison || 'none (new series)'}\n\n` +
    'Units hold position. Systems: spawn/command application at 30 Hz; Burst draw-matrix fill and instanced drawing every frame. No movement, spatial grid, targeting, combat or commander AI exists in this build. Seed is fixed; no random work is present.\n\n' +
    '| Units | Average ms | 1% low ms | Maximum ms | CPU main ms | GPU ms | FPS | Result |\n' +
    '| --- | --- | --- | --- | --- | --- | --- | --- |\n' + data.steps.map(step =>
      `| ${step.units} | ${number(step.frameMs)} | ${number(step.lowMs)} | ${number(step.maxMs)} | ${number(step.mainMs)} | ${number(step.gpuMs)} | ${number(step.fps)} | ${step.result}${step.regressed ? ', regressed' : ''}${step.previouslyPassing ? ', previously passing' : ''} |`).join('\n') +
    '\n\nMachine-readable summary (includes all attempts, three-run averages/spread and missing optional samples):\n\n```json\n' + JSON.stringify(data, null, 2) + '\n```\n';
}
function verifyBuildSources(root) {
  const lines = fs.readFileSync(path.join(root, 'Game/Builds/PerformanceLadder/sources.txt'), 'utf8').trim().split(/\r?\n/);
  const expected = new Set();
  for (const line of lines) {
    const match = line.match(/^([0-9a-f]{64}) ((?:Assets|Packages|ProjectSettings)\/[^\r\n]+)$/);
    if (!match || match[2].split('/').some(part => part === '..' || part === '.')) throw Error('Malformed build fingerprint.');
    const filename = path.join(root, 'Game', match[2]);
    if (createHash('sha256').update(fs.readFileSync(filename)).digest('hex') !== match[1]) throw Error('Source changed since build; rebuild Players.');
    expected.add(match[2]);
  }
  const tracked = git(root, 'ls-files', 'Game/Assets', 'Game/Packages', 'Game/ProjectSettings').split('\n');
  if (tracked.some(filename => !expected.has(filename.slice('Game/'.length)))) throw Error('New source since build; rebuild Players.');
}
async function main(args) {
  const root = path.resolve(__dirname, '..');
  if (args.length !== 2 || !/^(baseline|PR #\d+|re-run: .+)$/.test(args[0]) || !['yes', 'no'].includes(args[1]))
    throw Error('Usage: node tools/unit-ladder.js "baseline|PR #n|re-run: reason" yes|no');
  const context = safeText(args[0]);
  const commit = git(root, 'rev-parse', 'HEAD');
  if (git(root, 'status', '--porcelain')) throw Error('Commit the measured sources first; working tree must be clean.');
  if (fs.readFileSync(path.join(root, 'Game/Builds/PerformanceLadder/commit.txt'), 'utf8').trim() !== commit)
    throw Error('Rebuild both Players for HEAD.');
  verifyBuildSources(root);
  const hardware = windowsHardware(root);
  const logs = path.join(root, 'Game/Logs/unit-ladder', new Date().toISOString().replace(/[:.]/g, '-'));
  fs.mkdirSync(logs, { recursive: true });
  const steps = [];
  let firstMeta, comparison, summaryHardware;
  const run = async (units, attempt) => {
    process.stdout.write(`Units ${units}, attempt ${attempt}\n`);
    const output = path.join(logs, `${units}-${attempt}`);
    const error = await launch(path.join(root, 'Game/Builds/PerformanceLadder/PerformanceLadder.exe'),
      ['-ladder-units', String(units), '-ladder-output', output, '-screen-width', '1920', '-screen-height', '1080',
        '-screen-fullscreen', '1', '-window-mode', 'borderless', '-logFile', output + '.log'], root);
    if (error) return { valid: false, reasons: [error] };
    if (!fs.existsSync(output + '.json') || !fs.existsSync(output + '.csv')) return { valid: false, reasons: ['missing Player output'] };
    const meta = JSON.parse(fs.readFileSync(output + '.json', 'utf8'));
    const currentHardware = hardwareInfo(meta, windowsHardware(root));
    const fingerprint = JSON.stringify([meta.scenarioVersion, meta.seed, currentHardware, meta.vramMb, meta.ramMb, meta.graphicsVersion, meta.unity, meta.api, meta.quality]);
    if (!firstMeta) {
      firstMeta = { fingerprint, meta };
      summaryHardware = { ...currentHardware, renderedGpu: safeText(meta.gpu), vramMb: meta.vramMb, ramMb: meta.ramMb };
      comparison = mainBaseline(root, summaryHardware, meta.scenarioVersion);
    }
    if (firstMeta.fingerprint !== fingerprint) return { valid: false, reasons: ['hardware, build or scenario changed'] };
    const result = summarizeRun(meta, parseCsv(fs.readFileSync(output + '.csv', 'utf8')), units);
    const log = fs.readFileSync(output + '.log', 'utf8');
    if (hasPlayerLogFailure(log)) return { valid: false, reasons: ['Player logged an error or project warning'] };
    return result;
  };
  for (const units of STEPS) {
    // Initialize the comparison on the first attempt, before summarizing this step.
    const result = await collectStep(units, run);
    steps.push(summarizeStep(units, result.attempts, comparison?.data.steps.find(step => step.units === units)));
  }
  if (!firstMeta) throw Error('No Player metadata; raw failure logs remain in Game/Logs.');
  if (git(root, 'rev-parse', 'HEAD') !== commit || git(root, 'status', '--porcelain')) throw Error('Sources changed during the ladder; no entry written.');
  if (JSON.stringify(windowsHardware(root)) !== JSON.stringify(hardware)) throw Error('Hardware, driver or Windows changed during ladder; no entry written.');
  const data = { date: new Date().toISOString().slice(0, 10), context, commit, scenarioVersion: firstMeta.meta.scenarioVersion,
    seed: firstMeta.meta.seed, tickHz: 30, referenceClass: args[1], hardware: summaryHardware,
    build: { unity: safeText(firstMeta.meta.unity), backend: 'Mono', api: safeText(firstMeta.meta.api),
      quality: safeText(firstMeta.meta.quality), resolution: '1920x1080 borderless', vsync: 'off', development: false },
    comparison: comparison?.name || null, height: ladderHeight(steps), steps };
  const directory = path.join(root, 'docs/performance/unit-ladder');
  fs.mkdirSync(directory, { recursive: true });
  const stem = `${data.date}-${commit.slice(0, 7)}`;
  let filename = path.join(directory, stem + '.md'), repeat = 2;
  while (fs.existsSync(filename)) filename = path.join(directory, `${stem}-${repeat++}.md`);
  fs.writeFileSync(filename, renderEntry(data), { flag: 'wx' });
  process.stdout.write(`Entry: ${path.relative(root, filename)}; ladder height ${data.height}\n`);
  if (steps.some(step => step.result === 'invalid' || step.regressed || step.previouslyPassing)) process.exitCode = 1;
}

module.exports = { STEPS, median, nearestRank, summarizeRun, summarizeStep, collectStep, regressed, ladderHeight, newestBaseline, parseCsv, renderEntry, safeText, hasPlayerLogFailure, windowsHardware, hardwareInfo };
if (require.main === module) main(process.argv.slice(2)).catch(error => { process.stderr.write(error.message + '\n'); process.exitCode = 1; });
