using System;
using System.Collections.Generic;
using AdvancedPathfinder.UI;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder
{
    public class RailPathfinderManager: PathfinderManager<RailPathfinderManager, Rail, RailConnection, RailSection, RailPathfinderNode, RailPathfinderEdge>
    {
        protected override IReadOnlyCollection<RailConnection> GetStationStopsConnections()
        {
            return StationHelper<RailConnection, RailStation>.Current.GetStationStopsConnections();
        }

        protected override void HighlightNonProcessedTracks(HashSet<Rail> unprocessedTracks)
        {
            RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current; 
            foreach (Rail rail in unprocessedTracks)
            {
                Highlighter hl = hlMan.ForOneTrack(rail, Color.red);
                hl.transform.SetParent(rail.transform);
            }
        }

        protected override void HighlightConnection(RailConnection connection, bool halfConnection,
            Color color)
        {
            RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current;
            Highlighter hl = hlMan.ForOneConnection(connection, color);
            Rail rail = connection.Track;
            hl.transform.SetParent(rail.transform);
            
        }
    }
}