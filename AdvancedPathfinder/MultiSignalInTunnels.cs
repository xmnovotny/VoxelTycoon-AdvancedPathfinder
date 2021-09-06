using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon.Tools;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    [HarmonyPatch]
    public static class MultiSignalsInTunnels
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(RailSignalBuilderTool), "GetNextConnection")]
        // ReSharper disable InconsistentNaming
        private static bool RailSignalBuilderTool_GetNextConnection_prf(RailConnection connection, ref RailConnection __result)
        {
            connection = connection.InnerConnection;
            __result = null;
            if (connection.OuterConnectionCount != 1)
            {
                return false;
            }
            connection = connection.GetOuterConnection(0);
            if (connection.OuterConnectionCount != 1)
            {
                return false;
            }
            if (connection.Track.Parent != null && connection.Track.Parent is not (TrackBridge or TrackTunnel))
            {
                return false;
            }
            if (!connection.CanSetSignal())
            {
                return false;
            }

            __result = connection;
            return false;
        }
        
    }
}