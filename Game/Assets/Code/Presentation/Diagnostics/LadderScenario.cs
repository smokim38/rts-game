using Northwatch.Simulation;
using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class LadderScenario : ScriptableObject
    {
        public int Version = 1;
        public int Seed = 103;
        public float PlaneSize = UnitSimulation.MapHalfWidth * 2;
        public Vector3 FirstSpawn = new Vector3(-80, 0, 0);
        public Vector3 SecondSpawn = new Vector3(80, 0, 0);
        public UnitSize FirstSize = UnitSize.Small;
        public UnitSize SecondSize = UnitSize.Large;
        public float OrderCycleSeconds = 20;
        public Vector3 CameraAngles = new Vector3(35.26439f, 45, 0);
        public float CameraDistance = 400;
        public float CameraSize = 200;
        public Vector3 LightAngles = new Vector3(50, -30, 0);
        public Color Ambient = new Color(0.5f, 0.5f, 0.5f);
        public float LightIntensity = 1;
        public double WarmupSeconds = 10;
        public double WindowSeconds = 60;
        public int FrameCapacity = 1000000;
        public double DrainSeconds = 2;
    }
}
