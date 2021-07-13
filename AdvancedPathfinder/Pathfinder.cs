using System.Collections.Generic;
using FibonacciHeap;
using JetBrains.Annotations;

namespace AdvancedPathfinder
{
    public class Pathfinder
    {
        private readonly FibonacciHeap<PathfinderNodeBase, float> _heap = new(float.MinValue);
//        private readonly Dictionary<PathfinderNodeBase, FibonacciHeapNode<PathfinderNodeBase, float>> _nodes = new();
        private IReadOnlyCollection<PathfinderNodeBase> _nodes;
        
        public void Initialize([NotNull]PathfinderNodeBase startNode, [NotNull]IReadOnlyCollection<PathfinderNodeBase> nodes)
        {
            _heap.Clear();
            _nodes = nodes;
            foreach (PathfinderNodeBase pfNode in nodes)
            {
                FibonacciHeapNode<PathfinderNodeBase, float> node = pfNode.GetInitializedHeapNode();
                if (pfNode == startNode)
                {
                    node.Key = 0;
                }
                pfNode.Previous = null;
                _heap.Insert(node);
            }
        }

        public void Calculate(PathfinderNodeBase targetNode = null)
        {
            while (!_heap.IsEmpty())
            {
                FibonacciHeapNode<PathfinderNodeBase, float> node = _heap.RemoveMin();
                PathfinderNodeBase pfNode = node.Data;
                float baseScore = node.Key;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (baseScore == float.MaxValue) //in the heap there are only max values nodes = these are unreachable from starting node 
                    break;
                foreach (PathfinderEdgeBase edge in pfNode.GetEdges())
                {
                    float newScore = baseScore + edge.GetScore();
                    PathfinderNodeBase endPfNode = edge.NextNode;
                    if (endPfNode != null)
                    {
                        FibonacciHeapNode<PathfinderNodeBase, float> endNode = endPfNode.HeapNode;
                        if (endNode.Key > newScore)
                        {
                            endPfNode.Previous = pfNode;
                            _heap.DecreaseKey(endNode, newScore);
                        }
                    }
                }
            }
        }

        public IEnumerable<(PathfinderNodeBase node, float distance)> GetDistances()
        {
            foreach (PathfinderNodeBase node in _nodes)
            {
                float? value = node.GetActualNodeValue();
                if (value.HasValue)
                {
                    yield return (node, value.Value);
                }
            }
        }
    }
}