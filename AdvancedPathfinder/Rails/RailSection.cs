using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.Rails
{
    public class RailSection: TrackSection<Rail, RailConnection>
    {
        private const float ClosedBlockMult = 1.5f;
        private const float ClosedBlockPlatformMult = 10f;
        private readonly RailSectionData _data = new();
        private readonly Dictionary<RailBlock, float> _railBlocksLengths = new();

        public RailSectionData Data => _data;

        public float GetClosedBlocksLength()
        {
            float result = 0f;
            foreach (KeyValuePair<RailBlock,float> pair in _railBlocksLengths)
            {
                if (!pair.Key.IsOpen)
                {
                    result += pair.Value * (_data.HasPlatform ? ClosedBlockPlatformMult : ClosedBlockMult);
                }
            }

            return result;
        }
        
        protected override void FillSetup()
        {
            base.FillSetup();
            Data.Reset();
        }

        protected override void ProcessTrack(Rail track, RailConnection startConnection)
        {
            base.ProcessTrack(track, startConnection);
            Data.IsElectrified = track.Electrified;
            Data.LengthAdd = startConnection.Length;
            if (track.SignalCount > 0)
            {
                if (startConnection.Signal != null && startConnection.InnerConnection.Signal == null)
                {
                    _data.AllowedDirection = SectionDirection.Forward;
                } else if (startConnection.Signal == null && startConnection.InnerConnection.Signal != null)
                {
                    _data.AllowedDirection = SectionDirection.Backward;
                }
            }

            if (Data.HasPlatform == false && LazyManager<StationHelper<RailConnection, RailStation>>.Current.IsPlatform(startConnection))
                Data.HasPlatform = true;

            RailBlock block1 = startConnection.Block;
            RailBlock block2 = startConnection.InnerConnection.Block;
            if (block1 != block2)
            {
                float length = startConnection.Length / 2;
                _railBlocksLengths.AddFloatToDict(block1, length);
                _railBlocksLengths.AddFloatToDict(block2, length);
            }
            else
            {
                _railBlocksLengths.AddFloatToDict(block1, startConnection.Length);
            }
        }
        
    }
}