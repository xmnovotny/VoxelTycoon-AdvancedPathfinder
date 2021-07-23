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
                    bool lastIsBlockFree = IsBlockFree;
                    _isFullBlocked = value;
                    if (lastIsBlockFree != IsBlockFree)
                        OnBlockFreeChanged(!lastIsBlockFree);
                }
            }
        } //no individual path reservation allowed, clears when whole block becomes free of vehicles

        public virtual bool IsBlockFree => Block.Value == 0 && _isFullBlocked == false;

        public RailBlockData(RailBlock block): this(block, true)
        {
            InboundSignals = new Dictionary<RailSignal, PathSignalData>();
        }

        public void RegisterBlockFreeChanged(Action<RailBlockData, bool> onBlockFreeChanged)
        {
            _blockFreeChangedEvent -= onBlockFreeChanged;
            _blockFreeChangedEvent += onBlockFreeChanged;
        }

        public void UnregisterBlockFreeChanged(Action<RailBlockData, bool> onBlockFreeChanged)
        {
            _blockFreeChangedEvent -= onBlockFreeChanged;
        }

        protected void OnBlockFreeChanged(bool isFree)
        {
            _blockFreeChangedEvent?.Invoke(this, isFree);
        }

        protected RailBlockData(RailBlock block, Dictionary<RailSignal, PathSignalData> inboundSignals): this(block, true)
        {
            InboundSignals.AddRange(inboundSignals);
        }
        private RailBlockData(RailBlock block, bool _)
        {
            Block = block;
            IsFullBlocked = block.Value != 0;
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
            if (startSignal == null)
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
                    if (signal == null || !InboundSignals.TryGetValue(signal, out PathSignalData signalData))
                        continue;
                        
                    if (signalData.ReservedForTrain == train)
                    {
                        signalData.ReservedForTrain = null;
//                        FileLog.Log($"ReleasedInboundSignal {signalData.GetHashCode():X8}");
                    }
                    else
                    {
                        if (signalData.ReservedForTrain != null)
                            FileLog.Log("Reserved for another train");
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
                if (signalData.ReservedForTrain != null)
                    return;
            }

            IsFullBlocked = false;
        }

        internal abstract void ReleaseRailSegment(Train train, Rail rail);
    }
}