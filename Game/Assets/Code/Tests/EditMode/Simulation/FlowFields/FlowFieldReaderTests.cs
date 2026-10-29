using Northwatch.Simulation.FlowFields;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Tests.EditMode.Simulation.FlowFields
{
    public sealed class FlowFieldReaderTests
    {
        private NativeArray<byte> directions, classes;
        private NativeArray<ushort> distances;
        private FlowFieldReader reader;

        [SetUp]
        public void SetUp()
        {
            directions = new NativeArray<byte>(4, Allocator.Persistent);
            distances = new NativeArray<ushort>(4, Allocator.Persistent);
            classes = new NativeArray<byte>(1, Allocator.Persistent);
            for (int i = 0; i < 4; i++) distances[i] = 10;
            reader = new FlowFieldReader { Directions = directions.AsReadOnly(), Distances = distances.AsReadOnly(),
                Classes = classes.AsReadOnly(), Width = 2, Height = 2, Origin = new float2(-3, 7) };
        }

        [TearDown]
        public void TearDown() { directions.Dispose(); distances.Dispose(); classes.Dispose(); }

        private FlowSample Sample(float2 local, FlowSampleStatus expected = FlowSampleStatus.Ready)
        {
            Assert.That(reader.Sample(0, reader.Origin + local, out FlowSample sample), Is.EqualTo(expected));
            Assert.That(math.all(math.isfinite(sample.Direction)) && math.isfinite(sample.Distance), Is.True);
            return sample;
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void OwnPointDecodesEveryDirectionAndTenths(int direction)
        {
            directions[0] = (byte)direction; distances[0] = 123;
            FlowSample sample = Sample(new float2(.5f));
            Assert.That(sample.Distance, Is.EqualTo(12.3f).Within(1e-5));
            Assert.That(math.distance(sample.Direction, new float2(math.cos(direction * math.PI / 4),
                math.sin(direction * math.PI / 4))), Is.LessThan(1e-5));
        }

        [Test]
        public void HalfwayBlendSkipsUnreachableAndRenormalizes()
        {
            directions[1] = 2; distances[1] = 30;
            distances[2] = ushort.MaxValue; distances[3] = ushort.MaxValue;
            FlowSample sample = Sample(new float2(1));
            Assert.That(sample.Distance, Is.EqualTo(2).Within(1e-5));
            Assert.That(math.distance(sample.Direction, math.normalize(new float2(1, 1))), Is.LessThan(1e-5));
        }

        [TestCase(false)] [TestCase(true)]
        public void ZeroWeightReachableValuesNeverContribute(bool onLine)
        {
            distances[0] = ushort.MaxValue; distances[1] = ushort.MaxValue;
            FlowSample sample = Sample(new float2(onLine ? 1 : .5f, .5f), FlowSampleStatus.Unreachable);
            Assert.That(sample.Distance, Is.Zero);
            Assert.That(math.lengthsq(sample.Direction), Is.Zero);
        }

        [Test]
        public void ExactGoalHasZeroDirectionDespiteReachableNeighbours()
        {
            distances[0] = 0;
            FlowSample sample = Sample(new float2(.5f));
            Assert.That(sample.Distance, Is.Zero);
            Assert.That(math.lengthsq(sample.Direction), Is.Zero);
        }

        [Test]
        public void AllUnreachableAndFullyOutsideGiveZeroWhileBoundaryWeightsRenormalize()
        {
            for (int i = 0; i < 4; i++) distances[i] = ushort.MaxValue;
            Assert.That(Sample(new float2(1), FlowSampleStatus.Unreachable).Distance, Is.Zero);
            Assert.That(Sample(new float2(-5), FlowSampleStatus.Unreachable).Distance, Is.Zero);
            distances[0] = 65534;
            Assert.That(Sample(float2.zero).Distance, Is.EqualTo(6553.4f).Within(.001f));
        }

        [Test]
        public void CancelledDirectionsUseHeaviestAndFirstOnTie()
        {
            directions[1] = 4; distances[2] = distances[3] = ushort.MaxValue;
            Assert.That(math.distance(Sample(new float2(1, .5f)).Direction, new float2(1, 0)), Is.LessThan(1e-5));
            directions[2] = 4; directions[3] = 4;
            // The east candidate is heavier than any single west candidate, but their sum cancels it.
            Assert.That(math.distance(Sample(new float2(.7928932f)).Direction, new float2(1, 0)), Is.LessThan(1e-5));
        }

        [Test]
        public void ClassOneUsesCellCornerAndFieldIndexOffsetsStorage()
        {
            classes[0] = 1; distances[0] = 25;
            Assert.That(Sample(new float2(1)).Distance, Is.EqualTo(2.5f));
            using var twoClasses = new NativeArray<byte>(2, Allocator.Persistent);
            using var twoDirections = new NativeArray<byte>(new byte[] { 0, 0, 0, 0, 2, 0, 0, 0 }, Allocator.Persistent);
            using var twoDistances = new NativeArray<ushort>(new ushort[] { 0, 0, 0, 0, 50, 0, 0, 0 }, Allocator.Persistent);
            reader.Classes = twoClasses.AsReadOnly(); reader.Directions = twoDirections.AsReadOnly();
            reader.Distances = twoDistances.AsReadOnly();
            Assert.That(reader.Sample(1, reader.Origin + .5f, out var sample), Is.EqualTo(FlowSampleStatus.Ready));
            Assert.That(sample.Distance, Is.EqualTo(5));
            Assert.That(sample.Direction.y, Is.EqualTo(1).Within(1e-5));
        }

        [Test]
        public void NoFieldAndUnallocatedReaderAreUnavailable()
        {
            Assert.That(reader.Sample(FlowFieldReader.NoField, 0, out var sample), Is.EqualTo(FlowSampleStatus.Unavailable));
            Assert.That(math.lengthsq(sample.Direction), Is.Zero);
            Assert.That(default(FlowFieldReader).Sample(0, 0, out _), Is.EqualTo(FlowSampleStatus.Unavailable));
        }

        [Test]
        public void IndependentParallelBurstJobsCanShareReaderWithSafetyChecks()
        {
            using var first = new NativeArray<float>(128, Allocator.Persistent);
            using var second = new NativeArray<float>(128, Allocator.Persistent);
            JobHandle a = new ReadJob { Reader = reader, Output = first }.Schedule(128, 8);
            JobHandle b = new ReadJob { Reader = reader, Output = second }.Schedule(128, 8);
            JobHandle.CombineDependencies(a, b).Complete();
            for (int i = 0; i < 128; i++) { Assert.That(first[i], Is.EqualTo(1)); Assert.That(second[i], Is.EqualTo(1)); }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ReadJob : IJobParallelFor
        {
            internal FlowFieldReader Reader;
            internal NativeArray<float> Output;
            public void Execute(int index)
            {
                Reader.Sample(0, Reader.Origin + 1, out var sample);
                Output[index] = sample.Distance;
            }
        }
    }
}
