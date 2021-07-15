using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderNode: PathfinderNode<Rail, RailConnection, RailSection, RailPathfinderEdge>
    {
        public bool IsElReachable { get; private set; }
        protected override void ProcessNewEdge(RailPathfinderEdge edge)
        {
            if (edge.IsPassable())
            {
                edge.NextNode.IsReachable = true;
                if (edge.Data.IsElectrified)
                    ((RailPathfinderNode) edge.NextNode).IsElReachable = true;
            }
        }
    }
}