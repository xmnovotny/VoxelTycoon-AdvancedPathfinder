using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AdvancedPathfinder.Helpers;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using TrackHelper = AdvancedPathfinder.Helpers.TrackHelper;

namespace AdvancedPathfinder.RailPathfinder
{
    public class RailPathfinderGraph: IRailSectionFinder, IRailNodeFinder
    {
        private readonly List<RailSection> _sections = new();
        private readonly List<RailPathfinderNode> _nodes = new();
        private readonly Dictionary<Rail, RailSection> _trackToSection = new();
        /** inbound connection to node */
        private readonly Dictionary<RailConnection, RailPathfinderNode> _connectionToNode = new();
        private readonly Dictionary<RailSignal, RailPathfinderNode> _edgeLastSignals = new(); //last non-chain signals on edges = good place when to update a train path
        private readonly Dictionary<PathfinderNodeBase, float> _reachableNodes = new(); //list of reachable nodes (value in this case is always 1)
        private readonly Dictionary<PathfinderNodeBase, float> _electricalNodes = new(); //nodes reachable by electric trains (value is always 1)

        public IReadOnlyList<RailSection> Sections => _sections;
        public IReadOnlyList<RailPathfinderNode> Nodes => _nodes;
        public IReadOnlyDictionary<RailSignal, RailPathfinderNode> EdgeLastSignals => _edgeLastSignals;
        public IReadOnlyDictionary<PathfinderNodeBase, float> ReachableNodes => _reachableNodes;
        public IReadOnlyDictionary<PathfinderNodeBase, float> ElectricalNodes => _electricalNodes;

        internal Dictionary<PathfinderNodeBase, float> ReachableNodesRW => _reachableNodes;
        internal Dictionary<PathfinderNodeBase, float> ElectricalNodesRW => _electricalNodes;

        [CanBeNull]
        public RailSection FindSection(RailConnection connection)
        {
            return _trackToSection.GetValueOrDefault(connection.Track);
        }

        [CanBeNull]
        public RailSection FindSection(Rail track)
        {
            return _trackToSection.GetValueOrDefault(track);
        }
        
        public PathfinderNodeBase FindNodeByInboundConn(RailConnection connection)
        {
            return connection != null ? _connectionToNode.GetValueOrDefault(connection) : null;
        }

        internal void UpdateNodeScore(IReadOnlyDictionary<PathfinderNodeBase, float> nodeList, PathfinderNodeBase node, float newScore)
        {
            if (nodeList == _reachableNodes)
                _reachableNodes[node] = newScore;
            else if (nodeList == _electricalNodes)
                _electricalNodes[node] = newScore;
            else
                throw new ArgumentException("Node list is not from this graph.", nameof(nodeList));
        }
        
        internal void GetNonProcessedTracks(HashSet<Rail> nonProcessedTracks)
        {
            ImmutableList<Rail> tracks = LazyManager<BuildingManager>.Current.GetAll<Rail>();
            nonProcessedTracks.UnionWith(tracks.ToList());
            nonProcessedTracks.ExceptWith(_trackToSection.Keys);
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

        private IReadOnlyCollection<RailConnection> GetStationStopsConnections()
        {
            return StationHelper<RailConnection, RailStation>.Current.GetStationStopsConnections();
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
//                HighlightNonProcessedTracks(nonProcessedTracks);
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

        internal void Clear()
        {
            _reachableNodes.Clear();
            _electricalNodes.Clear();
            _edgeLastSignals.Clear();
            _nodes.Clear();
            _sections.Clear();
            _edgeLastSignals.Clear();
            _trackToSection.Clear();
        }

        internal void BuildGraph()
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                Clear();
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
                sw.Stop();
//                FileLog.Log("Find edges={0}".Format(sw.Elapsed));
                //HighlightSections();
            }
            catch (Exception e)
            {
                AdvancedPathfinderMod.Logger.LogException(e);
            }
        }
   }
}