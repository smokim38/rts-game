'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const L = require('./unit-ladder.js');

const meta = { featureEnabled: true, focusLost: false, paused: false, bufferInvalid: false, development: false, activeUnitsValid: true,
  width: 1920, height: 1080, borderless: true, vsync: 0, targetFrameRate: -1, units: 10,
  warmupSeconds: 10, seconds: 60, renderedFrames: 3, recordedFrames: 3 };
const rows = [1, 2, 3].map((frameMs, i) => ({ callback: String(200 + i * 100), timestamp: String(150 + i * 100), samples: 1, frameMs, mainMs: 0.5, gpuMs: 0.7 }));
const run = (frameMs = 10, lowMs = 12) => ({ valid: true, frameMs, lowMs, maxMs: 15, mainMs: 5, gpuMs: 3, fps: 1000 / frameMs });

test('median requires exactly three and is independent of order', () => {
  assert.equal(L.median([100, 1, 3]), 3);
  assert.deepEqual([100, 1, 3], [100, 1, 3]);
  assert.throws(() => L.median([1, 2]));
  assert.throws(() => L.median([1, NaN, 3]));
});
test('nearest rank chooses ceil(0.99*n), including non-integral ranks', () => {
  assert.equal(L.nearestRank(Array.from({ length: 100 }, (_, i) => 100 - i)), 99);
  assert.equal(L.nearestRank(Array.from({ length: 101 }, (_, i) => i + 1)), 100);
  assert.equal(L.nearestRank([1, 3, 2]), 3);
  assert.equal(L.nearestRank([4]), 4);
  assert.throws(() => L.nearestRank([]));
});
test('all summary metrics and missing optional samples', () => {
  const result = L.summarizeRun(meta, rows, 10);
  assert.equal(result.valid, true);
  assert.equal(result.frameMs, 2);
  assert.equal(result.lowMs, 3);
  assert.equal(result.maxMs, 3);
  assert.equal(result.fps, 500);
  const optional = L.summarizeRun(meta, rows.map(row => ({ ...row, mainMs: 0, gpuMs: NaN })), 10);
  assert.equal(optional.valid, true);
  assert.equal(optional.mainMs, null);
  assert.equal(optional.gpuMs, null);
  assert.equal(optional.missingMain, 3);
  assert.equal(optional.missingGpu, 3);
});
test('invalid run detection covers unavailable timings, focus, pause, window, display and count', () => {
  for (const change of [{ featureEnabled: false }, { focusLost: true }, { paused: true }, { bufferInvalid: true }, { activeUnitsValid: false },
    { development: true }, { renderedFrames: 4 }, { recordedFrames: 2 }, { warmupSeconds: 9.99 },
    { seconds: 59.99 }, { width: 1280 }, { borderless: false }, { vsync: 1 }, { units: 20 }])
    assert.equal(L.summarizeRun({ ...meta, ...change }, rows, 10).valid, false, JSON.stringify(change));
  for (const change of [{ samples: 0 }, { samples: 2 }, { frameMs: 0 }, { frameMs: -1 }, { frameMs: NaN },
    { frameMs: Infinity }, { timestamp: '0' }, { timestamp: '250' }, { timestamp: 'invalid' }])
    assert.equal(L.summarizeRun(meta, [{ ...rows[0], ...change }, ...rows.slice(1)], 10).valid, false, JSON.stringify(change));
  assert.equal(L.summarizeRun(meta, [rows[0], { ...rows[1], timestamp: rows[0].timestamp }, rows[2]], 10).valid, false);
});
test('pass rule uses both medians and includes exactly 16.67 ms', () => {
  assert.equal(L.summarizeStep(10, [run(16.67, 16.67), run(1, 1), run(30, 30)]).result, 'pass');
  assert.equal(L.summarizeStep(10, [run(16.671), run(16.671), run()]).result, 'fail');
  assert.equal(L.summarizeStep(10, [run(1, 16.671), run(1, 16.671), run()]).result, 'fail');
  assert.equal(L.summarizeStep(10, [run(), run()]).result, 'invalid');
});
test('regression needs strictly more than both 10% and 0.5 ms', () => {
  assert.equal(L.regressed(11, 10), false);
  assert.equal(L.regressed(11.001, 10), true);
  assert.equal(L.regressed(11.00000000001, 10), true);
  assert.equal(L.regressed(2.5, 2), false);
  assert.equal(L.regressed(2.501, 2), true);
  assert.equal(L.regressed(20.6, 20), false);
  const step = L.summarizeStep(10, [run(10, 19), run(10, 19), run(10, 19)], { result: 'pass', frameMs: 10, lowMs: 16 });
  assert.equal(step.regressed, true);
  assert.equal(step.previouslyPassing, true);
  const spread = L.summarizeStep(10, [run(2), run(2.6), run(2.1)]);
  assert.equal(spread.spreadMs, 0.6000000000000001);
  assert.equal(spread.spreadExceedsTolerance, true);
});
test('invalid runs are replaced, stop after three valid runs, cap attempts at six', async () => {
  let calls = 0;
  const result = await L.collectStep(10, async () => ++calls % 2 ? { valid: false, reasons: ['focus lost'] } : run());
  assert.equal(calls, 6); assert.equal(result.result, 'pass');
  assert.equal(result.attempts.filter(item => !item.valid).length, 3);
  calls = 0;
  await L.collectStep(10, async () => { calls++; return run(); });
  assert.equal(calls, 3);
  calls = 0;
  assert.equal((await L.collectStep(10, async () => { calls++; return { valid: false, reasons: ['missing'] }; })).result, 'invalid');
  assert.equal(calls, 6);
});
test('all nine steps continue after a failure and ladder height is contiguous', async () => {
  const counts = [];
  const results = [];
  for (const units of L.STEPS) results.push(await L.collectStep(units, async count => { counts.push(count); return count === 300 ? run(20, 21) : run(); }));
  assert.deepEqual([...new Set(counts)], [10, 50, 100, 200, 300, 400, 500, 1000, 2000]);
  assert.equal(L.ladderHeight(results), 200);
  assert.equal(L.ladderHeight(L.STEPS.map(units => ({ units, result: 'pass' }))), 2000);
  results[0].result = 'invalid'; assert.equal(L.ladderHeight(results), 0);
});
test('newest comparison matches hardware and scenario using main addition history', () => {
  const hardware = { cpu: 'model' };
  const entry = (name, version, order, machine = hardware) => ({ name, order, data: { hardware: machine, scenarioVersion: version } });
  const newest = entry('2026-10-23-aaaaaaa.md', 1, 2);
  assert.equal(L.newestBaseline([entry('2026-10-24-aaaaaaa.md', 2, 0), entry('2026-10-25-aaaaaaa.md', 1, 1, {}),
    entry('2026-10-23-zzzzzzz.md', 1, 3), newest], hardware, 1), newest);
  assert.equal(L.newestBaseline([], hardware, 1), undefined);
});
test('CSV preserves 64-bit timestamps and unsafe public fields are rejected', () => {
  assert.equal(L.parseCsv('callback,timestamp,samples,frameMs,mainMs,gpuMs\n9007199254741001,9007199254741000,1,1,2,3\n')[0].timestamp, '9007199254741000');
  for (const value of ['C:\\folder\\private', 'host\nname', '10.0.0.1']) assert.throws(() => L.safeText(value));
});
