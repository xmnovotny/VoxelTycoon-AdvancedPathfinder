using System.Collections.Generic;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public class RailPathfinderEdge: PathfinderEdge<Rail, RailConnection, RailSection>
    {
        private const float PlatformMultiplier = 4f;
        private const float CurveMultiplier = 2f;
        private readonly Dictionary<RailEdgeSettings, float> _baseScoreCache = new();
        private readonly RailPathfinderEdgeData _data = new();

        public RailPathfinderEdgeData Data => _data;
        public RailSignal LastSignal { get; private set; }

        internal bool IsPassable(bool electric=false)
        {
            if (electric && !_data.IsElectrified)
                return false;
            return _data.IsValidDirection(PathDirection.Forward);
        }

        internal override float GetScore(object edgeSettings)
        {
            if (!_data.AllowedDirection.CanPass(PathDirection.Forward) || edgeSettings is not RailEdgeSettings railEdgeSettings)
                return NoConnection;

            if (!_baseScoreCache.TryGetValue(railEdgeSettings, out float baseScore))
            {
                baseScore = _baseScoreCache[railEdgeSettings] = CalculateBaseScore(railEdgeSettings);
            }

            if (railEdgeSettings.CalculateOnlyBaseScore)
                return baseScore;

            baseScore += GetClosedBlocksLength();

            return baseScore;
        }

        private float GetClosedBlocksLength()
        {
            float result = 0f;
            foreach ((RailSection section, PathDirection _) in Sections)
            {
                result += section.GetClosedBlocksLength();
            }

            return result;
        }

        private float CalculateBaseScore(RailEdgeSettings railEdgeSettings)
        {
            if (railEdgeSettings.Electric && !_data.IsElectrified)
                return NoConnection;
            
            float result = _data.Length + _data.CurvedLength * CurveMultiplier;
            if (_data.HasPlatform)
                result += _data.PlatformLength * PlatformMultiplier;

            return result;
        }

        protected override void UpdateSections()
        {
            _data.Reset();
            RailSignal lastSignal = null;
            foreach ((RailSection section, PathDirection direction) in Sections)
            {
                _data.Combine(section.Data, direction == PathDirection.Backward);
                RailSignal signal = section.GetLastSignal(direction);
                if (signal != null)
                    lastSignal = signal;
            }

            LastSignal = lastSignal;
        }
    }
}