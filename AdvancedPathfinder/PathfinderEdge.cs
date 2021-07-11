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
        public ImmutableList<(TTrackSection section, PathDirection direction)> Sections => _sections.ToImmutableList();
        public float Length { get; private set; }

        private readonly List<(TTrackSection section, PathDirection direction)> _sections = new ();

        protected virtual void AddSection(TTrackSection section, PathDirection direction)
        {
            _sections.Add((section, direction));
            Length += section.Length;
        }
        
        internal void Fill(TTrackConnection startConnection, ISectionFinder<TTrack, TTrackConnection, TTrackSection> sectionFinder)
        {
            TTrackConnection  currentConnection = startConnection;
            while (currentConnection != null)
            {
                TTrackSection section = sectionFinder.FindSection(currentConnection);
                PathDirection direction =
                    section.First == currentConnection ? PathDirection.Forward : section.Last == currentConnection ? PathDirection.Backward : throw new InvalidOperationException("connection is not on the any end of the section");

                AddSection(section, direction);
                TTrackConnection lastConn = section.GetEndConnection(direction);
                if (lastConn.OuterConnectionCount != 1)
                {
                    //new node or end of the track
                    break;
                }

                currentConnection = (TTrackConnection)lastConn.OuterConnections[0];
            }
        }
    }
}