using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public class StationHelper<TTrackConnection, TStation>: LazyManager<StationHelper<TTrackConnection, TStation>>
        where TTrackConnection : TrackConnection
        where TStation : VehicleStation
    {
        private readonly HashSet<TTrackConnection> _stationStops = new();
        private bool _isDirty = true;

        public IReadOnlyCollection<TTrackConnection> GetStationStopsConnections()
        {
            InvalidateStationsConnections();
            return _stationStops;
        }

        private void InvalidateStationsConnections()
        {
            if (!_isDirty)
                return;
            _stationStops.Clear();
            ImmutableList<TStation> stations = LazyManager<BuildingManager>.Current.GetAll<TStation>();
            for (int i = 0; i < stations.Count; i++)
            {
                TStation station = stations[i];
                if (!station.IsBuilt)
                    continue;

                foreach (VehicleStationPlatform platform in station.Platforms)
                {
                    if (platform.Stop1 != null)
                        _stationStops.Add((TTrackConnection)platform.Stop1);

                    if (platform.Stop2 != null)
                        _stationStops.Add((TTrackConnection)platform.Stop2);
                }
            }
        }
        
    }
}