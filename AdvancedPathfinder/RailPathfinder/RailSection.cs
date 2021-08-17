using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedPathfinder.Helpers;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.RailPathfinder
{
    public class RailSection
    {
        private const float ClosedBlockMult = 1.5f;
        private const float ClosedBlockPlatformMult = 10f;
        private readonly UniqueList<RailConnection> _connectionList = new ();  //list of first connections of all tracks from start to end
        private readonly UniqueList<RailConnection> _backwardConnectionList = new ();  //list of first connections of all tracks from end to start
        private readonly RailSectionData _data = new();
        private readonly Dictionary<RailBlock, float> _railBlocksLengths = new();
        private readonly Dictionary<RailBlock, int> _railBlocksStates = new(); //0 = free block, otherwise block is blocked
        private float? _closedBlockLength;
        internal float? CachedClosedBlockLength => _closedBlockLength;
        private RailSignal _lastSignalForward;
        private RailSignal _lastSignalBackward;

        public RailSectionData Data => _data;
        public float Length { get; private set; }
        public bool Empty => _connectionList.Count == 0;

        public RailConnection First { get; private set; }
        public RailConnection Last { get; private set; }
        public PathfinderNodeBase ForwardNode { get; private set; }  //nearest node in forward direction (from First to Last)
        public PathfinderNodeBase BackwardNode { get; private set; } //nearest node in backward direction

        public (RailSection section, PathDirection direction)
            ForwardConnectedSection { get; private set; } = (null, default); //next section in forward direction if there is no node on the end of this section (direction is for the next section), filled when edges are being founded
        public (RailSection section, PathDirection direction) BackwardConnectedSection{ get; private set; } = (null, default);

        public float GetClosedBlocksLength()
        {
            if (_closedBlockLength.HasValue)
                return _closedBlockLength.Value;
            
            float result = 0f;
            ImmutableUniqueList<RailConnection> connList = _connectionList.ToImmutableUniqueList();
            foreach (KeyValuePair<RailBlock, float> pair in _railBlocksLengths)
            {
                int blockedCount = SimpleLazyManager<RailBlockHelper>.Current.BlockBlockedCount(pair.Key, connList); 
                if (blockedCount > 0)
                {
                    result += CalculateCloseBlockLength(pair.Value);
                }
//                FileLog.Log($"InitValue, section: {GetHashCode():X8}, block: {pair.Key.GetHashCode():X8}, count: {blockedCount}");

                _railBlocksStates[pair.Key] = blockedCount;
            }

            //FileLog.Log($"Blocked length {result:N1}, ({this.GetHashCode():X8})");
            _closedBlockLength = result;
            Manager<RailPathfinderManager>.Current.MarkClosedSectionsDirty();
            return result;
        }

        public RailSignal GetLastSignal(PathDirection direction)
        {
            return direction == PathDirection.Forward ? _lastSignalForward : _lastSignalBackward;
        }

        public ImmutableUniqueList<RailConnection> GetConnectionList()
        {
            return _connectionList.ToImmutableUniqueList();
        }

        public RailConnection GetEndConnection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? Last : First;
        }

        public RailConnection GetStartConnection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? First : Last;
        }

        public PathfinderNodeBase FindNextNode(RailConnection startConnection)
        {
            ValidateConnectionIsIncluded(startConnection);

            return GetNextNode(GetDirection(startConnection));
        }

        public PathfinderNodeBase GetNextNode(PathDirection direction)
        {
            return direction == PathDirection.Forward ? ForwardNode : BackwardNode;
        }

        public (RailSection section, PathDirection direction) GetNextSection(PathDirection direction)
        {
            return direction == PathDirection.Forward ? ForwardConnectedSection : BackwardConnectedSection;
        }

        public PathDirection GetDirection(RailConnection connection)
        {
            if (_connectionList.Contains(connection))
                return PathDirection.Forward;
            if (_connectionList.Contains(connection.InnerConnection))
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
        public void SetNextSection(RailSection nextSection, PathDirection nextSectionDirection, PathDirection ownDirection)
        {
            (RailSection section, PathDirection direction) newValue =
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
        
        public void GetConnectionsToNextNode(RailConnection startConnection, List<TrackConnection> connections)
        {
            GetConnectionsToNextNodeInternal(GetDirection(startConnection), connections, startConnection);
        }

        public void GetConnectionsToNextNode(PathDirection direction, List<TrackConnection> connections)
        {
            GetConnectionsToNextNodeInternal(direction, connections);
        }
        
        public void GetConnectionsToEnd(RailConnection startConnection, List<TrackConnection> connections)
        {
            (UniqueList<RailConnection> list, int index) = GetConnectionListAndIndex(startConnection);
            AddConnectionsToList(list, connections, index);
        }
        
        public void GetConnectionsInDirection(PathDirection direction, List<TrackConnection> connections)
        {
            AddConnectionsToList(GetConnectionListInternal(direction), connections);
        }

        private void GetConnectionsToNextNodeInternal(PathDirection direction, List<TrackConnection> result, RailConnection startConnection = null)
        {
            if (GetNextNode(direction) == null)
            {
                throw new InvalidOperationException("Next node is null");
            }
            UniqueList<RailConnection> list;
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

        private UniqueList<RailConnection> GetConnectionListInternal(PathDirection direction)
        {
            return direction == PathDirection.Forward ? _connectionList : _backwardConnectionList;
        }

        private (UniqueList<RailConnection> list, int index) GetConnectionListAndIndex(
            RailConnection startConnection)
        {
            int index;
            if ((index = _connectionList.IndexOf(startConnection)) > -1)
            {
                return (_connectionList, index);
            }
            if ((index = _backwardConnectionList.IndexOf(startConnection)) > -1)
            {
                return (_backwardConnectionList, index);
            }

            throw new ArgumentException("Connection in not in the track");
        }

        private void AddConnectionsToList(UniqueList<RailConnection> source, List<TrackConnection> result, int startIndex = 0)
        {
            int count = source.Count;
            for (int i = startIndex; i < count; i++)
            {
                result.Add(source[i]);
            }
        }

        internal void OnSectionRemoved()
        {
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.UnregisterBlockStateAction(block, OnBlockFreeConditionStateChange);
            }
        }

        private bool IsNodeOnTheEnd(RailConnection connection, [CanBeNull] IReadOnlyCollection<RailConnection> stationStopsConnections)
        {
            return stationStopsConnections?.Contains(connection)==true || connection.OuterConnectionCount != 1;
        }
        
        private void FillSetup()
        {
            _connectionList.Clear();
            _backwardConnectionList.Clear();
            Data.Reset();
        }

        private void FinalizeFill()
        {
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.RegisterBlockStateAction(block, OnBlockFreeConditionStateChange);
            }
        }

        private void ProcessTrack(Rail track, RailConnection startConnection)
        {
            _connectionList.Add(startConnection);
            Length += startConnection.Length;
            Data.IsElectrified = track.Electrified;
            Data.LengthAdd = startConnection.Length;
            if (track.Type is RailType.TurnLeft or RailType.TurnRight)
            {
                Data.CurvedLengthAdd = startConnection.Length;
            }
            if (track.SignalCount > 0)
            {
                RailSignal backwardSignal = startConnection.InnerConnection.Signal;
                bool isBackwardSignal = !ReferenceEquals(backwardSignal, null) && backwardSignal.IsBuilt;
                if (!ReferenceEquals(startConnection.Signal, null) && startConnection.Signal.IsBuilt)
                {
                    if (!isBackwardSignal)
                        _data.AllowedDirection = SectionDirection.Forward;
                    _lastSignalForward = startConnection.Signal;
                } else if (isBackwardSignal)
                {
                    _data.AllowedDirection = SectionDirection.Backward;
                }

                if (isBackwardSignal && ReferenceEquals(_lastSignalBackward, null))
                    _lastSignalBackward = backwardSignal;
            }

            if (Data.HasPlatform == false && LazyManager<StationHelper<RailConnection, RailStation>>.Current.IsPlatform(startConnection))
                Data.HasPlatform = true;

            RailBlock block1 = startConnection.Block;
            RailBlock block2 = startConnection.InnerConnection.Block;
            if (ReferenceEquals(block1, null))
                block1 = block2;
            if (!ReferenceEquals(block1, block2))
            {
                float length = startConnection.Length / 2;
                _railBlocksLengths.AddFloatToDict(block1, length);
                _railBlocksLengths.AddFloatToDict(block2, length);
            }
            else
            {
                if (!ReferenceEquals(block1, null))
                    _railBlocksLengths.AddFloatToDict(block1, startConnection.Length);
            }
        }

        internal void AddBlockedRail(Rail rail, int count, RailBlock block)
        {
            if (!_closedBlockLength.HasValue)
                return;
            if (count <= 0)
                throw new ArgumentException("Count must be positive.");
            if (ReferenceEquals(rail, null) || ReferenceEquals(block, null))
                throw new AggregateException("Rail or block cannot be null");
            
            if (_railBlocksStates.TryGetValue(block, out int blockedCount))
            {
                _railBlocksStates[block] = blockedCount + count;
//                FileLog.Log($"AddBlockedRail, section: {GetHashCode():X8}, block: {block.GetHashCode():X8}, count: {count}, origCount: {blockedCount}");

                if (blockedCount == 0)
                {
                    //block is now occupied
                    ChangeBlockStateInternal(block, false);
                }
            }
        }

        internal void ReleaseBlockedRail(Rail rail, int count, RailBlock block)
        {
            if (!_closedBlockLength.HasValue)
                return;
            if (count <= 0)
                throw new ArgumentException("Count must be positive.");
            if (ReferenceEquals(rail, null) || ReferenceEquals(block, null))
                throw new AggregateException("Rail or block cannot be null");


            if (_railBlocksStates.TryGetValue(block, out int blockedCount))
            {
                int newBlockedCount = blockedCount - count;
//                FileLog.Log($"ReleaseBlockedRail, section: {GetHashCode():X8}, block: {block.GetHashCode():X8}, count: {count}, newCount: {newBlockedCount}");

                if (newBlockedCount < 0)
                    throw new InvalidOperationException("Block state count is negative");

                _railBlocksStates[block] = newBlockedCount;
                if (newBlockedCount == 0)
                {
                    //block is now free
                    ChangeBlockStateInternal(block, true);
                }
            }

        }
        private void ValidateConnectionIsIncluded(RailConnection connection)
        {
            if (!_connectionList.Contains(connection) && !_backwardConnectionList.Contains(connection))
            {
                throw new InvalidOperationException("Connection is not in the section or has no valid track");
            }
        }

        private void FillBackwardConnections()
        {
            for (int i = _connectionList.Count - 1; i >= 0; i--)
            {
                _backwardConnectionList.Add(_connectionList[i].InnerConnection);
            }
        }

        private void AddConnectionToProcess(RailConnection connection, HashSet<Rail> processedTracks,
            HashSet<RailConnection> connectionsToProcess)
        {
            if (connection.Track != null && !processedTracks.Contains(connection.Track))
            {
                connectionsToProcess.Add(connection);
            }
        }
        
        internal bool Fill(RailConnection startConnection,[CanBeNull] IReadOnlyCollection<RailConnection> stationStopsConnections, HashSet<Rail> processedTracks, HashSet<RailConnection> connectionsToProcess, HashSet<RailConnection> foundNodesList)
        {
            FillSetup();
            Rail connectionTrack = startConnection.Track;

            if (processedTracks.Contains(connectionTrack))
                return false;

            for (int i = 0; i < startConnection.OuterConnectionCount; i++)
            {
                AddConnectionToProcess((RailConnection)startConnection.OuterConnections[i], processedTracks, connectionsToProcess);
            }

            First = startConnection;
            RailConnection current = startConnection;
            bool isNodeAtStart = !foundNodesList.Contains(startConnection) && IsNodeOnTheEnd(startConnection, stationStopsConnections);
            bool isNodeAtEnd = false;
            RailConnection currentEnd;
            while (true)
            {
                currentEnd = current.InnerConnection;
                Rail track = current.Track;
                if (ReferenceEquals(track, null) || !track.IsBuilt)
                {
//                    FileLog.Log("Invalid track");
                    AdvancedPathfinderMod.Logger.LogError("Invalid track");
                    return false;
                }

                if (!processedTracks.Add(track))
                {
                    //possible circular track, break processing
                    break;
                }
                ProcessTrack(track, current);
                if (IsNodeOnTheEnd(currentEnd, stationStopsConnections))
                {
                    if (ReferenceEquals(currentEnd.Track, null) || !currentEnd.Track.IsBuilt)
                    {
//                        FileLog.Log("End track is null");
                        AdvancedPathfinderMod.Logger.LogError("End track is null");
                    }
                    //node at the end of the connection
                    isNodeAtEnd = true;
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        AddConnectionToProcess((RailConnection) currentEnd.OuterConnections[i], processedTracks, connectionsToProcess);
                    }
                    break;
                }

                current = (RailConnection) currentEnd.OuterConnections[0];
                if (IsNodeOnTheEnd(current, stationStopsConnections))
                {
                    for (int i = 0; i < currentEnd.OuterConnectionCount; i++)
                    {
                        AddConnectionToProcess((RailConnection) currentEnd.OuterConnections[i], processedTracks, connectionsToProcess);
                    }
                    //node in opposite direction = end of the section
                    for (int i = 0; i < current.OuterConnectionCount; i++)
                    {
                        RailConnection conn = (RailConnection) current.OuterConnections[i];
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
                    //FileLog.Log("First.Track == null");
                    AdvancedPathfinderMod.Logger.LogError("First.Track == null");
                }
            }

            if (isNodeAtEnd)
            {
                foundNodesList.Add(Last);
                if (Last.Track == null)
                {
                    //FileLog.Log("Last.Track == null");
                    AdvancedPathfinderMod.Logger.Log("Last.Track == null");
                }
            }

            FillBackwardConnections();
            FinalizeFill();
            return true;
        }

        private void ChangeBlockStateInternal(RailBlock block, bool isOpen)
        {
            if (_railBlocksLengths.TryGetValue(block, out float length))
            {
//                FileLog.Log($"Block state changed section: {GetHashCode():X8}, {block.GetHashCode():X8}: {isOpen}");
                float value = CalculateCloseBlockLength(length);
                if (isOpen)
                    _closedBlockLength -= value;
                else
                    _closedBlockLength += value;
                Manager<RailPathfinderManager>.Current.MarkClosedSectionsDirty();
            }
        }

        internal float CalculateCloseBlockLength(float length)
        {
            return length * (_data.HasPlatform ? ClosedBlockPlatformMult : ClosedBlockMult);
        }

        /** change of one condition of free block */
        private void OnBlockFreeConditionStateChange(RailBlock block, bool oldIsOpen, bool newIsOpen)
        {
//            _closedBlockLength = null;
//            GetClosedBlocksLength();
            if (_closedBlockLength.HasValue && oldIsOpen != newIsOpen && _railBlocksStates.TryGetValue(block, out int blockedCount))
            {
//                FileLog.Log($"OnBlockFreeConditionStateChange, section: {GetHashCode():X8}, block: {block.GetHashCode():X8}, IsOpen: {newIsOpen}, origBlockedCount: {blockedCount}");
                bool oldState = blockedCount == 0;
                
                blockedCount += newIsOpen ? -1 : 1;
                if (blockedCount < 0)
                {
                    throw new InvalidOperationException("Block blocked value is less than 0");
                }
                _railBlocksStates[block] = blockedCount;

                bool newState = blockedCount == 0;
                if (oldState != newState)
                {
                    ChangeBlockStateInternal(block, newState);
                }
            }
        }
    }
}