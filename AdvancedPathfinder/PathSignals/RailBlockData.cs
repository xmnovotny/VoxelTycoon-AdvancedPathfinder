using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using MonoMod.Utils;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

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

        internal abstract bool TryReservePath([NotNull] Train train, [NotNull] PathCollection path, int startIndex);

        protected PathSignalData GetAndTestStartSignal(PathCollection path, int startIndex)
        {
            RailSignal startSignal = ((RailConnection) path[startIndex]).Signal;
            if (startSignal == null)
                throw new InvalidOperationException("No signal on path start index");
            if (!InboundSignals.TryGetValue(startSignal, out PathSignalData data))
                throw new InvalidOperationException("Signal isn't in the inbound signal list");

            return data;
        }
    }
}