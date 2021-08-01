using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AdvancedPathfinder.UI;
using HarmonyLib;
using JetBrains.Annotations;
using ModSettingsUtils;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    [HarmonyPatch]
    public class PathSignalManager : SimpleManager<PathSignalManager>
    {
        //TODO: adjust reserved path index after removing track within reserved path
        //TODO: Fix reservation of last segment when reserving instead of beyond path
        //TODO: Fix correct platform penalty when there is a path block within platform
        //TODO: Test removing rail segment within reserved path
        private readonly Dictionary<RailSignal, PathSignalData> _pathSignals = new();
        private readonly Dictionary<RailBlock, RailBlockData> _railBlocks = new();
        private readonly HashSet<RailSignal> _changedStates = new(); //list of signals with changed states (for performance)
        private readonly Dictionary<Train, RailSignal> _passedSignals = new(); //for delay of passing signal 
        private readonly Dictionary<PathCollection, Train> _pathToTrain = new();
        private bool _highlightDirty = true;

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

        public bool IsBlockOpen(RailBlock block)
        {
            if (_railBlocks.TryGetValue(block, out RailBlockData data))
            {
                return data.IsBlockFree;
            }

            return block.IsOpen;
        }

        [NotNull]
        internal PathSignalData GetPathSignalData(RailSignal signal)
        {
            if (!_pathSignals.TryGetValue(signal, out PathSignalData signalData) || signalData == null)
                throw new InvalidOperationException("Signal data not found");
            return signalData;
        }

        protected override void OnInitialize()
        {
            Behaviour.OnLateUpdateAction -= OnLateUpdate;
            Behaviour.OnLateUpdateAction += OnLateUpdate;
            Stopwatch sw = Stopwatch.StartNew();
            ModSettings<Settings>.Current.Subscribe(OnSettingsChanged);
            FindBlocksAndSignals();
            sw.Stop();
            SimpleLazyManager<RailBlockHelper>.Current.RegisterBlockCreatedAction(BlockCreated);
            SimpleLazyManager<RailBlockHelper>.Current.RegisterBlockRemovingAction(BlockRemoving);
            FileLog.Log(string.Format("Path signals initialized in {0:N3}ms, found signals: {1:N0}, found blocks: {2:N0}", sw.ElapsedTicks / 10000f, _pathSignals.Count, _railBlocks.Count));
        }

        protected override void OnDeinitialize()
        {
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.UnregisterBlockCreatedAction(BlockCreated);
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.UnregisterBlockRemovingAction(BlockRemoving);
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
            if (_highlightDirty)
                HighlightReservedPaths();
        }

        private void TrainPassedSignal(Train train, RailSignal signal)
        {
//            FileLog.Log("Train passed signal");
            if (!_pathSignals.TryGetValue(signal, out PathSignalData data))
                throw new InvalidOperationException("No data for signal.");
            data.TrainPassedSignal(train);
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
        }

        private bool IsSimpleBlock(RailBlockData blockData)
        {
                using PooledHashSet<RailSignal> signalsToCheck = PooledHashSet<RailSignal>.Take();
                int inbCount = blockData.InboundSignals.Count;
                if (inbCount == 0)
                {
                    return true;
                }

                foreach (RailSignal signal in blockData.InboundSignals.Keys)
                {
                    if (PathSignalData.CheckIsChainSignal(signal)) //some of inbound signals are chain = no simple block
                        return false;
                    ;
                }

                signalsToCheck.Clear();
                signalsToCheck.UnionWith(blockData.InboundSignals.Keys);
                while (signalsToCheck.Count > 0)
                {
                    RailSignal signal = signalsToCheck.First();
                    signalsToCheck.Remove(signal);
                    RailConnection connection = signal.Connection.InnerConnection;
                    while (true)
                    {
                        if (connection.OuterConnectionCount > 1)
                        {
                            return false;
                        }

                        if (connection.OuterConnectionCount == 0) //end of track
                            break;

                        connection = (RailConnection) connection.OuterConnections[0];
                        if (connection.OuterConnectionCount > 1)
                        {
                            return false;
                        }

                        connection = connection.InnerConnection;

                        if (connection.Signal != null)
                        {
                            //opposite signal - we can safely remove it from testing
                            signalsToCheck.Remove(connection.Signal);
                            break;
                        }

                        if (connection.InnerConnection.Signal != null) //next signal, end of searching path of this signal
                            break;
                    }
                }

                return true;
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
                data.RegisterBlockFreeChanged(OnBlockFreeChanged);    
                _railBlocks.Add(block, data);
            }

            if (data is not PathRailBlockData pathData)
                throw new InvalidOperationException("RailBlockData contains SimpleBlockData");

            return pathData;
        }

        private void OnBlockFreeChanged(RailBlockData data, bool isFree)
        {
            //FileLog.Log($"OnBlockFreeChanged: {isFree}, ({data.GetHashCode():X8})");
            SimpleLazyManager<RailBlockHelper>.CurrentWithoutInit?.PathSignalBlockFreeChanged(data.Block, isFree);
        }

        private void OnSignalStateChanged(PathSignalData signalData)
        {
            _changedStates.Add(signalData.Signal);
        }

        private bool IsSignalOpenForTrain(RailSignal signal, Train train, PathCollection path)
        {
            _pathToTrain[path] = train;
            PathSignalData signalData = GetPathSignalData(signal);
//            FileLog.Log($"IsSignalOpenForTrain, train: {train.GetHashCode():X8}, signal: {signalData.GetHashCode():X8}");
            if (signalData.ReservedForTrain == train)
                return true;
            if (signalData.ReservedForTrain != null)
            {
                //it should not be
                FileLog.Log("Signal is reserved for another train.");
                return false;
            }

            RailConnection conn = signal.Connection;
            int? pathIndex = path.FindConnectionIndex(conn, train.FrontBound.ConnectionIndex);
            if (pathIndex == null)
            {
//                throw new InvalidOperationException("Signal connection not found in the path");
                FileLog.Log("Signal connection not found in the path");
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

            HighlightReservedPaths();
            return result;
        }

        private void TrainConnectionReached(Train train, TrackConnection connection)
        {
            if (_passedSignals.TryGetValue(train, out RailSignal signal))
            {
                TrainPassedSignal(train, signal);
                _passedSignals.Remove(train);
            }

            RailConnection railConn = (RailConnection) connection;
            if (railConn.Signal != null)
            {
                _passedSignals.Add(train, railConn.Signal);
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
            if (path.RearIndex >= newRearIndex || !_pathToTrain.TryGetValue(path, out Train train))
                return;
            PathShrinking(train, path, path.RearIndex, newRearIndex - 1);
        }

        private void PathShrinkingFront(PathCollection path, int newFrontIndex)
        {
            if (path.FrontIndex <= newFrontIndex || !_pathToTrain.TryGetValue(path, out Train train))
                return;
            (int reservedIdx, int? nextDestinationIdx) reservedPathIndex = _reservedPathIndex.GetValueOrDefault(train, (int.MinValue, null));
//            FileLog.Log($"Path front shrinking {train.GetHashCode():X8}, newFrontIndex {newFrontIndex}, origFrontIndex {path.FrontIndex}, reservedIndex {reservedPathIndex.reservedIdx}");
            PathShrinking(train, path, newFrontIndex + 1, path.FrontIndex, reservedPathIndex.reservedIdx);
            if (reservedPathIndex.reservedIdx > newFrontIndex || reservedPathIndex.nextDestinationIdx > newFrontIndex)
            {
                if (reservedPathIndex.nextDestinationIdx > newFrontIndex)
                    reservedPathIndex.nextDestinationIdx = null;
                if (reservedPathIndex.reservedIdx > newFrontIndex)
                    reservedPathIndex.reservedIdx = newFrontIndex;
//                FileLog.Log($"Shrink reserved index: old {_reservedPathIndex[train].reservedIdx} new {newFrontIndex}");
                _reservedPathIndex[train] = reservedPathIndex;
            }
        }

        private void PathShrinking(Train train, PathCollection path, int from, int to, int reservedIndex = Int32.MinValue) //indexes from and to are inclusive 
        {
            //TODO: Rework releasing path using connection instead of track, so duplicity check will not be necessary
            bool changed = false;
            RailBlockData currBlockData = null;
            using PooledHashSet<Track> usedTracks = PooledHashSet<Track>.Take();
            for (int index = path.RearIndex; index < from; index++)
            {
                usedTracks.Add(path[index].Track);
            }
            for (int index = from; index <= to; index++)
            {
                changed = true;
                RailConnection currConnection = (RailConnection) path[index];
                if (!usedTracks.Add(currConnection.Track))
                {
                    //path has duplicity connection, this is second round = skip releasing segments
                    break;
                }
                if (currBlockData?.Block != currConnection.Block)
                {
                    currBlockData = GetBlockData(currConnection.Block);
                }

                if (currBlockData != null)
                {
                    currBlockData.ReleaseRailSegment(train, currConnection.Track);
                    if (reservedIndex >= index)
                        currBlockData.FullBlock();
                }

                currConnection = currConnection.InnerConnection;
                if (currBlockData?.Block != currConnection.Block)
                {
                    currBlockData = GetBlockData(currConnection.Block);
                    currBlockData.ReleaseRailSegment(train, currConnection.Track);
                    if (reservedIndex >= index)
                        currBlockData.FullBlock();
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

        private void TrainAttached(PathCollection path)
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
            if (path.Count > 0 && _pathToTrain.TryGetValue(path, out Train train))
            {
                PathShrinking(train, path, path.RearIndex, path.FrontIndex);
                _pathToTrain.Remove(path);
            }
        }

        private void DeleteTrainData(Train train)
        {
            _passedSignals.Remove(train);
            _reservedPathIndex.Remove(train);
        }

        private void TrainDetached(Train train)
        {
            DeleteTrainData(train);
            TryReleaseFullBlock();
            HighlightReservedPaths();
        }

        private void BlockRemoving(RailBlock block)
        {
            if (_railBlocks.TryGetValue(block, out RailBlockData data))
            {
                FileLog.Log($"Block removing {data.GetHashCode():X8}");
                using PooledList<Train> passed = PooledList<Train>.Take(); 
                foreach (RailSignal signal in data.InboundSignals.Keys)
                {
                    _pathSignals.Remove(signal);
                    FileLog.Log($"Removed signal {signal.GetHashCode():X8}");
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

        private void BlockCreated(RailBlock block)
        {
            UniqueList<RailConnection> connections = Traverse.Create(block).Field<UniqueList<RailConnection>>("Connections").Value;
            PathRailBlockData blockData = GetOrCreateRailBlockData(block);
            FileLog.Log($"Block created {blockData.GetHashCode():X8}");
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                RailConnection conn = connections[i];
                RailSignal signal = conn.InnerConnection.Signal;
                if (signal != null && signal.IsBuilt)
                {
                    blockData.InboundSignals.Add(signal, null);
                    FileLog.Log($"Added signal {signal.GetHashCode():X8}");
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
                FileLog.Log($"Is a new simple block {newBlockData.GetHashCode():X8}");
            }
            
            CreatePathSignalData(newBlockData);
            HighlightReservedPaths();
        }

        private void AfterUpdateDestinationAndPath(PathCollection path, Train train)
        {
            RailConnection conn = (RailConnection) train.FrontBound.Connection;

            if (conn == null || conn.Block == null) return;

            if (_railBlocks.GetValueOrDefault(conn.Block) is PathRailBlockData blockData)
            {
                int? reservedIndex = blockData.TryReserveUpdatedPathInsteadOfBeyond(train, path);
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
            RailConnectionHighlighter man = LazyManager<RailConnectionHighlighter>.Current;
            _highlighters.Add(man.ForOneTrack(rail, color, halfWidth));
        }

        private void HighlightReservedBounds()
        {
            foreach (KeyValuePair<PathCollection, Train> pair in _pathToTrain)
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
                        HighlightRail((Rail) conn.Track, Color.blue, 0.8f);
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

                        foreach (KeyValuePair<Train, (PooledHashSet<Rail> rails, Rail lastPathRail)> railPair in pathRailBlockData.ReservedBeyondPath)
                        {
                            foreach (Rail rail in railPair.Value.rails)
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

        #endregion

        #region TrainMovement

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "Attach")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Attach_pof(TrackUnit __instance, PathCollection ___Path)
        {
            if (Current != null && __instance is Train train)
            {
                Current.TrainAttached(___Path);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "Detach")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Detach_pof(TrackUnit __instance)
        {
            if (Current != null && __instance is Train train)
            {
                Current.TrainDetached(train);
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "ShrinkFront")]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_ShrinkFront_prf(PathCollection __instance, ref int newFrontIndex)
        {
            if (!_canShrinkReservedPath && _origReservedPathIndex > Int32.MinValue)
                newFrontIndex = _origReservedPathIndex;
            Current?.PathShrinkingFront(__instance, newFrontIndex);
            _origReservedPathIndex = int.MinValue;
//            FileLog.Log($"Shrink front, new front index {_oldPath.IndexOf(__instance[newFrontIndex])}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PathCollection), "AddToFront")]
        [HarmonyPatch(new Type[] {typeof(IList<TrackConnection>), typeof(int)})]
        // ReSharper disable once InconsistentNaming
        private static void PathCollection_AddToFront_prf(PathCollection __instance, IList<TrackConnection> connections, int startIndex)
        {
            if (connections.Count > startIndex && !__instance[__instance.FrontIndex].InnerConnection.OuterConnections.Contains(connections[startIndex]))
            {
                List<int> indexes = new();
                List<int> indexes2 = new();
                for (int i = __instance.RearIndex; i <= __instance.FrontIndex; i++)
                {
                    indexes.Add(_oldPath.IndexOf(__instance[i]));
                }

                foreach (TrackConnection trackConnection in connections)
                {
                    indexes2.Add(_oldPath.IndexOf(trackConnection));
                }
                FileLog.Log("Remained indexes " + indexes.Join());
                FileLog.Log("New indexes " + indexes2.Join());

                _nextDestinationResultIdx = null;
                if (Current != null && Current._pathToTrain.TryGetValue(__instance, out Train train2))
                {
                    Current._reservedPathIndex.Remove(train2);  //remove any reservation
                }
                throw new InvalidOperationException("Inconsistent add to path front.");
            }
            if (_nextDestinationResultIdx.HasValue && Current != null && Current._pathToTrain.TryGetValue(__instance, out Train train))
            {
                (int reservedIdx, int? nextDestinationIdx) idx = Current._reservedPathIndex.GetValueOrDefault(train, (Int32.MinValue, _nextDestinationResultIdx));
                idx.nextDestinationIdx = _nextDestinationResultIdx + __instance.FrontIndex - 1;
//                FileLog.Log($"New NextDestinationIdx {idx.nextDestinationIdx}");
                Current._reservedPathIndex[train] = idx;
            }
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vehicle), "TryUpdateDestinationAndPath")]
        // ReSharper disable once InconsistentNaming
        private static void Vehicle_TryUpdateDestinationAndPath_pof(Vehicle __instance, PathCollection ___Path)
        {
            if (Current != null && __instance is Train train)
            {
                Current.AfterUpdateDestinationAndPath(___Path, train);
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