using AdvancedPathfinder.UI;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Game.UI;
using VoxelTycoon.Modding;
using VoxelTycoon.Serialization;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    [SchemaVersion(1)]
    [HarmonyPatch]
    public class AdvancedPathfinderMod: Mod
    {
        private Harmony _harmony;
        private const string HarmonyID = "cz.xmnovotny.advancedpathfinder.patch";
        public static readonly Logger Logger = new Logger("AdvancedPathfinder");

        protected override void Initialize()
        {
            Harmony.DEBUG = false;
            _harmony = new Harmony(HarmonyID);
            FileLog.Reset();
            _harmony.PatchAll();
        }

        protected override void OnGameStarted()
        {
            Manager<RailPathfinderManager>.Initialize();
            var current = Manager<RailPathfinderManager>.Current;
        }

        protected override void Deinitialize()
        {
            _harmony.UnpatchAll(HarmonyID);
            _harmony = null;
        }

        protected override void Write(StateBinaryWriter writer)
        {
        }

        protected override void Read(StateBinaryReader reader)
        {
        }
        
/*        [HarmonyPostfix]
        [HarmonyPatch(typeof(VehiclePathfinder<TrackConnection, TrackPathNode, Train>), "FindImmediately")]
        private static void TrainPathfinder_FindImmediately_pof(VehiclePathfinder<TrackConnection, TrackPathNode, Train> __instance)
        {
            FileLog.Log("FindImmediately");
        }*/

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Train), "TryFindPath")]
        private static void Train_TryFindPath_pof(Train __instance)
        {
            Manager<RailPathfinderManager>.Current?.Find();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(VehicleWindow), "Initialize")]
        private static void VehicleWindow_Initialize_pof(Vehicle vehicle)
        {
            if (vehicle is Train train)
            {
                LazyManager<TrainPathHighlighter>.Current.ShowFor(train);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VehicleWindow), "OnClose")]
        private static void VehicleWindow_OnClose_pof(VehicleWindow __instance)
        {
            if (__instance.Vehicle is Train train)
            {
                LazyManager<TrainPathHighlighter>.Current.HideFor(train);
            }
        }
    }
}