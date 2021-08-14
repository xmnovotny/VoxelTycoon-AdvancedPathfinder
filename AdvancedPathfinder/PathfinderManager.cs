﻿using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using AdvancedPathfinder.Rails;
using AdvancedPathfinder.UI;
using HarmonyLib;
using JetBrains.Annotations;
using MonoMod.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Diagnostics;
using VoxelTycoon.Game.UI;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
 
    [HarmonyPatch]
    public abstract class PathfinderManager<T, TTrack, TTrackConnection, TTrackSection, TPathfinderNode, TPathfinderEdge>: Manager<T>, ISectionFinder<TTrack, TTrackConnection, TTrackSection>, INodeFinder<TTrackConnection>
        where T: PathfinderManager<T, TTrack, TTrackConnection, TTrackSection, TPathfinderNode, TPathfinderEdge>, new()
        where TTrack : Track 
        where TTrackConnection : TrackConnection 
        where TTrackSection: TrackSection<TTrack, TTrackConnection>, new()
        where TPathfinderNode: PathfinderNode<TTrack, TTrackConnection, TTrackSection, TPathfinderEdge>, new()
        where TPathfinderEdge : PathfinderEdge<TTrack, TTrackConnection, TTrackSection>, new()
    {
        protected readonly Color[] Colors = 
        {
            Color.blue,
            Color.cyan,
            Color.magenta,
            Color.green,
            Color.red,
            Color.yellow
        };
        
        private readonly List<TTrackSection> _sections = new();
        private readonly List<TPathfinderNode> _nodes = new();
        private readonly Dictionary<PathfinderNodeBase, float> _reachableNodes = new(); //list of reachable nodes (value in this case is always 1)

        private readonly Dictionary<TTrack, TTrackSection> _trackToSection = new();
        /** inbound connection to node */
        private readonly Dictionary<TTrackConnection, TPathfinderNode> _connectionToNode = new();
        [CanBeNull]
        private Pathfinder<TPathfinderNode> _pathfinder;
        private readonly Dictionary<int, HashSet<TPathfinderNode>> _convertedDestinationCache = new();
        private Pathfinder<TPathfinderNode> Pathfinder { get => _pathfinder ??= new Pathfinder<TPathfinderNode>(); }

        private readonly HashSet<Highlighter> _highlighters = new();
        private bool _graphDirty;
        public float ElapsedMilliseconds { get; private set; }

        protected List<TTrackSection> Sections => _sections;
        public PathfinderStats Stats { get; protected set; }

        [CanBeNull]
        public TTrackSection FindSection(TTrackConnection connection)
        {
            return _trackToSection.GetValueOrDefault((TTrack) connection.Track);
        }

        public TTrackSection FindSection(TTrack track)
        {
            return _trackToSection.GetValueOrDefault(track);
        }
        
        [CanBeNull]
        public TPathfinderNode FindNearestNode(TTrackConnection connection)
        {
            return (TPathfinderNode) FindSection(connection)?.FindNextNode(connection);
        }

        public void CreateStats()
        {
            if (Stats != null)
                throw new InvalidOperationException("Stats already created");
            Stats = new PathfinderStats();
        }

        protected virtual void ProcessFilledSection(TTrackSection section)
        {
            ImmutableUniqueList<TTrackConnection> connections = section.GetConnectionList();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].Track == null)
                {
                    throw new InvalidOperationException("Connection has no track");
                }
                _trackToSection.Add((TTrack)connections[i].Track, section);
            }
        }

        [CanBeNull]
        protected virtual IReadOnlyCollection<TTrackConnection> GetStationStopsConnections()
        {
            return null;
        }

        protected virtual void HighlightNonProcessedTracks(HashSet<TTrack> unprocessedTracks)
        {
        }

        protected virtual Highlighter HighlightConnection(TTrackConnection connection, bool halfConnection, Color color)
        {
            return null;
        }

        protected virtual Dictionary<PathfinderNodeBase, float> GetNodesList(object edgeSettings,
            TPathfinderNode originNode, out bool calculateReachableNodes)
        {
            calculateReachableNodes = false;
            return _reachableNodes;
        }

        internal HashSet<TPathfinderNode> GetConvertedDestination(IVehicleDestination destination)
        {
            int hash = destination.GetDestinationHash();
            if (_convertedDestinationCache.TryGetValue(hash, out HashSet<TPathfinderNode> convertedDest))
                return convertedDest;

            convertedDest = ConvertDestination(destination);
            _convertedDestinationCache[hash] = convertedDest;

            return convertedDest;
        }

        private HashSet<TPathfinderNode> ConvertDestination(IVehicleDestination destination)
        {
            HashSet<TPathfinderNode> result = new HashSet<TPathfinderNode>();
            foreach (TrackConnection connection in destination.Stops)
            {
                TPathfinderNode node = (TPathfinderNode) FindNodeByInboundConn(connection.InnerConnection as TTrackConnection);
                if (node == null)
                    throw new ArgumentException("Cannot convert target to node");
                result.Add(node);
            }

            return result;
        }
        
        private void FillFoundedPath(TPathfinderNode originNode, TPathfinderNode targetNode, List<TrackConnection> result)
        {
            TPathfinderNode node = targetNode;
            using PooledList<TPathfinderEdge> edges = PooledList<TPathfinderEdge>.Take();
            while (node != originNode)
            {
                if (node?.PreviousBestEdge == null)
                {
                    throw new InvalidOperationException("Cannot reconstruct founded path");
                }
                edges.Add(node.PreviousBestEdge);
                node = (TPathfinderNode) node.PreviousBestNode;
            }

            for (int i = edges.Count - 1; i >= 0; i--)
            {
                edges[i].GetConnections(result);
            }
        }

        /** will test connections from starting connection to the first node for right direction */
        protected virtual bool TestPathToFirstNode(List<TrackConnection> connections)
        {
            return true;
        }

        protected virtual object GetEdgeSettingsForCalcReachableNodes(object origEdgeSettings)
        {
            return origEdgeSettings;
        }
        
        protected bool FindPath([NotNull] TTrackConnection origin, [NotNull] IVehicleDestination target,
            object edgeSettings, List<TrackConnection> result, Dictionary<PathfinderNodeBase, float> nodesList = null)
        {
            InvalidateGraph();
            Stopwatch sw = Stopwatch.StartNew();
            TPathfinderNode originNode = FindNearestNode(origin);
            if (originNode == null)
            {
                //it can be on circular track without any signal or switch in desired direction
                AdvancedPathfinderMod.Logger.Log("Starting path node not found.");
                return false;
            }

            HashSet<TPathfinderNode> targetNodes = GetConvertedDestination(target);
            TPathfinderNode endNode;
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

        protected virtual void ProcessNodeToSubLists(TPathfinderNode node)
        {
            if (node.IsReachable)
                _reachableNodes.Add(node, 0);
        }
        private void FillNodeSubLists()
        {
            foreach (TPathfinderNode node in _nodes)
            {
                ProcessNodeToSubLists(node);
            }
        }

        protected void FindAllReachableNodesInternal(
            Dictionary<PathfinderNodeBase, float> nodeList, object edgeSettings)
        {
            foreach (PathfinderNodeBase node in nodeList.Keys)
            {
                TPathfinderNode originNode = (TPathfinderNode) node;
                Pathfinder.FindAll(originNode, _reachableNodes, edgeSettings);
                Dictionary<PathfinderNodeBase, float> reachableNodes = new();
                Pathfinder.GetDistances(reachableNodes);
                originNode.SetReachableNodes(reachableNodes, edgeSettings);
            }
        }

        protected virtual void FindAllReachableNodes()
        {
            FindAllReachableNodesInternal(_reachableNodes, GetEdgeSettingsForCalcReachableNodes(null));
        }
        
        protected virtual void FinalizeBuildGraph()
        {
            Stopwatch sw = Stopwatch.StartNew();
            FindAllReachableNodes();
            sw.Stop();
//            FileLog.Log($"Finding all reachable nodes in {sw.ElapsedTicks / 10000f:N2}ms");
        }

        private void GetNonProcessedTracks(HashSet<TTrack> nonProcessedTracks)
        {
            ImmutableList<TTrack> tracks = LazyManager<BuildingManager>.Current.GetAll<TTrack>();
            nonProcessedTracks.UnionWith(tracks.ToList());
            nonProcessedTracks.ExceptWith(_trackToSection.Keys);
        }

        private void FindConnectionsAfterSwitch(HashSet<TTrack> tracks, HashSet<TTrackConnection> foundConnections)
        {
            foreach (TTrack track in tracks)
            {
                for (int i = track.ConnectionCount - 1; i >= 0; i--)
                {
                    TrackConnection conn = track.GetConnection(i);
                    if (conn.OuterConnectionCount > 1)
                    {
                        foreach (TrackConnection outerConnection in conn.OuterConnections)
                        {
                            foundConnections.Add((TTrackConnection) outerConnection);
                        }
                    }
                }
            }
        }

        private void FindSections(HashSet<TTrackConnection> foundNodesConnections)
        {
            _sections.Clear();
            _trackToSection.Clear();

            using PooledHashSet<TTrackConnection> connectionsToProcess = PooledHashSet<TTrackConnection>.Take();
            using PooledHashSet<TTrack> processedTracks = PooledHashSet<TTrack>.Take();
            using PooledHashSet<TTrack> nonProcessedTracks = PooledHashSet<TTrack>.Take();
            bool addedStationStops = false;
            bool addedSwitches = false;

            IReadOnlyCollection<TTrackConnection> stationStopsConnections = GetStationStopsConnections();
            TrackHelper.GetStartingConnections<TTrack, TTrackConnection>(connectionsToProcess);
            TTrackSection section = null;
            ImmutableList<TTrack> tracks = LazyManager<BuildingManager>.Current.GetAll<TTrack>();
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
                        foreach (TTrackConnection connection in stationStopsConnections)
                        {
                            if (!processedTracks.Contains(connection.Track))
                                connectionsToProcess.Add((TTrackConnection)connection.InnerConnection); //from the end of platform back to the start of platform
                            foreach (TrackConnection outerConnection in connection.InnerConnection.OuterConnections)
                            {
                                //from the track beyond the platform away from station
                                if (!processedTracks.Contains(outerConnection.Track))
                                    connectionsToProcess.Add((TTrackConnection) outerConnection);
                            }
                        }
                        continue;
                    }
                    //only simple circular track remained, add one of unprocessed connection to process until all tracks are processed
                    connectionsToProcess.Add((TTrackConnection) nonProcessedTracks.First().GetConnection(0));
                }
                TTrackConnection currentConn = connectionsToProcess.First();
                connectionsToProcess.Remove(currentConn);
                if (processedTracks.Contains(currentConn.Track))
                    continue;
                if (section == null) //for reusing created and unfilled section from previous cycle
                {
                    section = new TTrackSection();
                }
                if (section.Fill(currentConn, stationStopsConnections, processedTracks, connectionsToProcess, foundNodesConnections))
                {
                    _sections.Add(section);
                    ProcessFilledSection(section);
                    section = null;
                }

            }
            //FileLog.Log($"Found {_sections.Count} sections, iterations: {count}, average per iteration: {(count > 0 ? (ticks / count / 10000) : 0)}");

//            HashSet<TTrack> nonProcessedTracks = GetNonProcessedTracks();
            
//            FileLog.Log($"Unprocessed tracks: {nonProcessedTracks.Count}");
            if (nonProcessedTracks.Count > 0)
            {
                HighlightNonProcessedTracks(nonProcessedTracks);
            }
        }

        private void FindNodes(HashSet<TTrackConnection> foundNodesConnections)
        {
            _nodes.Clear();
            _connectionToNode.Clear();
            foreach (TTrackConnection conn in foundNodesConnections)
            {
                TPathfinderNode node = null;
                if (conn.OuterConnectionCount >= 1 && !_connectionToNode.ContainsKey(conn))
                {
                    node = new TPathfinderNode();
                    node.Initialize(conn);
                } else if (conn.OuterConnectionCount == 0 && !_connectionToNode.ContainsKey(conn))
                {
                    //end of the track - create starting node with no inbound connection and ending node with no outbound connection
                    //starting node (will not be added to connectionToNode)
                    node = new TPathfinderNode();
                    node.Initialize(conn, true);
                    _nodes.Add(node);
                    //ending node (will be added to connectionToNode)
                    node = new TPathfinderNode();
                    node.Initialize(conn);
                }
                if (node != null)
                {
                    _nodes.Add(node);
                    foreach (TTrackConnection connection in node.InboundConnections)
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
            foreach (TPathfinderNode node in _nodes)
            {
                if (node.OutboundConnections.Count == 0)  //end node, no edges
                    continue;
                node.FindEdges(this, this);
                sumEdges += node.Edges.Count;
            }
            
//            FileLog.Log($"Found {sumEdges} edges");
        }

        private void HighlightNode(TPathfinderNode node, Color color)
        {
            foreach (TTrackConnection connection in node.InboundConnections)
            {
                if (!connection.Track.IsBuilt)
                    continue;
                _highlighters.Add(HighlightConnection(connection, true, color));
            }
        }
        
        private void HighlightNodes()
        {
            HideHighlighters();
            foreach (TPathfinderNode node in _nodes)
            {
                HighlightNode(node, Color.green.WithAlpha(0.25f));
            }
        }

        private void HighlightLastVisitedNodes()
        {
            HideHighlighters();
            foreach (TPathfinderNode node in _nodes)
            {
                if (node.LastPathLength == null)
                    continue;
                HighlightNode(node, Color.green.WithAlpha(0.4f));
            }
        }

        protected void HideHighlighters()
        {
            foreach (Highlighter highlighter in _highlighters)
            {
                if (highlighter == null)
                    continue;
                highlighter.gameObject.SetActive(false);
            }
            _highlighters.Clear();
        }

        protected void HighlightSection(TTrackSection section, Color color)
        {
            List<TrackConnection> connections = new();
            section.GetConnectionsInDirection(PathDirection.Backward, connections);
            foreach (TrackConnection connection in connections)
            {
                if (!connection.Track.IsBuilt)
                    continue;
                _highlighters.Add(HighlightConnection((TTrackConnection)connection, false, color));
            }
        }

        private void HighlightSections()
        {
            HideHighlighters();
            int idx = 0;
            foreach (TTrackSection section in _sections)
            {
                Color color = Colors[idx].WithAlpha(0.4f);
                HighlightSection(section, color);
                idx = (idx + 1) % Colors.Length;
            }
        }

        private void HighlightEdge(TPathfinderEdge edge, Color color)
        {
            foreach ((TTrackSection section, PathDirection direction) in edge.Sections)
            {
                HighlightSection(section, color);
            }
        }

        public void HighlightConnections(List<TrackConnection> connections)
        {
            HideHighlighters();
            foreach (TrackConnection connection in connections)
            {
                if (!connection.Track.IsBuilt)
                    continue;
                _highlighters.Add(HighlightConnection((TTrackConnection) connection, false, Color.green.WithAlpha(0.5f)));
            }
        }

        protected virtual void ClearGraph()
        {
            _reachableNodes.Clear();
            _convertedDestinationCache.Clear();
        }

        protected void BuildGraph()
        {
            try
            {
                HideHighlighters();
                Stopwatch sw = Stopwatch.StartNew();
                ClearGraph();
                HashSet<TTrackConnection> foundNodesConnections = new();
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

/*                sw.Restart();
                Pathfinder.FindAll(_nodes[0], _reachableNodes, new RailEdgeSettings());
                sw.Stop();
                FileLog.Log("Find paths={0} ms".Format(sw.ElapsedTicks / 10000f));
                List<string> distances = new();
                foreach ((PathfinderNodeBase node, float distance) in Pathfinder.GetDistances())
                {
                    distances.Add(distance.ToString("N1"));
                }

                FileLog.Log("Distances: " + distances.JoinToString("; "));*/
                //HighlightSections();
            }
            catch (Exception e)
            {
                AdvancedPathfinderMod.Logger.LogException(e);
            }
        }

        protected void MarkGraphDirty()
        {
            _graphDirty = true;
        }

        protected override void OnInitialize()
        {
            BuildGraph();
        }

/*        private int _lastNodeIndex = 0;
        private int _lastEdgeIndex = 0;
        private int _lastColorIndex = 0;
        protected override void OnUpdate()
        {
            if (TimeHelper.OncePerTime(3f))
            {
                HideHighlighters();
                TPathfinderNode node = _nodes[_lastNodeIndex];
                Color color = _colors[_lastColorIndex++];
                HighlightNode(node, color);
                if (node.Edges.Count > 0)
                {
                    TPathfinderEdge edge = node.Edges[_lastEdgeIndex++];
                    HighlightEdge(edge, color.WithAlpha(0.4f));
                    HighlightNode((TPathfinderNode) edge.NextNode, color.GetOppositeColor());
                }

                if (_lastEdgeIndex >= node.Edges.Count)
                {
                    _lastEdgeIndex = 0;
                    _lastNodeIndex = (_lastNodeIndex + 1) % _nodes.Count;
                }

                _lastColorIndex %= _colors.Length;
            }
        }*/

        public PathfinderNodeBase FindNodeByInboundConn(TTrackConnection connection)
        {
            return connection != null ? _connectionToNode.GetValueOrDefault(connection) : null;
        }
        
    }
}