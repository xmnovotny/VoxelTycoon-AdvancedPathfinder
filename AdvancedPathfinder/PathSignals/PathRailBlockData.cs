using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;
using ExceptionHelper = VoxelTycoon.ExceptionHelper;

namespace AdvancedPathfinder.PathSignals
{
    public class PathRailBlockData: RailBlockData
    {
        public readonly HashSet<RailSignal> OutboundSignals = new();
        private readonly Dictionary<Rail, int> _blockedRails = new();
        private readonly Dictionary<Rail, int> _blockedLinkedRails = new();
        private int _lastPathIndex = 0;
        private RailSignal _lastEndSignal = null;

        private readonly Dictionary<Train, (PooledList<Rail> rails, Rail lastPathRail)> _reservedBeyondPath = new(); //only rails in possible path, not linked rails
        private readonly Dictionary<Train, PooledList<Rail>> _reservedTrainPath = new(); //only rails in train path, not linked rails

        internal IReadOnlyDictionary<Rail, int> BlockedLinkedRails => _blockedLinkedRails;
        internal IReadOnlyDictionary<Rail, int> BlockedRails => _blockedRails;

        public PathRailBlockData([NotNull] RailBlock block): base(block)
        {
        }

        public SimpleRailBlockData ToSimpleBlockData()
        {
            return new (Block, InboundSignals);
        }


        internal override bool TryReservePath(Train train, PathCollection path, int startIndex)
        {
            PathSignalData startSignalData = GetAndTestStartSignal(path, startIndex);
            if (startSignalData.IsChainSignal)
            {
                using PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railCache = PooledList<(Rail, bool, bool)>.Take();
                if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                    return false;
                if (_lastEndSignal != null)
                {
                    PathSignalData nextSignalData = SimpleManager<PathSignalManager>.SafeCurrent.GetPathSignalData(_lastEndSignal);
                    RailBlockData nextBlockData = nextSignalData.BlockData;
                    if (nextBlockData == this)
                        throw new InvalidOperationException("Next block in the path is the same block");
                    if (!nextBlockData.TryReservePath(train, path, _lastPathIndex))
                        return false;
                }
                ReserveOwnPathInternal(train, railCache, startSignalData);
                return true;
            }

            return TryReserveOwnPath(train, path, startIndex, startSignalData);
        }
        
        
        private bool TryReserveOwnPath([NotNull] Train train, [NotNull] PathCollection path, int startIndex, PathSignalData startSignalData)
        {
            using PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railCache = PooledList<(Rail, bool, bool)>.Take();
            if (!CanReserveOwnPath(path, startIndex, startSignalData, railCache))
                return false;

            ReserveOwnPathInternal(train, railCache, startSignalData);
            return true;
        }
        
        private bool CanReserveOwnPath([NotNull] PathCollection path, int startIndex, PathSignalData startSignalData, PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> cacheList = null)
        {
            if (startSignalData.ReservedForTrain != null)
            {
                return false;
            }
            foreach ((Rail rail, bool isLinkedRail, bool beyondPath) in AffectedRailsEnum(path, startIndex))
            {
                if (_blockedRails.TryGetValue(rail, out int value) && value > 0 || (!isLinkedRail && _blockedLinkedRails.TryGetValue(rail, out int value2) && value2 > 0))
                {
                    //rail in the path is blocked by another reserved path
                    return false;
                }

                cacheList?.Add((rail, isLinkedRail, beyondPath));
            }

            return true;
        }

        private void ReserveOwnPathInternal(Train train, PooledList<(Rail rail, bool isLinkedRail, bool beyondPath)> railsToBlock, PathSignalData startSignal)
        {
            Rail lastRail = null;
            PooledList<Rail> beyondRails = null;
            PooledList<Rail> trainRails = null;
            if (_reservedTrainPath.ContainsKey(train))
            {
                //throw new InvalidOperationException("Already reserved path for the train");
                trainRails = _reservedTrainPath[train];
                _reservedTrainPath.Remove(train);
            }
            else
            {
                trainRails = PooledList<Rail>.Take();
            }

            try
            {
                foreach ((Rail rail, bool isLinkedRail, bool beyondPath) in railsToBlock)
                {
                    if (beyondPath == false)
                    {
                        lastRail = rail;
                        if (!isLinkedRail)
                            trainRails.Add(rail);
                    }
                    else
                    {
                        if (beyondRails == null)
                        {
                            if (_reservedBeyondPath.ContainsKey(train))
                                throw new InvalidOperationException("Already defined beyond rail path for the train");
                            beyondRails = PooledList<Rail>.Take();
                        }

                        if (!isLinkedRail)
                            beyondRails.Add(rail);
                    }
                    if (!isLinkedRail)
                        _blockedRails.AddIntToDict(rail, 1);
                    else
                        _blockedLinkedRails.AddIntToDict(rail, 1);
                }

                if (beyondRails != null)
                {
                    _reservedBeyondPath.Add(train, (beyondRails, lastRail));
                }
                _reservedTrainPath.Add(train, trainRails);
                startSignal.ReservedForTrain = train;
            }
            catch (Exception e) when (ExceptionFault.FaultBlock(e, delegate
            {
                trainRails?.Dispose();
                beyondRails?.Dispose();
            }))
            {
            }
        }
        
        private IEnumerable<(Rail rail, bool isLinkedRail, bool beyondPath)> AffectedRailsEnum(PathCollection path, int startIndex)
        {
            int index = startIndex + 1;
            _lastPathIndex = index;
            _lastEndSignal = null;
            RailConnection connection = null;
            while (index <= path.FrontIndex)
            {
                connection = (RailConnection) path[index];
                Rail rail = connection.Track;
                if (connection.Block != Block)
                    throw new InvalidOperationException("Connection is from another block");
                yield return (rail, false, false);
                for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                {
                    yield return (rail.GetLinkedRail(j), true, false);
                }

                if (connection.Signal != null || connection.InnerConnection.Signal != null)
                {
                    _lastEndSignal = connection.Signal;
                    yield break;
                }

                _lastPathIndex = ++index;
            }

            if (connection == null)
                throw new InvalidOperationException("No available connection in the provided path");

                //end of the path, but no signal found (=need to go through all possible connections until signal is found) 
            using PooledList<RailConnection> connections = PooledList<RailConnection>.Take();
            connections.Add(connection);
            for (int i = 0; i < connections.Count; i++)
            {
                connection = connections[i];
                foreach (TrackConnection trackConnection in connection.InnerConnection.OuterConnections)
                {
                    RailConnection outerConnection = (RailConnection) trackConnection;
                    if (outerConnection.Signal != null || outerConnection.InnerConnection.Signal != null)
                        continue;
                    Rail rail = outerConnection.Track;
                    yield return (rail, false, true);
                    connections.Add(outerConnection);
                    for (int j = rail.LinkedRailCount - 1; j >= 0; j--)
                    {
                        yield return (rail.GetLinkedRail(j), true, true);
                    }
                }
            }
        }
    }
}