using System;
using System.Collections.Generic;
using HarmonyLib;
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
        private readonly Dictionary<RailBlock, bool> _railBlocksStates = new();
        private float? _closedBlockLength;

        public RailSectionData Data => _data;

        public float GetClosedBlocksLength()
        {
            if (_closedBlockLength.HasValue)
                return _closedBlockLength.Value;
            
            float result = 0f;
            foreach (KeyValuePair<RailBlock, float> pair in _railBlocksLengths)
            {
                bool isOpen = SimpleLazyManager<RailBlockHelper>.Current.IsBlockOpen(pair.Key); 
                if (!isOpen)
                {
                    result += CalculateCloseBlockLength(pair.Value);
                }

                _railBlocksStates[pair.Key] = isOpen;
            }

            //FileLog.Log($"Blocked length {result:N1}, ({this.GetHashCode():X8})");
            _closedBlockLength = result;

            return result;
        }

        internal override void OnSectionRemoved()
        {
            base.OnSectionRemoved();
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.UnregisterBlockStateAction(block, OnBlockStateChange);
            }
        }

        protected override void FillSetup()
        {
            base.FillSetup();
            Data.Reset();
        }

        protected override void FinalizeFill()
        {
            var helper = SimpleLazyManager<RailBlockHelper>.Current;
            foreach (RailBlock block in _railBlocksLengths.Keys)
            {
                helper.RegisterBlockStateAction(block, OnBlockStateChange);
            }
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
            RailBlockHelper blockHelper = SimpleLazyManager<RailBlockHelper>.Current;
            _railBlocksStates[block1] = blockHelper.IsBlockOpen(block1);
            if (block1 != block2)
            {
                float length = startConnection.Length / 2;
                _railBlocksLengths.AddFloatToDict(block1, length);
                _railBlocksStates[block2] = blockHelper.IsBlockOpen(block2);
                _railBlocksLengths.AddFloatToDict(block2, length);
            }
            else
            {
                _railBlocksLengths.AddFloatToDict(block1, startConnection.Length);
            }
        }

        private float CalculateCloseBlockLength(float length)
        {
            return length * (_data.HasPlatform ? ClosedBlockPlatformMult : ClosedBlockMult);
        }

        private void OnBlockStateChange(RailBlock block, bool oldIsOpen, bool newIsOpen)
        {
            _closedBlockLength = null;
//            GetClosedBlocksLength();
            if (_closedBlockLength.HasValue && _railBlocksStates.TryGetValue(block, out bool oldState) && oldState != newIsOpen && _railBlocksLengths.TryGetValue(block, out float length))
            {
                FileLog.Log($"Block state change, new state: {newIsOpen}");
                float value = CalculateCloseBlockLength(length);
                if (newIsOpen)
                    _closedBlockLength -= value;
                else
                    _closedBlockLength += value;
                _railBlocksStates[block] = newIsOpen;
            }
        }
    }
}