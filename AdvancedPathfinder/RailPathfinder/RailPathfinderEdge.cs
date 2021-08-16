using System;
using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.RailPathfinder
{
    public class RailPathfinderEdge: PathfinderEdgeBase
    {
        private const float PlatformMultiplier = 4f;
        private const float CurveMultiplier = 2f;

        public PathfinderNodeBase Owner { get; init; }
        public IReadOnlyList<(RailSection section, PathDirection direction)> Sections => _sections;
        public float Length { get; private set; }

        private readonly List<(RailSection section, PathDirection direction)> _sections = new ();
        private readonly Dictionary<RailEdgeSettings, float> _baseScoreCache = new();
        private readonly RailPathfinderEdgeData _data = new();

        public RailPathfinderEdgeData Data => _data;
        public RailSignal LastSignal { get; private set; }

        public void GetConnections(List<TrackConnection> connections)
        {
            foreach ((RailSection section, PathDirection direction) in _sections)
            {
                section.GetConnectionsInDirection(direction, connections);
            }            
        }

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

        internal bool Fill(RailConnection startConnection, IRailSectionFinder railSectionFinder, IRailNodeFinder railNodeFinder)
        {
            RailConnection  currentConnection = startConnection;
            NextNode = null;
            RailSection lastSection = null;
            PathDirection lastDirection = default;
            using PooledHashSet<RailSection> processedSections = PooledHashSet<RailSection>.Take();
            while (currentConnection != null)
            {
                RailSection section = railSectionFinder.FindSection(currentConnection);
                if (!processedSections.Add(section))
                {
                    //we already processed this section = circular (invalid) edge
                    return false;
                }
                PathDirection direction =
                    section.First == currentConnection ? PathDirection.Forward : section.Last == currentConnection ? PathDirection.Backward : throw new InvalidOperationException("connection is not on the any end of the section");

                AddSection(section, direction);
                if (lastSection != null)
                {
                    lastSection.SetNextSection(section, direction, lastDirection);
                }
                RailConnection lastConn = section.GetEndConnection(direction);
                if ((NextNode = railNodeFinder.FindNodeByInboundConn(lastConn)) != null)
                {
                    SetNextNodeInSections();

                    //new node or end of the track
                    break;
                }

                lastSection = section;
                lastDirection = direction;
                currentConnection = (RailConnection)lastConn.OuterConnections[0];
            }
            UpdateSections();
            return true;
        }

        private void SetNextNodeInSections()
        {
            foreach ((RailSection section, PathDirection direction) in _sections)
            {
                section.SetNextNode(NextNode, direction);
            }
        }

        private void AddSection(RailSection section, PathDirection direction)
        {
            _sections.Add((section, direction));
            Length += section.Length;
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

        private void UpdateSections()
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