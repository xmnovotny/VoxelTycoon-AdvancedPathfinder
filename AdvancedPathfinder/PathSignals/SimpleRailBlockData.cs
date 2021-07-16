using System.Collections.Generic;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public class SimpleRailBlockData: RailBlockData
    {
        public bool IsReserved { get; private set; }
        public SimpleRailBlockData(RailBlock block) : base(block)
        {
        }

        public SimpleRailBlockData(RailBlock block, Dictionary<RailSignal, PathSignalData> inboundSignals): base(block, inboundSignals)
        {
        }

        internal override bool TryReservePath(Train train, PathCollection path, int startIndex)
        {
            if (IsReserved)
                return false;
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.ReservedForTrain)
                return false;
            IsReserved = true;
            startSignalData.ReservedForTrain = train;
            return true;
        }

        internal override void ReleaseRailSegment(Train train, Rail rail)
        {
            if (Block.Value == 0)
                IsReserved = false;
        }
    }
}