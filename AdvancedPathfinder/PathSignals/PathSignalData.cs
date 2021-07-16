using System;
using System.Collections.Generic;
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
            if (ReservedForTrain == train)
                ReservedForTrain = null;
        }

        public static bool CheckIsChainSignal([NotNull] RailSignal signal)
        {
            return signal is ChainBlockRailSignal;
        }
    }
}