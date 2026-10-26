using Northwatch.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class LadderFrameBufferTests
    {
        [Test]
        public void DelayedOutOfOrderSamplesMatchTheRenderedFrame()
        {
            var buffer = new LadderFrameBuffer(5, 100);
            buffer.Rendered(200); buffer.Rendered(300); buffer.Rendered(400);
            buffer.Add(new FrameTiming { frameStartTimestamp = 350, cpuFrameTime = 3 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            Assert.That(buffer.SampleCount(0), Is.EqualTo(1));
            Assert.That(buffer.SampleCount(1), Is.Zero, "A missing frame must remain missing.");
            Assert.That(buffer.Sample(2).cpuFrameTime, Is.EqualTo(3));
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Unmatched, Is.Zero);
        }

        [Test]
        public void DifferentClockOriginsProduceUnmatchedSamples()
        {
            var buffer = new LadderFrameBuffer(2, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            buffer.Add(new FrameTiming { frameStartTimestamp = 10150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 10250, cpuFrameTime = 1 });
            Assert.That(buffer.Matched, Is.Zero);
            Assert.That(buffer.Unmatched, Is.EqualTo(2));
            Assert.That(buffer.SampleCount(0), Is.Zero);
            Assert.That(buffer.SampleCount(1), Is.Zero);
        }

        [Test]
        public void DrainMatchesDelayedWindowSamplesAndIgnoresFramesOutsideWindow()
        {
            var buffer = new LadderFrameBuffer(2, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            buffer.Add(new FrameTiming { frameStartTimestamp = 350, cpuFrameTime = 1 });
            buffer.End();
            buffer.Add(new FrameTiming { frameStartTimestamp = 100, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 300, cpuFrameTime = 2 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 450, cpuFrameTime = 1 });
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Unmatched, Is.EqualTo(1), "Closing the window must not hide earlier unmatched samples.");
            Assert.That(buffer.SampleCount(0), Is.EqualTo(1));
            Assert.That(buffer.SampleCount(1), Is.EqualTo(1));
            Assert.That(buffer.Invalid, Is.False);
        }

        [Test]
        public void DuplicateNonPositiveAndOverflowAreRejected()
        {
            var buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200);
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 160, cpuFrameTime = 1 });
            Assert.That(buffer.SampleCount(0), Is.EqualTo(2));
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Invalid, Is.True);
            buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200);
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 0 });
            Assert.That(buffer.Invalid, Is.True);
            buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            Assert.That(buffer.Invalid, Is.True);
        }
    }
}
