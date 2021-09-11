using System;
using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.PathSignals
{
    public class ReserveResult: IDisposable
    {
        private PooledHashSet<PathSignalData> _signalsToPreReserve;
        
        public bool IsReserved = false;
        public int ReservedIndex = 0;
        public bool PreReservationFailed = false;

        public void Dispose()
        {
            _signalsToPreReserve?.Dispose();
        }

        public void AddSignalToPreReserve(PathSignalData signalData)
        {
            if (_signalsToPreReserve == null)
            {
                _signalsToPreReserve = PooledHashSet<PathSignalData>.Take();
            }

            _signalsToPreReserve.Add(signalData);
        }

        public void PreReserveSelectedSignals(Train train)
        {
            if (_signalsToPreReserve == null) return;
            
            foreach (PathSignalData signalData in _signalsToPreReserve)
            {
                if (!ReferenceEquals(signalData.ReservedForTrain, train))
                    signalData.PreReserveForTrain(train);
            }
        }
        
    }
}