using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        
    }
}