using System;
using System.Collections.Generic;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public abstract class PathfinderEdge<TTrack, TTrackConnection, TTrackSection>: PathfinderEdgeBase
        where TTrack : Track
        where TTrackConnection : TrackConnection
        where TTrackSection : TrackSection<TTrack, TTrackConnection>
    {
        public PathfinderNodeBase Owner { get; init; }
        public IReadOnlyList<(TTrackSection section, PathDirection direction)> Sections => _sections;
        public float Length { get; private set; }

        private readonly List<(TTrackSection section, PathDirection direction)> _sections = new ();

        public void GetConnections(List<TrackConnection> connections)
        {
            foreach ((TTrackSection section, PathDirection direction) in _sections)
            {
                section.GetConnectionsInDirection(direction, connections);
            }            
        }
        
        protected virtual void AddSection(TTrackSection section, PathDirection direction)
        {
            _sections.Add((section, direction));
            Length += section.Length;
        }

        internal override float GetScore(object edgeSettings)
        {
            return Length;
        }

        internal void Fill(TTrackConnection startConnection, ISectionFinder<TTrack, TTrackConnection, TTrackSection> sectionFinder, INodeFinder<TTrackConnection> nodeFinder)
        {
            TTrackConnection  currentConnection = startConnection;
            NextNode = null;
            TTrackSection lastSection = null;
            PathDirection lastDirection = default;
            while (currentConnection != null)
            {
                TTrackSection section = sectionFinder.FindSection(currentConnection);
                PathDirection direction =
                    section.First == currentConnection ? PathDirection.Forward : section.Last == currentConnection ? PathDirection.Backward : throw new InvalidOperationException("connection is not on the any end of the section");

                AddSection(section, direction);
                if (lastSection != null)
                {
                    lastSection.SetNextSection(section, direction, lastDirection);
                }
                TTrackConnection lastConn = section.GetEndConnection(direction);
                if ((NextNode = nodeFinder.FindNodeByInboundConn(lastConn)) != null)
                {
                    SetNextNodeInSections();

                    //new node or end of the track
                    break;
                }

                lastSection = section;
                lastDirection = direction;
                currentConnection = (TTrackConnection)lastConn.OuterConnections[0];
            }
            UpdateSections();
        }

        private void SetNextNodeInSections()
        {
            foreach ((TTrackSection section, PathDirection direction) in _sections)
            {
                section.SetNextNode(NextNode, direction);
            }
        }

        protected abstract void UpdateSections();
    }
}