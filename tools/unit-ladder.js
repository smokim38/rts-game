'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { execFileSync, spawn } = require('node:child_process');

const STEPS = Object.freeze([10, 50, 100, 200, 300, 400, 500, 1000, 2000]);
const LOW_REGRESSION_CUTOFF_MS = 3.0;
const INCOMPLETE_CROSSING = 'window closed before both crossing cycles finished';
const MOVEMENT_STATUS = 'armies mostly stuck, movement unsolved';
const LANE_LAYOUT = 'Armies pass side by side on separate lanes; head-on is not tested (S3.4).';
const JOB_MARKERS = Object.freeze(['ApplyCommandsJob', 'SelectionJob', 'PublishFieldsJob', 'RequestFieldsJob',
  'AssignFieldBuildJob', 'BuildFlowFieldJob', 'BucketUnitsJob', 'BuildSpatialGridJob', 'OrderProgressJob',
  'PreferredVelocityJob', 'IntegrateMovementJob', 'OverlapMovementJob', 'MoveCheckPassJob', 'CommitMovementJob',
  'FillDrawDataJob', 'LadderMeetJob', 'LadderPassJob']);
const finitePositive = value => Number.isFinite(value) && value > 0;
const average = values => values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
const publicCrossing = (counts, limit) => Array.isArray(counts) ? counts.slice(0, limit).map(count => Object.fromEntries(
  ['cycle', 'side', 'units', 'passed', 'met', 'travelShare', 'minimumTravelShare'].map(key => [key, Number.isFinite(count?.[key]) ? count[key] : null]))) : null;
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
  const publicCounts = {
    renewedOrdersByArrivalRule: Array.isArray(meta.renewedOrdersByArrivalRule) ?
      meta.renewedOrdersByArrivalRule.slice(0, 4).map(value => Number.isSafeInteger(value) && value >= 0 ? value : null) : null,
    windowCloseTick: Number.isSafeInteger(meta.windowCloseTick) && meta.windowCloseTick >= 0 ? meta.windowCloseTick : null,
    crossing: publicCrossing(meta.crossing, 4),
    cycleZeroCrossing: publicCrossing(meta.cycleZeroCrossing, 2)
  };
  if (meta.scenarioVersion !== 4) reasons.push('incorrect scenario version');
  if (!meta.featureEnabled) reasons.push('FrameTimingManager unavailable or timer clocks differ');
  if (meta.focusLost) reasons.push('focus lost');
  if (meta.paused) reasons.push('paused');
  if (meta.bufferInvalid) reasons.push('invalid or overflowing frame buffer');
  if (meta.unmatchedSamples !== 0) reasons.push('unmatched frame timing samples');
  if (meta.matchedSamples !== rows.reduce((count, row) => count + row.samples, 0))
    reasons.push('frame timing sample count mismatch');
  if (!meta.activeUnitsValid) reasons.push('active unit counts, drawing or order continuity failed');
  if (!Array.isArray(meta.renewedOrdersByArrivalRule) || meta.renewedOrdersByArrivalRule.length !== 4 ||
    !meta.renewedOrdersByArrivalRule.every(value => Number.isSafeInteger(value) && value >= 0))
    reasons.push('missing or invalid order renewal counts');
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
  if (reasons.length) return { valid: false, reasons: [...new Set(reasons)], ...publicCounts,
    crossingChecked: checkCrossing(meta, units).checked };
  const frameTimes = rows.map(row => row.frameMs);
  const main = rows.map(row => row.mainMs).filter(finitePositive);
  const gpu = rows.map(row => row.gpuMs).filter(finitePositive);
  const frameMs = average(frameTimes);
  const result = { valid: true, frames: rows.length, frameMs, lowMs: nearestRank(frameTimes), maxMs: frameTimes.reduce((a, b) => Math.max(a, b), 0),
    mainMs: average(main), gpuMs: average(gpu), fps: 1000 / frameMs,
    missingMain: rows.length - main.length, missingGpu: rows.length - gpu.length,
    ...publicCounts };
  const crossing = checkCrossing(meta, units);
  result.crossingChecked = crossing.checked;
  if (crossing.reasons.length || (!crossing.checked && frameMs <= 16.67 && result.lowMs <= 16.67))
    return { ...result, valid: false, reasons: crossing.reasons.length ? crossing.reasons : [INCOMPLETE_CROSSING] };
  return result;
}
function checkCrossing(meta, units) {
  if (!Number.isSafeInteger(meta.windowCloseTick) || meta.windowCloseTick < 0)
    return { checked: false, reasons: ['missing or invalid window-close tick'] };
  if (meta.windowCloseTick < 1800) return { checked: false, reasons: [] };
  const half = units / 2;
  if (meta.meetDistance !== 2 || !Array.isArray(meta.crossing) || meta.crossing.length !== 4 ||
      !Array.isArray(meta.cycleZeroCrossing) || meta.cycleZeroCrossing.length !== 2)
    return { checked: false, reasons: ['missing or invalid crossing counters'] };
  const reasons = [];
  const counts = [...meta.cycleZeroCrossing, ...meta.crossing];
  for (let i = 0; i < counts.length; i++) {
    const count = counts[i];
    if (!count || count.cycle !== Math.floor(i / 2) || count.side !== i % 2 || count.units !== half ||
        !Number.isSafeInteger(count.passed) || count.passed < 0 || count.passed > half ||
        !Number.isSafeInteger(count.met) || count.met < 0 || count.met > half ||
        !Number.isFinite(count.travelShare) || count.travelShare < 0 ||
        !Number.isFinite(count.minimumTravelShare) || count.minimumTravelShare < 0 ||
        count.minimumTravelShare > count.travelShare + .00001)
      reasons.push('missing or invalid crossing counters');
  }
  return { checked: true, reasons: [...new Set(reasons)] };
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
  while (true) {
    while (attempts.length < 6 && attempts.filter(item => item.valid).length < 3)
      attempts.push(await run(units, attempts.length + 1));
    const step = summarizeStep(units, attempts, before);
    if (step.result !== 'pass' || !attempts.some(item => item.valid && item.crossingChecked === false)) return step;
    for (const item of attempts)
      if (item.valid && item.crossingChecked === false) { item.valid = false; item.reasons = [INCOMPLETE_CROSSING]; }
  }
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
  data = { ...data, movementStatus: MOVEMENT_STATUS };
  const number = value => value == null ? 'unavailable' : value.toFixed(3);
  return `# Unit performance ladder — ${data.date}\n\nContext: ${data.context}\n\nCommit: ${data.commit}\n\n` +
    `Reference-class: ${data.referenceClass}\n\nLadder height: ${data.height} (${MOVEMENT_STATUS})\n\nComparison: ${data.comparison || 'none (new series)'}\n\n` +
    'Scenario version 4: the two armies receive seeded orders along separate offset lanes to alternating far ends every 20 simulation seconds (ADRs 0017, 0018 and 0019). Spawn pitch is 1.42, both blocks fill from the seam outward, and orders use the mean z of each spawn block with own spots. Pass, meet and travel shares are reported, not required. Commands, spatial grid, flow fields and movement run at 30 Hz; instanced drawing uses the current game mesh every frame (ADR 0016). Ended orders are renewed before the next movement step; live orders are never renewed early. All movement rules stay enabled, but rule 2 (contact with an arrived member) cannot fire because renewal removes idle members before its next check. Targeting, combat and commander AI are not implemented. Version 1 held position: versions 1, 3 and 4 are not a like-for-like series. Each attempt reports renewals by arrival rules 1 (exact goal), 2, 3 (goal-disc congestion), 4 (six-second group stall), the window-close tick, cycle 0 and both judged cycles. Travel share is mean endpoint displacement in any direction divided by free travel; it cannot distinguish sideways movement from progress along the lane. These results do not establish smooth movement or the cost of free movement or mixing armies.\n\n' +
    '| Units | Average ms | 1% low ms | Maximum ms | CPU main ms | GPU ms | FPS | Result |\n' +
    '| --- | --- | --- | --- | --- | --- | --- | --- |\n' + data.steps.map(step =>
      `| ${step.units} | ${number(step.frameMs)} | ${number(step.lowMs)} | ${number(step.maxMs)} | ${number(step.mainMs)} | ${number(step.gpuMs)} | ${number(step.fps)} | ${step.result}${step.regressed ? ', regressed' : ''}${step.previouslyPassing ? ', previously passing' : ''} |`).join('\n') +
    `\n\nCrossing shares, ${MOVEMENT_STATUS} (worst judged cycle across valid attempts; first / second side):\n\n` +
    LANE_LAYOUT + '\n\n' +
    '| Units | Pass share | Meet share | Travel share | Cycle 0 travel share |\n| --- | --- | --- | --- | --- |\n' +
    data.steps.map(step => {
      const shares = (key, field = 'crossing') => [0, 1].map(side => {
        const counts = step.attempts.filter(run => run.valid && run.crossingChecked).flatMap(run => run[field].filter(c => c.side === side));
        return counts.length ? number(Math.min(...counts.map(c => key === 'travelShare' ? c[key] : c[key] / c.units))) : 'unchecked';
      }).join(' / ');
      return `| ${step.units} | ${shares('passed')} | ${shares('met')} | ${shares('travelShare')} | ${shares('travelShare', 'cycleZeroCrossing')} |`;
    }).join('\n') + '\n\nLowest single-vehicle travel share (worst valid attempt; first / second side):\n\n' +
    '| Units | Cycle 0 | Cycle 1 | Cycle 2 |\n| --- | --- | --- | --- |\n' +
    data.steps.map(step => '| ' + step.units + ' | ' + [0, 1, 2].map(cycle => [0, 1].map(side => {
      const counts = step.attempts.filter(run => run.valid && run.crossingChecked).flatMap(run =>
        (cycle === 0 ? run.cycleZeroCrossing : run.crossing).filter(c => c.side === side && c.cycle === cycle));
      return counts.length ? number(Math.min(...counts.map(c => c.minimumTravelShare))) : 'unchecked';
    }).join(' / ')).join(' | ') + ' |').join('\n') + '\n\n' + renderMovementCost(data.movementCost, number) +
    '\nMachine-readable summary (includes all attempts, three-run averages/spread and missing optional samples):\n\n```json\n' + JSON.stringify(data, null, 2) + '\n```\n';
}

function renderMovementCost(cost, number) {
  if (!cost) return '';
  return `Movement cost at 2,000 units, ${MOVEMENT_STATUS} (development Player; separate from release frame times):\n\n` +
    '| Measurement | Value |\n| --- | --- |\n' +
    `| Worker jobs, 25 fps, mean ms/tick | ${number(cost.worker.meanMs)} |\n` +
    `| Worker jobs, one-tick-frame 99th percentile ms | ${number(cost.worker.p99Ms)} |\n` +
    `| Wait, uncapped, median / 99th percentile / maximum ms | ${number(cost.wait.medianMs)} / ${number(cost.wait.p99Ms)} / ${number(cost.wait.maxMs)} |\n` +
    `| Wait frames completing a tick | ${cost.wait.frames} |\n` +
    `| Worker capture seconds / ticks / bytes | ${cost.worker.profileSeconds} / ${cost.worker.ticks} / ${cost.worker.captureBytes} |\n` +
    `| Peak working set / committed bytes | ${cost.worker.peakWorkingSetBytes} / ${cost.worker.peakCommittedBytes} |\n` +
    `| Crossing checked, worker / wait run | ${cost.worker.crossingChecked} / ${cost.wait.crossingChecked} |\n\n` +
    'Per-job means and excluded drawing/crossing jobs are in JSON. These single development runs show trends on this hardware only; they do not measure reference-PC headroom or the future ORCA cost.\n';
}

function movementCost(worker, waitMeta, csv, commit) {
  if (worker.commit !== commit || waitMeta.commit !== commit || worker.scenarioVersion !== 4 || waitMeta.scenarioVersion !== 4 ||
      worker.frameRate !== 25 || !worker.ticks || !worker.oneTickFrames || !finitePositive(worker.meanMs) || !finitePositive(worker.p99Ms) ||
      ![20, 40, 60].includes(worker.profileSeconds) || worker.gcAllocBytes !== 0 ||
      typeof worker.crossingChecked !== 'boolean' || !worker.crossingValid ||
      !(worker.captureBytes > 0 && worker.captureBytes <= 201326592) ||
      ![worker.peakWorkingSetBytes, worker.peakCommittedBytes].every(value => value > 0 && value < 16000000000) ||
      !Array.isArray(worker.jobs) || worker.jobs.length !== JOB_MARKERS.length ||
      !waitMeta.development || !waitMeta.waitMode || waitMeta.profile || waitMeta.units !== 2000 || waitMeta.targetFrameRate !== -1 ||
      waitMeta.focusLost || waitMeta.paused || !waitMeta.activeUnitsValid || !(waitMeta.seconds >= 60) || !(waitMeta.warmupSeconds >= 10))
    throw Error('Missing, invalid or stale development movement cost; run both development modes for this commit.');
  const check = checkCrossing(waitMeta, 2000);
  if (check.reasons.length) throw Error('Development wait run has invalid crossing counters: ' + check.reasons.join(', '));
  const lines = csv.trim().split(/\r?\n/);
  if (lines.shift() !== 'waitMs' || !lines.length) throw Error('Missing wait samples.');
  const values = lines.map(Number).sort((a, b) => a - b);
  if (!values.every(value => Number.isFinite(value) && value >= 0)) throw Error('Invalid wait samples.');
  const middle = Math.floor(values.length / 2);
  const markers = new Set();
  const jobs = worker.jobs.map(job => {
    if (!JOB_MARKERS.includes(job.marker) || markers.has(job.marker) ||
        !Number.isSafeInteger(job.samples) || job.samples < 0 ||
        ![job.totalMs, job.meanMsPerTick].every(value => Number.isFinite(value) && value >= 0) ||
        typeof job.includedInTotal !== 'boolean') throw Error('Invalid worker job summary.');
    markers.add(job.marker);
    return { marker: job.marker, samples: job.samples, totalMs: job.totalMs,
      meanMsPerTick: job.meanMsPerTick, includedInTotal: job.includedInTotal };
  });
  const publicWorker = Object.fromEntries(['commit', 'scenarioVersion', 'ticks', 'oneTickFrames', 'frameRate',
    'profileSeconds', 'meanMs', 'p99Ms', 'captureBytes', 'gcAllocBytes', 'peakWorkingSetBytes', 'peakCommittedBytes',
    'crossingChecked', 'crossingValid'].map(key => [key, worker[key]]));
  return { worker: { ...publicWorker, jobs }, wait: { development: true, frameRate: -1, frames: values.length,
    medianMs: values.length % 2 ? values[middle] : (values[middle - 1] + values[middle]) / 2,
    p99Ms: values[Math.ceil(.99 * values.length) - 1], maxMs: values.at(-1), crossingChecked: check.checked } };
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
  if (args.length !== 4 || !/^(baseline|PR #\d+|re-run: .+)$/.test(args[0]) || !['yes', 'no'].includes(args[1]))
    throw Error('Usage: node tools/unit-ladder.js "baseline|PR #n|re-run: reason" yes|no WORKER_SUMMARY_JSON WAIT_OUTPUT_PREFIX');
  const context = safeText(args[0]);
  const commit = git(root, 'rev-parse', 'HEAD');
  if (git(root, 'status', '--porcelain')) throw Error('Commit the measured sources first; working tree must be clean.');
  if (fs.readFileSync(path.join(root, 'Game/Builds/PerformanceLadder/commit.txt'), 'utf8').trim() !== commit)
    throw Error('Rebuild both Players for HEAD.');
  verifyBuildSources(root);
  const cost = movementCost(JSON.parse(fs.readFileSync(args[2], 'utf8')),
    JSON.parse(fs.readFileSync(args[3] + '.json', 'utf8')), fs.readFileSync(args[3] + '-wait.csv', 'utf8'), commit);
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
    if (meta.commit !== commit) return { valid: false, reasons: ['Player build commit mismatch'] };
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
    comparison: comparison?.name || null, height: ladderHeight(steps), steps, movementCost: cost };
  const directory = path.join(root, 'docs/performance/unit-ladder');
  fs.mkdirSync(directory, { recursive: true });
  const stem = `${data.date}-${commit.slice(0, 7)}`;
  let filename = path.join(directory, stem + '.md'), repeat = 2;
  while (fs.existsSync(filename)) filename = path.join(directory, `${stem}-${repeat++}.md`);
  fs.writeFileSync(filename, renderEntry(data), { flag: 'wx' });
  process.stdout.write(`Entry: ${path.relative(root, filename)}; ladder height ${data.height} (${MOVEMENT_STATUS})\n`);
  if (steps.some(step => step.result === 'invalid' || step.regressed || step.previouslyPassing)) process.exitCode = 1;
}

module.exports = { STEPS, median, nearestRank, summarizeRun, summarizeStep, collectStep, regressed, ladderHeight, newestBaseline, parseCsv, renderEntry, safeText, hasPlayerLogFailure, windowsHardware, hardwareInfo, checkCrossing, movementCost };
if (require.main === module) main(process.argv.slice(2)).catch(error => { process.stderr.write(error.message + '\n'); process.exitCode = 1; });
