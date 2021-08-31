using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AdvancedPathfinder.Helpers;
using AdvancedPathfinder.UI;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;
using TrackHelper = AdvancedPathfinder.Helpers.TrackHelper;

namespace AdvancedPathfinder.RailPathfinder
{
    public class RailPathfinderManager: Manager<RailPathfinderManager>
    {
        //TODO: Optimize checking direction of path from starting connection to the first node
        //TODO: refresh highlighted path after detaching a train
        //TODO: calculate a closed block penalty based on a distance from the start of the path
        //TODO: add a high penalty for a occupied platform section when that section is the path target (for a better result of selecting a free platform)
        //TODO: set a different penalty for an occupied simple block and a block with switches
        private bool _closedSectionsDirty; 
        private Action<Vehicle, bool> _trainUpdatePathAction;

        private readonly Color[] Colors = 
        {
            Color.blue,
            Color.cyan,
            Color.magenta,
            Color.green,
            Color.red,
            Color.yellow
        };
        

        [CanBeNull]
        private Pathfinder<RailPathfinderNode> _pathfinder;
        private readonly Dictionary<int, HashSet<RailPathfinderNode>> _convertedDestinationCache = new();
        private Pathfinder<RailPathfinderNode> Pathfinder { get => _pathfinder ??= new Pathfinder<RailPathfinderNode>(); }

        private readonly HashSet<Highlighter> _highlighters = new();
        private bool _graphDirty;
        private int _visitedId;
        public float ElapsedMilliseconds { get; private set; }
        public RailPathfinderGraph Graph { get; private set; }

        public PathfinderStats Stats { get; protected set; }

        [CanBeNull]
        public RailSection FindSection(RailConnection connection)
        {
            return Graph.FindSection(connection.Track);
        }

        public RailSection FindSection(Rail track)
        {
            return Graph.FindSection(track);
        }
        
        [CanBeNull]
        public RailPathfinderNode FindNearestNode(RailConnection connection)
        {
            return (RailPathfinderNode) FindSection(connection)?.FindNextNode(connection);
        }

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
            return Graph.EdgeLastSignals.ContainsKey(signal);
        }

        public bool IsLastEdgeSignalWithPathDiversion(Train train, RailSignal signal)
        {
            if (!Graph.EdgeLastSignals.TryGetValue(signal, out RailPathfinderNode node))
                return false;
            IVehicleDestination destination = SimpleLazyManager<TrainHelper>.Current.GetTrainDestination(train);
            return destination == null || node.HasPathDiversion(train, destination);
        }

        public void CreateStats()
        {
            if (Stats != null)
                throw new InvalidOperationException("Stats already created");
            Stats = new PathfinderStats();
        }

        internal int GetNewVisitedId() => ++_visitedId;

        internal HashSet<RailPathfinderNode> GetConvertedDestination(IVehicleDestination destination)
        {
            int hash = destination.GetDestinationHash();
            if (_convertedDestinationCache.TryGetValue(hash, out HashSet<RailPathfinderNode> convertedDest))
                return convertedDest;

            convertedDest = ConvertDestination(destination);
            _convertedDestinationCache[hash] = convertedDest;

            return convertedDest;
        }

        private Dictionary<PathfinderNodeBase, float> GetNodesList(object edgeSettings,
            RailPathfinderNode originNode, out bool calculateReachableNodes)
        {
            calculateReachableNodes = false;
            Dictionary<PathfinderNodeBase, float> nodes = originNode.GetReachableNodes(edgeSettings);
            if (nodes != null)
            {
                if (Stats != null)
                {
                    IReadOnlyDictionary<PathfinderNodeBase, float> fullNodes = edgeSettings is RailEdgeSettings {Electric: true}
                        ? Graph.ElectricalNodes
                        : Graph.ReachableNodes;
                    Stats.AddFullNodesCount(fullNodes.Count);
                    Stats.AddSubNodesCount(nodes.Count);
                }

                return nodes;
            }

            calculateReachableNodes = true;
            return edgeSettings is RailEdgeSettings {Electric: true} ? Graph.ElectricalNodesRW : Graph.ReachableNodesRW;
        }

        private HashSet<RailPathfinderNode> ConvertDestination(IVehicleDestination destination)
        {
            HashSet<RailPathfinderNode> result = new HashSet<RailPathfinderNode>();
            foreach (TrackConnection connection in destination.Stops)
            {
                RailPathfinderNode node = (RailPathfinderNode) Graph.FindNodeByInboundConn(connection.InnerConnection as RailConnection);
                if (node == null)
                    throw new ArgumentException("Cannot convert target to node");
                result.Add(node);
            }

            return result;
        }

        private void FillFoundedPath(RailPathfinderNode originNode, RailPathfinderNode targetNode, List<TrackConnection> result)
        {
            RailPathfinderNode node = targetNode;
            using PooledList<RailPathfinderEdge> edges = PooledList<RailPathfinderEdge>.Take();
            while (node != originNode)
            {
                if (node?.PreviousBestEdge == null)
                {
                    throw new InvalidOperationException("Cannot reconstruct founded path");
                }
                edges.Add(node.PreviousBestEdge);
                node = node.PreviousBestNode;
            }

            for (int i = edges.Count - 1; i >= 0; i--)
            {
                edges[i].GetConnections(result);
            }
        }

        private void FindAllReachableNodesInternal(
            Dictionary<PathfinderNodeBase, float> nodeList, object edgeSettings)
        {
            foreach (PathfinderNodeBase node in nodeList.Keys)
            {
                RailPathfinderNode originNode = (RailPathfinderNode) node;
                Pathfinder.FindAll(originNode, nodeList, edgeSettings);
                Dictionary<PathfinderNodeBase, float> reachableNodes = new();
                Pathfinder.GetDistances(reachableNodes);
                originNode.SetReachableNodes(reachableNodes, edgeSettings);
            }
        }

        private void FindAllReachableNodes()
        {
            FindAllReachableNodesInternal(Graph.ReachableNodesRW, GetEdgeSettingsForCalcReachableNodes(null));
            object edgeSettings = GetEdgeSettingsForCalcReachableNodes(new RailEdgeSettings() {Electric = true});
            FindAllReachableNodesInternal(Graph.ElectricalNodesRW, edgeSettings);
        }

        private void FinalizeBuildGraph()
        {
            Stopwatch sw = Stopwatch.StartNew();
            FindAllReachableNodes();
            sw.Stop();
//            FileLog.Log($"Finding all reachable nodes in {sw.ElapsedTicks / 10000f:N2}ms");
        }


        private void ClearGraph()
        {
            _convertedDestinationCache.Clear();
        }

        private void BuildGraph()
        {
            HideHighlighters();
            ClearGraph();
            Graph.BuildGraph();
            FinalizeBuildGraph();
        }

        private void MarkGraphDirty()
        {
            _graphDirty = true;
        }

        protected override void OnInitialize()
        {
            Graph = new RailPathfinderGraph();
            BuildGraph();
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

        private void HighlightNonProcessedTracks(HashSet<Rail> unprocessedTracks)
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

        private Highlighter HighlightConnection(RailConnection connection, bool halfConnection,
            Color color)
        {
            RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current;
            Highlighter hl = halfConnection ? hlMan.ForOneConnection(connection, color) : hlMan.ForOneTrack(connection.Track, color);
            Rail rail = connection.Track;
            hl.transform.SetParent(rail.transform);
            return hl;
        }

        private object GetEdgeSettingsForCalcReachableNodes(object origEdgeSettings)
        {
            return (origEdgeSettings is RailEdgeSettings railEdgeSettings) ? railEdgeSettings with {CalculateOnlyBaseScore = true} : new RailEdgeSettings() {CalculateOnlyBaseScore = true};
        }

        private bool TestPathToFirstNode(List<TrackConnection> connections)
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

        private bool FindPath([NotNull] RailConnection origin, [NotNull] IVehicleDestination target,
            object edgeSettings, List<TrackConnection> result, Dictionary<PathfinderNodeBase, float> nodesList = null)
        {
            InvalidateGraph();
            Stopwatch sw = Stopwatch.StartNew();
            RailPathfinderNode originNode = FindNearestNode(origin);
            if (originNode == null)
            {
                //it can be on circular track without any signal or switch in desired direction
                AdvancedPathfinderMod.Logger.Log("Starting path node not found.");
                return false;
            }

            HashSet<RailPathfinderNode> targetNodes = GetConvertedDestination(target);
            RailPathfinderNode endNode;
            if (targetNodes.Contains(originNode))
            {
                //first node is the target node - only fill path to the first node 
                endNode = originNode;
            }
            else
            {
                if (nodesList == null)
                {
                    nodesList = GetNodesList(edgeSettings, originNode, out var calculateReachableNodes);
                    if (calculateReachableNodes)
                    {
                        Stopwatch sw2 = Stopwatch.StartNew();
                        object newEdgeSettings = GetEdgeSettingsForCalcReachableNodes(edgeSettings);
                        Pathfinder.FindAll(originNode, nodesList, newEdgeSettings);
                        Dictionary<PathfinderNodeBase, float> reachableNodes = new();
                        Pathfinder.GetDistances(reachableNodes);
                        originNode.SetReachableNodes(reachableNodes, newEdgeSettings);
                        sw2.Stop();
//                        FileLog.Log(
//                            $"Find reachable nodes, original count {nodesList.Count}, reachable {reachableNodes.Count}, in {(sw2.ElapsedTicks / 10000f):N2}ms");
                        nodesList = reachableNodes;
                    }
                }

                if (nodesList == null)
                    throw new ArgumentException("Cannot get node list");
                endNode = Pathfinder.FindOne(originNode, targetNodes, nodesList, edgeSettings, true,
                    delegate(PathfinderNodeBase nodeToUpdate, float newScore) { nodesList[nodeToUpdate] = newScore; });
                Stats?.AddReducedNodesCount(Pathfinder.NodesUsed);
            }

            if (result != null && endNode != null)
            {
                FindSection(origin)?.GetConnectionsToNextNode(origin, result);
                if (!TestPathToFirstNode(result))
                {
                    result.Clear();
                    sw.Stop();
                    ElapsedMilliseconds = sw.ElapsedTicks / 10000f;
                    return false;
                }
                FillFoundedPath(originNode, endNode, result);
            }
            sw.Stop();
            ElapsedMilliseconds = sw.ElapsedTicks / 10000f;

            return endNode != null;
        }

        private void InvalidateGraph()
        {
            if (!_graphDirty)
                return;
            _graphDirty = false;
//            FileLog.Log("Rebuild graph");
            BuildGraph();
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

        private void HighlightNode(RailPathfinderNode node, Color color)
        {
            foreach (RailConnection connection in node.InboundConnections)
            {
                if (!connection.Track.IsBuilt)
                    continue;
                _highlighters.Add(HighlightConnection(connection, true, color));
            }
        }
        
        private void HighlightNodes()
        {
            HideHighlighters();
            foreach (RailPathfinderNode node in Graph.Nodes)
            {
                HighlightNode(node, Color.cyan.WithAlpha(0.8f));
            }
        }

        private void HighlightLastVisitedNodes()
        {
            HideHighlighters();
            foreach (RailPathfinderNode node in Graph.Nodes)
            {
                if (node.LastPathLength == null)
                    continue;
                HighlightNode(node, Color.green.WithAlpha(0.4f));
            }
        }

        private void HideHighlighters()
        {
            foreach (Highlighter highlighter in _highlighters)
            {
                if (highlighter == null)
                    continue;
                highlighter.gameObject.SetActive(false);
            }
            _highlighters.Clear();
        }

        protected void HighlightSection(RailSection section, Color color)
        {
            List<TrackConnection> connections = new();
            section.GetConnectionsInDirection(PathDirection.Backward, connections);
            foreach (TrackConnection connection in connections)
            {
                if (!connection.Track.IsBuilt)
                    continue;
                _highlighters.Add(HighlightConnection((RailConnection)connection, false, color));
            }
        }

        private void HighlightSections()
        {
            HideHighlighters();
            int idx = 0;
            foreach (RailSection section in Graph.Sections)
            {
                Color color = Colors[idx].WithAlpha(0.4f);
                HighlightSection(section, color);
                idx = (idx + 1) % Colors.Length;
            }
        }

        private void HighlightEdge(RailPathfinderEdge edge, Color color)
        {
            foreach ((RailSection section, PathDirection direction) in edge.Sections)
            {
                HighlightSection(section, color);
            }
        }

        private void HighlightEdgeLastSignalConnections()
        {
            foreach (RailSignal signal in Graph.EdgeLastSignals.Keys)
            {
                _highlighters.Add(HighlightConnection(signal.Connection, true, Color.magenta));
            }
        }
        
        private void HighlightClosedBlockSections()
        {
            HideHighlighters();
            int idx = 0;
            foreach (RailSection section in Graph.Sections)
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