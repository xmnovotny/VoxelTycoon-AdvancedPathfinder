using System.Collections.Generic;
using System.Diagnostics;
using AdvancedPathfinder.RailPathfinder;
using HarmonyLib;
using ModSettingsUtils;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Game.UI;
using VoxelTycoon.Modding;
using VoxelTycoon.Serialization;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using VoxelTycoon.UI;
using XMNUtils;
using Logger = VoxelTycoon.Logger;

namespace AdvancedPathfinder.Benchmark
{
    [HarmonyPatch]
    public class AdvancedPathfinderMod: Mod
    {
        private Harmony _harmony;
        private const string HarmonyID = "cz.xmnovotny.advancedpathfinderbench.patch";
        private static readonly Stopwatch PathStopwatch = new();
        private static readonly Stopwatch VehicleStopwatch = new();
        private static PathfinderStats _stats;
        private static bool _trainMoveStart;

        protected override void Initialize()
        {
            Harmony.DEBUG = false;
            _harmony = new Harmony(HarmonyID);
            FileLog.Reset();
            _harmony.PatchAll();
        }

        protected override void OnGameStarted()
        {
        }

        protected override void Deinitialize()
        {
            _harmony.UnpatchAll(HarmonyID);
            _harmony = null;
            _stats = null;
        }

        protected override void OnLateUpdate()
        {
            PathfinderStats stats = Manager<RailPathfinderManager>.Current?.Stats ?? _stats;
            if (stats != null)
            {
                GUIHelper.Draw(delegate
                {
                    GUILayout.Space(250f);
                    GUILayout.TextArea(stats.GetStatsText());
                });
            }
        }

        private static PathfinderStats GetStats()
        {
            PathfinderStats stats = Manager<RailPathfinderManager>.Current?.Stats;
            if (stats != null)
                return stats;

            if (Manager<RailPathfinderManager>.Current != null)
            {
                Manager<RailPathfinderManager>.Current.CreateStats();
                return Manager<RailPathfinderManager>.Current.Stats;
            }

            if (_stats != null)
                return _stats;

            _stats = new PathfinderStats();
            return _stats;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Vehicle), "OnUpdate")]
        private static void Vehicle_OnUpdate_prf(Vehicle __instance)
        {
            if (__instance is Train)
            {
                VehicleStopwatch.Restart();
                _trainMoveStart = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vehicle), "Move")]
        private static void Vehicle_Move_pof(Vehicle __instance)
        {
            if (__instance is Train)
            {
                VehicleStopwatch.Stop();
                PathfinderStats stats = GetStats();
                stats.AddTrainMoveTime(VehicleStopwatch.ElapsedTicks / 10000f);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        [HarmonyPriority(Priority.VeryHigh)]
        private static void Train_TryFindPath_prf()
        {
            PathStopwatch.Restart();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Train_TryFindPath_pof()
        {
            PathStopwatch.Stop();
            PathfinderStats stats = GetStats();
            stats.AddPathfindingTime(PathStopwatch.ElapsedTicks / 10000f);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TrackUnit), "WrappedTravel")]
        private static void TrackUnit_WrappedTravel_prf()
        {
            if (_trainMoveStart)
                GetStats().StartWrappedTravel();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "WrappedTravel")]
        private static void TrackUnit_WrappedTravel_pof()
        {
            if (_trainMoveStart)
                GetStats().StopWrappedTravel();

            _trainMoveStart = false;
        }
    }
}