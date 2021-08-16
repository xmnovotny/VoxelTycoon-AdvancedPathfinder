using AdvancedPathfinder.Rails;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    public interface ISectionFinder
    {
        public RailSection FindSection(RailConnection connection);
    }
}