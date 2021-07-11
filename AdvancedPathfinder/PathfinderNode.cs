using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public abstract class PathfinderNode<TTrack, TTrackConnection, TTrackSection, TPathfinderEdge>: PathfinderNodeBase 
        where TTrack : Track
        where TTrackSection: TrackSection<TTrack, TTrackConnection>
        where TTrackConnection : TrackConnection
        where TPathfinderEdge : PathfinderEdge<TTrack, TTrackConnection, TTrackSection>, new()
    {
        private readonly HashSet<TTrackConnection> _inboundConnections = new();
        private readonly HashSet<TTrackConnection> _outboundConnections = new();
        private readonly List<TPathfinderEdge> _edges = new();
        private bool _initialized = false;

        public IReadOnlyCollection<TTrackConnection> InboundConnections => _inboundConnections;
        public ImmutableList<TPathfinderEdge> Edges => _edges.ToImmutableList();

        internal virtual void Initialize(TTrackConnection inboundConnection, bool trackStart = false)
        {
            if (_initialized)
                throw new InvalidOperationException("Already initialized");
            _initialized = true;
            if (trackStart)
            {
                if (inboundConnection.OuterConnectionCount > 0)
                {
                    throw new InvalidOperationException("Some outer connection on start connection");
                }

                _outboundConnections.Add(inboundConnection);  //add provided connection os outbound (there will be no inbound connections)
            }
            else
            {
                if (inboundConnection.OuterConnectionCount == 0)
                {
                    throw new InvalidOperationException("No outer connection on nonstart connection");
                }

                foreach (TrackConnection conn in inboundConnection.OuterConnections)
                {
                    _outboundConnections.Add((TTrackConnection) conn);
                }

                TrackConnection outConn = inboundConnection.OuterConnections[0];
                foreach (TrackConnection conn in outConn.OuterConnections)
                {
                    _inboundConnections.Add((TTrackConnection) conn);
                }
            }
        }

        internal void FindEdges(ISectionFinder<TTrack, TTrackConnection, TTrackSection> sectionFinder)
        {
            foreach (TTrackConnection connection in _outboundConnections)
            {
                TPathfinderEdge edge = new() {Owner = this};
                edge.Fill(connection, sectionFinder);
                _edges.Add(edge);
            }
        }
    }
}