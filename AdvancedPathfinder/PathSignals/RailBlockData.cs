using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using MonoMod.Utils;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    public abstract class RailBlockData
    {
        private Action<RailBlockData, bool> _blockFreeChangedEvent;
        public readonly Dictionary<RailSignal, PathSignalData> InboundSignals = new();
        private bool _isFullBlocked;
        public RailBlock Block { get; }

        public bool IsFullBlocked
        {
            get => _isFullBlocked;
            internal set
            {
                if (_isFullBlocked != value)
                {
                    _isFullBlocked = value;
                    OnBlockFreeConditionChanged(_isFullBlocked == false);
                    GetHighlighter()?.FullBlockChange(this);
                }
            }
        } //no individual path reservation allowed, clears when whole block becomes free of vehicles

        public virtual int BlockBlockedCount => _isFullBlocked ? 1 : 0;

        public RailBlockData(RailBlock block): this(block, true)
        {
            InboundSignals = new Dictionary<RailSignal, PathSignalData>();
        }

        public void RegisterBlockFreeConditionChanged(Action<RailBlockData, bool> onBlockFreeConditionChanged)
        {
            _blockFreeChangedEvent -= onBlockFreeConditionChanged;
            _blockFreeChangedEvent += onBlockFreeConditionChanged;
        }

        public void UnregisterBlockFreeConditionChanged(Action<RailBlockData, bool> onBlockFreeConditionChanged)
        {
            _blockFreeChangedEvent -= onBlockFreeConditionChanged;
        }

        /** change of one condition of free block (for cumulative blocked state counting) */
        protected void OnBlockFreeConditionChanged(bool isFree)
        {
            //FileLog.Log("RailBlockData.OnBlockFreeChanged");
            _blockFreeChangedEvent?.Invoke(this, isFree);
        }

        protected RailBlockData(RailBlockData blockData): this(blockData.Block)
        {
            InboundSignals.AddRange(blockData.InboundSignals);
            _blockFreeChangedEvent = blockData._blockFreeChangedEvent;
        }
        
        private RailBlockData(RailBlock block, bool _)
        {
            Block = block;
            //IsFullBlocked = block.Value != 0;
        }

        internal void FullBlock()
        {
            if (!IsFullBlocked)
                IsFullBlocked = Block.Value != 0;
        }

        internal abstract bool TryReservePath([NotNull] Train train, [NotNull] PathCollection path, int startIndex,
            out int reservedIndex);

        protected PathSignalData GetAndTestStartSignal(PathCollection path, int startIndex)
        {
            RailSignal startSignal = ((RailConnection) path[startIndex]).Signal;
            if (ReferenceEquals(startSignal, null))
                throw new InvalidOperationException("No signal on path start index");
            if (!InboundSignals.TryGetValue(startSignal, out PathSignalData data))
                throw new InvalidOperationException("Signal isn't in the inbound signal list");

            return data;
        }

        protected void ReleaseInboundSignal(Train train, Rail rail)
        {
//            FileLog.Log($"TryReleaseInboundSignal rail:{rail.GetHashCode():X8}");
            if (rail.SignalCount > 0)
            {
                for (int i = 0; i <= 1; i++)
                {
                    RailSignal signal = rail.GetConnection(i).Signal;
                    if (ReferenceEquals(signal, null) || !signal.IsBuilt || !InboundSignals.TryGetValue(signal, out PathSignalData signalData))
                        continue;
                        
                    if (ReferenceEquals(signalData.ReservedForTrain, train))
                    {
                        signalData.ReservedForTrain = null;
//                        FileLog.Log($"ReleasedInboundSignal {signalData.GetHashCode():X8}");
                    }
                    else
                    {
                        if (!ReferenceEquals(signalData.ReservedForTrain, null))
                        {
//                            FileLog.Log("Reserved for another train");
                            AdvancedPathfinderMod.Logger.Log("Reserved for another train when releasing inbound signal");
                        }
                    }

                }
            }
        }

        public void TryFreeFullBlock()
        {
            if (!IsFullBlocked || Block.Value != 0)
                return;
            foreach (PathSignalData signalData in InboundSignals.Values)
            {
                if (!ReferenceEquals(signalData.ReservedForTrain, null))
                    return;
            }

            IsFullBlocked = false;
        }

        internal abstract void ReleaseRailSegment(Train train, Rail rail);

        [CanBeNull]
        private protected PathSignalHighlighter GetHighlighter()
        {
            PathSignalHighlighter highMan = SimpleLazyManager<PathSignalHighlighter>.Current;
            return highMan.HighlightPaths ? highMan : null;
        }
    }
}