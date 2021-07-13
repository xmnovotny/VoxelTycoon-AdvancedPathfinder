using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailSection: TrackSection<Rail, RailConnection>
    {
        public bool IsElectrified { get; private set; }
        protected override void FillSetup()
        {
            base.FillSetup();
            IsElectrified = true;
        }

        protected override void ProcessTrack(Rail track, RailConnection startConnection)
        {
            base.ProcessTrack(track, startConnection);
            IsElectrified &= track.Electrified;
        }
    }
}