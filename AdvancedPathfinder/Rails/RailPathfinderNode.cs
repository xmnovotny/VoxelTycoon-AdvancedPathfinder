using System.Collections.Generic;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderNode: PathfinderNode<Rail, RailConnection, RailSection, RailPathfinderEdge>
    {
        public bool IsElReachable { get; private set; }
        public int NumPassableOutboundEdges { get; private set; }  //number of passable edges, that leads to this node
        private Dictionary<PathfinderNodeBase, float> _elReachableNodes;

        public override Dictionary<PathfinderNodeBase, float> GetReachableNodes(object edgeSettings)
        {
            if (edgeSettings is not RailEdgeSettings {Electric: true})
            {
                return base.GetReachableNodes(edgeSettings);
            }

            return _elReachableNodes;
        }

        internal override void SetReachableNodes(Dictionary<PathfinderNodeBase, float> reachableNodes, object edgeSettings)
        {
            if (edgeSettings is not RailEdgeSettings {Electric: true})
            {
                base.SetReachableNodes(reachableNodes, edgeSettings);
            }

            _elReachableNodes = reachableNodes;
        }

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