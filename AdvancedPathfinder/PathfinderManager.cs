using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public abstract class PathfinderManager<T, TTrack, TTrackConnection, TTrackSection, TPathfinderNode, TPathfinderEdge>: Manager<T>, ISectionFinder<TTrack, TTrackConnection, TTrackSection>
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
                _trackToSection[tracks[i]] = section;
            }
        }

        private void FindSections()
        {
            _sections.Clear();
            _trackToSection.Clear();

            HashSet<TTrackConnection> connectionsToProcess = new();
            HashSet<TTrack> processedTracks = new();
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
                if (section.Fill(currentConn, processedTracks, connectionsToProcess))
                {
                    _sections.Add(section);
                    ProcessFilledSection(section);
                    section = null;
                }
            }
            FileLog.Log($"Found {_sections.Count} sections");
        }

        private void FindNodes()
        {
            _nodes.Clear();
            _connectionToNode.Clear();
            foreach (TTrackSection section in _sections)
            {
                foreach (TTrackConnection conn in new[] {section.First, section.Last})
                {
                    TPathfinderNode node = null;
                    if (conn.OuterConnectionCount > 1 && !_connectionToNode.ContainsKey(conn))
                    {
                        node = new TPathfinderNode();
                        node.Initialize(conn);
                    } else if (conn.OuterConnectionCount == 0 && !_connectionToNode.ContainsKey(conn))
                    {
                        //end of the track - create starting node with no inbound connection
                        node = new TPathfinderNode();
                        node.Initialize(conn, true);
                        _connectionToNode.Add(conn, node);
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
            }
            FileLog.Log($"Found {_nodes.Count} nodes");
        }

        private void FindEdges()
        {
            int sumEdges = 0;
            foreach (TPathfinderNode node in _nodes)
            {
                node.FindEdges(this);
                sumEdges += node.Edges.Count;
            }
            FileLog.Log($"Found {sumEdges} edges");
        }

        protected override void OnInitialize()
        {
            Stopwatch sw = Stopwatch.StartNew();
            FindSections();
            sw.Stop();
            FileLog.Log("Find sections={0}".Format(sw.Elapsed));

            sw = Stopwatch.StartNew();
            FindNodes();
            sw.Stop();
            FileLog.Log("Find nodes={0}".Format(sw.Elapsed));

            sw = Stopwatch.StartNew();
            FindEdges();
            sw.Stop();
            FileLog.Log("Find edges={0}".Format(sw.Elapsed));
        }

        [CanBeNull]
        public TTrackSection FindSection(TTrackConnection connection)
        {
            return _trackToSection.GetValueOrDefault((TTrack) connection.Track);
        }
    }
}