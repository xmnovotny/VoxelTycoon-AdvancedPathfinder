using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;
using ExceptionHelper = VoxelTycoon.ExceptionHelper;

namespace AdvancedPathfinder.PathSignals
{
    public class PathRailBlockData: RailBlockData
    {
        public readonly HashSet<RailSignal> OutboundSignals = new();
        private readonly Dictionary<Rail, int> _blockedRails = new();
        private readonly Dictionary<Rail, int> _blockedLinkedRails = new();
        private int _lastPathIndex = 0;
        private RailSignal _lastEndSignal = null;

        private readonly Dictionary<Train, (PooledHashSet<Rail> rails, Rail lastPathRail)> _reservedBeyondPath = new(); //only rails in possible path, not linked rails
        private readonly Dictionary<Train, PooledHashSet<Rail>> _reservedTrainPath = new(); //only rails in train path, not linked rails

        internal IReadOnlyDictionary<Rail, int> BlockedLinkedRails => _blockedLinkedRails;
        internal IReadOnlyDictionary<Rail, int> BlockedRails => _blockedRails;
        internal IReadOnlyDictionary<Train, (PooledHashSet<Rail> rails, Rail lastPathRail)> ReservedBeyondPath => _reservedBeyondPath; //only rails in possible path, not linked rails

        public PathRailBlockData([NotNull] RailBlock block): base(block)
        {
        }

        public SimpleRailBlockData ToSimpleBlockData()
        {
            return new (this);
        }

        // ReSharper restore Unity.PerformanceCriticalContext
        internal override void ReleaseRailSegment(Train train, Rail rail)
        {
//            FileLog.Log($"ReleaseSegmentStart, rail: {rail.GetHashCode():X8} block: {GetHashCode():X}");
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> reservedList))
            {
                TryFreeFullBlock();
                return;
            }

            if (reservedList.Remove(rail))
            {
                ReleaseRailSegmentInternal(rail);
//                FileLog.Log($"ReleaseSegmentSuccess, rail: {rail.GetHashCode():X8} block: {GetHashCode():X}");
                ReleaseInboundSignal(train, rail);
            }

            if (reservedList.Count == 0)
            {
                _reservedTrainPath.Remove(train);
                reservedList.Dispose();
                ReleaseBeyondPath(train);
                TryFreeFullBlock();
            }
        }

        internal void ReleaseBeyondPath(Train train)
        {
            if (!_reservedBeyondPath.TryGetValue(train, out (PooledHashSet<Rail> rails, Rail lastPathRail) data))
                return;
            
            foreach (Rail rail in data.rails)
            {
                ReleaseRailSegmentInternal(rail);
            }

            _reservedBeyondPath.Remove(train);
            data.rails.Dispose();
        }

        // ReSharper restore Unity.PerformanceCriticalContext
        internal override bool TryReservePath(Train train, PathCollection path, int startIndex, out int reservedIndex)
        {
//            FileLog.Log($"Try reserve path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            reservedIndex = 0;
            if (IsFullBlocked)
                return false;
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.IsChainSignal)
            {
//                FileLog.Log($"TryReservePath ChainSignal: {GetHashCode():X}");
                using PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railCache = PooledList<(Rail, bool, bool)>.Take();
                if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                    return false;
                if (!ReferenceEquals(_lastEndSignal, null))
                {
                    PathSignalData nextSignalData = SimpleManager<PathSignalManager>.SafeCurrent.GetPathSignalData(_lastEndSignal);
                    RailBlockData nextBlockData = nextSignalData.BlockData;
                    if (nextBlockData == this)
                        throw new InvalidOperationException("Next block in the path is the same block");
                    if (!nextBlockData.TryReservePath(train, path, _lastPathIndex, out reservedIndex))
                        return false;
                }
                ReserveOwnPathInternal(train, railCache, startSignalData);
                return true;
            }

            if (TryReserveOwnPath(train, path, startIndex, startSignalData))
            {
                reservedIndex = Math.Min(_lastPathIndex, path.FrontIndex);
                return true;
            }

            return false;
        }

        // ReSharper restore Unity.PerformanceCriticalContext
        /**
         * Only for reservation path after stop (=with reserved beyond path)
         * Will reserve path within this block from rearbound to the end of the block
         * @return path index of last reserved track 
         */
        internal int? TryReserveUpdatedPathInsteadOfBeyond(Train train, PathCollection path)
        {
            if (!_reservedBeyondPath.TryGetValue(train,
                out (PooledHashSet<Rail> rails, Rail lastPathRail) beyondPathList)) return null;
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> reservedPath))
                return null;
            
            int idx = train.RearBound.ConnectionIndex;
            RailConnection connection = (RailConnection)path[idx];
            while (connection != null && connection.Block != Block && idx<path.FrontIndex)
            {
                connection = (RailConnection)path[++idx];
            }

            if (connection == null || connection.Block != Block)
                return null;

            using PooledList<RailConnection> connections = PooledList<RailConnection>.Take();
            while (connection != null && connection.Block == Block && idx<path.FrontIndex)
            {
                connections.Add(connection);
                connection = (RailConnection)path[++idx];
            }

            if (idx==path.FrontIndex || connection == null)
                return null; //no new block = path is not to the end of the own block

            if (ReferenceEquals(connection.InnerConnection.Block, Block))
            {
                //reserve last segment of path (it has right block on the inner connection)
                connections.Add(connection);
            } 
            ReleaseBeyondPath(train);

            foreach (RailConnection railConnection in connections)
            {
                Rail rail = railConnection.Track;
                if (!ReferenceEquals(rail, null) && rail.IsBuilt && !reservedPath.Contains(rail))
                {
                    reservedPath.Add(rail);
                    _blockedRails.AddIntToDict(rail, 1);
                    for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
                    {
                        Rail linkedRail = rail.GetLinkedRail(i);
                        _blockedLinkedRails.AddIntToDict(linkedRail, 1);
                    }
                }
            }

            return idx-1;
        }

        private void ReleaseRailSegmentInternal(Rail rail)
        {
            _blockedRails.SubIntFromDict(rail, 1, 0);
            for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
            {
                _blockedLinkedRails.SubIntFromDict(rail.GetLinkedRail(i), 1, 0);
            }
        }
        
        private bool TryReserveOwnPath([NotNull] Train train, [NotNull] PathCollection path, int startIndex, PathSignalData startSignalData)
        {
//            FileLog.Log($"TryReserveOwnPath: {GetHashCode():X}");
            using PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railCache = PooledList<(Rail, bool, bool)>.Take();
            if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                return false;

            ReserveOwnPathInternal(train, railCache, startSignalData);
            return true;
        }
        
        private bool CanReserveOwnPath([NotNull] PathCollection path, int startIndex, PathSignalData startSignalData, PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> cacheList = null)
        {
            if (!ReferenceEquals(startSignalData.ReservedForTrain, null))
            {
                return false;
            }
            foreach ((Rail rail, bool isLinkedRail, bool beyondPath) in AffectedRailsEnum(path, startIndex))
            {
                if (_blockedRails.TryGetValue(rail, out int value) && value > 0 || (!isLinkedRail && _blockedLinkedRails.TryGetValue(rail, out int value2) && value2 > 0))
                {
                    //rail in the path is blocked by another reserved path
                    return false;
                }

                cacheList?.Add((rail, isLinkedRail, beyondPath));
            }

            return true;
        }

        private void ReserveOwnPathInternal(Train train, PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railsToBlock, PathSignalData startSignal)
        {
//            FileLog.Log($"ReserveOwnPath: {GetHashCode():X}");
//            FileLog.Log($"Reserve own path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            Rail lastRail = null;
            PooledHashSet<Rail> beyondRails = null;
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> trainRails))
                trainRails = PooledHashSet<Rail>.Take();

            try
            {
                foreach ((Rail rail, bool isLinkedRail, bool beyondPath) in railsToBlock)
                {
                    if (!beyondPath)
                    {
                        lastRail = rail;
                        if (!isLinkedRail)
                        {
                            if (!trainRails.Add(rail))
                            {
                                throw new InvalidOperationException("Already reserved segment");
                            }
                        }
                    }
                    else
                    {
                        if (beyondRails == null)
                        {
                            if (_reservedBeyondPath.ContainsKey(train))
                                throw new InvalidOperationException("Already defined beyond rail path for the train");
                            beyondRails = PooledHashSet<Rail>.Take();
                        }

                        if (!isLinkedRail)
                            beyondRails.Add(rail);
                    }
                    if (!isLinkedRail)
                        _blockedRails.AddIntToDict(rail, 1);
                    else
                        _blockedLinkedRails.AddIntToDict(rail, 1);
                }

                if (beyondRails != null)
                {
//                    FileLog.Log("FoundBeyondPath");
                    _reservedBeyondPath.Add(train, (beyondRails, lastRail));
                }
                _reservedTrainPath[train] = trainRails;
                startSignal.ReservedForTrain = train;
            }
            catch (Exception e) when (ExceptionFault.FaultBlock(e, delegate
            {
                if (trainRails != null && !_reservedTrainPath.ContainsKey(train))
                    trainRails?.Dispose();
                beyondRails?.Dispose();
            }))
            {}
        }
        
        private IEnumerable<(Rail rail, bool isLinkedRail, bool beyondPath)> AffectedRailsEnum(PathCollection path, int startIndex)
        {
            int index = startIndex + 1;
            _lastPathIndex = index;
            _lastEndSignal = null;
            RailConnection connection = (RailConnection)path[startIndex];
            if (connection.InnerConnection.Block != Block)
                throw new InvalidOperationException("Connection is from another block");
            yield return (connection.Track, false, false); //track with the inbound signal - we reserve only this track, not linked
            Rail lastRail = null;
            while (index <= path.FrontIndex)
            {
                connection = (RailConnection) path[index];
                Rail rail = connection.Track;
                if (rail.IsBuilt)
                {
                    lastRail = rail;
                    if (connection.Block != Block)
                        throw new InvalidOperationException("Connection is from another block");
                    yield return (rail, false, false);
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return (rail.GetLinkedRail(j), true, false);
                    }

                    if (index != startIndex && (!ReferenceEquals(connection.Signal, null) && connection.Signal.IsBuilt || 
                        !ReferenceEquals(connection.InnerConnection.Signal, null) && connection.InnerConnection.Signal.IsBuilt))
                    {
                        _lastEndSignal = connection.Signal;
                        yield break;
                    }
                }

                _lastPathIndex = ++index;
            }

            if (connection == null)
                throw new InvalidOperationException("No available connection in the provided path");

                //end of the path, but no signal found (=need to go through all possible connections until signal is found) 
            using PooledList<RailConnection> connections = PooledList<RailConnection>.Take();
            connections.Add(connection);
            for (int i = 0; i < connections.Count; i++)
            {
                connection = connections[i];
                foreach (TrackConnection trackConnection in connection.InnerConnection.OuterConnections)
                {
                    RailConnection outerConnection = (RailConnection) trackConnection;
                    if (!ReferenceEquals(outerConnection.Signal, null) && outerConnection.Signal.IsBuilt 
                        || !ReferenceEquals(outerConnection.InnerConnection.Signal, null) && outerConnection.InnerConnection.Signal.IsBuilt)
                        continue;
                    Rail rail = outerConnection.Track;
                    yield return (rail, false, true);
                    connections.Add(outerConnection);
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return (rail.GetLinkedRail(j), true, true);
                    }
                }
            }
        }
    }
}