using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.RailPathfinder
{
    internal interface IRailNodeFinder
    {
        public PathfinderNodeBase FindNodeByInboundConn(RailConnection connection);

    }
}