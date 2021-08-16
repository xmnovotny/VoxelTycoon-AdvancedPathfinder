using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    internal interface INodeFinder
    {
        public PathfinderNodeBase FindNodeByInboundConn(RailConnection connection);

    }
}