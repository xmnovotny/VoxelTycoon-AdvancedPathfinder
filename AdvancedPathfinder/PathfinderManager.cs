using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Networking;
using VoxelTycoon;
using VoxelTycoon.Buildings;
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
        private readonly List<TTrackSection> _sections = new();
        private readonly List<TPathfinderNode> _nodes = new();

        private readonly Dictionary<TTrack, TTrackSection> _trackToSection = new();
        /** inbound connection to node */
        private readonly Dictionary<TTrackConnection, TPathfinderNode> _connectionToNode = new();

        protected virtual void ProcessFilledSection(TTrackSection section)
        {
            ImmutableUniqueList<TTrack> tracks = section.GetTrackList();
            for (int i = 0; i < tracks.Count; i++)
            {
                _trackToSection.Add(tracks[i], section);
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

        protected virtual void HighlightConnection(TTrackConnection connection, bool halfConnection, Color color)
        {
            
        }

        private HashSet<TTrack> GetNonProcessedTracks()
        {
            HashSet<TTrack> result = new HashSet<TTrack>();
            ImmutableList<TTrack> tracks = LazyManager<BuildingManager>.Current.GetAll<TTrack>();
            result.UnionWith(tracks.ToList());
            result.ExceptWith(_trackToSection.Keys);
            return result;
        }

        private void FindSections(HashSet<TTrackConnection> foundNodesConnections)
        {
            _sections.Clear();
            _trackToSection.Clear();

            HashSet<TTrackConnection> connectionsToProcess = new();
            HashSet<TTrack> processedTracks = new();
            IReadOnlyCollection<TTrackConnection> stationStopsConnections = GetStationStopsConnections();
            TrackHelper.GetStartingConnections<TTrack, TTrackConnection>(connectionsToProcess);
            TTrackSection section = null;
            while (connectionsToProcess.Count > 0)
            {
                TTrackConnection currentConn = connectionsToProcess.First();
                connectionsToProcess.Remove(currentConn);
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
            FileLog.Log($"Found {_sections.Count} sections");

            HashSet<TTrack> nonProcessedTracks = GetNonProcessedTracks();
            
            FileLog.Log($"Unprocessed tracks: {nonProcessedTracks.Count}");
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
            FileLog.Log($"Found {_nodes.Count} nodes");
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
            FileLog.Log($"Found {sumEdges} edges");
        }

        private void HighlightNodes()
        {
            foreach (TPathfinderNode node in _nodes)
            {
                foreach (TTrackConnection connection in node.InboundConnections)
                {
                    HighlightConnection(connection, true, Color.green.WithAlpha(0.25f));
                }
            }
        }

        protected override void OnInitialize()
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                HashSet<TTrackConnection> foundNodesConnections = new();
                FindSections(foundNodesConnections);
                sw.Stop();
                FileLog.Log("Find sections={0}".Format(sw.Elapsed));

                sw.Restart();
                FindNodes(foundNodesConnections);
                sw.Stop();
                FileLog.Log("Find nodes={0}".Format(sw.Elapsed));
//                HighlightNodes();

                sw.Restart();
                FindEdges();
                sw.Stop();
                FileLog.Log("Find edges={0}".Format(sw.Elapsed));

                Pathfinder pf = new();
                sw.Restart();
                pf.Initialize(_nodes[0], _nodes);
                Stopwatch sw2 = Stopwatch.StartNew();
                pf.Calculate();
                sw2.Stop();
                sw.Stop();
                FileLog.Log("Find paths all={0} ms".Format(sw.ElapsedTicks / 10000f));
                FileLog.Log("Find paths no initialization={0} ms".Format(sw2.ElapsedTicks / 10000f));
                List<string> distances = new();
                foreach ((PathfinderNodeBase node, float distance) in pf.GetDistances())
                {
                    distances.Add(distance.ToString("N1"));
                }

                FileLog.Log("Distances: " + distances.JoinToString("; "));
            }
            catch (Exception e)
            {
                FileLog.Log("Exception");
                AdvancedPathfinderMod.Logger.LogException(e);
            }
        }

        public void Find()
        {
            FileLog.Log("Find");
            Pathfinder pf = new();
            Stopwatch sw = Stopwatch.StartNew();
            pf.Initialize(_nodes[0], _nodes);
            Stopwatch sw2 = Stopwatch.StartNew();
            pf.Calculate();
            sw2.Stop();
            sw.Stop();
            FileLog.Log("Find paths all={0} ms".Format(sw.ElapsedTicks / 10000f));
            FileLog.Log("Find paths no initialization={0} ms".Format(sw2.ElapsedTicks / 10000f));
        }

        [CanBeNull]
        public TTrackSection FindSection(TTrackConnection connection)
        {
            return _trackToSection.GetValueOrDefault((TTrack) connection.Track);
        }

        public PathfinderNodeBase FindNodeByInboundConn(TTrackConnection connection)
        {
            return _connectionToNode.GetValueOrDefault(connection);
        }
        
 /*       [HarmonyPostfix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        private static void Train_TryFindPath_pof(Train __instance)
        {
            Manager<RailPathfinderManager>.Current.Find();
        }*/
        
    }
}