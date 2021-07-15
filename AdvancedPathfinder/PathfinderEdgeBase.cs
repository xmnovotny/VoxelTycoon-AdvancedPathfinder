namespace AdvancedPathfinder
{
    public abstract class PathfinderEdgeBase
    {
        public const float NoConnection = float.MaxValue;
        internal abstract float GetScore(object edgeSettings);
        public PathfinderNodeBase NextNode { get; protected set; }
    }
}