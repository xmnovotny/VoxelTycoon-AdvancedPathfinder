using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderNode: PathfinderNode<Rail, RailConnection, RailSection, RailPathfinderEdge>
    {
        public bool IsElReachable { get; private set; }
        public int NumPassableOutboundEdges { get; private set; }  //number of passable edges, that leads to this node
        private Dictionary<PathfinderNodeBase, float> _elReachableNodes;
        private readonly Dictionary<(int destinationHash, bool electric), bool> _pathDiversionCache = new();

        public override Dictionary<PathfinderNodeBase, float> GetReachableNodes(object edgeSettings)
        {
            if (edgeSettings is not RailEdgeSettings {Electric: true})
            {
                return base.GetReachableNodes(edgeSettings);
            }

            return _elReachableNodes;
        }

        public bool HasPathDiversion(Train train, IVehicleDestination destination)
        {
            int hash = destination.GetDestinationHash();
            if (_pathDiversionCache.TryGetValue((hash, train.Electric), out bool isDiversion))
                return isDiversion;
            
            int possibilities = 0;
            HashSet<RailPathfinderNode> convertedDest = Manager<RailPathfinderManager>.Current.GetConvertedDestination(destination);
            for (int i = Edges.Count - 1; i >= 0; i--)
            {
                RailPathfinderEdge edge = Edges[i];
                if (!edge.IsPassable(train.Electric))
                    continue;
                ;

                RailPathfinderNode nextNode = (RailPathfinderNode) edge.NextNode;
                if (nextNode == null)
                    continue;

                Dictionary<PathfinderNodeBase, float> reachNodes = nextNode.GetReachableNodes(new RailEdgeSettings() {Electric = train.Electric});
                if (reachNodes == null) //incomplete reachable nodes, cannot determine if path can be diversified, so return true for path update 
                    return true;

                foreach (RailPathfinderNode targetNode in convertedDest)
                {
                    if (reachNodes.ContainsKey(targetNode))
                    {
                        if (++possibilities > 1)
                        {
                            _pathDiversionCache[(hash, train.Electric)] = true;
                            return true;
                        }

                        break;
                    }
                }
            }

            _pathDiversionCache[(hash, train.Electric)] = false;
            return false;
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