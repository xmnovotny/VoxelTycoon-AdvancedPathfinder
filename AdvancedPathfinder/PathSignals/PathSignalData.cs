using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Serialization;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    [SchemaVersion(1)]
    public class PathSignalData
    {
        public RailSignal Signal { get; }
        public RailBlockData BlockData { get; }
        public bool IsChainSignal { get; }

        public Action<PathSignalData> StateChanged;
        private Train _reservedForTrain;
        private RailSignal _oppositeSignal;
        private PathSignalData _oppositeSignalData;
        private HashSet<Train> _preReservedForTrains;
        
        public Train BlockedForTrain { get; private set; } //path to the opposite signal is blocked (as a result after loading a game with bi-directional single track)

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

        public bool HasOppositeSignal { get; private set; }
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
                        BlockedForTrain = null;
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
            if (ReferenceEquals(train, BlockedForTrain))
                BlockedForTrain = null;
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

        internal void OppositeSignalRemoved()
        {
            if (HasOppositeSignal)
            {
                _oppositeSignal = null;
                _oppositeSignalData = null;
                _preReservedForTrains?.Clear();
                _preReservedForTrains = null;
                HasOppositeSignal = false;
            }
        }

        private void AssignOppositeSignal([NotNull] RailSignal oppositeSignal, [NotNull] PathSignalData oppoSignalData)
        {
            if (oppositeSignal == null) throw new ArgumentNullException(nameof(oppositeSignal));
            if (oppoSignalData == null) throw new ArgumentNullException(nameof(oppoSignalData));
            if (oppoSignalData == this) throw new ArgumentException("Cannot assign own data as a opposite data");
            if (HasOppositeSignal && !ReferenceEquals(_oppositeSignal, oppositeSignal) || _oppositeSignalData != null)
                OppositeSignalRemoved();
            
            _oppositeSignal = oppositeSignal;
            _oppositeSignalData = oppoSignalData;
            HasOppositeSignal = true;
        }

        /** try assign own signal data to the opposite signal data */
        internal void TryUpdateOppositeSignalData()
        {
            if (HasOppositeSignal)
            {
                PathSignalData signalData = SimpleManager<PathSignalManager>.Current?.TryGetPathSignalData(_oppositeSignal);
                if (signalData != null)
                {
                    signalData.AssignOppositeSignal(_oppositeSignal, this);
                }
            }
        }

        internal void Write(StateBinaryWriter writer)
        {
            //only writing when there is a opposite signal and there is pre-reservation or is blocked for a train
            writer.WriteInt(_preReservedForTrains.Count);
            foreach (Train train in _preReservedForTrains)
            {
                writer.WriteInt(train.Id);
            }
            //write if block path to the opposite signal (= there is a reserved path within this signal)
            if (!ReferenceEquals(BlockedForTrain, null) && BlockedForTrain.IsAttached)
            {
                writer.WriteInt(BlockedForTrain.Id);
            } else
            if (!ReferenceEquals(ReservedForTrain, null) && ReservedForTrain.IsAttached)
            {
                writer.WriteInt(ReservedForTrain.Id);
            } else 
                writer.WriteInt(0);
        }

        internal void Read(StateBinaryReader reader)
        {
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt();
                if (id == 0)
                    continue;
                Train train = LazyManager<TrackUnitManager>.Current.FindById(id) as Train;
                if (!ReferenceEquals(train, null))
                    _preReservedForTrains.Add(train);
            }

            int id2 = reader.ReadInt();
            BlockedForTrain = id2 == 0 ? null : LazyManager<TrackUnitManager>.Current.FindById(id2) as Train;
        }
    }
}