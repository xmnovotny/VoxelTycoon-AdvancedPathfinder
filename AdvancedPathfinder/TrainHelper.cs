using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder
{
    [HarmonyPatch]
    internal class TrainHelper: SimpleLazyManager<TrainHelper>
    {
        private Func<Train, float> _updatePathTimeFieldGetter;
        private Func<Train, IVehicleDestination> _trainDestinationFieldGetter;
        private Func<Train, PathCollection> _trainPathFieldGetter;
        private Action<Train, float> _updatePathTimeFieldSetter;
        private readonly Dictionary<Train, PathCollection> _trainToPath = new ();
        private readonly Dictionary<PathCollection, Train> _pathToTrain = new ();
        private Action<Train, PathCollection> _trainAttachedAction;
        private Action<Train> _trainDetachedAction;

        public IReadOnlyDictionary<PathCollection, Train> PathToTrain => _pathToTrain;
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
            if (_trainToPath.TryGetValue(train, out PathCollection path))
                return path;

            if (_trainPathFieldGetter == null)
            {
                _trainPathFieldGetter = SimpleDelegateFactory.FieldGet<Train, PathCollection>("Path");
            }

            // ReSharper disable once PossibleNullReferenceException
            path = _trainPathFieldGetter(train);
            _trainToPath[train] = path;
            _pathToTrain[path] = train;
            return path;
        }

        [CanBeNull]
        public Train GetTrainFromPath(PathCollection path)
        {
            if (!_pathToTrain.TryGetValue(path, out Train train))
            {
                AssignTrainPaths();
                train = _pathToTrain.GetValueOrDefault(path);
            }

            return train;
        }

        public bool GetTrainFromPath(PathCollection path, out Train train)
        {
            if (!_pathToTrain.TryGetValue(path, out train))
            {
                AssignTrainPaths();
                if (!_pathToTrain.TryGetValue(path, out train)) {
                    return false;
                }
            }

            return true;
        }

        public void RegisterTrainAttachedAction(Action<Train, PathCollection> onTrainAttached)
        {
            _trainAttachedAction -= onTrainAttached;
            _trainAttachedAction += onTrainAttached;
        }

        public void UnregisterTrainAttachedAction(Action<Train, PathCollection> onTrainAttached)
        {
            _trainAttachedAction -= onTrainAttached;
        }

        public void RegisterTrainDetachedAction(Action<Train> onTrainDetached)
        {
            _trainDetachedAction -= onTrainDetached;
            _trainDetachedAction += onTrainDetached;
        }

        public void UnregisterTrainDetachedAction(Action<Train> onTrainDetached)
        {
            _trainDetachedAction -= onTrainDetached;
        }
        
        private void AssignTrainPaths()
        { 
            ImmutableList<Vehicle> trains = LazyManager<VehicleManager>.Current.GetAll<Train>();
            for (int i = trains.Count - 1; i >= 0; i--)
            {
                GetTrainPath((Train) trains[i]);
            }
        }

        protected override void OnInitialize()
        {
            LazyManager<VehicleManager>.Current.VehicleRemoved += VehicleRemoved;
        }

        private void VehicleRemoved(Vehicle vehicle)
        {
            if (vehicle is Train train)
            {
                if (_trainToPath.TryGetValue(train, out PathCollection path))
                {
                    _pathToTrain.Remove(path);
                }

                _trainToPath.Remove(train);
            }
        }

        private void TrainAttached(Train train, PathCollection path)
        {
            _trainToPath[train] = path;
            _pathToTrain[path] = train;
            _trainAttachedAction?.Invoke(train, path);
        }

        private void TrainDetached(Train train)
        {
            _trainDetachedAction(train);
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "Attach")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Attach_pof(TrackUnit __instance, PathCollection ___Path)
        {
            if (CurrentWithoutInit != null && __instance is Train train)
            {
                Current.TrainAttached(train, ___Path);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "Detach")]
        // ReSharper disable once InconsistentNaming
        private static void TrackUnit_Detach_pof(TrackUnit __instance)
        {
            if (CurrentWithoutInit != null && __instance is Train train)
            {
                Current.TrainDetached(train);
            }
        }
    }
}