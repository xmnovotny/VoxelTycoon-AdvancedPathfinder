using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdvancedPathfinder.Helpers;
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
        private readonly Dictionary<Rail, int> _blockedRails = new();
        private readonly Dictionary<Rail, int> _blockedLinkedRails = new();
        private int _lastPathIndex;
        private RailSignal _lastEndSignal;
        private RailSignal _lastEndSignalOpposite;

        private readonly Dictionary<Train, PooledHashSet<Rail>> _reservedBeyondPath = new(); //only rails in possible path, not linked rails
        private readonly Dictionary<Train, PooledHashSet<Rail>> _reservedTrainPath = new(); //only rails in train path, not linked rails
        private readonly HashSet<Rail> _reservedSignalRails = new();  //track with reserved inbound signal (for proper releasing only)

        internal IReadOnlyDictionary<Rail, int> BlockedLinkedRails => _blockedLinkedRails;
        internal IReadOnlyDictionary<Rail, int> BlockedRails => _blockedRails;
        internal IReadOnlyDictionary<Train, PooledHashSet<Rail>> ReservedBeyondPath => _reservedBeyondPath; //only rails in possible path, not linked rails
        internal bool IsSomeReservedPath => _reservedTrainPath.Count > 0;

        public PathRailBlockData([NotNull] RailBlock block): base(block)
        {
        }

        public SimpleRailBlockData ToSimpleBlockData()
        {
            return new SimpleRailBlockData(this);
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
                RemovePreReservation(train, rail);
                TryFreeFullBlock();
                return;
            }

            using PooledDictionary<Rail, int> releasedRailsSum = PooledDictionary<Rail, int>.Take();
            if (reservedList.Remove(rail))
            {
                ReleaseRailSegmentInternal(rail, releasedRailsSum);
//                FileLog.Log($"ReleaseSegmentSuccess, rail: {rail.GetHashCode():X8} block: {GetHashCode():X}");
            }
            if (_reservedSignalRails.Remove(rail))
                ReleaseInboundSignal(train, rail);

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
            if (!_reservedBeyondPath.TryGetValue(train, out PooledHashSet<Rail> rails))
                return;

            PathSignalHighlighter highMan = GetHighlighter();
            foreach (Rail rail in rails)
            {
                highMan?.HighlightChange(rail, PathSignalHighlighter.HighlighterType.BeyondPath, -1);
                ReleaseRailSegmentInternal(rail, releasedRailsSum);
            }

            _reservedBeyondPath.Remove(train);
            rails.Dispose();
        }

        
        // ReSharper restore Unity.PerformanceCriticalContext
        protected internal override bool TryReservePathInternal(Train train, PathCollection path, int startIndex,
            ReserveResult reserveResult, bool onlyPreReservation = false)
        {
//            FileLog.Log($"Try reserve path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            if (IsFullBlocked)
                return false;
            reserveResult.ReservedIndex = 0;
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.HasOppositeSignal && startSignalData.OppositeSignalData.IsPreReserved)
            {
                reserveResult.PreReservationFailed = true;
                return false;
            }
            if (onlyPreReservation)
            {
                if (startSignalData.IsChainSignal)
                {
                    if (startSignalData.HasOppositeSignal)
                    {
                        reserveResult.ReservedIndex = Math.Min(startIndex, path.FrontIndex);
                        reserveResult.AddSignalToPreReserve(startSignalData);
                    }
                    return true; //chain signal stops pre-reservations
                }

                FindNextSignal(path, startIndex);
                reserveResult.ReservedIndex = Math.Min(_lastPathIndex, path.FrontIndex);
                return TryPreReserveSignal(train, path, reserveResult, startSignalData);
            }
            if (startSignalData.IsChainSignal)
            {
//                FileLog.Log($"TryReservePath ChainSignal: {GetHashCode():X}");
                using PooledList<RailToBlock> railCache = PooledList<RailToBlock>.Take();
                if (!CanReserveOwnPath(train, path, startIndex, startSignalData, railCache))
                    return false;
                if (!ReferenceEquals(_lastEndSignal, null))
                {
                    // ReSharper disable once PossibleNullReferenceException
                    if (SimpleManager<PathSignalManager>.Current.ShouldUpdatePath(train, _lastEndSignal))
                        return false;
                    PathSignalData nextSignalData = SimpleManager<PathSignalManager>.Current.GetPathSignalData(_lastEndSignal);
                    if (nextSignalData == null) //no signal data, probably network was changed, and in the next update cycle it will be available 
                        return false;
                    if (!ReferenceEquals(nextSignalData.ReservedForTrain, train)) //if path is reserved for this train, we can stop finding following chain signals
                    {
                        RailBlockData nextBlockData = nextSignalData.BlockData;
                        if (!nextBlockData.TryReservePathInternal(train, path, _lastPathIndex, reserveResult))
                            return false;
                    }
                }
                ReserveOwnPathInternal(train, railCache, startSignalData);
                return true;
            }

            if (TryReserveOwnPath(train, path, startIndex, startSignalData, reserveResult))
            {
                return true;
            }

            return false;
        }
        
        // ReSharper restore Unity.PerformanceCriticalContext

        /**
         * Reserve path from rear of the train to the nearest signal (used when original path (or shrunk on) ends before signal or after loading and recreating a signal block)
         * If path is reserved up to the next signal, it will clear beyond path, otherwise it will reserve beyond path
         * <param name="startIndex">starting index of the path, if null, train.RearBound will be used</param>
         * <param name="path"></param>
         * <param name="train"></param>
         * <returns>path index of last reserved track</returns>
         */
        internal int? TryReservePathToFirstSignal(Train train, PathCollection path, int? startIndex = null)
        {
            PooledHashSet<Rail> reservedPath = GetOrTakeTrainReservedPath(train);
            
            int idx = startIndex ?? train.RearBound.ConnectionIndex;
            RailConnection connection = (RailConnection)path[idx];
            
            //find first connection of this block in the path from rear of the train
            while (connection != null && !ReferenceEquals(connection.Block, Block) && idx<path.FrontIndex)
            {
                connection = (RailConnection)path[++idx];
            }

            if (connection == null || !ReferenceEquals(connection.Block, Block))
                return null;

            //find and store all connections in the path within this block 
            using PooledList<RailConnection> connections = PooledList<RailConnection>.Take();
            while (idx<path.FrontIndex)
            {
                connections.Add(connection);
                connection = (RailConnection)path[++idx];
                if (ReferenceEquals(connection, null) || !ReferenceEquals(connection.Block, Block) || 
                    !ReferenceEquals(connection.Signal, null) && connection.Signal.IsBuilt  || 
                    !ReferenceEquals(connection.InnerConnection.Signal, null) && connection.InnerConnection.Signal.IsBuilt)
                    break;
            }

            if (connection != null && ReferenceEquals(connection.InnerConnection.Block, Block))
            {
                //reserve last segment of path (it has this block on the inner connection)
                connections.Add(connection);
            } 

            using PooledDictionary<Rail, int> releasedRailsSum = PooledDictionary<Rail, int>.Take();
            using PooledDictionary<Rail, int> blockedRailsSum = PooledDictionary<Rail, int>.Take();
            PathSignalHighlighter highMan = GetHighlighter();
            
            if (idx<path.FrontIndex && !ReferenceEquals(connection, null))
                ReleaseBeyondPath(train, releasedRailsSum); //we have reached a next signal, so we can dispose beyond path

            foreach (RailConnection railConnection in connections)
            {
                Rail rail = railConnection.Track;
                if (!ReferenceEquals(rail, null) && rail.IsBuilt && !reservedPath.Contains(rail))
                {
                    reservedPath.Add(rail);
                    _blockedRails.AddIntToDict(rail, 1);
                    highMan?.HighlightChange(rail, PathSignalHighlighter.HighlighterType.BlockedRail, 1);
                    if (!releasedRailsSum.TrySubIntFromDict(rail, 1, 0, true))
                        blockedRailsSum.AddIntToDict(rail, 1);
                    for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
                    {
                        Rail linkedRail = rail.GetLinkedRail(i);
                        _blockedLinkedRails.AddIntToDict(linkedRail, 1);
                        highMan?.HighlightChange(linkedRail, PathSignalHighlighter.HighlighterType.BlockedLinkedRail, 1);
                        if (!releasedRailsSum.TrySubIntFromDict(linkedRail, 1, 0, true))
                            blockedRailsSum.AddIntToDict(linkedRail, 1);
                    }
                }
            }
            if (idx == path.FrontIndex && !ReferenceEquals(connection, null) && ReferenceEquals(connection.Block, Block))
            {
                //reserve beyond path
                ReleaseBeyondPath(train, releasedRailsSum); //release old beyond path
                PooledHashSet<Rail> reservedBeyondPath = PooledHashSet<Rail>.Take();
                bool skipUntilNextBaseRail = false;
                try
                {
                    foreach (RailToBlock railToBlock in BeyondPathEnum(connection))
                    {
                        if (skipUntilNextBaseRail && railToBlock.IsLinkedRail)
                            continue;
                        if (!railToBlock.IsLinkedRail)
                        {
                            if (reservedBeyondPath.Contains(railToBlock.Rail))  //rail is already reserved, this is opposite direction for reserve - do not add to blocked
                            {
                                skipUntilNextBaseRail = true;
                                continue;
                            }

                            skipUntilNextBaseRail = false;
                            _blockedRails.AddIntToDict(railToBlock.Rail, 1);
                            highMan?.HighlightChange(railToBlock.Rail, PathSignalHighlighter.HighlighterType.BlockedRail, 1);
                            highMan?.HighlightChange(railToBlock.Rail, PathSignalHighlighter.HighlighterType.BeyondPath, 1);
                            if (!reservedBeyondPath.Add(railToBlock.Rail))
                            {
                                AdvancedPathfinderMod.Logger.Log("Repeated reservation of beyond path");
                            }
                        }
                        else
                        {
                            highMan?.HighlightChange(railToBlock.Rail, PathSignalHighlighter.HighlighterType.BlockedLinkedRail, 1);
                            _blockedLinkedRails.AddIntToDict(railToBlock.Rail, 1);
                        }
                        if (!releasedRailsSum.TrySubIntFromDict(railToBlock.Rail, 1, 0, true))
                            blockedRailsSum.AddIntToDict(railToBlock.Rail, 1);
                    }

                    if (reservedBeyondPath.Count > 0)
                        _reservedBeyondPath[train] = reservedBeyondPath;
                    else
                        reservedBeyondPath.Dispose();
                }
                catch (Exception e) when (ExceptionFault.FaultBlock(e, delegate
                {
                    if (!_reservedBeyondPath.ContainsKey(train))
                        reservedBeyondPath.Dispose();
                }))
                {}
            }
                
            if (blockedRailsSum.Count > 0)
                SimpleLazyManager<RailBlockHelper>.Current.AddBlockedRails(blockedRailsSum, Block);
            if (releasedRailsSum.Count > 0)
                SimpleLazyManager<RailBlockHelper>.Current.ReleaseBlockedRails(releasedRailsSum, Block);

            return idx-1;
        }

        private void ReleaseRailSegmentInternal(Rail rail, Dictionary<Rail, int> releasedRailsSum)
        {
            PathSignalHighlighter highMan = GetHighlighter();
            highMan?.HighlightChange(rail,  PathSignalHighlighter.HighlighterType.BlockedRail, -1);
            _blockedRails.SubIntFromDict(rail, 1, 0);
            releasedRailsSum.AddIntToDict(rail, 1);
            for (int i = rail.LinkedRailCount - 1; i >= 0; i--)
            {
                Rail linkedRail = rail.GetLinkedRail(i);
                _blockedLinkedRails.SubIntFromDict(linkedRail, 1, 0);
                highMan?.HighlightChange(linkedRail,  PathSignalHighlighter.HighlighterType.BlockedLinkedRail, -1);
                releasedRailsSum.AddIntToDict(linkedRail, 1);
            }
        }
        
        private bool TryReserveOwnPath([NotNull] Train train, [NotNull] PathCollection path, int startIndex, PathSignalData startSignalData, ReserveResult reserveResult)
        {
//            FileLog.Log($"TryReserveOwnPath: {GetHashCode():X}");
            using PooledList<RailToBlock> railCache = PooledList<RailToBlock>.Take();
            if (!CanReserveOwnPath(train, path, startIndex, startSignalData, railCache))
                return false;

            reserveResult.ReservedIndex = Math.Min(_lastPathIndex, path.FrontIndex);
            if (!TryPreReserveSignal(train, path, reserveResult, startSignalData, false)) 
                return false;

            ReserveOwnPathInternal(train, railCache, startSignalData);
            return true;
        }

        private bool TryPreReserveSignal(Train train, PathCollection path, ReserveResult reserveResult,
            PathSignalData startSignalData, bool doReservation = true)
        {
            if (!ReferenceEquals(_lastEndSignal, null) && !ReferenceEquals(_lastEndSignalOpposite, null))
            {
                //bidirectional track, we need to test and pre-reserve whole bidirectional part of track
                PathSignalData nextSignalData =
                    SimpleManager<PathSignalManager>.Current!.GetPathSignalData(_lastEndSignal);
                if (nextSignalData == null)
                    return false;
                if (!startSignalData.IsPreReservedForTrain(train) && !nextSignalData.IsPreReservedForTrain(train))
                {
                    if (!nextSignalData.BlockData.TryReservePathInternal(train, path, _lastPathIndex, reserveResult, true))
                        return false;
                    if (doReservation)
                        reserveResult.AddSignalToPreReserve(startSignalData);
                }
            } else if (startSignalData.HasOppositeSignal)
            {
                reserveResult.AddSignalToPreReserve(startSignalData);
            }

            return true;
        }

        private bool CanReserveOwnPath(Train train, [NotNull] PathCollection path, int startIndex, PathSignalData startSignalData, PooledList<RailToBlock> cacheList = null)
        {
            if (!ReferenceEquals(startSignalData.ReservedForTrain, null))
            {
                return false;
            }

            bool isFirst = true;
            foreach (RailToBlock railToBlock in AffectedRailsEnum(path, startIndex))
            {
                if (_blockedRails.TryGetValue(railToBlock.Rail, out int value) && value > 0 || (!railToBlock.IsLinkedRail && _blockedLinkedRails.TryGetValue(railToBlock.Rail, out int value2) && value2 > 0))
                {
                    if (isFirst && _reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> reserved) && reserved.Contains(railToBlock.Rail))
                    {
                        AdvancedPathfinderMod.Logger.LogError($"Train \"{train.Name}\": Try to reserve already reserved path for train, probably signal early turned to red");
//                        FileLog.Log($"{train.Name}, signal {startSignalData.Signal.GetHashCode():X8}: Try to reserve already reserved path for train, probably signal early turned to red");
                    }
                    //rail in the path is blocked by another reserved path
                    return false;
                }

                isFirst = false;
                cacheList?.Add(railToBlock);
            }

            if (!ReferenceEquals(_lastEndSignalOpposite, null))
            {
                PathSignalData oppositeData = SimpleManager<PathSignalManager>.Current!.GetPathSignalData(_lastEndSignalOpposite);
                if (oppositeData != null && !ReferenceEquals(oppositeData.BlockedForTrain, null)) 
                    return false; //opposite signal at the end of the track is blocked for train in opposite direction (takes place only after loading the game)
            }
            return true;
        }

        private PooledHashSet<Rail> GetOrTakeTrainReservedPath(Train train)
        {
            if (!_reservedTrainPath.TryGetValue(train, out PooledHashSet<Rail> trainRails))
            {
                trainRails = PooledHashSet<Rail>.Take();
                _reservedTrainPath[train] = trainRails;
            }

            return trainRails;
        }

        private void ReserveOwnPathInternal(Train train, PooledList<RailToBlock> railsToBlock, PathSignalData startSignal)
        {
//            FileLog.Log($"ReserveOwnPath: {GetHashCode():X}");
//            FileLog.Log($"Reserve own path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            using PooledDictionary<Rail, int> railsSum = PooledDictionary<Rail, int>.Take();
            PooledHashSet<Rail> beyondRails = null;
            PooledHashSet<Rail> trainRails = GetOrTakeTrainReservedPath(train);
            PathSignalHighlighter highMan = GetHighlighter();

            try
            {
                foreach (RailToBlock railToBlock in railsToBlock)
                {
                    railsSum.AddIntToDict(railToBlock.Rail, 1);
                    if (!railToBlock.IsBeyondPath)
                    {
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
                        {
                            highMan?.HighlightChange(railToBlock.Rail, PathSignalHighlighter.HighlighterType.BeyondPath, 1);
                            beyondRails.Add(railToBlock.Rail);
                        }
                    }

                    if (!railToBlock.IsLinkedRail)
                    {
                        _blockedRails.AddIntToDict(railToBlock.Rail, 1);
                    }
                    else
                    {
                        _blockedLinkedRails.AddIntToDict(railToBlock.Rail, 1);
                    }
                    highMan?.HighlightChange(railToBlock.Rail, railToBlock.IsLinkedRail ? PathSignalHighlighter.HighlighterType.BlockedLinkedRail : PathSignalHighlighter.HighlighterType.BlockedRail, 1);

                }

                if (beyondRails != null)
                {
//                    FileLog.Log("FoundBeyondPath");
                    _reservedBeyondPath.Add(train, beyondRails);
                }
                startSignal.ReservedForTrain = train;
//                FileLog.Log($"{train.Name}, signal {startSignal.GetHashCode():X8}: Reserved");

                if (!_reservedSignalRails.Add(startSignal.Signal.Connection.Track))
                {
                    AdvancedPathfinderMod.Logger.Log("Multiple reservation of one signal");
                }
                SimpleLazyManager<RailBlockHelper>.Current.AddBlockedRails(railsSum, Block);
            }
            catch (Exception e) when (ExceptionFault.FaultBlock(e, delegate
            {
                if (trainRails != null && !_reservedTrainPath.ContainsKey(train))
                    trainRails.Dispose();
                beyondRails?.Dispose();
                highMan?.Redraw();
            }))
            {}
        }

        private void FindNextSignal(PathCollection path, int startIndex)
        {
            foreach (var _ in AffectedRailsEnum(path, startIndex, false, false, false))
            {}
        }
        
        private IEnumerable<RailToBlock> AffectedRailsEnum(PathCollection path, int startIndex, bool getMainRails = true, bool getLinkedRails = true, bool getBeyondPath = true)
        {
            int index = startIndex + 1;
            _lastPathIndex = index;
            _lastEndSignal = null;
            _lastEndSignalOpposite = null;
            RailConnection connection = (RailConnection)path[startIndex];
            if (connection.InnerConnection.Block != Block)
                throw new InvalidOperationException("Connection is from another block");
            while (index <= path.FrontIndex)
            {
                connection = (RailConnection) path[index];
                if (ReferenceEquals(connection, null))
                {
                    //invalid path, break
                    yield break;;
                }

                Rail rail = connection.Track;
                if (!rail.IsBuilt) //invalid path, break
                    yield break;
                if (connection.Block != Block)
                    throw new InvalidOperationException("Connection is from another block");
                if (getMainRails)
                    yield return new RailToBlock(rail, false, false);
                if (getLinkedRails)
                {
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return new RailToBlock(rail.GetLinkedRail(j), true, false);
                    }
                }

                if (index != startIndex)
                {
                    RailSignal signal = connection.Signal;
                    RailSignal oppositeSignal = connection.InnerConnection.Signal;
                    if (!ReferenceEquals(signal, null) && signal.IsBuilt ||
                        !ReferenceEquals(oppositeSignal, null) && oppositeSignal.IsBuilt)
                    {
                        _lastEndSignal = signal;
                        _lastEndSignalOpposite = oppositeSignal;
                        yield break;
                    }
                }

                _lastPathIndex = ++index;
            }

            if (connection == null)
                throw new InvalidOperationException("No available connection in the provided path");

            if (!getBeyondPath) yield break;
            
            //end of the path, but no signal found (=need to go through all possible connections until signal is found) 
            foreach (var railToBlock in BeyondPathEnum(connection)) yield return railToBlock;
        }

        private static IEnumerable<RailToBlock> BeyondPathEnum(RailConnection connection)
        {
            using PooledList<RailConnection> connections = PooledList<RailConnection>.Take();
            using PooledHashSet<RailConnection> processed = PooledHashSet<RailConnection>.Take();
            connections.Add(connection);
            processed.Add(connection);
            for (int i = 0; i < connections.Count; i++)
            {
                connection = connections[i];
                foreach (TrackConnection trackConnection in connection.InnerConnection.OuterConnections)
                {
                    RailConnection outerConnection = (RailConnection) trackConnection;
                    if (!ReferenceEquals(outerConnection.Signal, null) && outerConnection.Signal.IsBuilt
                        || !ReferenceEquals(outerConnection.InnerConnection.Signal, null) &&
                        outerConnection.InnerConnection.Signal.IsBuilt)
                        continue;
                    Rail rail = outerConnection.Track;
                    if (!processed.Add(outerConnection)) //connection was already processed (=circular track)
                        continue;
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