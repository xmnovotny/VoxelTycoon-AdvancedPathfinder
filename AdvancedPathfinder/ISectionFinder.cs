using VoxelTycoon.Tracks;

namespace AdvancedPathfinder
{
    public interface ISectionFinder<TTrack, in TTrackConnection, out TTrackSection>
        where TTrack : Track
        where TTrackConnection : TrackConnection
        where TTrackSection : TrackSection<TTrack, TTrackConnection>
    {
        public TTrackSection FindSection(TTrackConnection connection);
    }
}