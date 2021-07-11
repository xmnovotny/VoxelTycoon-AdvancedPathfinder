using System;
using System.Collections.Generic;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    public static class TrackHelper
    {
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
    }
}