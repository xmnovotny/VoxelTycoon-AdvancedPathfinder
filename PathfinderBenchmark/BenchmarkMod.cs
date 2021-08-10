using System.Collections.Generic;
using System.Diagnostics;
using AdvancedPathfinder.Rails;
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
                    GUILayout.TextArea(stats.GetStatsText());
                });
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Vehicle), "OnUpdate")]
        private static void Vehicle_OnUpdate_prf(Vehicle __instance)
        {
            if (__instance is Train)
            {
                VehicleStopwatch.Restart();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vehicle), "Move")]
        private static void Vehicle_Move_pof(Vehicle __instance)
        {
            if (__instance is Train)
            {
                VehicleStopwatch.Stop();
                PathfinderStats stats = Manager<RailPathfinderManager>.Current?.Stats;
                if (stats == null)
                {
                    if (_stats == null)
                        _stats = new PathfinderStats();
                    stats = _stats;
                }
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
            PathfinderStats stats = Manager<RailPathfinderManager>.Current?.Stats;
            if (stats == null)
            {
                if (_stats == null)
                    _stats = new PathfinderStats();
                stats = _stats;
            }
            stats.AddPathfindingTime(PathStopwatch.ElapsedTicks / 10000f);
        }
    }
}