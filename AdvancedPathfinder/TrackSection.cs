using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
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
        protected virtual bool IsNodeOnTheEnd(TTrackConnection connection, [CanBeNull] IReadOnlyCollection<TTrackConnection> stationStopsConnections)
        {
            return stationStopsConnections?.Contains(connection)==true || connection.OuterConnectionCount != 1;
        }
        
        internal bool Fill(TTrackConnection startConnection,[CanBeNull] IReadOnlyCollection<TTrackConnection> stationStopsConnections, HashSet<TTrack> processedTracks, HashSet<TTrackConnection> connectionsToProcess, HashSet<TTrackConnection> foundNodesList)
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
            bool isNodeAtStart = !foundNodesList.Contains(startConnection) && IsNodeOnTheEnd(startConnection, stationStopsConnections);
            bool isNodeAtEnd = false;
            TTrackConnection currentEnd = null;
            while (true)
            {
                currentEnd = (TTrackConnection) current.InnerConnection;
                TTrack track = (TTrack) current.Track;
                if (track == null || !track.IsBuilt)
                {
                    FileLog.Log("Invalid track");
                    return false;
                }

                if (!processedTracks.Add(track))
                {
                    FileLog.Log("Failed track");
                    return false;
                }
                ProcessTrack(track, current);
                if (IsNodeOnTheEnd(currentEnd, stationStopsConnections))
                {
                    if (currentEnd.Track == null)
                    {
                        FileLog.Log("End track is null");
                    }
                    //node at the end of the connection
                    isNodeAtEnd = true;
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        connectionsToProcess.Add((TTrackConnection) currentEnd.OuterConnections[i]);
                    }
                    break;
                }

                current = (TTrackConnection) currentEnd.OuterConnections[0];
                if (IsNodeOnTheEnd(current, stationStopsConnections))
                {
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        connectionsToProcess.Add((TTrackConnection) currentEnd.OuterConnections[i]);
                    }
                    //node in opposite direction = end of the section
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
            if (Empty) return false;

            if (isNodeAtStart)
            {
                foundNodesList.Add(First);
                if (First.Track == null)
                {
                    FileLog.Log("First.Track == null");
                }

                if (!TrackList.Contains((TTrack)First.Track))
                {
                    FileLog.Log("First.Track not in list");
                }
            }

            if (isNodeAtEnd)
            {
                foundNodesList.Add(Last);
                if (Last.Track == null)
                {
                    FileLog.Log("Last.Track == null");
                }
                if (!TrackList.Contains((TTrack)Last.Track))
                {
                    FileLog.Log("Last.Track not in list");
                }
            }

            return true;
        }
    }
}