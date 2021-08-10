using System;
using System.Collections.Generic;
using System.Linq;
using FibonacciHeap;
using HarmonyLib;
using JetBrains.Annotations;
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

        private Dictionary<PathfinderNodeBase, float> _reachableNodes;
        
        private bool _initialized = false;

        public IReadOnlyCollection<TTrackConnection> InboundConnections => _inboundConnections;
        public IReadOnlyCollection<TTrackConnection> OutboundConnections => _outboundConnections;
        public ImmutableList<TPathfinderEdge> Edges => _edges.ToImmutableList();
        [CanBeNull] 
        internal new PathfinderNode<TTrack, TTrackConnection, TTrackSection, TPathfinderEdge> PreviousBestNode => (PathfinderNode<TTrack, TTrackConnection, TTrackSection, TPathfinderEdge>) base.PreviousBestNode;
        [CanBeNull] 
        internal new TPathfinderEdge PreviousBestEdge => (TPathfinderEdge) base.PreviousBestEdge;

        internal override IReadOnlyList<PathfinderEdgeBase> GetEdges()
        {
            return _edges;
        }

        public virtual Dictionary<PathfinderNodeBase, float> GetReachableNodes(object edgeSettings)
        {
            return _reachableNodes;
        }  
        
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
                foreach (TrackConnection conn in inboundConnection.OuterConnections)
                {
                    _outboundConnections.Add((TTrackConnection) conn);
                }

                if (inboundConnection.OuterConnectionCount > 0)
                {
                    TrackConnection outConn = inboundConnection.OuterConnections[0];
                    foreach (TrackConnection conn in outConn.OuterConnections)
                    {
                        _inboundConnections.Add((TTrackConnection) conn);
                    }
                }
                else
                {
                    //end connection, add provided connection as only inbound
                    _inboundConnections.Add(inboundConnection);
                }
            }
        }

        internal void FindEdges(ISectionFinder<TTrack, TTrackConnection, TTrackSection> sectionFinder, INodeFinder<TTrackConnection> nodeFinder)
        {
            foreach (TTrackConnection connection in _outboundConnections)
            {
                TPathfinderEdge edge = new() {Owner = this};
                if (!edge.Fill(connection, sectionFinder, nodeFinder))
                    continue;
                _edges.Add(edge);
                ProcessNewEdge(edge);
            }
        }

        internal virtual void SetReachableNodes(
            Dictionary<PathfinderNodeBase, float> reachableNodes, object edgeSettings)
        {
            _reachableNodes = reachableNodes;
        }

        protected virtual void ProcessNewEdge(TPathfinderEdge edge)
        {
            edge.NextNode.IsReachable = true;
        }
    }
}