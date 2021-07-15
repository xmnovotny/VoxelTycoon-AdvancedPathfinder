namespace AdvancedPathfinder
{
    public static class PathDirectionExtensions
    {
        public static SectionDirection ToSectionDirection(this PathDirection direction)
        {
            return direction == PathDirection.Forward ? SectionDirection.Forward : SectionDirection.Backward;
        }

        public static PathDirection Reverse(this PathDirection direction)
        {
            return direction == PathDirection.Forward ? PathDirection.Backward : PathDirection.Forward;
        }
    }

    public enum PathDirection
    {
        Forward, Backward
    }
}