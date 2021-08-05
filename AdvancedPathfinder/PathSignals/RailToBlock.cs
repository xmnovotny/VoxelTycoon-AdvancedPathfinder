using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public record RailToBlock
    {
        public Rail Rail { get; init; }
        public bool IsLinkedRail { get; init; }
        public bool IsBeyondPath { get; init; }

        public RailToBlock(Rail rail, bool isLinkedRail, bool isBeyondPath)
        {
            Rail = rail;
            IsLinkedRail = isLinkedRail;
            IsBeyondPath = isBeyondPath;
        }
    }
}