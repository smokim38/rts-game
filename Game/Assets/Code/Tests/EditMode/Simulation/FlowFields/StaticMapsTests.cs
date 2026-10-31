using Northwatch.Simulation.FlowFields;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Tests.EditMode.Simulation.FlowFields
{
    public sealed class StaticMapsTests
    {
        private static StaticBox Box(int x, int y, int width, int height) => new StaticBox
            { Min = new int2(x, y), MaxExclusive = new int2(x + width, y + height) };

        [Test]
        public void WalkableRasterizesWholeBoxesAndClipsAtMapEdges()
        {
            using var maps = new StaticMaps(8, 6);
            using var boxes = new NativeArray<StaticBox>(new[] { Box(-1, 1, 3, 2), Box(7, 5, 3, 3) }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 8; x++)
                    Assert.That(maps.Walkable[y * 8 + x], Is.EqualTo(
                        (x < 2 && y >= 1 && y < 3) || (x == 7 && y == 5) ? 0 : 1));
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void GapFitsOnlyClassesWithEnoughClearance(int gap, bool largePasses)
        {
            using var maps = new StaticMaps(9, 9);
            using var boxes = new NativeArray<StaticBox>(new[] { Box(4, 0, 1, 3), Box(4, 3 + gap, 1, 6 - gap) }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            Assert.That(maps.Passable[3 * 9 + 4], Is.EqualTo(1));
            Assert.That(maps.Passable[81 + 3 * 9 + 4], Is.EqualTo(largePasses ? 1 : 0));
            Assert.That(maps.Regions[1 * 9 + 1], Is.EqualTo(maps.Regions[1 * 9 + 6]));
            Assert.That(maps.Regions[81 + 1 * 9 + 1] == maps.Regions[81 + 1 * 9 + 6], Is.EqualTo(largePasses));
        }

        [Test]
        public void ClassOneAnchorsAtTheLowestCellAndCannotExtendPastMapEdges()
        {
            using var maps = new StaticMaps(4, 4);
            using var boxes = new NativeArray<StaticBox>(new[] { Box(2, 2, 1, 1) }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    Assert.That(maps.Passable[16 + y * 4 + x], Is.EqualTo(
                        x == 3 || y == 3 || (x >= 1 && y >= 1) ? 0 : 1));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void DiagonalTouchDoesNotConnectRegions(int sizeClass)
        {
            using var maps = new StaticMaps(8, 8);
            using var boxes = new NativeArray<StaticBox>(new[] { Box(0, 4, 4, 4), Box(4, 0, 4, 4) }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            int offset = sizeClass * 64;
            ushort first = maps.Regions[offset + 2 * 8 + 2], second = maps.Regions[offset + 4 * 8 + 4];
            Assert.That(first, Is.Not.Zero);
            Assert.That(second, Is.Not.Zero.And.Not.EqualTo(first));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ClosedRingSeparatesInteriorFromOutside(int sizeClass)
        {
            using var maps = new StaticMaps(12, 12);
            using var boxes = new NativeArray<StaticBox>(new[]
                { Box(3, 3, 6, 1), Box(3, 8, 6, 1), Box(3, 4, 1, 4), Box(8, 4, 1, 4) }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            int offset = sizeClass * 144;
            ushort inside = maps.Regions[offset + 5 * 12 + 5], outside = maps.Regions[offset];
            Assert.That(inside, Is.Not.Zero);
            Assert.That(outside, Is.Not.Zero.And.Not.EqualTo(inside));
            Assert.That(maps.Regions[offset + 3 * 12 + 3], Is.Zero);
        }

        [Test]
        public void RebuildClearsOldObstaclesAndRegionLabels()
        {
            using var maps = new StaticMaps(8, 8);
            using var boxes = new NativeArray<StaticBox>(new[] { Box(3, 0, 1, 8) }, Allocator.TempJob);
            using var empty = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            maps.ScheduleBuild(empty).Complete();
            for (int i = 0; i < 64; i++)
            {
                Assert.That(maps.Walkable[i], Is.EqualTo(1));
                Assert.That(maps.Regions[i], Is.EqualTo(1));
            }
            Assert.That(maps.MapBytes, Is.EqualTo(64 * 7));
            Assert.That(maps.ScratchBytes, Is.EqualTo(64 * 4));
        }
    }
}
