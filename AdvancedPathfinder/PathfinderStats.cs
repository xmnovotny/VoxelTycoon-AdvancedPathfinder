using VoxelTycoon;

namespace AdvancedPathfinder
{
    public class PathfinderStats
    {
        public float StartTime { get; }
        public float PathfindingTimeSum { get; private set; }
        public int PathfindingTimeCount { get; private set; }
        public int FullNodesCountSum { get; private set; }
        public int SubNodesCountSum { get; private set; }
        public int ReducedNodesCountSum { get; private set; }
        public float TrainMoveTimeSum { get; private set; }
        public int TrainMoveTimeCount { get; private set; }


        public PathfinderStats()
        {
            StartTime = LazyManager<TimeManager>.Current.UnscaledUnpausedSessionTime;
        }

        public string GetStatsText()
        {
            string result = $"Pathfinding time: average {PathfindingTimeSum / PathfindingTimeCount:N2} ms, total {PathfindingTimeSum:N2} ms\n";
            float seconds = LazyManager<TimeManager>.Current.UnscaledUnpausedSessionTime - StartTime;
            result += $"Values per seconds: pathfinding count: {(float)PathfindingTimeCount/seconds:N2}, time: {PathfindingTimeSum / seconds:N3} ms\n";
            result += $"Train move time: average: {(float)TrainMoveTimeSum/TrainMoveTimeCount:N3} ms, total: {TrainMoveTimeSum:N2} ms\n";
            if (FullNodesCountSum > 0) 
                result += $"Sub nodes {(float)SubNodesCountSum/FullNodesCountSum*100f:N1}%, reduced nodes {(float)ReducedNodesCountSum/FullNodesCountSum*100f:N1}%";
            return result;
        }

        public void AddPathfindingTime(float milliseconds)
        {
            PathfindingTimeSum += milliseconds;
            PathfindingTimeCount++;
        }

        public void AddFullNodesCount(int count)
        {
            FullNodesCountSum += count;
        }

        public void AddSubNodesCount(int count)
        {
            SubNodesCountSum += count;
        }

        public void AddReducedNodesCount(int count)
        {
            ReducedNodesCountSum += count;
        }

        public void AddTrainMoveTime(float milliseconds)
        {
            TrainMoveTimeSum += milliseconds;
            TrainMoveTimeCount++;
        }
    }
}