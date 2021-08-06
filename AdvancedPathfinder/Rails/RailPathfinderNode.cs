using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderNode: PathfinderNode<Rail, RailConnection, RailSection, RailPathfinderEdge>
    {
        public bool IsElReachable { get; private set; }
        public int NumPassableOutboundEdges { get; private set; }  //number of passable edges, that leads to this node

        protected override void ProcessNewEdge(RailPathfinderEdge edge)
        {
            if (edge.IsPassable())
            {
                edge.NextNode.IsReachable = true;
                NumPassableOutboundEdges++;
                if (edge.Data.IsElectrified)
                    ((RailPathfinderNode) edge.NextNode).IsElReachable = true;
            }
        }
    }
}