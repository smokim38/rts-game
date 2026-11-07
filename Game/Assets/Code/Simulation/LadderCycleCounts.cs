namespace Northwatch.Simulation
{
    [System.Serializable]
    public struct LadderCycleCounts
    {
        public int cycle, side, units, passed, met;
        public float travelShare;
    }
}
