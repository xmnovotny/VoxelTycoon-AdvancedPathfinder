using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public class PathSignalData
    {
        public RailSignal Signal { get; }
        public RailBlockData BlockData { get; }
        public bool IsChainSignal { get; }

        public Action<PathSignalData> StateChanged;
        private Train _reservedForTrain;

        public Train ReservedForTrain
        {
            get => _reservedForTrain;
            internal set
            {
                if (!ReferenceEquals(value,_reservedForTrain))
                {
/*                    if (value == null )
                        FileLog.Log($"ReleasedSignal {GetHashCode():X8}");
                    else
                        FileLog.Log($"ReservedSignal {GetHashCode():X8}");*/
                    _reservedForTrain = value;
                    StateChanged?.Invoke(this);
                }
            }
        }

        public PathSignalData([NotNull] RailSignal signal,[NotNull] RailBlockData blockData)
        {
            Signal = signal;
            BlockData = blockData;
            IsChainSignal = CheckIsChainSignal(signal);
        }

        public RailSignalState GetSignalState()
        {
            return ReservedForTrain != null ? RailSignalState.Green : RailSignalState.Red;
        }

        public void TrainPassedSignal(Train train)
        {
            if (!ReferenceEquals(_reservedForTrain, train))
                BlockData.FullBlock(); //train passed signal that is not reserved for it = set block as fully blocked
            else
                ReservedForTrain = null;
        }

        public void TrainPassingSignal(Train train)
        {
            if (_reservedForTrain != train && train.IgnoreSignals) //train passed signal that is not reserved for it while have ignore signals turned on = set block as fully blocked (if no ignoring signal, it may be only at low-fps entering track with signal without passing it)
                BlockData.FullBlock();
        }

        public static bool CheckIsChainSignal([NotNull] RailSignal signal)
        {
            return signal is ChainBlockRailSignal;
        }
    }
}