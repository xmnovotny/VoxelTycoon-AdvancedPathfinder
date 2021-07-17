using System.Collections.Generic;
using HarmonyLib;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public class SimpleRailBlockData: RailBlockData
    {
        public Train ReservedForTrain { get; private set; }
        public PathSignalData _reservedSignal;
        public SimpleRailBlockData(RailBlock block) : base(block)
        {
        }

        public SimpleRailBlockData(RailBlock block, Dictionary<RailSignal, PathSignalData> inboundSignals): base(block, inboundSignals)
        {
        }

        internal override bool TryReservePath(Train train, PathCollection path, int startIndex, out int reservedIndex)
        {
//            FileLog.Log($"Try reserve simple path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            reservedIndex = 0;
            if (IsFullBlocked || ReservedForTrain != null)
                return false;
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.ReservedForTrain)
                return false;
            ReservedForTrain = train;
            startSignalData.ReservedForTrain = train;
            _reservedSignal = startSignalData;
            reservedIndex = startIndex + 1;
//            FileLog.Log($"Reserved simple block {GetHashCode():X}, signal {startSignalData.GetHashCode():X}");
            return true;
        }

        internal override void ReleaseRailSegment(Train train, Rail rail)
        {
            if (ReservedForTrain == train)
            {
                ReleaseInboundSignal(train, rail);
                if (Block.Value == 0 && _reservedSignal?.ReservedForTrain == null)
                {
//                    FileLog.Log($"Released simple block {GetHashCode():X}");
                    ReservedForTrain = null;
                    _reservedSignal = null;
                    TryFreeFullBlock();
                }
            }
            else
            {
                TryFreeFullBlock();
            }
        }
    }
}