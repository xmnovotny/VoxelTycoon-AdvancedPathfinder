using System;
using System.Collections.Generic;
using System.Reflection;
using AdvancedPathfinder.UI;
using Delegates;
using JetBrains.Annotations;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using VoxelTycoon.Tracks.Tasks;
using XMNUtils;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderManager: PathfinderManager<RailPathfinderManager, Rail, RailConnection, RailSection, RailPathfinderNode, RailPathfinderEdge>
    {
        //TODO: Optimize checking direction of path from starting connection to the first node
        //TODO: refresh highlighted path after detaching a train
        //TODO: calculate a closed block penalty based on a distance from the start of the path
        //TODO: add a high penalty for a occupied platform section when that section is the path target (for a better result of selecting a free platform)
        //TODO: set a different penalty for an occupied simple block and a block with switches
        //TODO: in testing, if a path has to be updated, test if there are more path possibilities and cache the results
        private readonly Dictionary<PathfinderNodeBase, float> _electricalNodes = new(); //nodes reachable by electric trains (value is always 1)
        private bool _closedSectionsDirty; 
        private Action<Vehicle, bool> _trainUpdatePathAction;
        private readonly Dictionary<RailSignal, RailPathfinderNode> _edgeLastSignals = new(); //last non-chain signals on edges = good place when to update a train path

        public bool FindImmediately([NotNull] Train train, [NotNull] RailConnection origin, [NotNull] IVehicleDestination target,
            List<TrackConnection> result)
        {
            RailEdgeSettings settings = new RailEdgeSettings(train);
            return FindPath(origin, target, settings, result);
        }

        public void MarkClosedSectionsDirty()
        {
//            _closedSectionsDirty = true;
        }

        public void TrainUpdatePath(Train train)
        {
            if (_trainUpdatePathAction == null)
            {
                MethodInfo mInf = typeof(Vehicle).GetMethod("UpdatePath", BindingFlags.NonPublic | BindingFlags.Instance);
                _trainUpdatePathAction = (Action<Vehicle, bool>)Delegate.CreateDelegate(typeof(Action<Vehicle, bool>), mInf);
            }
            _trainUpdatePathAction(train, false);
        }

        public bool IsLastEdgeSignal(RailSignal signal)
        {
            return _edgeLastSignals.ContainsKey(signal);
        }

        public bool IsLastEdgeSignalWithPathDiversion(Train train, RailSignal signal)
        {
            if (!_edgeLastSignals.TryGetValue(signal, out RailPathfinderNode node))
                return false;
            IVehicleDestination destination = SimpleLazyManager<TrainHelper>.Current.GetTrainDestination(train);
            return destination == null || node.HasPathDiversion(train, destination);
        }

        protected override Dictionary<PathfinderNodeBase, float> GetNodesList(object edgeSettings,
            RailPathfinderNode originNode, out bool calculateReachableNodes)
        {
            calculateReachableNodes = false;
            Dictionary<PathfinderNodeBase, float> nodes = originNode.GetReachableNodes(edgeSettings);
            if (nodes != null)
            {
                if (Stats != null)
                {
                    Dictionary<PathfinderNodeBase, float> fullNodes = edgeSettings is RailEdgeSettings {Electric: true}
                        ? _electricalNodes
                        : base.GetNodesList(edgeSettings, originNode, out _);
                    Stats.AddFullNodesCount(fullNodes.Count);
                    Stats.AddSubNodesCount(nodes.Count);
                }

                return nodes;
            }

            calculateReachableNodes = true;
            return edgeSettings is RailEdgeSettings {Electric: true} ? _electricalNodes : base.GetNodesList(edgeSettings, originNode, out _);
        }

        protected override void ProcessNodeToSubLists(RailPathfinderNode node)
        {
            base.ProcessNodeToSubLists(node);
            if (node.IsElReachable)
                _electricalNodes.Add(node, 0);
            if (!node.IsReachable)
                return;

            for (int i = node.Edges.Count - 1; i >= 0; i--)
            {
                RailPathfinderEdge edge = node.Edges[i];
                if (!edge.IsPassable()) continue;

                RailPathfinderNode nextNode = (RailPathfinderNode) edge.NextNode;
                
                if (nextNode is not {NumPassableOutboundEdges: > 1})
                    continue;

                RailSignal lastSignal = edge.LastSignal;
                if (!ReferenceEquals(lastSignal, null))
                {
                    _edgeLastSignals[lastSignal] = nextNode;
                }
            }
        }

        protected override void FindAllReachableNodes()
        {
            base.FindAllReachableNodes();
            object edgeSettings = GetEdgeSettingsForCalcReachableNodes(new RailEdgeSettings() {Electric = true});
            FindAllReachableNodesInternal(_electricalNodes, edgeSettings);
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            LazyManager<TrackHelper>.Current.RegisterRailsChanged(OnRailsChanged);
            LazyManager<TrackHelper>.Current.RegisterSignalBuildChanged(OnSignalBuildChanged);
            SimpleLazyManager<RailBlockHelper>.Current.RegisterAddedBlockedRailAction(OnAddedBlockedRail);
            SimpleLazyManager<RailBlockHelper>.Current.RegisterReleasedBlockedRailAction(OnReleasedBlockedRail);
        }

        protected override void OnLateUpdate()
        {
            if (_closedSectionsDirty)
            {
                _closedSectionsDirty = false;
                HighlightClosedBlockSections();
            }
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

        protected override void ClearGraph()
        {
            base.ClearGraph();
            _electricalNodes.Clear();
            _edgeLastSignals.Clear();
        }

        protected override object GetEdgeSettingsForCalcReachableNodes(object origEdgeSettings)
        {
            return (origEdgeSettings is RailEdgeSettings railEdgeSettings) ? railEdgeSettings with {CalculateOnlyBaseScore = true} : new RailEdgeSettings() {CalculateOnlyBaseScore = true};
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

        private void OnRailsChanged(IReadOnlyList<Rail> newRails, IReadOnlyList<Rail> removedRails, IReadOnlyList<Rail> electrificationChangedRails)
        {
            MarkGraphDirty();
        }
        
        private void OnSignalBuildChanged(IReadOnlyList<RailSignal> newSignals,
            IReadOnlyList<RailSignal> removedSignals)
        {
            MarkGraphDirty();
        }

        private void OnAddedBlockedRail(Rail rail, int count, RailBlock block)
        {
            RailSection section = FindSection(rail);
            section?.AddBlockedRail(rail, count, block);
        }

        private void OnReleasedBlockedRail(Rail rail, int count, RailBlock block)
        {
            RailSection section = FindSection(rail);
            section?.ReleaseBlockedRail(rail, count, block);
        }

        private void HighlightClosedBlockSections()
        {
            HideHighlighters();
            int idx = 0;
            foreach (RailSection section in Sections)
            {
                float? closedLength = section.CachedClosedBlockLength;
                if (closedLength is > 0)
                {
                    float ratio = closedLength.Value / section.CalculateCloseBlockLength(section.Length);
                    Color color = Colors[idx].WithAlpha(ratio);
                    HighlightSection(section, color);
                }
                idx = (idx + 1) % Colors.Length;
            }
        }

    }
}