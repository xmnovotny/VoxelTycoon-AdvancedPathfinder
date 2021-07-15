
using System;

namespace AdvancedPathfinder
{
    public static class SectionDirectionExtensions
    {
        public static SectionDirection Reverse(this SectionDirection direction)
        {
            switch (direction)
            {
                case SectionDirection.Forward:
                    return SectionDirection.Backward;
                case SectionDirection.Backward:
                    return SectionDirection.Forward;
                default:
                    return direction;
            }
        }

        public static SectionDirection Combine(this SectionDirection ownDirection, SectionDirection direction, bool isReversed)
        {
            if (direction == SectionDirection.Both || ownDirection == SectionDirection.None)
                return ownDirection;

            if (isReversed)
                direction = direction.Reverse();
            
            switch (direction)
            {
                case SectionDirection.Forward:
                    return ownDirection == SectionDirection.Backward
                        ? SectionDirection.None
                        : SectionDirection.Forward; 
                case SectionDirection.Backward:
                    return ownDirection == SectionDirection.Forward
                        ? SectionDirection.None
                        : SectionDirection.Backward; 
                case SectionDirection.None:
                    return SectionDirection.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        public static bool CanPass(this SectionDirection ownDirection, PathDirection direction)
        {
            return ownDirection == SectionDirection.Both || direction.ToSectionDirection() == ownDirection;
        }
    }
    
    public enum SectionDirection
    {
        None, Forward, Backward, Both
    }
}