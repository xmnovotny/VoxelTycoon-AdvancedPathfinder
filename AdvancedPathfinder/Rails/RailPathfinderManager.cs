using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AdvancedPathfinder.UI;
using Delegates;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using VoxelTycoon.Tracks.Tasks;
using XMNUtils;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderManager: Manager<RailPathfinderManager>, ISectionFinder<Rail, RailConnection, RailSection>, INodeFinder<RailConnection>
    {
        //TODO: Optimize checking direction of path from starting connection to the first node
        //TODO: refresh highlighted path after detaching a train
        //TODO: calculate a closed block penalty based on a distance from the start of the path
        //TODO: add a high penalty for a occupied platform section when that section is the path target (for a better result of selecting a free platform)
        //TODO: set a different penalty for an occupied simple block and a block with switches
        private readonly Dictionary<PathfinderNodeBase, float> _electricalNodes = new(); //nodes reachable by electric trains (value is always 1)
        private bool _closedSectionsDirty; 
        private Action<Vehicle, bool> _trainUpdatePathAction;
        private readonly Dictionary<RailSignal, RailPathfinderNode> _edgeLastSignals = new(); //last non-chain signals on edges = good place when to update a train path

        private readonly Color[] Colors = 
        {
            Color.blue,
            Color.cyan,
            Color.magenta,
            Color.green,
            Color.red,
            Color.yellow
        };
        
        private readonly List<RailSection> _sections = new();
        private readonly List<RailPathfinderNode> _nodes = new();
        private readonly Dictionary<PathfinderNodeBase, float> _reachableNodes = new(); //list of reachable nodes (value in this case is always 1)

        private readonly Dictionary<Rail, RailSection> _trackToSection = new();
        /** inbound connection to node */
        private readonly Dictionary<RailConnection, RailPathfinderNode> _connectionToNode = new();
        [CanBeNull]
        private Pathfinder<RailPathfinderNode> _pathfinder;
        private readonly Dictionary<int, HashSet<RailPathfinderNode>> _convertedDestinationCache = new();
        private Pathfinder<RailPathfinderNode> Pathfinder { get => _pathfinder ??= new Pathfinder<RailPathfinderNode>(); }

        private readonly HashSet<Highlighter> _highlighters = new();
        private bool _graphDirty;
        public float ElapsedMilliseconds { get; private set; }

        public PathfinderStats Stats { get; protected set; }

        [CanBeNull]
        public RailSection FindSection(RailConnection connection)
        {
            return _trackToSection.GetValueOrDefault(connection.Track);
        }

        public RailSection FindSection(Rail track)
        {
            return _trackToSection.GetValueOrDefault(track);
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

        public PathfinderNodeBase FindNodeByInboundConn(RailConnection connection)
        {
            return connection != null ? _connectionToNode.GetValueOrDefault(connection) : null;
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

        public void CreateStats()
        {
            if (Stats != null)
                throw new InvalidOperationException("Stats already created");
            Stats = new PathfinderStats();
        }

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
                    Dictionary<PathfinderNodeBase, float> fullNodes = edgeSettings is RailEdgeSettings {Electric: true}
                        ? _electricalNodes
                        : _reachableNodes;
                    Stats.AddFullNodesCount(fullNodes.Count);
                    Stats.AddSubNodesCount(nodes.Count);
                }

                return nodes;
            }

            calculateReachableNodes = true;
            return edgeSettings is RailEdgeSettings {Electric: true} ? _electricalNodes : _reachableNodes;
        }

        private HashSet<RailPathfinderNode> ConvertDestination(IVehicleDestination destination)
        {
            HashSet<RailPathfinderNode> result = new HashSet<RailPathfinderNode>();
            foreach (TrackConnection connection in destination.Stops)
            {
                RailPathfinderNode node = (RailPathfinderNode) FindNodeByInboundConn(connection.InnerConnection as RailConnection);
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
                node = (RailPathfinderNode) node.PreviousBestNode;
            }

            for (int i = edges.Count - 1; i >= 0; i--)
            {
                edges[i].GetConnections(result);
            }
        }

        private void ProcessNodeToSubLists(RailPathfinderNode node)
        {
            if (node.IsReachable)
                _reachableNodes.Add(node, 0);
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
        private void FillNodeSubLists()
        {
            foreach (RailPathfinderNode node in _nodes)
            {
                ProcessNodeToSubLists(node);
            }
        }

        private void FindAllReachableNodesInternal(
            Dictionary<PathfinderNodeBase, float> nodeList, object edgeSettings)
        {
            foreach (PathfinderNodeBase node in nodeList.Keys)
            {
                RailPathfinderNode originNode = (RailPathfinderNode) node;
                Pathfinder.FindAll(originNode, _reachableNodes, edgeSettings);
                Dictionary<PathfinderNodeBase, float> reachableNodes = new();
                Pathfinder.GetDistances(reachableNodes);
                originNode.SetReachableNodes(reachableNodes, edgeSettings);
            }
        }

        private void FindAllReachableNodes()
        {
            FindAllReachableNodesInternal(_reachableNodes, GetEdgeSettingsForCalcReachableNodes(null));
            object edgeSettings = GetEdgeSettingsForCalcReachableNodes(new RailEdgeSettings() {Electric = true});
            FindAllReachableNodesInternal(_electricalNodes, edgeSettings);
        }
        private void FinalizeBuildGraph()
        {
            Stopwatch sw = Stopwatch.StartNew();
            FindAllReachableNodes();
            sw.Stop();
//            FileLog.Log($"Finding all reachable nodes in {sw.ElapsedTicks / 10000f:N2}ms");
        }

        private void GetNonProcessedTracks(HashSet<Rail> nonProcessedTracks)
        {
            ImmutableList<Rail> tracks = LazyManager<BuildingManager>.Current.GetAll<Rail>();
            nonProcessedTracks.UnionWith(tracks.ToList());
            nonProcessedTracks.ExceptWith(_trackToSection.Keys);
        }

        private void FindConnectionsAfterSwitch(HashSet<Rail> tracks, HashSet<RailConnection> foundConnections)
        {
            foreach (Rail track in tracks)
            {
                for (int i = track.ConnectionCount - 1; i >= 0; i--)
                {
                    TrackConnection conn = track.GetConnection(i);
                    if (conn.OuterConnectionCount > 1)
                    {
                        foreach (TrackConnection outerConnection in conn.OuterConnections)
                        {
                            foundConnections.Add((RailConnection) outerConnection);
                        }
                    }
                }
            }
        }

        private void FindSections(HashSet<RailConnection> foundNodesConnections)
        {
            _sections.Clear();
            _trackToSection.Clear();

            using PooledHashSet<RailConnection> connectionsToProcess = PooledHashSet<RailConnection>.Take();
            using PooledHashSet<Rail> processedTracks = PooledHashSet<Rail>.Take();
            using PooledHashSet<Rail> nonProcessedTracks = PooledHashSet<Rail>.Take();
            bool addedStationStops = false;
            bool addedSwitches = false;

            IReadOnlyCollection<RailConnection> stationStopsConnections = GetStationStopsConnections();
            TrackHelper.GetStartingConnections<Rail, RailConnection>(connectionsToProcess);
            RailSection section = null;
            ImmutableList<Rail> tracks = LazyManager<BuildingManager>.Current.GetAll<Rail>();
            nonProcessedTracks.UnionWith(tracks.ToList());  //mark all tracks as unprocessed

            while (true)
            {
                if (connectionsToProcess.Count == 0)
                {
                    nonProcessedTracks.ExceptWith(_trackToSection.Keys); //except all processed tracks from nonprocessed tracks

                    if (nonProcessedTracks.Count == 0)
                        break;
                    if (!addedSwitches)
                    {
                        //find unprocessed connections just after switches
                        addedSwitches = true;
                        FindConnectionsAfterSwitch(nonProcessedTracks, connectionsToProcess);
                        continue;
                    }
                    if (!addedStationStops && stationStopsConnections?.Count > 0)
                    {
                        //try start processing from stations 
                        addedStationStops = true;
                        foreach (RailConnection connection in stationStopsConnections)
                        {
                            if (!processedTracks.Contains(connection.Track))
                                connectionsToProcess.Add((RailConnection)connection.InnerConnection); //from the end of platform back to the start of platform
                            foreach (TrackConnection outerConnection in connection.InnerConnection.OuterConnections)
                            {
                                //from the track beyond the platform away from station
                                if (!processedTracks.Contains(outerConnection.Track))
                                    connectionsToProcess.Add((RailConnection) outerConnection);
                            }
                        }
                        continue;
                    }
                    //only simple circular track remained, add one of unprocessed connection to process until all tracks are processed
                    connectionsToProcess.Add((RailConnection) nonProcessedTracks.First().GetConnection(0));
                }
                RailConnection currentConn = connectionsToProcess.First();
                connectionsToProcess.Remove(currentConn);
                if (processedTracks.Contains(currentConn.Track))
                    continue;
                if (section == null) //for reusing created and unfilled section from previous cycle
                {
                    section = new RailSection();
                }
                if (section.Fill(currentConn, stationStopsConnections, processedTracks, connectionsToProcess, foundNodesConnections))
                {
                    _sections.Add(section);
                    ProcessFilledSection(section);
                    section = null;
                }

            }
            //FileLog.Log($"Found {_sections.Count} sections, iterations: {count}, average per iteration: {(count > 0 ? (ticks / count / 10000) : 0)}");

//            HashSet<Rail> nonProcessedTracks = GetNonProcessedTracks();
            
//            FileLog.Log($"Unprocessed tracks: {nonProcessedTracks.Count}");
            if (nonProcessedTracks.Count > 0)
            {
                HighlightNonProcessedTracks(nonProcessedTracks);
            }
        }

        private void FindNodes(HashSet<RailConnection> foundNodesConnections)
        {
            _nodes.Clear();
            _connectionToNode.Clear();
            foreach (RailConnection conn in foundNodesConnections)
            {
                RailPathfinderNode node = null;
                if (conn.OuterConnectionCount >= 1 && !_connectionToNode.ContainsKey(conn))
                {
                    node = new RailPathfinderNode();
                    node.Initialize(conn);
                } else if (conn.OuterConnectionCount == 0 && !_connectionToNode.ContainsKey(conn))
                {
                    //end of the track - create starting node with no inbound connection and ending node with no outbound connection
                    //starting node (will not be added to connectionToNode)
                    node = new RailPathfinderNode();
                    node.Initialize(conn, true);
                    _nodes.Add(node);
                    //ending node (will be added to connectionToNode)
                    node = new RailPathfinderNode();
                    node.Initialize(conn);
                }
                if (node != null)
                {
                    _nodes.Add(node);
                    foreach (RailConnection connection in node.InboundConnections)
                    {
                        _connectionToNode.Add(connection, node);
                    }
                }
            }
//            FileLog.Log($"Found {_nodes.Count} nodes");
        }

        private void FindEdges()
        {
            int sumEdges = 0;
            foreach (RailPathfinderNode node in _nodes)
            {
                if (node.OutboundConnections.Count == 0)  //end node, no edges
                    continue;
                node.FindEdges(this, this);
                sumEdges += node.Edges.Count;
            }
            
//            FileLog.Log($"Found {sumEdges} edges");
        }

        private void ClearGraph()
        {
            _reachableNodes.Clear();
            _convertedDestinationCache.Clear();
            _electricalNodes.Clear();
            _edgeLastSignals.Clear();
        }

        private void BuildGraph()
        {
            try
            {
                HideHighlighters();
                Stopwatch sw = Stopwatch.StartNew();
                ClearGraph();
                HashSet<RailConnection> foundNodesConnections = new();
                FindSections(foundNodesConnections);
                sw.Stop();
//                FileLog.Log("Find sections={0}".Format(sw.Elapsed));

                sw.Restart();
                FindNodes(foundNodesConnections);
                sw.Stop();
//                FileLog.Log("Find nodes={0}".Format(sw.Elapsed));
//                HighlightNodes();

                sw.Restart();
                FindEdges();
                FillNodeSubLists();
                FinalizeBuildGraph();
                sw.Stop();
//                FileLog.Log("Find edges={0}".Format(sw.Elapsed));
                //HighlightSections();
            }
            catch (Exception e)
            {
                AdvancedPathfinderMod.Logger.LogException(e);
            }
        }

        private void MarkGraphDirty()
        {
            _graphDirty = true;
        }

        protected override void OnInitialize()
        {
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

        private IReadOnlyCollection<RailConnection> GetStationStopsConnections()
        {
            return StationHelper<RailConnection, RailStation>.Current.GetStationStopsConnections();
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

        private void ProcessFilledSection(RailSection section)
        {
            ImmutableUniqueList<RailConnection> connections = section.GetConnectionList();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].Track == null)
                {
                    throw new InvalidOperationException("Connection has no track");
                }
                _trackToSection.Add(connections[i].Track, section);
            }
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
                        FileLog.Log(
                            $"Find reachable nodes, original count {nodesList.Count}, reachable {reachableNodes.Count}, in {(sw2.ElapsedTicks / 10000f):N2}ms");
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
            foreach (RailPathfinderNode node in _nodes)
            {
                HighlightNode(node, Color.green.WithAlpha(0.25f));
            }
        }

        private void HighlightLastVisitedNodes()
        {
            HideHighlighters();
            foreach (RailPathfinderNode node in _nodes)
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
            foreach (RailSection section in _sections)
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
        
        
        private void HighlightClosedBlockSections()
        {
            HideHighlighters();
            int idx = 0;
            foreach (RailSection section in _sections)
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