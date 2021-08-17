using System;
using System.Collections.Generic;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Helpers
{
    [HarmonyPatch]
    public class TrackHelper: LazyManager<TrackHelper>
    {
        private Action<IReadOnlyList<Rail>, IReadOnlyList<Rail>, IReadOnlyList<Rail>> _railsChangedEvent; //new, removed, electrification changed
        private Action<IReadOnlyList<RailSignal>, IReadOnlyList<RailSignal>> _signalsBuildChangedEvent;
        private readonly List<Rail> _newRails = new();
        private readonly List<Rail> _removedRails = new();
        private readonly List<Rail> _electrificationChanged = new();
        private readonly List<RailSignal> _newSignals = new();
        private readonly List<RailSignal> _removedSignals = new();
        private bool _changedRails;
        private bool _changedBuildSignals;

        public static void GetStartingConnections<TTrack, TTrackConnection>(HashSet<TTrackConnection> startingConnections)
            where TTrack : Track where TTrackConnection : TrackConnection
        {
            ImmutableList<TTrack> tracks = LazyManager<BuildingManager>.Current.GetAll<TTrack>();
            for (int i = 0; i < tracks.Count; i++)
            {
                TTrack track = tracks[i];
                for (int j = 0; j < track.ConnectionCount; j++)
                {
                    TrackConnection connection = track.GetConnection(j);
                    if (connection is not TTrackConnection trackConnection)
                        throw new ArgumentException("TTrackConnection is not TTrack.GetConnection");
                    if (!trackConnection.IsConnected)
                    {
                        startingConnections.Add(trackConnection);
                    }
                }   
            }
        }

        public static HashSet<RailSignal> GetAllRailSignals()
        {
            ImmutableList<Rail> rails = LazyManager<BuildingManager>.Current.GetAll<Rail>();
            HashSet<RailSignal> signals = new();

            for (int i = rails.Count - 1; i >= 0; i--)
            {
                Rail rail = rails[i];
                for (int j = 0; j < rail.ConnectionCount; j++)
                {
                    RailConnection conn = rail.GetConnection(j);
                    if (conn.Signal != null)
                    {
                        signals.Add(conn.Signal);
                    }
                }
            }

            return signals;
        }

        public void RegisterRailsChanged(Action<IReadOnlyList<Rail>, IReadOnlyList<Rail>, IReadOnlyList<Rail>> onRailsChanged)
        {
            _railsChangedEvent -= onRailsChanged;
            _railsChangedEvent += onRailsChanged;
        }

        public void RegisterSignalBuildChanged(
            Action<IReadOnlyList<RailSignal>, IReadOnlyList<RailSignal>> onSignalBuildChanged)
        {
            _signalsBuildChangedEvent -= onSignalBuildChanged;
            _signalsBuildChangedEvent += onSignalBuildChanged;
        }

        private void OnRailsChanged()
        {
            _railsChangedEvent?.Invoke(_newRails, _removedRails, _electrificationChanged);
            _newRails.Clear();
            _removedRails.Clear();
            _electrificationChanged.Clear();
            _changedRails = false;
        }

        private void OnSignalsBuildChanged()
        {
            _signalsBuildChangedEvent?.Invoke(_newSignals, _removedSignals);
            _newSignals.Clear();
            _removedRails.Clear();
            _changedBuildSignals = false;
        }

        protected override void OnLateUpdate()
        {
            if (_changedRails)
                OnRailsChanged();
            if (_changedBuildSignals)
                OnSignalsBuildChanged();
        }

        #region HARMONY

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RailSignal), "Build")]
        private static void RailSignal_Build_pof(RailSignal __instance)
        {
            Current._newSignals.Add(__instance);
            Current._changedBuildSignals = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RailSignal), "Remove")]
        private static void RailSignal_Remove_pof(RailSignal __instance)
        {
            Current._removedSignals.Add(__instance);
            Current._changedBuildSignals = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Track), "OnBuilt")]
        private static void Track_OnBuilt_pof(Track __instance)
        {
            if (__instance is Rail rail)
            {
                Current._newRails.Add(rail);
                Current._changedRails = true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Track), "Remove")]
        private static void Track_Remove_prf(Track __instance)
        {
            if (__instance.IsBuilt && __instance is Rail rail)
            {
                Current._removedRails.Add(rail);
                Current._changedRails = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Rail), "set_ElectrificationMode")]
        private static void Rail_set_ElectrificationMode_pof(Rail __instance)
        {
            Current._electrificationChanged.Add(__instance);
            Current._changedRails = true;
        }
        
        #endregion
    }
}