using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public static class Extensions
    {
        public static int GetDestinationHash(this IVehicleDestination destination)
        {
            destination.Invalidate();
            int result = 0;
            foreach (TrackConnection trackConnection in destination.Stops)
            {
                result += trackConnection.GetHashCode() * -0x5AAAAAD7;
            }

            return result;
        }
        
    }
}