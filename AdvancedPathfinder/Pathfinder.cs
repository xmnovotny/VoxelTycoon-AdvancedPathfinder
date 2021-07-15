using System.Collections.Generic;
using System.ComponentModel;
using FibonacciHeap;
using JetBrains.Annotations;

namespace AdvancedPathfinder
{
    public class Pathfinder<TPathfinderNode> where TPathfinderNode: PathfinderNodeBase
    {
        private readonly FibonacciHeap<PathfinderNodeBase, float> _heap = new(float.MinValue);
        private IReadOnlyCollection<TPathfinderNode> _nodes;

        [CanBeNull]
        public TPathfinderNode FindOne([NotNull] TPathfinderNode startNode,
            [NotNull] HashSet<TPathfinderNode> targetNodes, IReadOnlyCollection<TPathfinderNode> nodeList,
            object edgeSettings)
        {
            Initialize(startNode, nodeList);
            OneOfManyNodesTargetChecker checker = new(targetNodes);
            return Calculate(edgeSettings, checker) ? checker.FoundNode : null;
        }
        
        public TPathfinderNode Find([NotNull] TPathfinderNode startNode,
            [NotNull]TPathfinderNode targetNode, IReadOnlyCollection<TPathfinderNode> nodeList, object edgeSettings)
        {
            Initialize(startNode, nodeList);
            OneNodeTargetChecker checker = new(targetNode);
            return Calculate(edgeSettings, checker) ? checker.FoundNode : null;
        }

        public void FindAll([NotNull] TPathfinderNode startNode, IReadOnlyCollection<TPathfinderNode> nodeList,
            object edgeSettings)
        {
            Initialize(startNode, nodeList);
            Calculate(edgeSettings, null);
        }
        
        private void Initialize([NotNull]TPathfinderNode startNode, IReadOnlyCollection<TPathfinderNode> nodeList)
        {
            _heap.Clear();
            _nodes = nodeList;
            FibonacciHeapNode<PathfinderNodeBase, float> node2 = startNode.GetInitializedHeapNode();
            node2.Key = 0;
            _heap.Insert(node2);
            foreach (TPathfinderNode pfNode in _nodes)
            {
                if (pfNode == startNode)
                    continue;
                FibonacciHeapNode<PathfinderNodeBase, float> node = pfNode.GetInitializedHeapNode();
                _heap.Insert(node);
            }
        }

        private bool Calculate(object edgeSettings, [CanBeNull] TargetChecker targetChecker)
        {
            while (!_heap.IsEmpty())
            {
                FibonacciHeapNode<PathfinderNodeBase, float> node = _heap.RemoveMin();
                TPathfinderNode pfNode = (TPathfinderNode) node.Data;
                float baseScore = node.Key;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (baseScore == float.MaxValue) //in the heap there are only max values nodes = these are unreachable from starting node 
                    return false; //not found
                if (targetChecker?.CheckNode(pfNode) == true)
                    return true;
                foreach (PathfinderEdgeBase edge in pfNode.GetEdges())
                {
                    float newScore = baseScore + edge.GetScore(edgeSettings);
                    PathfinderNodeBase endPfNode = edge.NextNode;
                    if (endPfNode == null) continue;
                    
                    FibonacciHeapNode<PathfinderNodeBase, float> endNode = endPfNode.HeapNode;
                    if (endNode?.Key > newScore)
                    {
                        endPfNode.PreviousBestNode = pfNode;
                        endPfNode.PreviousBestEdge = edge;
                        _heap.DecreaseKey(endNode, newScore);
                    }
                }
            }

            return targetChecker != null;
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

        private abstract class TargetChecker
        {
            /** return true if pathfinding is complete */
            public abstract bool CheckNode(TPathfinderNode node);
        }

        private class OneNodeTargetChecker: TargetChecker
        {
            private readonly TPathfinderNode _targetNode;
            public TPathfinderNode FoundNode { get; private set; }

            public OneNodeTargetChecker([NotNull] TPathfinderNode targetNode)
            {
                _targetNode = targetNode;
            }

            public override bool CheckNode(TPathfinderNode node)
            {
                if (_targetNode != node) return false;
                
                FoundNode = node;
                return true;
            }
        }
        
        private class OneOfManyNodesTargetChecker: TargetChecker
        {
            private readonly HashSet<TPathfinderNode> _targetNodes;
            public TPathfinderNode FoundNode { get; private set; }

            public OneOfManyNodesTargetChecker([NotNull] HashSet<TPathfinderNode> targetNodes)
            {
                if (targetNodes.Count == 0)
                    throw new InvalidEnumArgumentException("Target nodes are empty");
                _targetNodes = targetNodes;
            }

            public override bool CheckNode(TPathfinderNode node)
            {
                if (!_targetNodes.Contains(node)) return false;
                
                FoundNode = node;
                return true;
            }
        }
    }
}