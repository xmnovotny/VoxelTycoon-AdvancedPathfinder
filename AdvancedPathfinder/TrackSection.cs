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
/*        protected readonly UniqueList<TTrack> TrackList = new UniqueList<TTrack>();*/
        protected readonly UniqueList<TTrackConnection> ConnectionList = new ();  //list of first connections of all tracks from start to end
        protected readonly UniqueList<TTrackConnection> BackwardConnectionList = new ();  //list of first connections of all tracks from end to start
        public float Length { get; protected set; }
        public bool Empty => ConnectionList.Count == 0;

        public TTrackConnection First { get; private set; }
        public TTrackConnection Last { get; private set; }
        public PathfinderNodeBase ForwardNode { get; private set; }  //nearest node in forward direction (from First to Last)
        public PathfinderNodeBase BackwardNode { get; private set; } //nearest node in backward direction

        [CanBeNull]
        public (TrackSection<TTrack, TTrackConnection> section, PathDirection direction)
            ForwardConnectedSection { get; private set; } = (null, default); //next section in forward direction if there is no node on the end of this section (direction is for the next section), filled when edges are being founded
        [CanBeNull]
        public (TrackSection<TTrack, TTrackConnection> section, PathDirection direction) BackwardConnectedSection{ get; private set; } = (null, default);

        public TrackSection()
        {
        }

        public ImmutableUniqueList<TTrackConnection> GetConnectionList()
        {
            return ConnectionList.ToImmutableUniqueList();
        }

        public TTrackConnection GetEndConnection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? Last : First;
        }

        public TTrackConnection GetStartConnection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? First : Last;
        }

        public PathfinderNodeBase FindNextNode(TTrackConnection startConnection)
        {
            ValidateConnectionIsIncluded(startConnection);

            return GetNextNode(GetDirection(startConnection));
        }

        public PathfinderNodeBase GetNextNode(PathDirection direction)
        {
            return direction == PathDirection.Forward ? ForwardNode : BackwardNode;
        }

        public (TrackSection<TTrack, TTrackConnection> section, PathDirection direction) GetNextSection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? ForwardConnectedSection : BackwardConnectedSection;
        }

        public PathDirection GetDirection(TTrackConnection connection)
        {
            if (ConnectionList.Contains(connection))
                return PathDirection.Forward;
            if (ConnectionList.Contains((TTrackConnection) connection.InnerConnection))
                return PathDirection.Backward;
            throw new InvalidOperationException("Connection is not in the section");
        }

        public void SetNextNode(PathfinderNodeBase nextNode, PathDirection direction)
        {
            if (direction == PathDirection.Forward)
            {
                if (ForwardNode != null && ForwardNode != nextNode)
                    throw new InvalidOperationException("Attempt to change already set forward node");
                ForwardNode = nextNode;
            }
            else
            {
                if (BackwardNode != null && BackwardNode != nextNode)
                    throw new InvalidOperationException("Attempt to change already set backward node");
                BackwardNode = nextNode;
            }
        }
        public void SetNextSection(TrackSection<TTrack, TTrackConnection> nextSection, PathDirection nextSectionDirection, PathDirection ownDirection)
        {
            (TrackSection<TTrack, TTrackConnection> section, PathDirection direction) newValue =
                new(nextSection, nextSectionDirection);
            if (ownDirection == PathDirection.Forward)
            {
                if (ForwardConnectedSection.section != null && ForwardConnectedSection != newValue)
                    throw new InvalidOperationException("Attempt to change already set forward next section");
                ForwardConnectedSection = newValue;
            }
            else
            {
                if (BackwardConnectedSection.section != null && BackwardConnectedSection != newValue)
                    throw new InvalidOperationException("Attempt to change already set backward next section");
                BackwardConnectedSection = newValue;
            }
        }
        
        public void GetConnectionsToNextNode(TTrackConnection startConnection, List<TrackConnection> connections)
        {
            GetConnectionsToNextNodeInternal(GetDirection(startConnection), connections, startConnection);
        }

        public void GetConnectionsToNextNode(PathDirection direction, List<TrackConnection> connections)
        {
            GetConnectionsToNextNodeInternal(direction, connections);
        }
        
        public void GetConnectionsToEnd(TTrackConnection startConnection, List<TrackConnection> connections)
        {
            (UniqueList<TTrackConnection> list, int index) = GetConnectionListAndIndex(startConnection);
            AddConnectionsToList(list, connections, index);
        }
        
        public void GetConnectionsInDirection(PathDirection direction, List<TrackConnection> connections)
        {
            AddConnectionsToList(GetConnectionListInternal(direction), connections);
        }

        protected void GetConnectionsToNextNodeInternal(PathDirection direction, List<TrackConnection> result, TTrackConnection startConnection = null)
        {
            if (GetNextNode(direction) == null)
            {
                throw new InvalidOperationException("Next node is null");
            }
            UniqueList<TTrackConnection> list = null;
            int index = 0;
            if (startConnection != null)
            {
                (list, index) = GetConnectionListAndIndex(startConnection);
            }
            else
            {
                list = GetConnectionListInternal(direction);
            }
            AddConnectionsToList(list, result, index);
            (var nextSection, PathDirection nextDirection) = GetNextSection(direction);
            if (nextSection != null)
            {
                nextSection.GetConnectionsToNextNodeInternal(nextDirection, result);
            }
        }

        protected UniqueList<TTrackConnection> GetConnectionListInternal(PathDirection direction)
        {
            return direction == PathDirection.Forward ? ConnectionList : BackwardConnectionList;
        }

        protected (UniqueList<TTrackConnection> list, int index) GetConnectionListAndIndex(
            TTrackConnection startConnection)
        {
            int index;
            if ((index = ConnectionList.IndexOf(startConnection)) > -1)
            {
                return (ConnectionList, index);
            }
            if ((index = BackwardConnectionList.IndexOf(startConnection)) > -1)
            {
                return (BackwardConnectionList, index);
            }

            throw new ArgumentException("Connection in not in the track");
        }

        private void AddConnectionsToList(UniqueList<TTrackConnection> source, List<TrackConnection> result, int startIndex = 0)
        {
            int count = source.Count;
            for (int i = startIndex; i < count; i++)
            {
                result.Add(source[i]);
            }
        }
        
        protected virtual void ProcessTrack(TTrack track, TTrackConnection startConnection)
        {
            ConnectionList.Add(startConnection);
            Length += startConnection.Length;
        }

        protected virtual void FillSetup()
        {
            ConnectionList.Clear();
            BackwardConnectionList.Clear();
        }
        protected virtual bool IsNodeOnTheEnd(TTrackConnection connection, [CanBeNull] IReadOnlyCollection<TTrackConnection> stationStopsConnections)
        {
            return stationStopsConnections?.Contains(connection)==true || connection.OuterConnectionCount != 1;
        }

        private void ValidateConnectionIsIncluded(TTrackConnection connection)
        {
            if (!ConnectionList.Contains(connection) && !BackwardConnectionList.Contains(connection))
            {
                throw new InvalidOperationException("Connection is not in the section or has no valid track");
            }
        }

        private void FillBackwardConnections()
        {
            for (int i = ConnectionList.Count - 1; i >= 0; i--)
            {
                BackwardConnectionList.Add((TTrackConnection)ConnectionList[i].InnerConnection);
            }
        }

        private void AddConnectionToProcess(TTrackConnection connection, HashSet<TTrack> processedTracks,
            HashSet<TTrackConnection> connectionsToProcess)
        {
            if (connection.Track != null && !processedTracks.Contains(connection.Track))
            {
                connectionsToProcess.Add(connection);
            }
        }
        
        internal bool Fill(TTrackConnection startConnection,[CanBeNull] IReadOnlyCollection<TTrackConnection> stationStopsConnections, HashSet<TTrack> processedTracks, HashSet<TTrackConnection> connectionsToProcess, HashSet<TTrackConnection> foundNodesList)
        {
            if (startConnection.Track is not TTrack)
                throw new ArgumentException("Connection track is not type of TTrack");

            if (processedTracks.Contains((TTrack) startConnection.Track))
                return false;

            for (int i = 0; i < startConnection.OuterConnectionCount; i++)
            {
                AddConnectionToProcess((TTrackConnection)startConnection.OuterConnections[i], processedTracks, connectionsToProcess);
            }

            First = startConnection;
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
                        AddConnectionToProcess((TTrackConnection) currentEnd.OuterConnections[i], processedTracks, connectionsToProcess);
                    }
                    break;
                }

                current = (TTrackConnection) currentEnd.OuterConnections[0];
                if (IsNodeOnTheEnd(current, stationStopsConnections))
                {
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        AddConnectionToProcess((TTrackConnection) currentEnd.OuterConnections[i], processedTracks, connectionsToProcess);
                    }
                    //node in opposite direction = end of the section
                    for (int i = 0; i < current.OuterConnectionCount; i++)
                    {
                        TTrackConnection conn = (TTrackConnection) current.OuterConnections[i];
                        if (conn != currentEnd)
                        {
                            AddConnectionToProcess(conn, processedTracks, connectionsToProcess);
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
            }

            if (isNodeAtEnd)
            {
                foundNodesList.Add(Last);
                if (Last.Track == null)
                {
                    FileLog.Log("Last.Track == null");
                }
            }

            FillBackwardConnections();
            return true;
        }
    }
}