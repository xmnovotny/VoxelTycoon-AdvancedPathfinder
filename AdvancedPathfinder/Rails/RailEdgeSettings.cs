using System;
using UnityEngine;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.Rails
{
    public record RailEdgeSettings
    {
        private const float VelocityResolution = 5f;
        private const float LengthResolution = 50f;
        private const float AccelerationResolution = 10f;
        public bool Electric { get; init; }
        public float MaxSpeed { get; init; }
        public float AccelerationSec { get; init; }
        public float Length { get; init; }
        public bool CalculateOnlyBaseScore { get; init; }

        public RailEdgeSettings()
        { 
        }

        public RailEdgeSettings(Train train)
        {
            Electric = train.Electric;
            MaxSpeed = Mathf.Ceil(train.VelocityLimit / VelocityResolution) * VelocityResolution;
            Length = Mathf.Ceil(train.Length / LengthResolution) * LengthResolution;
            CalculateOnlyBaseScore = false;
        }
    }
}