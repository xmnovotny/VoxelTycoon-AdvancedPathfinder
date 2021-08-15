using System;
using System.Collections.Generic;
using AdvancedPathfinder.UI;
using HarmonyLib;
using ModSettingsUtils;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.PathSignals
{
    internal class PathSignalHighlighter: SimpleLazyManager<PathSignalHighlighter>
    {
        internal enum HighlighterType
        {
            BlockedRail = 0, BlockedLinkedRail, SimpleBlock, BeyondPath, NumberOfTypes
        }
        
        private static readonly Color[] Colors =
        {
            Color.green, //BlockedRail
            Color.red, //BlockedLinkedRail
//            Color.magenta,
            new (0, 230, 0, 1), //SimpleBlock
            Color.blue 
        };

        private static readonly float[] Widths =
        {
            0.42f, 0.2f, 0.43f, 0.44f
        };

        private static readonly float[] AlphaBases =
        {
            0.2f, 0.1f, 0.3f, 0.9f
        };
        
        private static readonly float[] AlphaMult =
        {
            0.2f, 0.1f, 0f, 0f
        };
        
        private bool _highlightPaths = false;
        private bool _highlightReservedBounds;
        private readonly Dictionary<Rail,HighlightersData> _highlighters = new();
        private readonly Dictionary<Train, (Rail reservedRail, Rail nonstopRail, Highlighter reservedHigh, Highlighter nonstopHigh)> _bounds = new();
        private readonly HashSet<RailBlockData> _fullHighlights = new();

        public bool HighlightPaths
        {
            get => _highlightPaths;
            set
            {
                if (SimpleManager<PathSignalManager>.Current == null)
                    value = false;
                if (_highlightPaths != value)
                {
                    _highlightPaths = value;
                    if (!value)
                        HideHighlighters();
                    else
                        HighlightReservedPaths();
                }
            }
        }

        public bool HighlightReservedBounds
        {
            get => _highlightReservedBounds;
            set
            {
                if (SimpleManager<PathSignalManager>.Current == null)
                    value = false;
                if (_highlightReservedBounds != value)
                {
                    _highlightReservedBounds = value;
                    if (_highlightPaths)
                    {
                        if (value)
                            HighlightAllReservedBounds();
                        else
                            HideHighlightedBounds();
                    }
                }
            }
        }

        public void HighlightChange(Rail rail, HighlighterType type, int countDiff)
        {
            HighlightersData data = GetOrCreateHighlightersData(rail);
            data.UpdateCount(type, countDiff);
        }

        public void FullBlockChange(RailBlockData blockData)
        {
            RailBlock block = blockData.Block;
            if (blockData.BlockBlockedCount>0 && !_fullHighlights.Contains(blockData))
            {
                _fullHighlights.Add(blockData);
                HighlightFullBlock(block, true);
            } else if (blockData.BlockBlockedCount == 0 && _fullHighlights.Contains(blockData))
            {
                _fullHighlights.Remove(blockData);
                HighlightFullBlock(block, false);
            }
        }

        public void Redraw()
        {
            if (_highlightPaths)
            {
                HideHighlighters();
                HighlightReservedPaths();
            }
        }

        public void HighlightReservedBoundsForTrain(Train train, Rail newReserved, Rail newNonstop)
        {
            if (!_highlightReservedBounds)
                return;
            RailConnectionHighlighter highMan = LazyManager<RailConnectionHighlighter>.Current;
            (Rail reservedRail, Rail nonstopRail, Highlighter reservedHigh, Highlighter nonstopHigh) data = _bounds.GetValueOrDefault(train);
            bool change = false;
            if (!ReferenceEquals(data.reservedRail, newReserved))
            {
                change = true;
                if (!ReferenceEquals(data.reservedHigh, null))
                {
                    highMan.SafeHide(data.reservedHigh);
                }

                if (!ReferenceEquals(newReserved, null))
                {
                    data.reservedHigh = highMan.ForOneTrack(newReserved, Color.black, 0.6f);
                }
            }

            if (!ReferenceEquals(data.nonstopRail, newNonstop))
            {
                change = true;
                if (!ReferenceEquals(data.nonstopHigh, null))
                {
                    highMan.SafeHide(data.nonstopHigh);
                }

                if (!ReferenceEquals(newNonstop, null))
                {
                    data.nonstopHigh = highMan.ForOneTrack(newNonstop, Color.blue, 0.7f);
                }
            }

            if (change)
                _bounds[train] = data;
        }

        private HighlightersData GetOrCreateHighlightersData(Rail rail)
        {
            if (!_highlighters.TryGetValue(rail, out HighlightersData data))
                data = _highlighters[rail] = new HighlightersData(rail);
            return data;
        }

        private void HighlightReservedPaths()
        {
            foreach (RailBlockData blockData in SimpleManager<PathSignalManager>.Current!.RailBlocks.Values)
            {
                if (blockData is PathRailBlockData pathRailBlockData)
                {
                    foreach (KeyValuePair<Rail, int> railPair in pathRailBlockData.BlockedRails)
                    {
                        if (railPair.Value > 0)
                        {
                            HighlightersData data = GetOrCreateHighlightersData(railPair.Key);
                            data.UpdateCount(HighlighterType.BlockedRail, railPair.Value);
                        }
                    }

                    foreach (KeyValuePair<Rail, int> railPair in pathRailBlockData.BlockedLinkedRails)
                    {
                        if (railPair.Value > 0)
                        {
                            HighlightersData data = GetOrCreateHighlightersData(railPair.Key);
                            data.UpdateCount(HighlighterType.BlockedLinkedRail, railPair.Value);
                        }
                    }

                    foreach (KeyValuePair<Train, PooledHashSet<Rail>> railPair in pathRailBlockData.ReservedBeyondPath)
                    {
                        foreach (Rail rail in railPair.Value)
                        {
                            HighlightersData data = GetOrCreateHighlightersData(rail);
                            data.UpdateCount(HighlighterType.BeyondPath, 1);
                        }
                    }
                }

                if  (blockData is SimpleRailBlockData simpleRailBlockData && simpleRailBlockData.ReservedForTrain != null || blockData.IsFullBlocked)
                {
                    RailBlock block = blockData.Block;
                    HighlightFullBlock(block, true);

                    _fullHighlights.Add(blockData);
                }
            }
            
            if (_highlightReservedBounds)
                HighlightAllReservedBounds();
        }

        private void HighlightFullBlock(RailBlock block, bool highlighted)
        {
            UniqueList<RailConnection> connections = SimpleLazyManager<RailBlockHelper>.Current.GetBlockConnections(block);
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                HighlightersData data = GetOrCreateHighlightersData(connections[i].Track);
                data.SetCount(HighlighterType.SimpleBlock, highlighted ? 1 : 0);
            }
        }

        private void HideHighlighters()
        {
            foreach (HighlightersData highlighterData in _highlighters.Values)
            {
                highlighterData.HideAll();
            }

            _highlighters.Clear();
            _fullHighlights.Clear();
            
            HideHighlightedBounds();
        }

        private void HideHighlightedBounds()
        {
            RailConnectionHighlighter highMan = LazyManager<RailConnectionHighlighter>.Current;
            foreach ((Rail reservedRail, Rail nonstopRail, Highlighter reservedHigh, Highlighter nonstopHigh) in _bounds.Values)
            {
                highMan.SafeHide(reservedHigh);
                highMan.SafeHide(nonstopHigh);
            }

            _bounds.Clear();
        }

        private void HighlightAllReservedBounds()
        {
            foreach ((Train train, Rail reserved, Rail nonstop) in SimpleManager<PathSignalManager>.Current!.GetReservedBoundsForHighlight())
            {
                HighlightReservedBoundsForTrain(train, reserved, nonstop);
            }
        }

        private struct HighlighterData
        {
            public int Count;
            private Highlighter _highlighter;

            public void Hide()
            {
                if (!ReferenceEquals(_highlighter, null) && _highlighter != null && _highlighter.isActiveAndEnabled)
                {
                    _highlighter.gameObject.SetActive(false);
                    _highlighter = null;
                }
            }

            public void Show(Rail rail, Color color, float halfWidth)
            {
                if (!ReferenceEquals(_highlighter, null))
                {
//                    FileLog.Log($"Reused highlighter {_highlighter.GetHashCode():X8}, rail: {rail.GetHashCode():X8}");
                    LazyManager<RailConnectionHighlighter>.Current.UpdateColorAndWidth(_highlighter, color, halfWidth);
                }
                else
                {
                    _highlighter = LazyManager<RailConnectionHighlighter>.Current.ForOneTrack(rail, color, halfWidth);
//                    FileLog.Log($"New highlighter {_highlighter.GetHashCode():X8}, rail: {rail.GetHashCode():X8}");
                }
            }
        }

        private class HighlightersData
        {
            private readonly HighlighterData[] _data;
            public Rail Rail { get; }

            public HighlightersData(Rail rail)
            {
                Rail = rail;
                _data = new HighlighterData[(int) HighlighterType.NumberOfTypes];
            }

            public void SetCount(HighlighterType type, int newCount)
            {
                int index = (int) type;
                int oldCount = _data[index].Count;
                newCount = Math.Max(0, newCount);
                if (oldCount != newCount)
                {
                    _data[index].Count = newCount;
                    if (newCount > 0)
                    {
                        Color color = Colors[index].WithAlpha(AlphaBases[index] + AlphaMult[index] * newCount);
                        _data[index].Show(Rail, color, Widths[index]);
                    }
                    else
                    {
                        _data[index].Hide();
                    }
                }
            }

            public void UpdateCount(HighlighterType type, int countDiff)
            {
                SetCount(type, _data[(int) type].Count + countDiff);                
            }

            public void HideAll()
            {
                for (int i = _data.Length - 1; i >= 0; i--)
                {
                    _data[i].Hide();
                }
            }
        }
    }
}