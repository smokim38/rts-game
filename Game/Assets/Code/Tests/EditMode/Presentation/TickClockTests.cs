using Northwatch.Presentation;
using NUnit.Framework;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class TickClockTests
    {
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void OneSecondProducesThirtyTicksAtAnyFrameRate(int frames)
        {
            var clock = new TickClock();
            int ticks = 0;
            for (int i = 0; i < frames; i++) ticks += clock.Advance(1.0 / frames);
            Assert.That(ticks, Is.EqualTo(30));
        }

        [Test]
        public void SlowFramesCatchUpWithoutDroppingTicks()
        {
            var clock = new TickClock();
            Assert.That(clock.Advance(0.5), Is.EqualTo(15));
            Assert.That(clock.Advance(0), Is.Zero);
            Assert.That(clock.Advance(1.0 / 60), Is.Zero);
            Assert.That(clock.Advance(1.0 / 60), Is.EqualTo(1));
        }
    }
}
