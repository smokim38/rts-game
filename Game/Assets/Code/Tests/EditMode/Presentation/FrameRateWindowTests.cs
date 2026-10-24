using System;
using Northwatch.Presentation;
using NUnit.Framework;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class FrameRateWindowTests
    {
        [Test]
        public void AveragesFrameDurationsAndForgetsFramesOlderThanHalfASecond()
        {
            var window = new FrameRateWindow();
            for (int i = 0; i < 30; i++) window.Advance(1f / 60);
            Assert.That(window.FramesPerSecond, Is.EqualTo(60).Within(0.001));
            for (int i = 0; i < 15; i++) window.Advance(1f / 30);
            Assert.That(window.FramesPerSecond, Is.EqualTo(30).Within(0.001));
            Assert.That(window.Milliseconds, Is.EqualTo(1000f / 30).Within(0.001));
            window.Advance(1f);
            Assert.That(window.FramesPerSecond, Is.EqualTo(1));
            Assert.That(window.Milliseconds, Is.EqualTo(1000));
        }

        [Test]
        public void HighFrameRatesAndInvalidSamplesStayBoundedAndAllocateNothing()
        {
            var window = new FrameRateWindow();
            for (int i = 0; i < 20000; i++) window.Advance(0.00001f);
            window.Advance(float.NaN); window.Advance(float.PositiveInfinity); window.Advance(0);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++) window.Advance(1f / 120);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero);
            Assert.That(window.FramesPerSecond, Is.EqualTo(120).Within(0.001));
        }
    }
}
