using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.RailPathfinder
{
    public interface IRailSectionFinder
    {
        public RailSection FindSection(RailConnection connection);
    }
}