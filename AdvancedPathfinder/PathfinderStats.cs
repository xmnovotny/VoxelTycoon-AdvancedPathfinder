using System.Diagnostics;
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
        public float SignalOpenForTrainTimeSum { get; private set; }
        public int SignalOpenForTrainTimeCount { get; private set; }
        public float WrappedTravelTimeSum { get; private set; }
        public int WrappedTravelTimeCount { get; private set; }

        private Stopwatch _signalOpenForTrainTimeSw = new();
        private Stopwatch _wrappedTravelTimeSw = new();


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
            result += $"Wrapped travel in front time: average: {(float)WrappedTravelTimeSum/WrappedTravelTimeCount:N4} ms, total: {WrappedTravelTimeSum:N2} ms\n";
            result += $"Train is signal open: average: {(float)SignalOpenForTrainTimeSum/SignalOpenForTrainTimeCount:N4} ms, per train move: {(float)SignalOpenForTrainTimeSum/TrainMoveTimeCount:N4} ms, total: {SignalOpenForTrainTimeSum:N2} ms\n";
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
        public void AddSignalOpenForTrainTime(float milliseconds)
        {
            SignalOpenForTrainTimeSum += milliseconds;
            SignalOpenForTrainTimeCount++;
        }
        public void StartSignalOpenForTrain()
        {
            _signalOpenForTrainTimeSw.Restart();
        }
        public void StopSignalOpenForTrain()
        {
            if (_signalOpenForTrainTimeSw.IsRunning)
            {
                _signalOpenForTrainTimeSw.Stop();
                AddSignalOpenForTrainTime(_signalOpenForTrainTimeSw.ElapsedTicks / 10000f);
            }
        }
        public void AddWrappedTravelTime(float milliseconds)
        {
            WrappedTravelTimeSum += milliseconds;
            WrappedTravelTimeCount++;
        }
        public void StartWrappedTravel()
        {
            _wrappedTravelTimeSw.Restart();
        }
        public void StopWrappedTravel()
        {
            if (_wrappedTravelTimeSw.IsRunning)
            {
                _wrappedTravelTimeSw.Stop();
                AddWrappedTravelTime(_wrappedTravelTimeSw.ElapsedTicks / 10000f);
            }
        }
    }
}