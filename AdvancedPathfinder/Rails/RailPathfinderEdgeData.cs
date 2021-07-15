namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderEdgeData: RailSectionDataBase
    {
        public float PlatformLength { get; private set; }

        public float PlatformLengthAdd { set => PlatformLength += value; }

        public override void Reset()
        {
            base.Reset();
            PlatformLength = 0;
        }

        public override void Combine(RailSectionDataBase data, bool isReversed)
        {
            base.Combine(data, isReversed);
            if (data.HasPlatform)
            {
                PlatformLength += data.Length;
            }
        }
        
    }
}