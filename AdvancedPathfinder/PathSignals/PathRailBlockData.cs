using System;
using System.Collections.Generic;
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
            return new (Block, InboundSignals);
        }

        internal override void ReleaseRailSegment(Train train, Rail rail)
        {
//            FileLog.Log($"ReleaseSegmentStart, block: {GetHashCode():X}");
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> reservedList)) 
                return;
            
            if (reservedList.Remove(rail))
            {
//                FileLog.Log($"ReleaseSegmentSuccess, block: {GetHashCode():X}");
                ReleaseRailSegmentInternal(rail);
                ReleaseInboundSignal(train, rail);
            }

            if (reservedList.Count == 0)
            {
                _reservedTrainPath.Remove(train);
                reservedList.Dispose();
                ReleaseBeyondPath(train);
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

        internal override bool TryReservePath(Train train, PathCollection path, int startIndex, out int reservedIndex)
        {
//            FileLog.Log($"TryReservePath: {GetHashCode():X}");
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            reservedIndex = 0;
            if (startSignalData.IsChainSignal)
            {
//                FileLog.Log($"TryReservePath ChainSignal: {GetHashCode():X}");
                using PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railCache = PooledList<(Rail, bool, bool)>.Take();
                if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                    return false;
                if (_lastEndSignal != null)
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
                reservedIndex = _lastPathIndex;
                return true;
            }

            return false;
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
            if (startSignalData.ReservedForTrain != null)
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
                    FileLog.Log("FoundBeyondPath");
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
            RailConnection connection = null;
            Rail lastRail = null;
            while (index <= path.FrontIndex)
            {
                connection = (RailConnection) path[index];
                Rail rail = connection.Track;
                if (rail == lastRail)
                    FileLog.Log("LastRail = rail");
                lastRail = rail;
                if (connection.Block != Block)
                    throw new InvalidOperationException("Connection is from another block");
                yield return (rail, false, false);
                for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                {
                    yield return (rail.GetLinkedRail(j), true, false);
                }

                if (connection.Signal != null || connection.InnerConnection.Signal != null)
                {
                    _lastEndSignal = connection.Signal;
                    yield break;
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
                    if (outerConnection.Signal != null || outerConnection.InnerConnection.Signal != null)
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