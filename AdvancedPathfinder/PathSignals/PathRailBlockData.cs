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
        private int _lastPathIndex;
        private RailSignal _lastEndSignal;

        private readonly Dictionary<Train, (PooledHashSet<Rail> rails, Rail lastPathRail)> _reservedBeyondPath = new(); //only rails in possible path, not linked rails
        private readonly Dictionary<Train, PooledHashSet<Rail>> _reservedTrainPath = new(); //only rails in train path, not linked rails

        internal IReadOnlyDictionary<Rail, int> BlockedLinkedRails => _blockedLinkedRails;
        internal IReadOnlyDictionary<Rail, int> BlockedRails => _blockedRails;
        internal IReadOnlyDictionary<Train, (PooledHashSet<Rail> rails, Rail lastPathRail)> ReservedBeyondPath => _reservedBeyondPath; //only rails in possible path, not linked rails
        internal bool IsSomeReservedPath => _reservedTrainPath.Count > 0;

        public PathRailBlockData([NotNull] RailBlock block): base(block)
        {
        }

        public SimpleRailBlockData ToSimpleBlockData()
        {
            return new (this);
        }

        /** get a sum of blocked rails from provided connection list */
        internal int GetBlockedRailsSum(ImmutableUniqueList<RailConnection> connections)
        {
            int result = 0;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                RailConnection connection = connections[i];
                if (!ReferenceEquals(connection.Block, Block) && !ReferenceEquals(connection.InnerConnection.Block, Block)) //rail is not from this block
                    continue;
                Rail rail = connection.Track;
                result += _blockedRails.GetValueOrDefault(rail) + _blockedLinkedRails.GetValueOrDefault(rail);
            }

            return result;
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

            using PooledDictionary<Rail, int> releasedRailsSum = PooledDictionary<Rail, int>.Take();
            if (reservedList.Remove(rail))
            {
                ReleaseRailSegmentInternal(rail, releasedRailsSum);
//                FileLog.Log($"ReleaseSegmentSuccess, rail: {rail.GetHashCode():X8} block: {GetHashCode():X}");
                ReleaseInboundSignal(train, rail);
            }

            if (reservedList.Count == 0)
            {
                _reservedTrainPath.Remove(train);
                reservedList.Dispose();
                ReleaseBeyondPath(train, releasedRailsSum);
                TryFreeFullBlock();
            }

            if (releasedRailsSum.Count > 0)
            {
                SimpleLazyManager<RailBlockHelper>.Current.ReleaseBlockedRails(releasedRailsSum, Block);
            }
        }

        internal void ReleaseBeyondPath(Train train, PooledDictionary<Rail, int> releasedRailsSum)
        {
            if (!_reservedBeyondPath.TryGetValue(train, out (PooledHashSet<Rail> rails, Rail lastPathRail) data))
                return;
            
            foreach (Rail rail in data.rails)
            {
                ReleaseRailSegmentInternal(rail, releasedRailsSum);
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
                using PooledList<RailToBlock> railCache = PooledList<RailToBlock>.Take();
                if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                    return false;
                if (!ReferenceEquals(_lastEndSignal, null))
                {
                    // ReSharper disable once PossibleNullReferenceException
                    if (SimpleManager<PathSignalManager>.Current.ShouldUpdatePath(train, _lastEndSignal))
                        return false;
                    PathSignalData nextSignalData = SimpleManager<PathSignalManager>.SafeCurrent.GetPathSignalData(_lastEndSignal);
                    if (nextSignalData == null) //no signal data, probably network was changed, and in the next update cycle it will be available 
                        return false;
                    if (!ReferenceEquals(nextSignalData.ReservedForTrain, train)) //if path is reserved for this train, we can stop finding following chain signals
                    {
                        RailBlockData nextBlockData = nextSignalData.BlockData;
                        if (nextBlockData == this)
                            throw new InvalidOperationException("Next block in the path is the same block");
                        if (!nextBlockData.TryReservePath(train, path, _lastPathIndex, out reservedIndex))
                            return false;
                    }
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
            if (!_reservedBeyondPath.ContainsKey(train)) 
                return null;
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

            using PooledDictionary<Rail, int> releasedRailsSum = PooledDictionary<Rail, int>.Take();
            using PooledDictionary<Rail, int> blockedRailsSum = PooledDictionary<Rail, int>.Take();
            ReleaseBeyondPath(train, releasedRailsSum);

            foreach (RailConnection railConnection in connections)
            {
                Rail rail = railConnection.Track;
                if (!ReferenceEquals(rail, null) && rail.IsBuilt && !reservedPath.Contains(rail))
                {
                    reservedPath.Add(rail);
                    _blockedRails.AddIntToDict(rail, 1);
                    if (!releasedRailsSum.TrySubIntFromDict(rail, 1, 0, true))
                        blockedRailsSum.AddIntToDict(rail, 1);
                    for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
                    {
                        Rail linkedRail = rail.GetLinkedRail(i);
                        _blockedLinkedRails.AddIntToDict(linkedRail, 1);
                        if (!releasedRailsSum.TrySubIntFromDict(linkedRail, 1, 0, true))
                            blockedRailsSum.AddIntToDict(linkedRail, 1);
                    }
                }
            }
            
            if (blockedRailsSum.Count > 0)
                SimpleLazyManager<RailBlockHelper>.Current.AddBlockedRails(blockedRailsSum, Block);
            if (releasedRailsSum.Count > 0)
                SimpleLazyManager<RailBlockHelper>.Current.ReleaseBlockedRails(releasedRailsSum, Block);

            return idx-1;
        }

        private void ReleaseRailSegmentInternal(Rail rail, Dictionary<Rail, int> releasedRailsSum)
        {
            _blockedRails.SubIntFromDict(rail, 1, 0);
            releasedRailsSum.AddIntToDict(rail, 1);
            for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
            {
                Rail linkedRail = rail.GetLinkedRail(i);
                _blockedLinkedRails.SubIntFromDict(linkedRail, 1, 0);
                releasedRailsSum.AddIntToDict(linkedRail, 1);
            }
        }
        
        private bool TryReserveOwnPath([NotNull] Train train, [NotNull] PathCollection path, int startIndex, PathSignalData startSignalData)
        {
//            FileLog.Log($"TryReserveOwnPath: {GetHashCode():X}");
            using PooledList<RailToBlock> railCache = PooledList<RailToBlock>.Take();
            if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                return false;

            ReserveOwnPathInternal(train, railCache, startSignalData);
            return true;
        }
        
        private bool CanReserveOwnPath([NotNull] PathCollection path, int startIndex, PathSignalData startSignalData, PooledList<RailToBlock> cacheList = null)
        {
            if (!ReferenceEquals(startSignalData.ReservedForTrain, null))
            {
                return false;
            }
            foreach (RailToBlock railToBlock in AffectedRailsEnum(path, startIndex))
            {
                if (_blockedRails.TryGetValue(railToBlock.Rail, out int value) && value > 0 || (!railToBlock.IsLinkedRail && _blockedLinkedRails.TryGetValue(railToBlock.Rail, out int value2) && value2 > 0))
                {
                    //rail in the path is blocked by another reserved path
                    return false;
                }

                cacheList?.Add(railToBlock);
            }

            return true;
        }

        private void ReserveOwnPathInternal(Train train, PooledList<RailToBlock> railsToBlock, PathSignalData startSignal)
        {
//            FileLog.Log($"ReserveOwnPath: {GetHashCode():X}");
//            FileLog.Log($"Reserve own path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            using PooledDictionary<Rail, int> railsSum = PooledDictionary<Rail, int>.Take();
            Rail lastRail = null;
            PooledHashSet<Rail> beyondRails = null;
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> trainRails))
                trainRails = PooledHashSet<Rail>.Take();

            try
            {
                foreach (RailToBlock railToBlock in railsToBlock)
                {
                    railsSum.AddIntToDict(railToBlock.Rail, 1);
                    if (!railToBlock.IsBeyondPath)
                    {
                        lastRail = railToBlock.Rail;
                        if (!railToBlock.IsLinkedRail)
                        {
                            if (!trainRails.Add(railToBlock.Rail))
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

                        if (!railToBlock.IsLinkedRail)
                            beyondRails.Add(railToBlock.Rail);
                    }
                    if (!railToBlock.IsLinkedRail)
                        _blockedRails.AddIntToDict(railToBlock.Rail, 1);
                    else
                        _blockedLinkedRails.AddIntToDict(railToBlock.Rail, 1);
                }

                if (beyondRails != null)
                {
//                    FileLog.Log("FoundBeyondPath");
                    _reservedBeyondPath.Add(train, (beyondRails, lastRail));
                }
                _reservedTrainPath[train] = trainRails;
                startSignal.ReservedForTrain = train;
                
                SimpleLazyManager<RailBlockHelper>.Current.AddBlockedRails(railsSum, Block);
            }
            catch (Exception e) when (ExceptionFault.FaultBlock(e, delegate
            {
                if (trainRails != null && !_reservedTrainPath.ContainsKey(train))
                    trainRails?.Dispose();
                beyondRails?.Dispose();
            }))
            {}
        }
        
        private IEnumerable<RailToBlock> AffectedRailsEnum(PathCollection path, int startIndex)
        {
            int index = startIndex + 1;
            _lastPathIndex = index;
            _lastEndSignal = null;
            RailConnection connection = (RailConnection)path[startIndex];
            if (connection.InnerConnection.Block != Block)
                throw new InvalidOperationException("Connection is from another block");
            yield return new RailToBlock(connection.Track, false, false); //track with the inbound signal - we reserve only this track, not linked
            while (index <= path.FrontIndex)
            {
                connection = (RailConnection) path[index];
                Rail rail = connection.Track;
                if (rail.IsBuilt)
                {
                    if (connection.Block != Block)
                        throw new InvalidOperationException("Connection is from another block");
                    yield return new RailToBlock(rail, false, false);
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return new RailToBlock(rail.GetLinkedRail(j), true, false);
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
                    yield return new RailToBlock(rail, false, true);
                    connections.Add(outerConnection);
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return new RailToBlock(rail.GetLinkedRail(j), true, true);
                    }
                }
            }
        }
    }
}