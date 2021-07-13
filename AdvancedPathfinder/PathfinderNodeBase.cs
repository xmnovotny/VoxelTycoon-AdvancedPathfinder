using System.Collections.Generic;
using FibonacciHeap;
using JetBrains.Annotations;

namespace AdvancedPathfinder
{
    public abstract class PathfinderNodeBase
    {
        internal abstract IReadOnlyList<PathfinderEdgeBase> GetEdges();
        [CanBeNull] internal PathfinderNodeBase Previous = null;
        private FibonacciHeapNode<PathfinderNodeBase, float> _heapNode = null;

        /**
         * Returns new or reused reset heap node with maximum value
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
            return _heapNode;
        }

        internal float? GetActualNodeValue()
        {
            return _heapNode?.Key;
        }

        [CanBeNull]
        internal FibonacciHeapNode<PathfinderNodeBase, float> HeapNode => _heapNode;

    }
}