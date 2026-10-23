namespace Northwatch.Simulation
{
    public readonly struct UnitHandle
    {
        public readonly int Slot;
        public readonly uint Generation;

        internal UnitHandle(int slot, uint generation)
        {
            Slot = slot;
            Generation = generation;
        }
    }
}
