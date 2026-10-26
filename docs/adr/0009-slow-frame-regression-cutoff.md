# ADR 0009: Slow-frame regression cut-off

## Status

Proposed

## Date

2026-10-26

## Context

[ADR 0004](0004-unit-simulation-architecture.md#performance-ladder) marks a ladder step **regressed** when its average or its 1% low frame time is worse than the same step in the comparison entry by more than 10% **and** by more than 0.5 ms. `regressed()` in `tools/unit-ladder.js` applies that test, `current > before * 1.1 && current > before + 0.5`, to both medians.

On the ladder PC (not reference-class) the game now runs at about 1,250 to 1,400 fps, with average frame times of 0.70 to 0.80 ms at every step. At that speed the 1% low has started failing ladders at random. The table below gives the 1% low medians in ms, scenario version 1, from Issue #116 and PR #114. Commit f404a10 has the same game code as 444083a. Blank cells weren't reported.

| Ladder | Commit | 10 units | 300 units | 400 units | 2,000 units | Flagged by ADR 0004's rule |
|---|---|---|---|---|---|---|
| Baseline, the comparison for the rest ([entry](../performance/unit-ladder/2026-10-26-f056ade.md)) | f056ade | 1.785 | 1.453 | 1.444 | 1.486 | nothing compared (new series) |
| PR #114, author | 444083a | 1.008 | 1.024 | 1.028 | 1.106 | none |
| PR #114, QA run 1 | f404a10 | | 2.000 | 2.041 | | 300 and 400 |
| PR #114, QA run 2, not posted | f404a10 | 1.885 | 1.624 | 1.854 | | none |
| PR #114, QA run 3, stopped early | f404a10 | 2.460 | | | | 10 |

Average frame times never regressed in any of these ladders.

- **This is noise, and it is bigger than the tolerance.** The same game code gave a 10-unit 1% low of anything from 1.008 to 2.460 ms. Against the baseline's 1.453 ms at 300 units, ADR 0004's rule flags anything above 1.953 ms. So two of the three QA ladders failed an unchanged build.
- **The median of three doesn't remove it.** Within the baseline ladder, the three runs of each step agree on the 1% low within 0.17 ms (its machine-readable summary). The noise seems to come with each ladder session, not each run. So neither the median of three nor ADR 0004's three-run spread check, which looks at average frame times, catches it.
- **The cause isn't measured.** Frame time is `cpuFrameTime`, "the time between ends of two frames" ([FrameTiming][frame-timing]), so it includes any time the game's threads are kept waiting. Windows switches a thread out when, for example, "A thread with a higher priority has become ready to run" ([context switches][context-switches]), and a short pause of that kind is large next to a 0.7 ms frame. This ADR depends only on how big the noise is, which the table shows, so no spike is proposed.

**Why the slow-frame check must stay.** The simulation ticks at 30 Hz ([ADR 0008](0008-movement-and-pathfinding.md#tick-rate-30-hz)). At 1,300 fps only about 2% of frames run a tick. So the cost of a tick hardly moves the average, but once ticks cost real time, the slowest 1% of frames are tick frames. Suppose each tick frame took 3 ms longer, about ADR 0008's movement budget at 2,000 units. The average would rise from 0.73 to about 0.80 ms, inside the 0.5 ms tolerance, while the 1% low would rise to about 3.7 ms. These figures are arithmetic, not measurements.

**The owner's instruction** (Issue #116, 2026-10-26) was "go ahead with the rule change". It answered the Producer's proposal to keep the average check as it is and to judge the slowest frames only "once frames are slow enough for it to mean something, for example once a step runs below about 500 fps". The owner named no cut-off.

## Decision

**A step's 1% low is checked for regression only when its current 1% low median is 3.0 ms or more,** that is, when its slowest 1% of frames run at about 333 fps or slower. Below 3.0 ms the 1% low is still recorded in every entry, at every step, but it never sets `regressed`. Everything else in ADR 0004's regression check stays as it is, including the average frame time rule.

### The rule

For each step, `before` is the same step in the comparison entry (ADR 0004: the newest entry on `main` with the same hardware summary and scenario version). `avg` and `low` are the step's average and 1% low frame time medians in ms. Use the values in the entries' machine-readable summaries, not the table's three-decimal rounding.

```text
averageRegressed = avg > before.avg * 1.1 && avg > before.avg + 0.5                 (unchanged)
lowRegressed     = low >= 3.0 && low > before.low * 1.1 && low > before.low + 0.5
regressed        = averageRegressed || lowRegressed
```

- **Exactly at the cut-off,** a 1% low of 3.0 ms is checked. Anything below 3.0 ms is not, whatever the comparison.
- Only the current median is tested against the cut-off. If the comparison is at or above 3.0 ms and the current median is below it, the step has improved, so it can't regress anyway.
- When the check applies, the thresholds are ADR 0004's, unchanged.

### Why 3.0 ms

- **It clears the noise we measured.** The highest 1% low in any of these ladders is 2.460 ms. 3.0 ms is 0.54 ms above it, about the size of the rule's own 0.5 ms tolerance.
- **The owner's example, 2 ms, doesn't.** QA run 1's 300-unit step recorded exactly 2.0000 ms, so it would be checked. It, the 400-unit step (2.041 ms) and run 3's 10-unit step (2.460 ms) would all still be flagged.
- **It still leaves room.** 3 ms is under a fifth of the 16.67 ms pass mark, so the check comes on long before a step gets near failing. Movement's budget of up to 3 ms per tick at 2,000 units (ADR 0008) would by itself put that step's 1% low near or above the cut-off.

### How it behaves in the several-millisecond range

With a comparison 1% low of b ms, a step is flagged when its current 1% low is:

| b | Flagged when the current 1% low is |
|---|---|
| under 2.5 ms | 3.0 ms or more |
| 2.5 to 5 ms | more than b + 0.5 ms |
| 5 ms or more | more than b × 1.1 |

So the tick example above (1.5 to 3.7 ms) is flagged, and so are rises from 4.0 to 4.6 ms and from 10 to 11.2 ms. Above 3.0 ms the check is exactly ADR 0004's. We haven't measured the noise there. We expect it to matter less once real work sets the slowest frames, because a short pause then moves the 1% low only when it lands on one of those frames. That is an expectation, and the trigger in Consequences covers it.

### Against the evidence

- **Baseline f056ade** is the first entry in its series, so nothing is compared.
- **PR #114, author:** 1.008 to 1.106 ms, all under 3.0 ms. Not flagged.
- **QA run 1:** 300 units (2.000 ms) and 400 units (2.041 ms) are under 3.0 ms. Not flagged.
- **QA run 2:** 1.885, 1.624 and 1.854 ms. Not flagged.
- **QA run 3:** 10 units (2.460 ms). Not flagged.
- **Steps not shown:** ADR 0004's rule didn't flag them, and this rule only adds a condition to it, so it can't flag them either.
- **Averages:** the rule is unchanged, and at 0.70 to 0.80 ms they never regressed.

This ADR flags none of these ladders.

### What stays the same

- ADR 0004's pass rule (1% low at most 16.67 ms) at every step, the "previously passing" check, and what the author and the reviewer do with a regression.
- The three-run spread check on average frame times.
- **Every ladder runs all nine steps,** in order, even after a step fails or is flagged regressed ([ADR 0004, "Steps"](0004-unit-simulation-architecture.md#performance-ladder)). This holds for the author's ladder and for QA's. QA run 3, which stopped after its first step, should have run on.
- Entries already in `docs/performance/unit-ladder/` keep the flags they recorded, because entries are never edited (ADR 0004).

### What this ADR supersedes

Only the 1% low part of ADR 0004's "Regression check". ADR 0004 stays Accepted for everything else. ADR 0004 itself is not edited (AGENTS.md §9), so this section is the record of the partial supersession. ADR 0008 doesn't restate that part: it cites ADR 0004's pass rule and its fix-or-explain rule, neither of which changes.

## Alternatives considered

None of these options changes the game, so none costs anything at thousands of units. They differ in what they catch, in ladder time and in harness work. All but alternative 3 are a one-line change to `regressed()`, and any of them can be reversed by a superseding ADR.

1. **Gate on the average: check the 1% low only when the step's average frame time is 2 ms or more.** This reads the owner's "below about 500 fps" literally.
   - Evidence: it flags none of the ladders, and averages are steady (the baseline's three-run spread is at most 1.1%).
   - Not chosen: tick frames barely move the average (Context). Tick frames of 3.7 ms leave it at about 0.8 ms. Even tick frames of 20 ms, past the pass mark, leave it at about 1.7 ms. The check would be off exactly when slow frames regress, until the pass rule fails.
2. **A 2.0 ms cut-off on the 1% low.**
   - Gated on the current median, it keeps all three false flags (Why 3.0 ms).
   - Gated on the comparison's median instead, it clears them, because every baseline 1% low is under 1.8 ms. But it then never checks a step whose comparison is fast, so the first PR that adds real tick cost, for example from 1.5 to 6 ms, isn't flagged.
   - Not chosen.
3. **Confirm on a second ladder.** A 1% low regression counts only when a second ladder of the same commit flags the same step.
   - Evidence: QA run 2 didn't repeat run 1's flags, so one extra ladder would have cleared them. But run 3 then flagged a different step, and two of the three QA ladders flagged something. So most hot-path PRs would pay for an extra ladder.
   - Cost: about 35 minutes of the one ladder PC per extra ladder (ADR 0004), and no other Unity run may use the PC meanwhile (`docs/studio/workflow.md`). The harness and QA would also need a way to pair two append-only entries.
   - Not chosen: it pays every time the noise strikes, and two noisy ladders can still agree by chance, because at 1,300 fps the 1% low measures the machine, not the game. It is the first option to revisit if noise turns up above the cut-off.
4. **A wider 1% low tolerance at every speed,** for example more than 2 ms worse instead of 0.5 ms. It needs about 2 ms to clear the noise, because the same code ranged over 1.45 ms and a quiet ladder can become the comparison. It clears the evidence without a cut-off, and it would be the better choice if noise of this size persists above 3 ms.
   - Not chosen: it blunts the check at every speed. From a 4 ms comparison, only a rise past 6 ms (+50%) would be flagged, against 4.5 ms with this ADR.

## Consequences

- **Benefits.** At today's speeds, ladders no longer fail at random on the 1% low, so QA results on unchanged code stop flipping. The check switches itself on as the game gets heavier, with no further ADR. The rule adds one comparison.
- **Tradeoffs.** A slow-frame rise that stays under 3.0 ms isn't flagged, even when it is bigger than the noise we measured. For example, a rise from 1.0 to 2.9 ms is 1.9 ms, more than the 1.45 ms the same code ranged over (1.008 to 2.460 ms), and it isn't flagged. This is a blind spot of the rule. The average check and the pass rule still apply.
- **Risks.**
  - The noise was measured on one machine, which isn't reference-class, with scenario version 1 and frames of about 0.7 ms. Another machine may be noisier.
  - Noise above 3.0 ms is unmeasured (see the several-millisecond range above).
- **Reversibility.** Easy: one constant and one condition in `regressed()`. Entries record medians as well as flags, so any later rule can be checked against old entries. Changing the cut-off needs a superseding ADR.
- **Trigger for a superseding ADR.** Two ladders of the same game code on the same machine disagree on whether a step's 1% low regressed, at a 1% low of 3.0 ms or more. Alternatives 3 and 4 are then revisited for that range.
- **Dependencies and owner decisions.** This ADR adds no package and changes neither Unity nor the handbook. The owner's instruction named no cut-off. The owner then confirmed the 3.0 ms cut-off on the 1% low in the Producer session on 2026-10-26.
- **Follow-up Issue:**
  1. **#117** (gameplay-programmer): apply the rule in `regressed` in `tools/unit-ladder.js`, with tests in `tools/unit-ladder.test.js`.
     - The cut-off is one named constant.
     - The average comparison and the three-run spread check both call `regressed()` today, and both keep their behaviour.
     - The tests should cover a 1% low just under 3.0 ms and one of exactly 3.0 ms, plus the three flags in Context, which must no longer be set.
     - Existing entries aren't regenerated or edited.

[frame-timing]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTiming.html
[context-switches]: https://learn.microsoft.com/en-us/windows/win32/procthread/context-switches
