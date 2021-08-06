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
        private readonly Dictionary<RailBlock, PropertyChangedEventHandler<RailBlock, bool>> _blockStateChangeAction = new(); //action called when whole block state is changed (by original block system, full block in the path system or in the simple block)
        private Action<RailBlock> _blockCreatedAction;
        private Action<RailBlock> _blockRemovingAction;
        private Action<Rail, int, RailBlock> _addedBlockedRailAction;
        private Action<Rail, int, RailBlock> _releasedBlockedRailAction;
        private Stopwatch _stopwatch = new();

        public float ElapsedMilliseconds => _stopwatch.ElapsedTicks / 10000f;

        public bool OverrideBlockIsOpen = false; //if it is true, block state action will ignore standard block IsOpen changes, only calling other functions about blocked state will invoke proper actions

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

        public void RegisterBlockRemovingAction(Action<RailBlock> onBlockRemoving)
        {
            _blockRemovingAction -= onBlockRemoving;
            _blockRemovingAction += onBlockRemoving;
        }

        public void UnregisterBlockRemovingAction(Action<RailBlock> onBlockRemoving)
        {
            _blockRemovingAction -= onBlockRemoving;
        }

        public void RegisterBlockCreatedAction(Action<RailBlock> onBlockCreated)
        {
            _blockCreatedAction -= onBlockCreated;
            _blockCreatedAction += onBlockCreated;
        }

        public void UnregisterBlockCreatedAction(Action<RailBlock> onBlockCreated)
        {
            _blockCreatedAction -= onBlockCreated;
        }

        public void RegisterAddedBlockedRailAction(Action<Rail, int, RailBlock> onAddedBlockedRail)
        {
            _addedBlockedRailAction -= onAddedBlockedRail;
            _addedBlockedRailAction += onAddedBlockedRail;
        }
        
        public void UnregisterAddedBlockedRailAction(Action<Rail, int, RailBlock> onAddedBlockedRail)
        {
            _addedBlockedRailAction -= onAddedBlockedRail;
        }
        
        public void RegisterReleasedBlockedRailAction(Action<Rail, int, RailBlock> onReleasedBlockedRail)
        {
            _releasedBlockedRailAction -= onReleasedBlockedRail;
            _releasedBlockedRailAction += onReleasedBlockedRail;
        }
        
        public void UnregisterReleasedBlockedRailAction(Action<Rail, int, RailBlock> onReleasedBlockedRail)
        {
            _releasedBlockedRailAction -= onReleasedBlockedRail;
        }
        
        public int BlockBlockedCount(RailBlock block, ImmutableUniqueList<RailConnection> connections)
        {
            return OverrideBlockIsOpen && SimpleManager<PathSignalManager>.Current != null ? SimpleManager<PathSignalManager>.Current.BlockBlockedCount(block, connections) : (block.IsOpen ? 0 : 1);
        }

        /** call only when particular block condition is changed (it will have cumulative effect = calls with isFree = true must be equal to the calls with isFree = false for indicating a free block */ 
        internal void BlockFreeConditionChanged(RailBlock block, bool isFree)
        {
            OnBlockStateChange(block, !isFree, isFree);
        }
        
        internal void AddBlockedRails(IReadOnlyDictionary<Rail, int> railsSum, RailBlock block)
        {
            if (_addedBlockedRailAction == null)
                return;
            foreach (KeyValuePair<Rail,int> pair in railsSum)
            {
                _addedBlockedRailAction.Invoke(pair.Key, pair.Value, block);
            }
        }

        internal void ReleaseBlockedRails(IReadOnlyDictionary<Rail, int> railsSum, RailBlock block)
        {
            if (_releasedBlockedRailAction == null)
                return;
            foreach (KeyValuePair<Rail,int> pair in railsSum)
            {
                _releasedBlockedRailAction.Invoke(pair.Key, pair.Value, block);
            }
        }

        private void OnBlockStateChange(RailBlock block, bool oldValue, bool newValue)
        {
            if (_blockStateChangeAction.TryGetValue(block, out PropertyChangedEventHandler<RailBlock, bool> action))
            {
                action.Invoke(block, oldValue, newValue);
            }
        }

        private void OnBlockCreated(RailBlock block)
        {
            _blockCreatedAction?.Invoke(block);
        }

        private void OnBlockRemoving(RailBlock block)
        {
            _blockRemovingAction?.Invoke(block);
        }

        [HarmonyPrefix]
        [HarmonyPatch("Value", MethodType.Setter)]
        [HarmonyPatch(typeof(RailBlock))]
        // ReSharper disable once InconsistentNaming
        private static void RailBlock_setValue_prf(RailBlock __instance, int value)
        {
            RailBlockHelper man = CurrentWithoutInit;
            if (man == null || man.OverrideBlockIsOpen) return;
            
            man._stopwatch.Start();
            bool oldIsOpen = __instance.IsOpen;
            bool newIsOpen = value == 0;
            if (oldIsOpen != newIsOpen)
                man.OnBlockStateChange(__instance, oldIsOpen, newIsOpen);
            man._stopwatch.Stop();
        }

        [HarmonyPrefix]
        [HarmonyPatch("Remove")]
        [HarmonyPatch(typeof(RailBlockManager))]
        // ReSharper disable once InconsistentNaming
        private static void RailBlockManager_Remove_prf(RailBlock block)
        {
            if (CurrentWithoutInit != null)
                Current.OnBlockRemoving(block);
        }
        
        [HarmonyPostfix]
        [HarmonyPatch("Create")]
        [HarmonyPatch(typeof(RailBlockManager))]
        // ReSharper disable once InconsistentNaming
        private static void RailBlockManager_Create_prf(RailConnection origin)
        {
            if (CurrentWithoutInit != null && origin.Block != null)
                Current.OnBlockCreated(origin.Block);
        }
    }
}