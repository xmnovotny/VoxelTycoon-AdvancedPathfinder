using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AdvancedPathfinder.Rails;
using AdvancedPathfinder.UI;
using Delegates;
using HarmonyLib;
using JetBrains.Annotations;
using ModSettingsUtils;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Serialization;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    [HarmonyPatch]
    [SchemaVersion(1)]
    public class PathSignalManager : SimpleManager<PathSignalManager>
    {
        //TODO: optimize == operators on RailBlocks
        //TODO: rewrite functions for finding path when there is a nonstop task
        //TODO: Fix fully block a block when passing a signal at red while no train is in the block
        //TODO: Allow signals between same block (=invalid block in block signalling system)
        private readonly Dictionary<RailSignal, PathSignalData> _pathSignals = new();
        private readonly Dictionary<RailBlock, RailBlockData> _railBlocks = new();
        private readonly HashSet<RailSignal> _changedStates = new(); //list of signals with changed states (for performance)
        private readonly Dictionary<Train, RailSignal> _passedSignals = new(); //for delay of passing signal 
        private readonly HashSet<Train> _updateTrainPath = new(); //trains which have to update path on LateUpdate
        private bool _highlightDirty = true;
        private readonly PathCollection _detachingPathCache = new();
        internal readonly Dictionary<RailSignal, Train> OpenedSignals = new();
        private static bool _wasInvalidPath;
        
        private readonly Dictionary<Train, (int reservedIdx, int? nextDestinationIdx)>
            _reservedPathIndex =
                new(); //index of train path which is reserved in path signals (path before index should not be altered when updating path) and if path contains part from destination to the next destination (nonstop stations), index of first element of the second part

        public RailSignalState GetSignalState(RailSignal signal)
        {
            if (!signal.IsBuilt)
                return RailSignalState.Green;
            if (!_pathSignals.TryGetValue(signal, out PathSignalData data))
            {
                //can be when old block is removed and new is not yet created - signal is closed
                return RailSignalState.Red;
//                FileLog.Log($"No data for signal {signal.GetHashCode():X8}.");
//                throw new InvalidOperationException($"No data for signal {signal.GetHashCode():X8}.");
            }

            return data.GetSignalState();
        }

        public int BlockBlockedCount(RailBlock block, ImmutableUniqueList<RailConnection> connections)
        {
            if (_railBlocks.TryGetValue(block, out RailBlockData data))
            {
                int result = data.BlockBlockedCount;
                if (connections.Count > 0 && data is PathRailBlockData pathData)
                {
                    result += pathData.GetBlockedRailsSum(connections);
                }
                return result;
            }

            return 0;
        }

        [CanBeNull]
        internal PathSignalData GetPathSignalData(RailSignal signal)
        {
            if (!_pathSignals.TryGetValue(signal, out PathSignalData signalData) || signalData == null)
            {
                AdvancedPathfinderMod.Logger.LogError("Signal data not found");
                return null;
//                throw new InvalidOperationException("Signal data not found");
            }

            return signalData;
        }
        
        protected override void OnInitialize()
        {
            Behaviour.OnLateUpdateAction -= OnLateUpdate;
            Behaviour.OnLateUpdateAction += OnLateUpdate;
            Stopwatch sw = Stopwatch.StartNew();
            ModSettings<Settings>.Current.Subscribe(OnSettingsChanged);
            FindBlocksAndSignals();
            SetTrainsPathUpdateTimes();
            sw.Stop();
            SimpleLazyManager<RailBlockHelper>.Current.OverrideBlockIsOpen = true;
            SimpleLazyManager<RailBlockHelper>.Current.RegisterBlockCreatedAction(BlockCreated);
            SimpleLazyManager<RailBlockHelper>.Current.RegisterBlockRemovingAction(BlockRemoving);
            SimpleLazyManager<TrainHelper>.Current.RegisterTrainAttachedAction(TrainAttached);
            SimpleLazyManager<TrainHelper>.Current.RegisterTrainDetachedAction(TrainDetached);
            ReserveTrainsPathsAfterStart();
//            FileLog.Log(string.Format("Path signals initialized in {0:N3}ms, found signals: {1:N0}, found blocks: {2:N0}", sw.ElapsedTicks / 10000f, _pathSignals.Count, _railBlocks.Count));
        }

        protected override void OnDeinitialize()
        {
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.UnregisterBlockCreatedAction(BlockCreated);
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.UnregisterBlockRemovingAction(BlockRemoving);
        }

        internal void Write(StateBinaryWriter writer)
        {
            WriteReservedPathIndexes(writer);
        }

        internal void Read(StateBinaryReader reader)
        {
            ReadReservedPathIndexes(reader);
        }

        internal bool ShouldUpdatePath(Train train, RailSignal signal)
        {
            if (Manager<RailPathfinderManager>.Current?.IsLastEdgeSignalWithPathDiversion(train, signal) == true)
            {
                float updatePathTime = SimpleLazyManager<TrainHelper>.Current.GetTrainUpdatePathTime(train);
                float currentTime = LazyManager<TimeManager>.Current.UnscaledUnpausedSessionTime;
                if (updatePathTime - 10000 < currentTime)
                {
                    float newTime = currentTime + 10000f + 1f; //added 10000s for disabling original update algorithm
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (newTime != updatePathTime)
                        SimpleLazyManager<TrainHelper>.Current.SetTrainUpdatePathTime(train, newTime);
                    
                    _updateTrainPath.Add(train);
                    return true;
                }
            }

            return false;
        }
        
        private void SetTrainsPathUpdateTimes()
        { 
            ImmutableList<Vehicle> trains = LazyManager<VehicleManager>.Current.GetAll<Train>();
            for (int i = trains.Count - 1; i >= 0; i--)
            {
                SimpleLazyManager<TrainHelper>.Current.SetTrainUpdatePathTime((Train)trains[i], LazyManager<TimeManager>.Current.UnscaledUnpausedSessionTime + 10000f);

            }
        }

        private void ReadReservedPathIndexes(StateBinaryReader reader)
        {
            _reservedPathIndex.Clear();
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                Train train = LazyManager<VehicleManager>.Current.FindById(reader.ReadInt()) as Train;
                if (train == null)
                    throw new InvalidOperationException("Could not find train by ID");
                int reservedIdx = reader.ReadInt();
                int nextDestIdx = reader.ReadInt();
                _reservedPathIndex.Add(train, (reservedIdx, nextDestIdx != int.MinValue ? nextDestIdx : null));
            }

            _lastHighlightUpdate = 0;
            HighlightReservedPaths();
        }

        private void WriteReservedPathIndexes(StateBinaryWriter writer)
        {
            writer.WriteInt(_reservedPathIndex.Count);
            foreach (KeyValuePair<Train,(int reservedIdx, int? nextDestinationIdx)> pair in _reservedPathIndex)
            {
                writer.WriteInt(pair.Key.Id); //train ID
                writer.WriteInt(pair.Value.reservedIdx);
                writer.WriteInt(pair.Value.nextDestinationIdx ?? int.MinValue);
            }
        }

        private void UpdateReservedPathIndex(Train train, int reservedIndex, bool canShrink = false)
        {
            (int reservedIdx, int? nextDestinationIdx) reserved = _reservedPathIndex.GetValueOrDefault(train, (int.MinValue, null));
            if (canShrink || reserved.reservedIdx <= reservedIndex)
            {
                reserved.reservedIdx = reservedIndex;
                _reservedPathIndex[train] = reserved;
            }
        }

        /** will reserve train path from rear of the train to the nearest signal */
        private void ReserveTrainPath(Train train, [CanBeNull] PathCollection path=null, [CanBeNull] RailBlock onlyBlock = null)
        {
            if (!train.IsAttached)
                return;
            path ??= SimpleLazyManager<TrainHelper>.Current.GetTrainPath(train);
            RailBlock lastBlock = null;
            RailBlock frontTrainBlock = ((RailConnection) train.FrontBound.Connection).Block;
            int reservedIdx = train.FrontBound.ConnectionIndex;
            for (int pathIdx = path.RearIndex; pathIdx <= path.FrontIndex; pathIdx++)
            {
                RailConnection conn = (RailConnection)path[pathIdx];
                if (conn == null)
                {
                    //invalid train path, stop reserving
                    _reservedPathIndex.Remove(train);
                    return;
                }
                RailBlock[] currBlocks = {conn.Block, conn.InnerConnection.Block};
                for (int j = 0; j < 2; j++)
                {
                    RailBlock currBlock = currBlocks[j];
                    if (!ReferenceEquals(onlyBlock, null) && !ReferenceEquals(currBlock, onlyBlock))
                        continue;
                    if (!ReferenceEquals(currBlock, null) && !ReferenceEquals(currBlock, lastBlock))
                    {
                        if (ReferenceEquals(lastBlock, frontTrainBlock))  //this block is after block where train front is = stop reserving
                            return;

                        lastBlock = currBlock;

                        RailBlockData blockData = GetBlockData(currBlock);
                        switch (blockData)
                        {
                            case PathRailBlockData pathData:
                                int? newIdx = pathData.TryReservePathToFirstSignal(train, path, pathIdx);
                                if (newIdx > reservedIdx)
                                    reservedIdx = newIdx.Value;
                                break;
                            case SimpleRailBlockData simpleData:
                                simpleData.FullBlock();
                                break;
                        }
                    }
                }
            }
            if (reservedIdx > train.FrontBound.ConnectionIndex)
                UpdateReservedPathIndex(train, reservedIdx);
        }
        
        private void ReserveTrainsPathsAfterStart()
        {
            ImmutableList<Vehicle> trains = LazyManager<VehicleManager>.Current.GetAll<Train>();
            for (int i = trains.Count - 1; i >= 0; i--)
            {
                ReserveTrainPath((Train) trains[i]);
            }

            _lastHighlightUpdate = 0;
            HighlightReservedPaths();
        }

        private void OnSettingsChanged()
        {
            if (!ModSettings<Settings>.Current.HighlightReservedPaths)
            {
                HideHighlighters();
            } else 
                _highlightDirty = true;
        }

        private void OnLateUpdate()
        {
            foreach (Train train in _updateTrainPath)
            {
//                FileLog.Log("UpdateTrainPath");
                Manager<RailPathfinderManager>.Current.TrainUpdatePath(train);
            }
            _updateTrainPath.Clear();
            if (_highlightDirty)
                HighlightReservedPaths();
        }

        private void TrainPassedSignal(Train train, RailSignal signal)
        {
//            FileLog.Log($"{train.Name}, signal {signal.GetHashCode():X8}: Passed");
            if (!_pathSignals.TryGetValue(signal, out PathSignalData data))
                throw new InvalidOperationException("No data for signal.");
            data.TrainPassedSignal(train);
        }

        private void TrainPassingSignal(Train train, RailSignal signal)
        {
//            FileLog.Log($"{train.Name}, signal {signal.GetHashCode():X8}: Passing");
            if (!_pathSignals.TryGetValue(signal, out PathSignalData data))
                throw new InvalidOperationException("No data for signal.");
            data.TrainPassingSignal(train);
        }

        private void FindBlocksAndSignals()
        {
            HashSet<RailSignal> signals = TrackHelper.GetAllRailSignals();
            foreach (RailSignal signal in signals)
            {
                if (!signal.IsBuilt) continue;

                RailBlock block = signal.Connection?.InnerConnection.Block;
                if (block == null) continue;

                PathRailBlockData data = GetOrCreateRailBlockData(block);
                data.InboundSignals.Add(signal, null);
                RailSignal oppositeSignal = signal.Connection.InnerConnection.Signal;
                if (oppositeSignal != null)
                {
                    data.OutboundSignals.Add(oppositeSignal);
                }
            }

            DetectSimpleBlocks();
            CreatePathSignalsData();
            
            //check all starting tracks for blocks, that is not processed (train depots...)
            HashSet<RailConnection> startingConnections = new();
            TrackHelper.GetStartingConnections<Rail, RailConnection>(startingConnections);
            foreach (RailConnection connection in startingConnections)
            {
                if (!ReferenceEquals(connection.Block, null) && !_railBlocks.ContainsKey(connection.Block))
                    BlockCreated(connection.Block);
            }
        }

        private bool IsSimpleBlock(RailBlockData blockData)
        {
            int inbCount = blockData.InboundSignals.Count;
            switch (inbCount)
            {
                case 0:
                    //no signal = simple block
                    return true;
                case > 2:
                    //more than 2 signals = not a simple block
                    return false;
            }

            foreach (RailSignal railSignal in blockData.InboundSignals.Keys)
            {
                if (PathSignalData.CheckIsChainSignal(railSignal)) //some of inbound signals are chain = no simple block
                    return false;
            }

            RailSignal signal = blockData.InboundSignals.Keys.First();
            RailConnection connection = signal.Connection.InnerConnection;
            while (true)
            {
                switch (connection.OuterConnectionCount)
                {
                    case > 1:
                        return false;
                    case 0:
                        //end of track, simple block only when there is only one inbound signal
                        return inbCount == 1;
                }

                connection = (RailConnection) connection.OuterConnections[0];
                if (connection.OuterConnectionCount > 1)
                {
                    return false;
                }

                connection = connection.InnerConnection;

                if (connection.Signal != null)
                {
                    //opposite signal - if there are two signals in inbound signals, it should be the second one
                    return inbCount == 1 ||
                           inbCount == 2 && blockData.InboundSignals.ContainsKey(connection.Signal);
                }

                if (connection.InnerConnection.Signal != null)
                {
                    //next signal, there should be only one signal in inbound signals for a simple block
                    return inbCount == 1;
                }
            }
        }

        /**
         * Remove blocks, that have no switch in the block and have no chain signal
         */
        private void DetectSimpleBlocks()
        {
            List<KeyValuePair<RailBlock, RailBlockData>> toConvert = new List<KeyValuePair<RailBlock, RailBlockData>>();
            foreach (KeyValuePair<RailBlock, RailBlockData> pair in _railBlocks)
            {
                if (IsSimpleBlock(pair.Value))
                {
                    toConvert.Add(pair);
                }
            }

            foreach (KeyValuePair<RailBlock, RailBlockData> pair in toConvert)
            {
                _railBlocks[pair.Key] = ((PathRailBlockData) pair.Value).ToSimpleBlockData();
            }
        }
        
        private void CreatePathSignalsData()
        {
            foreach (KeyValuePair<RailBlock, RailBlockData> blockPair in _railBlocks)
            {
                CreatePathSignalData(blockPair.Value);
            }
        }

        private void CreatePathSignalData(RailBlockData blockData)
        {
            foreach (RailSignal signal in blockData.InboundSignals.Keys.ToArray())
            {
                PathSignalData data = new(signal, blockData) {StateChanged = OnSignalStateChanged};
                _pathSignals.Add(signal, data);
                _changedStates.Add(signal);
                blockData.InboundSignals[signal] = data;
            }
        }

        private PathRailBlockData GetOrCreateRailBlockData(RailBlock block)
        {
            if (!_railBlocks.TryGetValue(block, out RailBlockData data))
            {
                data = new PathRailBlockData(block);
                data.RegisterBlockFreeConditionChanged(OnBlockFreeConditionChanged);    
                _railBlocks.Add(block, data);
            }

            if (data is not PathRailBlockData pathData)
                throw new InvalidOperationException("RailBlockData contains SimpleBlockData");

            return pathData;
        }

        private void OnBlockFreeConditionChanged(RailBlockData data, bool isFree)
        {
            //FileLog.Log($"OnBlockFreeChanged: {isFree}, ({data.GetHashCode():X8})");
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.BlockFreeConditionChanged(data.Block, isFree);
        }

        private void OnSignalStateChanged(PathSignalData signalData)
        {
            _changedStates.Add(signalData.Signal);
        }

        private bool IsSignalOpenForTrain(RailSignal signal, Train train, PathCollection path)
        {
            if (Manager<RailPathfinderManager>.Current == null)
                return false;
            Manager<RailPathfinderManager>.Current.Stats?.StartSignalOpenForTrain();
            if (OpenedSignals.TryGetValue(signal, out Train reservedTrain))
            {
                Manager<RailPathfinderManager>.Current.Stats?.StopSignalOpenForTrain();
                return ReferenceEquals(reservedTrain, train);
            }
            PathSignalData signalData = GetPathSignalData(signal);
            if (signalData == null) //no signal data, probably network was changed, and in the next update cycle it will be available 
                return false;
            
//            FileLog.Log($"IsSignalOpenForTrain, train: {train.GetHashCode():X8}, signal: {signalData.GetHashCode():X8}");
            if (ReferenceEquals(signalData.ReservedForTrain, train))
            {
                Manager<RailPathfinderManager>.Current.Stats?.StopSignalOpenForTrain();
                return true;
            }
            
            if (!ReferenceEquals(signalData.ReservedForTrain, null))
            {
                //it should not be
                //FileLog.Log("Signal is reserved for another train.");
                AdvancedPathfinderMod.Logger.LogError("Signal is reserved for another train.");
                return false;
            }

            if (ShouldUpdatePath(train, signal))
                return false;

            RailConnection conn = signal.Connection;
            int? pathIndex = path.FindConnectionIndex(conn, train.FrontBound.ConnectionIndex);
            if (pathIndex == null)
            {
//                throw new InvalidOperationException("Signal connection not found in the path");
//                FileLog.Log("Signal connection not found in the path");
                AdvancedPathfinderMod.Logger.Log("Signal connection not found in the path");
                return false;
            }

            bool result = signalData.BlockData.TryReservePath(train, path, pathIndex.Value, out int reservedPathIndex) && signalData.ReservedForTrain == train;
//            FileLog.Log($"IsSignalOpenForTrain 2 {result}, train: {train.GetHashCode():X8}, signal: {signalData.GetHashCode():X8}");
            if (result)
            {
                (int reservedIdx, int? nextDestinationIdx) pathIds = _reservedPathIndex.GetValueOrDefault(train, (reservedPathIndex, null));
                pathIds.reservedIdx = reservedPathIndex;
//                FileLog.Log($"IsSignalOpenForTrain, train: {train.GetHashCode():X8}, signal: {signalData.GetHashCode():X8}, reservedPathIndex: {pathIds.reservedIdx}");
                _reservedPathIndex[train] = pathIds;
            }

            Manager<RailPathfinderManager>.Current!.Stats?.StopSignalOpenForTrain();
            HighlightReservedPaths();
            return result;
        }

        private void TrainConnectionReached(Train train, TrackConnection connection)
        {
            if (ReferenceEquals(connection, null))
                return;
            if (_passedSignals.TryGetValue(train, out RailSignal signal))
            {
                if (signal.Connection == connection)
                {
                    //repeatedly passing one signal, we call again train passing signal event (this is caused by low-fps)
//                    AdvancedPathfinderMod.Logger.LogError($"{train.Name}, signal {signal.GetHashCode():X8}: Same connection");
//                    FileLog.Log($"{train.Name}, signal {signal.GetHashCode():X8}: Same connection");
                    TrainPassingSignal(train, signal);
                    return;
                }
                TrainPassedSignal(train, signal);
                _passedSignals.Remove(train);
            }

            RailConnection railConn = (RailConnection) connection;
            if (!ReferenceEquals(railConn.Signal, null))
            {
                _passedSignals.Add(train, railConn.Signal);
                TrainPassingSignal(train, railConn.Signal);
            }
        }

        private RailBlockData GetBlockData(RailBlock block)
        {
            if (!_railBlocks.TryGetValue(block, out RailBlockData result))
            {
                throw new InvalidOperationException("Block data not found");
            }

            return result;
        }

        private void PathShrinkingRear(PathCollection path, int newRearIndex)
        {
            if (path.RearIndex >= newRearIndex || !SimpleLazyManager<TrainHelper>.Current.GetTrainFromPath(path, out Train train))
                return;
            PathShrinking(train, path, path.RearIndex, newRearIndex - 1, out _);
        }

        private void PathShrinkingFront(PathCollection path, ref int newFrontIndex)
        {
            if (path.FrontIndex <= newFrontIndex || !SimpleLazyManager<TrainHelper>.Current.GetTrainFromPath(path, out Train train))
                return;
            (int reservedIdx, int? nextDestinationIdx) reservedPathIndex = _reservedPathIndex.GetValueOrDefault(train, (int.MinValue, null));
//            FileLog.Log($"Path front shrinking {train.GetHashCode():X8}, newFrontIndex {newFrontIndex}, origFrontIndex {path.FrontIndex}, reservedIndex {reservedPathIndex.reservedIdx}");
            int? invalidIndex = null;
            if (path.ContainsIndex(newFrontIndex + 1))
                PathShrinking(train, path, newFrontIndex + 1, path.FrontIndex, out invalidIndex);
            if (_wasInvalidPath)
            {
                newFrontIndex = invalidIndex.Value - 1;
                _reservedPathIndex.Remove(train);
                _updateTrainPath.Add(train);
            } else if (reservedPathIndex.reservedIdx > newFrontIndex || reservedPathIndex.nextDestinationIdx > newFrontIndex)
            {
                if (reservedPathIndex.nextDestinationIdx > newFrontIndex)
                    reservedPathIndex.nextDestinationIdx = null;
                if (reservedPathIndex.reservedIdx > newFrontIndex)
                    reservedPathIndex.reservedIdx = newFrontIndex;
//                FileLog.Log($"Shrink reserved index: old {_reservedPathIndex[train].reservedIdx} new {newFrontIndex}");
                _reservedPathIndex[train] = reservedPathIndex;
            }
        }

        private void PathShrinking(Train train, PathCollection path, int from, int to, out int? invalidIndex) //indexes from and to are inclusive 
        {
            //TODO: Rework releasing path using connection instead of track, so duplicity check will not be necessary
            _wasInvalidPath = false;
            bool changed = false;
            invalidIndex = null;
            RailBlockData currBlockData = null;
            using PooledHashSet<Track> usedTracks = PooledHashSet<Track>.Take();
            for (int index = path.RearIndex; index < from; index++)
            {
                if (path[index] == null)
                {
                    invalidIndex ??= index;
                    _wasInvalidPath = true;
                    continue;
                }

                usedTracks.Add(path[index].Track);
            }
            for (int index = from; index <= to; index++)
            {
                changed = true;
                RailConnection currConnection = (RailConnection) path[index];
                if (currConnection == null || !currConnection.Track.IsBuilt)
                {
                    //invalid (=removed rail segment)
//                    invalidIndex ??= index;
                    continue;
                }

                if (!usedTracks.Add(currConnection.Track))
                {
                    //path has duplicity connections, this is second round = skip releasing segment
                    continue;
                }
                if (!ReferenceEquals(currBlockData?.Block,currConnection.Block))
                {
                    currBlockData = ReferenceEquals(currConnection.Block, null) ? null : GetBlockData(currConnection.Block);
                }

                if (currBlockData != null)
                {
                    currBlockData.ReleaseRailSegment(train, currConnection.Track);
                }

                currConnection = currConnection.InnerConnection;
                if (currBlockData?.Block != currConnection.Block)
                {
                    currBlockData = ReferenceEquals(currConnection.Block, null) ? null : GetBlockData(currConnection.Block);
                    if (currBlockData != null)
                    {
                        currBlockData.ReleaseRailSegment(train, currConnection.Track);
                    }
                }
            }

            if (changed)
                HighlightReservedPaths();
        }

        private void VerifyPath(PathCollection path)
        {
            TrackConnection lastConn = null;
            for (int i = path.RearIndex; i < path.FrontIndex; i++)
            {
                if (lastConn != null && !lastConn.InnerConnection.OuterConnections.Contains(path[i]))
                {
                    throw new InvalidOperationException("Inconsistent path");
                }
                if (lastConn == path[i])
                    throw new InvalidOperationException("Inconsistent path 2");
                lastConn = path[i];
//                FileLog.Log($"LastConn: {lastConn.GetHashCode():X8}");
            }
        }

        private void TrainAttached(Train train, PathCollection path)
        {
            RailBlock lastBlock = null;
            for (int i = path.RearIndex; i < path.FrontIndex; i++)
            {
                RailConnection conn = (RailConnection) path[i];
                RailBlock block = conn.Block;
                if (block != lastBlock)
                {
                    GetBlockData(block).FullBlock();
                    lastBlock = block;
                }

                block = conn.InnerConnection.Block;
                if (block != lastBlock)
                {
                    GetBlockData(block).FullBlock();
                    lastBlock = block;
                }
            }
            SimpleLazyManager<TrainHelper>.Current.SetTrainUpdatePathTime(train, LazyManager<TimeManager>.Current.UnscaledUnpausedSessionTime + 10000f);
            HighlightReservedPaths();
        }

        private void TryReleaseFullBlock()
        {
            foreach (RailBlockData blockData in _railBlocks.Values)
            {
                blockData.TryFreeFullBlock();
            }
        }
        
        private void PathClearing(PathCollection path)
        {
//            FileLog.Log("Path clearing");
            if (path.Count > 0 && SimpleLazyManager<TrainHelper>.Current.GetTrainFromPath(path, out Train train))
            {
//                FileLog.Log("Path cleared");
                PathShrinking(train, path, path.RearIndex, path.FrontIndex, out _);
            }
        }

        private void DeleteTrainData(Train train)
        {
            _passedSignals.Remove(train);
            _reservedPathIndex.Remove(train);
        }

        private void TrainDetached(Train train)
        {
            if (_detachingPathCache.Count > 0)
            {
//                FileLog.Log("Path cleared");
                PathShrinking(train, _detachingPathCache, _detachingPathCache.RearIndex, _detachingPathCache.FrontIndex, out _);
                _detachingPathCache.Clear();
            }
            DeleteTrainData(train);
            TryReleaseFullBlock();
            HighlightReservedPaths();
        }

        private void TrainDetaching(Train train, PathCollection path)
        {
            _detachingPathCache.Clear();
            for (int i = path.RearIndex; i <= path.FrontIndex; i++)
            {
                _detachingPathCache.AddToFront(path[i]);
            }
        }

        private void BlockRemoving(RailBlock block)
        {
            if (_railBlocks.TryGetValue(block, out RailBlockData data))
            {
//                FileLog.Log($"Block removing {data.GetHashCode():X8}");
                using PooledList<Train> passed = PooledList<Train>.Take(); 
                foreach (RailSignal signal in data.InboundSignals.Keys)
                {
                    _pathSignals.Remove(signal);
                    OpenedSignals.Remove(signal);
//                    FileLog.Log($"Removed signal {signal.GetHashCode():X8}");
                    passed.AddRange(from pair in _passedSignals where pair.Value == signal select pair.Key);
                    _changedStates.Add(signal);
                }
                _railBlocks.Remove(block);
                foreach (Train train in passed)
                {
                    _passedSignals.Remove(train);
                }
            }
            HighlightReservedPaths();
        }
        
        private void ReserveTrainPathsInNewBlock(RailBlock block, RailBlockData blockData)
        {
            if (block.Value == 0)
                return;
            switch (blockData)
            {
                case PathRailBlockData:
                {
                    using PooledHashSet<Train> trains = PooledHashSet<Train>.Take();
                    SimpleLazyManager<RailBlockHelper>.Current.FindTrainsInBlock(block, trains);
                    foreach (Train train in trains)
                    {
                        ReserveTrainPath(train, null, block);
                    }
                    break;
                }
                case SimpleRailBlockData simpleRailBlockData:
                    simpleRailBlockData.FullBlock();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(blockData));
            }
        }

        private void BlockCreated(RailBlock block)
        {
            UniqueList<RailConnection> connections = SimpleLazyManager<RailBlockHelper>.Current.GetBlockConnections(block);
            PathRailBlockData blockData = GetOrCreateRailBlockData(block);
//            FileLog.Log($"Block created {blockData.GetHashCode():X8}");
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                RailConnection conn = connections[i];
                RailSignal signal = conn.InnerConnection.Signal;
                if (!ReferenceEquals(signal, null) && signal.IsBuilt)
                {
                    blockData.InboundSignals.Add(signal, null);
//                    FileLog.Log($"Added signal {signal.GetHashCode():X8}");
                    _changedStates.Add(signal);
                    if (conn.Signal != null && conn.Signal.IsBuilt)
                    {
                        blockData.OutboundSignals.Add(conn.Signal);
                    }
                }
            }

            RailBlockData newBlockData = blockData;
            if (IsSimpleBlock(blockData))
            {
                 newBlockData = blockData.ToSimpleBlockData();
                 _railBlocks[block] = newBlockData;
//                FileLog.Log($"Is a new simple block {newBlockData.GetHashCode():X8}");
            }
            
            CreatePathSignalData(newBlockData);
            ReserveTrainPathsInNewBlock(block, newBlockData);
            HighlightReservedPaths();
        }

        private void AfterUpdateDestinationAndPath(PathCollection path, Train train)
        {
            RailConnection conn = (RailConnection) train.FrontBound.Connection;

            if (ReferenceEquals(conn?.Block, null)) return;

            if (_railBlocks.GetValueOrDefault(conn.Block) is PathRailBlockData blockData)
            {
                int? reservedIndex = blockData.TryReservePathToFirstSignal(train, path);
                if (reservedIndex.HasValue)
                {
                    (int reservedIdx, int? nextDestinationIdx) reserved = _reservedPathIndex.GetValueOrDefault(train);
                    if (reservedIndex > reserved.reservedIdx)
                        reserved.reservedIdx = reservedIndex.Value;
                    _reservedPathIndex[train] = reserved;
                }
            }
        }

        #region DEBUG

        private readonly HashSet<Highlighter> _highlighters = new();

        private float _lastHighlightUpdate = 0;

        private void HideHighlighters()
        {
            foreach (Highlighter highlighter in _highlighters)
            {
                if (highlighter != null && highlighter.isActiveAndEnabled)
                {
                    highlighter.gameObject.SetActive(false);
                }
            }

            _highlighters.Clear();
        }

        private void HighlightRail(Rail rail, Color color, float halfWidth = 0.5f)
        {
            if (!rail.IsBuilt)
                return;
            RailConnectionHighlighter man = LazyManager<RailConnectionHighlighter>.Current;
            _highlighters.Add(man.ForOneTrack(rail, color, halfWidth));
        }

        private void HighlightReservedBounds()
        {
            foreach (KeyValuePair<PathCollection, Train> pair in SimpleLazyManager<TrainHelper>.Current.PathToTrain)
            {
                if (_reservedPathIndex.TryGetValue(pair.Value, out (int reservedIdx, int? nextDestinationIdx) reservedIndex))
                {
                    if (reservedIndex.reservedIdx > 0 && pair.Key.ContainsIndex(reservedIndex.reservedIdx))
                    {
                        TrackConnection conn = pair.Key[reservedIndex.reservedIdx];
                        HighlightRail((Rail) conn.Track, Color.black, 0.6f);
                    }

                    if (reservedIndex.nextDestinationIdx != null && pair.Key.ContainsIndex(reservedIndex.nextDestinationIdx.Value))
                    {
                        TrackConnection conn = pair.Key[reservedIndex.nextDestinationIdx.Value];
                        HighlightRail((Rail) conn.Track, Color.blue, 0.7f);
                    }
                }
            }
        }

        private void HighlightReservedPaths()
        {
            if (!ModSettings<Settings>.Current.HighlightReservedPaths)
                return;
            if (_lastHighlightUpdate + 1f >= Time.time)
            {
                _highlightDirty = true;
                return;
            }

            _highlightDirty = false;
            _lastHighlightUpdate = Time.time;
            HideHighlighters();
            Color color = Color.green;
            Color linkedColor = Color.red;
            Color simpleBlockColor = Color.green;
            simpleBlockColor.g = 230;
            foreach (RailBlockData blockData in _railBlocks.Values)
            {
                switch (blockData)
                {
                    case PathRailBlockData pathRailBlockData:
                    {
                        foreach (KeyValuePair<Rail, int> railPair in pathRailBlockData.BlockedRails)
                        {
                            if (railPair.Value > 0)
                                HighlightRail(railPair.Key, color.WithAlpha(0.2f + railPair.Value * 0.2f), 0.42f);
                        }

                        foreach (KeyValuePair<Rail, int> railPair in pathRailBlockData.BlockedLinkedRails)
                        {
                            if (railPair.Value > 0)
                                HighlightRail(railPair.Key, linkedColor.WithAlpha(0.1f + railPair.Value * 0.1f), 0.2f);
                        }

                        foreach (KeyValuePair<Train, PooledHashSet<Rail>> railPair in pathRailBlockData.ReservedBeyondPath)
                        {
                            foreach (Rail rail in railPair.Value)
                            {
                                HighlightRail(rail, Color.blue.WithAlpha(0.9f), 0.44f);
                            }
                        }

                        break;
                    }
                }

                if (blockData is SimpleRailBlockData simpleRailBlockData && simpleRailBlockData.ReservedForTrain != null || blockData.IsFullBlocked)
                {
                    RailBlock block = blockData.Block;
                    UniqueList<RailConnection> connections = Traverse.Create(block).Field<UniqueList<RailConnection>>("Connections").Value;
                    for (int i = connections.Count - 1; i >= 0; i--)
                    {
                        HighlightRail(connections[i].Track, simpleBlockColor.WithAlpha(0.3f), 0.43f);
                    }
                }
            }

            if (ModSettings<Settings>.Current.HighlightReservedPathsExtended)
                HighlightReservedBounds();
        }

        #endregion

        #region HARMONY

        #region SignalStates

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RailSignal), "GetState")]
        // ReSharper disable once InconsistentNaming
        private static bool RailSignal_GetState_prf(RailSignal __instance, ref RailSignalState __result)
        {
            if (Current != null)
            {
                __result = Current.GetSignalState(__instance);
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RailSignal), "InvalidateState")]
        // ReSharper disable once InconsistentNaming
        private static bool RailSignal_InvalidateState_prf(RailSignal __instance)
        {
            if (Current != null)
            {
                return Current._changedStates.Remove(__instance); //if not found, return false to stop further execution of method
            }

            return true;
        }

        #endregion

        #region TrainSignalObstacle

        private static bool _isDetectingObstacle = false;

        private static PathCollection _trainPath = null;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Train), "DetectObstacle")]
        // ReSharper disable once InconsistentNaming
        private static void Train_DetectObstacle_prf(PathCollection ___Path)
        {
            _isDetectingObstacle = true;
            _trainPath = ___Path;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Train), "DetectObstacle")]
        // ReSharper disable once InconsistentNaming
        private static void Train_DetectObstacle_fin()
        {
            _isDetectingObstacle = false;
            _trainPath = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RailSignal), "IsOpen")]
        // ReSharper disable once InconsistentNaming
        private static bool RailSignal_IsOpen_prf(RailSignal __instance, ref bool __result, Train train)
        {
            if (_isDetectingObstacle && Current != null)
            {
                __result = Current.IsSignalOpenForTrain(__instance, train, _trainPath);
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RailDepot), "CanReleaseVehicle")]
        // ReSharper disable once InconsistentNaming
        private static void RailDepot_CanReleaseVehicle_pof(RailDepot __instance, ref bool __result, Vehicle vehicle)
        {
            if (__result == false || ((Train) vehicle).IgnoreSignals)
                return;
            if (Current != null)
            {
                RailBlockData blockData = Current.GetBlockData(((RailConnection) __instance.SpawnConnection).Block);
                if (blockData is PathRailBlockData pathData)
                {
                    if (pathData.IsSomeReservedPath || pathData.IsFullBlocked)
                        __result = false;
                }
            }
        }

        #endregion

        #region TrainMovement
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TrackUnit), "Detach")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Detach_prf(TrackUnit __instance, PathCollection ___Path)
        {
            if (Current != null && __instance is Train train)
            {
//                FileLog.Log("TrainDetaching");
                Current.TrainDetaching(train, ___Path);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Train), "OnConnectionReached")]
        // ReSharper disable once InconsistentNaming
        private static void Train_OnConnectionReached_pof(Train __instance, TrackConnection connection)
        {
            Current?.TrainConnectionReached(__instance, connection);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "Clear")]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_Clear_prf(PathCollection __instance)
        {
//            FileLog.Log("PathCollection.Clear");
            Current?.PathClearing(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "ShrinkRear")]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_ShrinkRear_prf(PathCollection __instance, int newRearIndex)
        {
            Current?.PathShrinkingRear(__instance, newRearIndex);
        }

        private static readonly List<TrackConnection> _oldPath = new();
        private static PathCollection _addedToFront = null;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "ShrinkFront")]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_ShrinkFront_prf(PathCollection __instance, ref int newFrontIndex)
        {
            if (!_canShrinkReservedPath && _origReservedPathIndex > Int32.MinValue)
                newFrontIndex = _origReservedPathIndex;
            Current?.PathShrinkingFront(__instance, ref newFrontIndex);
            _origReservedPathIndex = int.MinValue;
//            FileLog.Log($"Shrink front, new front index {_oldPath.IndexOf(__instance[newFrontIndex])}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "AddToFront")]
        [HarmonyPatch(new Type[] {typeof(IList<TrackConnection>), typeof(int)})]
        // ReSharper disable once InconsistentNaming
        private static bool PathCollection_AddToFront_prf(PathCollection __instance, IList<TrackConnection> connections, int startIndex)
        {
            if (_wasInvalidPath)
            {
                //invalid path between the train and the reserved index = do not add a new path, because it will have inconsistency
                _wasInvalidPath = false;
                return false;
            }
            if (connections.Count > startIndex && !__instance[__instance.FrontIndex].InnerConnection.OuterConnections.Contains(connections[startIndex]))
            {
/*                List<int> indexes = new();
                List<int> indexes2 = new();
                for (int i = __instance.RearIndex; i <= __instance.FrontIndex; i++)
                {
                    indexes.Add(_oldPath.IndexOf(__instance[i]));
                }

                foreach (TrackConnection trackConnection in connections)
                {
                    indexes2.Add(_oldPath.IndexOf(trackConnection));
                }*/
//                FileLog.Log("Remained indexes " + indexes.Join());
//                FileLog.Log("New indexes " + indexes2.Join());

                _nextDestinationResultIdx = null;
                if (Current != null && SimpleLazyManager<TrainHelper>.Current.GetTrainFromPath(__instance, out Train train2))
                {
                    Current._reservedPathIndex.Remove(train2);  //remove any reservation
                }
                throw new InvalidOperationException("Inconsistent add to path front.");
            }
            if (_nextDestinationResultIdx.HasValue && Current != null && SimpleLazyManager<TrainHelper>.Current.GetTrainFromPath(__instance, out Train train))
            {
                (int reservedIdx, int? nextDestinationIdx) idx = Current._reservedPathIndex.GetValueOrDefault(train, (Int32.MinValue, _nextDestinationResultIdx));
                idx.nextDestinationIdx = _nextDestinationResultIdx + __instance.FrontIndex - 1;
//                FileLog.Log($"New NextDestinationIdx {idx.nextDestinationIdx}");
                Current._reservedPathIndex[train] = idx;
            }

            return true;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(PathCollection), "AddToFront")]
        [HarmonyPatch(new Type[] {typeof(IList<TrackConnection>), typeof(int)})]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_AddToFront_fin(PathCollection __instance, IList<TrackConnection> connections, int startIndex)
        {

            _nextDestinationResultIdx = null;
            _origReservedPathIndex = int.MinValue;
            _skipFirstPart = false;
            _nextDestination = null;
            _addedToFront = __instance;
//            Current?.VerifyPath(__instance);
        }

        #endregion

        #region PathUpdate

        private static bool _canShrinkReservedPath = false;

        private static int _origReservedPathIndex;

        private static bool _skipFirstPart;

        private static IVehicleDestination _nextDestination;

        private static TrackConnection _origDestination;

        private static int? _nextDestinationResultIdx;

        private static bool _deleteResultList;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TrackUnit), "Flip")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Flip_prf(TrackUnit __instance)
        {
            if (Current != null && __instance is Train train)
            {
                _canShrinkReservedPath = true;
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(TrackUnit), "Flip")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Flip_fin()
        {
            _canShrinkReservedPath = false;
            _origReservedPathIndex = Int32.MinValue;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        // ReSharper disable once InconsistentNaming
        private static bool Train_TryFindPath_prf(Train __instance, ref TrackConnection origin, IVehicleDestination target, List<TrackConnection> result, ref bool __result, PathCollection ___Path, ref bool __state)
        {
            __state = false;
            _addedToFront = null;
            if (_skipFirstPart && target != _nextDestination)
            {
                result.Clear();
                result.Add(_origDestination);
                _deleteResultList = true;
                __result = true;
                //FileLog.Log($"Skip first part of path rearIndex: {___Path.RearIndex} frontIndex: {___Path.FrontIndex}");
                return false;
            }
            //FileLog.Log($"Train_TryFindPath_prf: can shrink {_canShrinkReservedPath}");
            
            if (!_canShrinkReservedPath && Current != null && origin != __instance.RearBound.Connection.InnerConnection && Current._reservedPathIndex.TryGetValue(__instance, out (int reservedIdx, int? nextDestinationIdx) reserved) &&
                reserved.reservedIdx >= __instance.FrontBound.ConnectionIndex && reserved.reservedIdx >=___Path.RearIndex && reserved.reservedIdx <=___Path.FrontIndex && (_skipFirstPart || target != _nextDestination))
            {
                _origReservedPathIndex = reserved.reservedIdx;
                origin = ___Path[_origReservedPathIndex];
                //FileLog.Log($"Refind path reservedIndex: origin path index {_oldPath.IndexOf(origin)}, {_origReservedPathIndex}, rearIndex: {___Path.RearIndex} frontIndex: {___Path.FrontIndex} nextDestinationIndex {reserved.nextDestinationIdx}");
                _skipFirstPart = false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        // ReSharper disable once InconsistentNaming
        private static void Train_TryFindPath_pof(Train __instance, ref TrackConnection origin, IVehicleDestination target, List<TrackConnection> result)
        {
            //FileLog.Log($"Train_TryFindPath_pof origin index {_oldPath.IndexOf(origin)}, result count {result.Count}");
            if (_deleteResultList)
            {
                result.Clear();
                _deleteResultList = false;
                result.Add(_origDestination);
            } else
            if (_nextDestination != null && target != _nextDestination)
            {
                _nextDestinationResultIdx = result.Count;
                //FileLog.Log($"Next destination result idx: {_nextDestinationResultIdx}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Vehicle), "TryUpdateDestinationAndPath")]
        // ReSharper disable once InconsistentNaming
        private static void Vehicle_TryUpdateDestinationAndPath_prf()
        {
            _addedToFront = null;
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vehicle), "TryUpdateDestinationAndPath")]
        // ReSharper disable once InconsistentNaming
        private static void Vehicle_TryUpdateDestinationAndPath_pof(Vehicle __instance, PathCollection ___Path)
        {
            if (Current != null && _addedToFront==___Path && __instance is Train train)
            {
                Current.AfterUpdateDestinationAndPath(___Path, train);
                _addedToFront = null;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Vehicle), "TryFindPath")]
        [HarmonyPatch(new Type[] {typeof(TrackConnection), typeof(IVehicleDestination), typeof(IVehicleDestination), typeof(List<TrackConnection>), typeof(TrackConnection)}, 
            new ArgumentType[] {ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out})]
        // ReSharper disable once InconsistentNaming
        private static void Vehicle_TryFindPath_prf(Vehicle __instance, IVehicleDestination nextTarget, PathCollection ___Path)
        {
            _nextDestination = nextTarget;
            _nextDestinationResultIdx = null;
            _skipFirstPart = false;
            _origReservedPathIndex = int.MinValue;
            _oldPath.Clear();
            for (int i = ___Path.RearIndex; i <= ___Path.FrontIndex; i++)
            {
                _oldPath.Add(___Path[i]);
            }
            if (__instance is Train train && !_canShrinkReservedPath && Current != null && nextTarget != null && Current._reservedPathIndex.TryGetValue(train, out (int reservedIdx, int? nextDestinationIdx) reserved))
            {
                if (reserved.nextDestinationIdx.HasValue && reserved.nextDestinationIdx <= reserved.reservedIdx && ___Path.FrontIndex >= reserved.nextDestinationIdx && ___Path.RearIndex < reserved.nextDestinationIdx)
                {
                    //search only part from destination to next destination
                    _skipFirstPart = true;
                    _origDestination = ___Path[reserved.nextDestinationIdx.Value];
                    //FileLog.Log($"TryFind for SkipFirstPath, origDestination path index: {_oldPath.IndexOf(_origDestination)}");
                }
            }
        }
        
        #endregion
        
        #endregion
        
    }
}