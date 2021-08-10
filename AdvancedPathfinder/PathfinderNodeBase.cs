using System.Collections.Generic;
using FibonacciHeap;
using JetBrains.Annotations;

namespace AdvancedPathfinder
{
    public abstract class PathfinderNodeBase
    {
        internal abstract IReadOnlyList<PathfinderEdgeBase> GetEdges();
        [CanBeNull] internal PathfinderNodeBase PreviousBestNode { get; set; } = null;
        [CanBeNull] internal PathfinderEdgeBase PreviousBestEdge { get; set; } = null;
        internal bool NodeIsIgnored; //used by pathfinder when finding path
        
        private FibonacciHeapNode<PathfinderNodeBase, float> _heapNode;
        public float? LastPathLength => _heapNode?.Key < float.MaxValue ? _heapNode?.Key : null;
        public bool IsReachable { get; internal set; }  //if false, there is no inbound node that is passable in forward direction

        /**
         * Returns new or reused reset heap node with maximum value and clears best previous nodes info
         */
        internal FibonacciHeapNode<PathfinderNodeBase, float> GetInitializedHeapNode()
        {
            if (_heapNode == null)
            {
                _heapNode = new FibonacciHeapNode<PathfinderNodeBase, float>(this, float.MaxValue);
            }
            else
            {
                _heapNode.Reset();
                _heapNode.Key = float.MaxValue;
            }

            PreviousBestEdge = null;
            PreviousBestNode = null;
            NodeIsIgnored = false;
            return _heapNode;
        }

        internal float? GetActualNodeValue()
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return _heapNode==null || _heapNode.Key == float.MaxValue ? null : _heapNode.Key;
        }

        [CanBeNull]
        internal FibonacciHeapNode<PathfinderNodeBase, float> HeapNode => _heapNode;

    }
}