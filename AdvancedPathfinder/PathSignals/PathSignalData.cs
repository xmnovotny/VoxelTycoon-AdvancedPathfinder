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
                if (value != _reservedForTrain)
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
            if (_reservedForTrain == train)
                ReservedForTrain = null;
        }

        public void TrainPassingSignal(Train train)
        {
            if (_reservedForTrain != train) //train passed signal that is not reserved for it = set block as fully blocked
                BlockData.FullBlock();
        }

        public static bool CheckIsChainSignal([NotNull] RailSignal signal)
        {
            return signal is ChainBlockRailSignal;
        }
    }
}