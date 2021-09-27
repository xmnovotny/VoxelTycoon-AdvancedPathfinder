using System.Collections.Generic;
using AdvancedPathfinder.Helpers;
using HarmonyLib;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;
using static AdvancedPathfinder.Helpers.RailBlockHelper;
using static AdvancedPathfinder.PathSignals.PathSignalManager;

namespace AdvancedPathfinder.PathSignals
{
    public class SimpleRailBlockData: RailBlockData
    {
        
        private readonly Dictionary<PathSignalData, PathSignalData> _outboundForInbound = new();  //outbound signal for inbound signal
        
        public Train ReservedForTrain
        {
            get => _reservedForTrain;
            private set
            {
                if (!ReferenceEquals(_reservedForTrain, value))
                {
                    _reservedForTrain = value;
                    GetHighlighter()?.FullBlockChange(this);
                    OnBlockFreeConditionChanged(ReferenceEquals(_reservedForTrain, null));
                }
            }
        }

        public override int BlockBlockedCount => base.BlockBlockedCount + (ReferenceEquals(_reservedForTrain, null) ? 0 : 1);
        private PathSignalData _reservedSignal;
        private Train _reservedForTrain;
        private int _tracksCount;

        private int TracksCount
        {
            get {
                if (_tracksCount == 0)
                {
                    _tracksCount = SimpleLazyManager<RailBlockHelper>.Current.GetBlockConnections(Block).Count / 2;
                }

                return _tracksCount;
            }
        }

        public SimpleRailBlockData(RailBlock block) : base(block)
        {
        }

        internal SimpleRailBlockData(RailBlockData blockData): base(blockData)
        {
            
        }
        
        protected internal override bool TryReservePathInternal(Train train, PathCollection path, int startIndex, ReserveResult reserveResult, bool onlyPreReservation = false)
        {
//            FileLog.Log($"Try reserve simple path, train: {train.GetHashCode():X8}, block: {GetHashCode():X8}");
            if (IsFullBlocked)
                return false;
            if (onlyPreReservation)
            {
                PathSignalData startSignalData2 = GetAndTestStartSignal(path, startIndex);
                if (startSignalData2.HasOppositeSignal)
                {
                    if (startSignalData2.IsPreReservedForTrain(train))
                        return true;
                    
                    if (startSignalData2.OppositeSignalData.IsPreReserved)
                    {
                        reserveResult.PreReservationFailed = true;
                        return false;
                    }

                    reserveResult.AddSignalToPreReserve(startSignalData2);

                    int nextIndex = startIndex + TracksCount;
                    if (!path.ContainsIndex(nextIndex))
                        return true;

                    RailConnection conn = (RailConnection) path[nextIndex];
                    if (conn == null)
                        return false; //invalid path
                    RailBlockData blockData = SimpleManager<PathSignalManager>.Current!.RailBlocks[conn.InnerConnection.Block];
                    return blockData.TryReservePathInternal(train, path, startIndex + TracksCount, reserveResult, true);
                }

                return true;
            }
            reserveResult.ReservedIndex = startIndex + 1;
            if (ReservedForTrain != null)
                return false;
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.ReservedForTrain)
                return false;
            ReservedForTrain = train;
            startSignalData.ReservedForTrain = train;
            _reservedSignal = startSignalData;
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
                RemovePreReservation(train, rail);
                TryFreeFullBlock();
            }
        }
    }
}