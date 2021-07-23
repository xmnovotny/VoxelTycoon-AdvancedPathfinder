using System;
using System.Collections.Generic;
using System.Diagnostics;
using AdvancedPathfinder.PathSignals;
using HarmonyLib;
using VoxelTycoon;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder
{
    [HarmonyPatch]
    public class RailBlockHelper: SimpleLazyManager<RailBlockHelper>
    {
        private Dictionary<RailBlock, PropertyChangedEventHandler<RailBlock, bool>> _blockStateChangeAction = new();
        private Stopwatch _stopwatch = new();

        public float ElapsedMilliseconds => _stopwatch.ElapsedTicks / 10000f;

        public void RegisterBlockStateAction(RailBlock block, PropertyChangedEventHandler<RailBlock, bool> onStateChange)
        {
            if (!_blockStateChangeAction.TryGetValue(block, out PropertyChangedEventHandler<RailBlock, bool> action))
            {
                action = onStateChange;
            }
            else
            {
                action -= onStateChange;
                action += onStateChange;
            }

            _blockStateChangeAction[block] = action;
        }

        public void UnregisterBlockStateAction(RailBlock block, PropertyChangedEventHandler<RailBlock, bool> onStateChange)
        {
            if (_blockStateChangeAction.TryGetValue(block, out PropertyChangedEventHandler<RailBlock, bool> action))
            {
                action -= onStateChange;
                if (action.GetInvocationList().Length == 0)
                {
                    _blockStateChangeAction.Remove(block);
                }
                else
                {
                    _blockStateChangeAction[block] = action;
                }
            }
        }

        public bool IsBlockOpen(RailBlock block)
        {
            return SimpleManager<PathSignalManager>.Current == null ? block.IsOpen : SimpleManager<PathSignalManager>.Current.IsBlockOpen(block);
        }

        internal void PathSignalBlockFreeChanged(RailBlock block, bool isFree)
        {
            FileLog.Log("PathSignalBlockFreeChanged");
            OnBlockStateChange(block, !isFree, isFree);
        }

        private void OnBlockStateChange(RailBlock block, bool oldValue, bool newValue)
        {
            if (_blockStateChangeAction.TryGetValue(block, out PropertyChangedEventHandler<RailBlock, bool> action))
            {
                action.Invoke(block, oldValue, newValue);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("Value", MethodType.Setter)]
        [HarmonyPatch(typeof(RailBlock))]
        private static void RailBlock_setValue_prf(RailBlock __instance, int value)
        {
            CurrentWithoutInit?._stopwatch.Start();
            bool oldIsOpen = __instance.IsOpen;
            bool newIsOpen = value == 0;
            if (oldIsOpen != newIsOpen) 
                CurrentWithoutInit?.OnBlockStateChange(__instance, oldIsOpen, newIsOpen);
            CurrentWithoutInit?._stopwatch.Stop();;
        }
        
    }
}