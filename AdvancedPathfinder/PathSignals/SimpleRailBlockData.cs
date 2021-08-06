using System.Collections.Generic;
using HarmonyLib;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public class SimpleRailBlockData: RailBlockData
    {
        public Train ReservedForTrain
        {
            get => _reservedForTrain;
            private set
            {
                if (_reservedForTrain != value)
                {
                    _reservedForTrain = value;
                    OnBlockFreeConditionChanged(ReferenceEquals(_reservedForTrain, null));
                }
            }
        }

        public override int BlockBlockedCount => base.BlockBlockedCount + (ReferenceEquals(_reservedForTrain, null) ? 0 : 1);
        private PathSignalData _reservedSignal;
        private Train _reservedForTrain;

        public SimpleRailBlockData(RailBlock block) : base(block)
        {
        }

        internal SimpleRailBlockData(RailBlockData blockData): base(blockData)
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
 //           FileLog.Log($"Releasing simple block {GetHashCode():X}");
            if (ReferenceEquals(ReservedForTrain, train))
            {
                ReleaseInboundSignal(train, rail);
                if ((Block.Value == 0 || IsFullBlocked) && _reservedSignal?.ReservedForTrain == null)
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