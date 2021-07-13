namespace AdvancedPathfinder
{
    public abstract class PathfinderEdgeBase
    {
        internal abstract float GetScore();
        public PathfinderNodeBase NextNode { get; protected set; }
    }
}