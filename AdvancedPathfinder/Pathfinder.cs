using System;
using System.Collections.Generic;
using System.ComponentModel;
using FibonacciHeap;
using HarmonyLib;
using JetBrains.Annotations;
using VoxelTycoon.Tools.TrackBuilder;

namespace AdvancedPathfinder
{
    public class Pathfinder<TPathfinderNode> where TPathfinderNode: PathfinderNodeBase
    {
        private const float ScoreCoeficient = 2f;
        private readonly FibonacciHeap<PathfinderNodeBase, float> _heap = new(float.MinValue);
        private IReadOnlyDictionary<PathfinderNodeBase, float> _nodes;
        private bool _isNodeListReduced;

        [CanBeNull]
        public TPathfinderNode FindOne([NotNull] TPathfinderNode startNode,
            [NotNull] HashSet<TPathfinderNode> targetNodes, IReadOnlyDictionary<PathfinderNodeBase, float> nodeList,
            object edgeSettings, bool reduceNodes=false, Action<PathfinderNodeBase, float> doUpdateNodeScore=null)
        {
            if (targetNodes.Count == 0)
                return null;  //empty nodes can be for destroyed stations

            float maxScore = float.MinValue;
            foreach (TPathfinderNode targetNode in targetNodes)
            {
                if (nodeList.TryGetValue(targetNode, out float score))
                {
                    if (score > maxScore)
                        maxScore = score;
                }
            }

            if (maxScore < 0) //target node is not in the node list
                return null;

            float desiredMaxScore = reduceNodes ? maxScore * ScoreCoeficient : float.MaxValue;
            
            Initialize(startNode, nodeList, desiredMaxScore);
            OneOfManyNodesTargetChecker checker = new(targetNodes);
            if (Calculate(edgeSettings, checker))
            {
                float finalScore = checker.FoundNode.GetActualNodeValue()!.Value;
                if (finalScore > desiredMaxScore)
                {
                    doUpdateNodeScore?.Invoke(checker.FoundNode, finalScore);
                    FileLog.Log("Final score is higher than max. score of selected nodes. Refind with higher max. score");
                    //there is a possibility that found path is not shortest = try extend max score to this value
                    Initialize(startNode, nodeList, finalScore);
                    checker = new OneOfManyNodesTargetChecker(targetNodes);
                    return Calculate(edgeSettings, checker) ? checker.FoundNode : null;
                }

                return checker.FoundNode;
            } else if (reduceNodes)
            {
                //path was not found with reduced node list, retry find with all nodes
                FileLog.Log("Path was not found with reduced node list. Refind with full list.");
                Initialize(startNode, nodeList);
                checker = new OneOfManyNodesTargetChecker(targetNodes);
                return Calculate(edgeSettings, checker) ? checker.FoundNode : null;
            }

            return null;
        }
        
        public TPathfinderNode Find([NotNull] TPathfinderNode startNode,
            [NotNull]TPathfinderNode targetNode, IReadOnlyDictionary<PathfinderNodeBase, float> nodeList, object edgeSettings)
        {
            Initialize(startNode, nodeList);
            OneNodeTargetChecker checker = new(targetNode);
            return Calculate(edgeSettings, checker) ? checker.FoundNode : null;
        }

        public void FindAll([NotNull] TPathfinderNode startNode, IReadOnlyDictionary<PathfinderNodeBase, float> nodeList,
            object edgeSettings)
        {
            Initialize(startNode, nodeList);
            Calculate(edgeSettings, null);
        }
        
        private void Initialize([NotNull]TPathfinderNode startNode, IReadOnlyDictionary<PathfinderNodeBase, float> nodeList, float maxScore = float.MaxValue)
        {
            _heap.Clear();
            _nodes = nodeList;
            FibonacciHeapNode<PathfinderNodeBase, float> node2 = startNode.GetInitializedHeapNode();
            node2.Key = 0;
            _heap.Insert(node2);
            _isNodeListReduced = maxScore < float.MaxValue;
//            int numNodes = 1;
            foreach (KeyValuePair<PathfinderNodeBase, float> pfNodePair in nodeList)
            {
                if (maxScore < pfNodePair.Value)
                {
                    pfNodePair.Key.NodeIsIgnored = true;
                    continue;
                }
                if (pfNodePair.Key == startNode)
                    continue;
                FibonacciHeapNode<PathfinderNodeBase, float> node = pfNodePair.Key.GetInitializedHeapNode();
                _heap.Insert(node);
//                numNodes++;
            }
            //FileLog.Log($"Total nodes: {nodeList.Count}, reduced nodes {numNodes}");
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
                    if (endPfNode == null || endPfNode.NodeIsIgnored) continue;
                    
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
            foreach (PathfinderNodeBase node in _nodes.Keys)
            {
                float? value = node.GetActualNodeValue();
                if (value.HasValue)
                {
                    yield return (node, value.Value);
                }
            }
        }

        public void GetDistances(Dictionary<PathfinderNodeBase, float> result)
        {
            foreach (PathfinderNodeBase node in _nodes.Keys)
            {
                float? value = node.GetActualNodeValue();
                if (value.HasValue)
                {
                    result.Add(node, value.Value);
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