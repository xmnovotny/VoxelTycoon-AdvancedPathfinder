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