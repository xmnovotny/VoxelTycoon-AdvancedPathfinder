using VoxelTycoon.Tracks;

namespace AdvancedPathfinder.PathSignals
{
    public static class Extensions
    {
        public static int? FindConnectionIndex(this PathCollection path, TrackConnection connection, int? startingIndex = null)
        {
            if (startingIndex == null)
                startingIndex = path.RearIndex;

            for (int i = startingIndex.Value; i < path.FrontIndex; i++)
            {
                if (path[i] == connection)
                    return i;
            }
            return null;
        }
    }
}