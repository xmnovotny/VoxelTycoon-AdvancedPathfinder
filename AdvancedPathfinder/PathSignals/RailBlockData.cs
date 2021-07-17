using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using MonoMod.Utils;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    public abstract class RailBlockData
    {
        public readonly Dictionary<RailSignal, PathSignalData> InboundSignals = new();
        public RailBlock Block { get; }
        
        public RailBlockData(RailBlock block)
        {
            Block = block;
            InboundSignals = new Dictionary<RailSignal, PathSignalData>();
        }

        protected RailBlockData(RailBlock block, Dictionary<RailSignal, PathSignalData> inboundSignals)
        {
            Block = block;
            InboundSignals.AddRange(inboundSignals);
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
            if (rail.SignalCount > 0)
            {
                for (int i = 0; i <= 1; i++)
                {
                    RailSignal signal = rail.GetConnection(i).Signal;
                    if (signal == null || !InboundSignals.TryGetValue(signal, out PathSignalData signalData))
                        continue;
                        
                    if (signalData.ReservedForTrain == train)
                        signalData.ReservedForTrain = null;
                }
            }
        }

        internal abstract void ReleaseRailSegment(Train train, Rail rail);
    }
}