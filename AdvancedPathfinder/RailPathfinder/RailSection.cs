using System;
using System.Collections.Generic;
using AdvancedPathfinder.Helpers;
using VoxelTycoon;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.RailPathfinder
{
    public class RailSection: TrackSection<Rail, RailConnection>
    {
        internal const float ClosedBlockMult = 1.5f;
        internal const float ClosedBlockPlatformMult = 10f;
        private readonly RailSectionData _data = new();
        private readonly Dictionary<RailBlock, float> _railBlocksLengths = new();
        private readonly Dictionary<RailBlock, int> _railBlocksStates = new(); //0 = free block, otherwise block is blocked
        private float? _closedBlockLength;
        internal float? CachedClosedBlockLength => _closedBlockLength;
        private RailSignal _lastSignalForward;
        private RailSignal _lastSignalBackward;

        public RailSectionData Data => _data;

        public float GetClosedBlocksLength()
        {
            if (_closedBlockLength.HasValue)
                return _closedBlockLength.Value;
            
            float result = 0f;
            ImmutableUniqueList<RailConnection> connList = ConnectionList.ToImmutableUniqueList();
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

        internal override void OnSectionRemoved()
        {
            base.OnSectionRemoved();
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.UnregisterBlockStateAction(block, OnBlockFreeConditionStateChange);
            }
        }

        protected override void FillSetup()
        {
            base.FillSetup();
            Data.Reset();
        }

        protected override void FinalizeFill()
        {
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.RegisterBlockStateAction(block, OnBlockFreeConditionStateChange);
            }
        }

        protected override void ProcessTrack(Rail track, RailConnection startConnection)
        {
            base.ProcessTrack(track, startConnection);
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