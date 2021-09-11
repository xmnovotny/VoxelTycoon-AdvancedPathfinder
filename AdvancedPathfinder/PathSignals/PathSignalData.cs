using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    public class PathSignalData
    {
        public RailSignal Signal { get; }
        public RailBlockData BlockData { get; }
        public bool IsChainSignal { get; }

        public Action<PathSignalData> StateChanged;
        private Train _reservedForTrain;
        private readonly RailSignal _oppositeSignal;
        private PathSignalData _oppositeSignalData;
        private HashSet<Train> _preReservedForTrains;

        public PathSignalData OppositeSignalData
        {
            get
            {
                if (_oppositeSignalData == null && HasOppositeSignal)
                {
                    _oppositeSignalData = SimpleManager<PathSignalManager>.Current!.GetPathSignalData(_oppositeSignal);
                }

                return _oppositeSignalData;
            }
        }

        public readonly bool HasOppositeSignal;
        public bool IsPreReserved => _preReservedForTrains?.Count > 0;

        public bool IsPreReservedForTrain(Train train)
        {
            return _preReservedForTrains?.Contains(train) == true;
        }
        
        public Train ReservedForTrain
        {
            get => _reservedForTrain;
            internal set
            {
                if (!ReferenceEquals(value,_reservedForTrain))
                {
                    _reservedForTrain = value;
                    if (ReferenceEquals(value, null))
                        SimpleManager<PathSignalManager>.Current!.OpenedSignals.Remove(Signal);
                    else
                    {
                        _preReservedForTrains?.Remove(value);
                        if (_preReservedForTrains?.Count == 0)
                            SimpleLazyManager<PathSignalHighlighter>.Current.HighlightPreReservedSignal(Signal, false);
                        SimpleManager<PathSignalManager>.Current!.OpenedSignals[Signal] = value;
                    }

                    StateChanged?.Invoke(this);
                }
            }
        }

        public PathSignalData([NotNull] RailSignal signal,[NotNull] RailBlockData blockData)
        {
            Signal = signal;
            BlockData = blockData;
            IsChainSignal = CheckIsChainSignal(signal);
            _oppositeSignal = signal.Connection.InnerConnection.Signal;
            if (_oppositeSignal != null)
            {
                _preReservedForTrains = new HashSet<Train>();
                HasOppositeSignal = true;
            }
        }

        public void PreReserveForTrain(Train train)
        {
            if (_preReservedForTrains == null)
                _preReservedForTrains = new HashSet<Train>();
            bool oldIsPreReserved = _preReservedForTrains.Count > 0;
            _preReservedForTrains.Add(train);
            if (!oldIsPreReserved)
                SimpleLazyManager<PathSignalHighlighter>.Current.HighlightPreReservedSignal(Signal, true);
        }

        public void RemovePreReservation(Train train)
        {
            _preReservedForTrains?.Remove(train);
            if (_preReservedForTrains?.Count == 0)
                SimpleLazyManager<PathSignalHighlighter>.Current.HighlightPreReservedSignal(Signal, false);
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