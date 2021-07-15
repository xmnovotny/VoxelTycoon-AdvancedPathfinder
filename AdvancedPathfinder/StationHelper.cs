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
        private readonly HashSet<Track> _platformTracks = new();
        private bool _isDirty = true;

        public IReadOnlyCollection<TTrackConnection> GetStationStopsConnections()
        {
            InvalidateStationsConnections();
            return _stationStops;
        }

        public bool IsPlatform(TTrackConnection connection)
        {
            if (_isDirty)
                InvalidateStationsConnections();
            return _platformTracks.Contains(connection.Track);
        }

        public bool IsWaypoint(VehicleStation station)
        {
            VehicleStationLocationType? type = station.Location?.Type;
            return type?.HasFlag(VehicleStationLocationType.Freight) == false &&
                   type?.HasFlag(VehicleStationLocationType.Passenger) == false;
        }

        private void InvalidateStationsConnections()
        {
            if (!_isDirty)
                return;
            _stationStops.Clear();
            _platformTracks.Clear();
            ImmutableList<TStation> stations = LazyManager<BuildingManager>.Current.GetAll<TStation>();
            for (int i = 0; i < stations.Count; i++)
            {
                TStation station = stations[i];
                if (!station.IsBuilt)
                    continue;

                foreach (VehicleStationPlatform platform in station.Platforms)
                {
                    if (platform.Stop1 != null)
                        _stationStops.Add((TTrackConnection)platform.Stop1.InnerConnection);

                    if (platform.Stop2 != null)
                        _stationStops.Add((TTrackConnection)platform.Stop2.InnerConnection);

                    if (!IsWaypoint(station))
                    {
                        foreach (Track track in platform.Tracks)
                        {
                            _platformTracks.Add(track);
                        }
                    }
                }
            }

            _isDirty = false;
        }
        
    }
}