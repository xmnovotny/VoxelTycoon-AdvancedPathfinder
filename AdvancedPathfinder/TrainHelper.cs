using System;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder
{
    internal class TrainHelper: SimpleLazyManager<TrainHelper>
    {
        private Func<Train, float> _updatePathTimeFieldGetter;
        private Func<Train, IVehicleDestination> _trainDestinationFieldGetter;
        private Func<Train, PathCollection> _trainPathFieldGetter;
        private Action<Train, float> _updatePathTimeFieldSetter;

        public float GetTrainUpdatePathTime(Train train)
        {
            if (_updatePathTimeFieldGetter == null)
            {
                _updatePathTimeFieldGetter = SimpleDelegateFactory.FieldGet<Train, float>("_updatePathTime");
            }

            // ReSharper disable once PossibleNullReferenceException
            return _updatePathTimeFieldGetter(train);
        }

        public void SetTrainUpdatePathTime(Train train, float time)
        {
            if (_updatePathTimeFieldSetter == null)
            {
                _updatePathTimeFieldSetter = SimpleDelegateFactory.FieldSet<Train, float>("_updatePathTime");
            }

            // ReSharper disable once PossibleNullReferenceException
            _updatePathTimeFieldSetter(train, time);
        }
        
        public PathCollection GetTrainPath(Train train)
        {
            if (_trainPathFieldGetter == null)
            {
                _trainPathFieldGetter = SimpleDelegateFactory.FieldGet<Train, PathCollection>("Path");
            }

            // ReSharper disable once PossibleNullReferenceException
            return _trainPathFieldGetter(train);
        }
        
        public IVehicleDestination GetTrainDestination(Train train)
        {
            if (_trainDestinationFieldGetter == null)
            {
                _trainDestinationFieldGetter = SimpleDelegateFactory.FieldGet<Vehicle, IVehicleDestination>("_destination");
            }

            // ReSharper disable once PossibleNullReferenceException

            return _trainDestinationFieldGetter(train);
        }
    }
}