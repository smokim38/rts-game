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
        }

        [Test]
        public void DuplicateNonPositiveAndOverflowAreRejected()
        {
            var buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200);
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 160, cpuFrameTime = 1 });
            Assert.That(buffer.SampleCount(0), Is.EqualTo(2));
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
