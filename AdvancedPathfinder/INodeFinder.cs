using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    internal interface INodeFinder<in TTrackConnection>
        where TTrackConnection : TrackConnection
    {
        public PathfinderNodeBase FindNodeByInboundConn(TTrackConnection connection);

    }
}