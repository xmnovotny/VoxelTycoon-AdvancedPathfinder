using System;
using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public class TrackSection<TTrack, TTrackConnection> where TTrack: Track where TTrackConnection: TrackConnection
    {
        protected readonly UniqueList<TTrack> TrackList = new UniqueList<TTrack>();
        internal bool IsListReversed {get; private protected set;}
        public float Length { get; protected set; }
        public bool Empty => TrackList.Count == 0;

        public TTrackConnection First { get; private set; }
        public TTrackConnection Last { get; private set; }
        
        public TrackSection()
        {
        }

        public ImmutableUniqueList<TTrack> GetTrackList()
        {
            return TrackList.ToImmutableUniqueList();
        }

        public TTrackConnection GetEndConnection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? Last : First;
        }
        
        protected virtual void ProcessTrack(TTrack track, TTrackConnection startConnection)
        {
            TrackList.Add(track);
            Length += startConnection.Length;
        }

        protected virtual void FillSetup()
        {
            TrackList.Clear();
        }

        internal bool Fill(TTrackConnection startConnection, HashSet<TTrack> processedTracks, HashSet<TTrackConnection> connectionsToProcess)
        {
            if (startConnection.Track is not TTrack)
                throw new ArgumentException("Connection track is not type of TTrack");

            if (processedTracks.Contains((TTrack) startConnection.Track))
                return false;

            for (int i = 0; i < startConnection.OuterConnectionCount; i++)
            {
                TrackConnection conn = startConnection.OuterConnections[i];
                if (!processedTracks.Contains((TTrack)conn.Track))
                {
                    connectionsToProcess.Add((TTrackConnection) conn);
                }
            }

            First = startConnection;
            IsListReversed = !startConnection.IsStart;
            TTrackConnection current = startConnection;
            TTrackConnection currentEnd = null;
            while (true)
            {
                currentEnd = (TTrackConnection) current.InnerConnection;
                TTrack track = (TTrack) current.Track;
                if (track == null || !track.IsBuilt)
                    break;
                processedTracks.Add(track);
                ProcessTrack(track, current);
                if (currentEnd.OuterConnectionCount != 1)
                {
                    //0 or more than one connection = end of the section
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        connectionsToProcess.Add((TTrackConnection) currentEnd.OuterConnections[i]);
                    }
                    break;
                }

                current = (TTrackConnection) currentEnd.OuterConnections[0];
                if (current.OuterConnectionCount > 1)
                {
                    //more than 1 connection on the next connection = end of the section
                    for (int i = 0; i < current.OuterConnectionCount; i++)
                    {
                        TTrackConnection conn = (TTrackConnection) current.OuterConnections[i];
                        if (conn != currentEnd)
                        {
                            connectionsToProcess.Add(conn);
                        }
                    }
                    break;
                }
            }

            Last = currentEnd;
            return !Empty;
        }
    }
}