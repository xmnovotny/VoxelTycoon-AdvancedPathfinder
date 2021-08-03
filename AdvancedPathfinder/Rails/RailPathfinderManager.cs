using System.Collections.Generic;
using AdvancedPathfinder.UI;
using JetBrains.Annotations;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderManager: PathfinderManager<RailPathfinderManager, Rail, RailConnection, RailSection, RailPathfinderNode, RailPathfinderEdge>
    {
        //TODO: Optimize checking direction of path from starting connection to the first node
        //TODO: refresh highlighted path after detaching a train
        //TODO: Implement  penalty based on reserved signal path for individual sections/edges 
        private readonly List<RailPathfinderNode> _electricalNodes = new(); //nodes reachable by electric trains

        public bool FindImmediately([NotNull] Train train, [NotNull] RailConnection origin, [NotNull] IVehicleDestination target,
            List<TrackConnection> result)
        {
            RailEdgeSettings settings = new RailEdgeSettings(train);
            return FindPath(origin, target, settings, result);
        }

        protected override IReadOnlyCollection<RailPathfinderNode> GetNodesList(object edgeSettings)
        {
            return edgeSettings is RailEdgeSettings {Electric: true} ? _electricalNodes : base.GetNodesList(edgeSettings);
        }

        protected override void ProcessNodeToSubLists(RailPathfinderNode node)
        {
            base.ProcessNodeToSubLists(node);
            if (node.IsElReachable)
                _electricalNodes.Add(node);
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            LazyManager<TrackHelper>.Current.RegisterRailsChanged(OnRailsChanged);
            LazyManager<TrackHelper>.Current.RegisterSignalBuildChanged(OnSignalBuildChanged);
        }

        protected override IReadOnlyCollection<RailConnection> GetStationStopsConnections()
        {
            return StationHelper<RailConnection, RailStation>.Current.GetStationStopsConnections();
        }

        protected override void HighlightNonProcessedTracks(HashSet<Rail> unprocessedTracks)
        {
            RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current; 
            foreach (Rail rail in unprocessedTracks)
            {
                if (!rail.IsBuilt)
                    return;
                Highlighter hl = hlMan.ForOneTrack(rail, Color.red);
                hl.transform.SetParent(rail.transform);
            }
        }

        protected override Highlighter HighlightConnection(RailConnection connection, bool halfConnection,
            Color color)
        {
            RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current;
            Highlighter hl = halfConnection ? hlMan.ForOneConnection(connection, color) : hlMan.ForOneTrack(connection.Track, color);
            Rail rail = connection.Track;
            hl.transform.SetParent(rail.transform);
            return hl;
        }

        protected override bool TestPathToFirstNode(List<TrackConnection> connections)
        {
            foreach (TrackConnection connection in connections)
            {
                if (connection is RailConnection railConn)
                {
                    RailSignal signal = railConn.InnerConnection.Signal; //opposite signal
                    RailSignal signal2 = railConn.Signal;
                    if (!ReferenceEquals(signal, null) && signal.IsBuilt && (ReferenceEquals(signal2, null) || signal2.IsBuilt == false))
                    {
                        //only oposite signal = wrong direction
                        return false;
                    }
                }
            }

            return true;
        }

        private void OnRailsChanged(IReadOnlyList<Rail> newRails, IReadOnlyList<Rail> removedRails)
        {
            MarkGraphDirty();
        }
        
        private void OnSignalBuildChanged(IReadOnlyList<RailSignal> newSignals,
            IReadOnlyList<RailSignal> removedSignals)
        {
            MarkGraphDirty();
        }

    }
}