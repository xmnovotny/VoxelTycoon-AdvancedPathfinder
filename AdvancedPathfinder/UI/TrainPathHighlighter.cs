using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Tracks;
using VoxelTycoon.Tracks.Rails;
using XMNUtils;

namespace AdvancedPathfinder.UI
{
    [HarmonyPatch]
    public class TrainPathHighlighter: SimpleLazyManager<TrainPathHighlighter>
    {
        private bool _displayIndividualPaths;
        private readonly Dictionary<Train, TrainData> _data = new();
        private readonly HashSet<Train> _displayedWindow = new();
        private bool _displayAllTrainsPaths;
        private readonly Color[] _colors = 
        {
            Color.blue.WithAlpha(0.5f),
            Color.cyan.WithAlpha(0.4f),
            Color.magenta.WithAlpha(0.4f),
            Color.green.WithAlpha(0.4f),
            Color.red.WithAlpha(0.4f),
            Color.yellow.WithAlpha(0.4f)
        };

        private readonly Dictionary<Color, int> _usedColors = new();

        public TrainPathHighlighter()
        {
            foreach (Color color in _colors)
            {
                _usedColors.Add(color, 0);
            }
        }

        public bool DisplayIndividualPaths
        {
            get => _displayIndividualPaths;
            set
            {
                if (_displayIndividualPaths != value)
                {
                    _displayIndividualPaths = value;
                    if (_displayAllTrainsPaths)
                        return;
                    if (value)
                    {
                        foreach (Train train in _displayedWindow)
                        {
                            ShowForInternal(train);
                        }
                    } else 
                    {
                        foreach (Train train in _displayedWindow)
                        {
                            HideForInternal(train);
                        }
                    }
                }
            }
        }

        public bool DisplayAllTrainsPaths
        {
            get => _displayAllTrainsPaths;
            set
            {
                if (_displayAllTrainsPaths != value)
                {
                    _displayAllTrainsPaths = value;
                    if (value)
                        ShowForAll();
                    else
                        HideForAll();
                }
            }
        }

        public void ShowFor(Train train)
        {
            if (_displayedWindow.Contains(train))
            {
                throw new InvalidOperationException("Already displayed for this train");
            }
            _displayedWindow.Add(train);
            ShowForInternal(train);
        }

        public void HideFor(Train train)
        {
            if (!_displayAllTrainsPaths)
                HideForInternal(train);
            _displayedWindow.Remove(train);
        }

        protected override void OnInitialize()
        {
            SimpleLazyManager<TrainHelper>.Current.RegisterTrainAttachedAction(OnTrainAttached);
            SimpleLazyManager<TrainHelper>.Current.RegisterTrainDetachedAction(OnTrainDetached);
        }

        private void OnTrainAttached(Train train, PathCollection path)
        {
            if (_displayAllTrainsPaths || _displayedWindow.Contains(train))
                ShowForInternal(train);
        }

        private void OnTrainDetached(Train train)
        {
            HideForInternal(train);
        }

        private void ShowForAll()
        {
            ImmutableList<Vehicle> trains = LazyManager<VehicleManager>.Current.GetAll<Train>();
            for (int i = trains.Count - 1; i >= 0; i--)
            {
                Train train = (Train) trains[i];
                ShowForInternal(train);
            }
        }

        private void HideForAll()
        {
            foreach (Train train in _data.Keys.ToArray())
            {
                if (!_displayedWindow.Contains(train))
                    HideForInternal(train);
            }
        }

        private void UpdatePath(Train train, bool force=false)
        {
            if (_data.TryGetValue(train, out TrainData data))
            {
                data.RedrawPath(force);
            }
        }

        private void ShowForInternal(Train train)
        {
            if (_data.ContainsKey(train))
                return;
            TrainData data = new TrainData(train, SimpleLazyManager<TrainHelper>.Current.GetTrainPath(train), GetColor());
            _data.Add(train, data);
            data.RedrawPath();
        }

        private void HideForInternal(Train train)
        {
            if (_data.TryGetValue(train, out TrainData data))
            {
                data.ReleaseHighlighters();
                _usedColors.AddIntToDict(data.Color, -1);
                _data.Remove(train);
            }
        }

        private Color GetColor()
        {
            Color minColor = _colors[0];
            int min = Int32.MaxValue;
            foreach (KeyValuePair<Color, int> colorPair in _usedColors)
            {
                if (colorPair.Value == 0)
                {
                    minColor = colorPair.Key;
                    break;
                }

                if (colorPair.Value < min)
                {
                    min = colorPair.Value;
                    minColor = colorPair.Key;
                }
            }
            _usedColors.AddIntToDict(minColor, 1);
            return minColor;
        }
        
        private class TrainData/*: IDisposable*/
        {
            private readonly Dictionary<RailConnection, Highlighter> _usedHighlighters = new();
            private readonly HashSet<RailConnection> _tmpToAddHashSet = new();
            private readonly HashSet<RailConnection> _tmpToRemoveHashSet = new();

            private readonly Train _train;
            private readonly PathCollection _path;
            private readonly Color _color;
            private float _lastUpdated;
            public bool IsDirty = true;
            public Color Color => _color;


            public TrainData(Train train, [NotNull] PathCollection path, Color color)
            {
                this._train = train;
                this._path = path;
                this._color = color;
            }

            public void ReleaseHighlighters()
            {
                foreach (Highlighter highlighter in _usedHighlighters.Values)
                {
                    if (highlighter == null)
                        continue;
                    highlighter.gameObject.SetActive(false);
                }
                _usedHighlighters.Clear();
            }

            public void RedrawPath(bool force=false)
            {
                if (!force && Time.time - _lastUpdated < 1f)
                    return;
                Stopwatch sw = Stopwatch.StartNew();
                _tmpToAddHashSet.Clear();
                _tmpToRemoveHashSet.Clear();

                RailConnectionHighlighter hlMan = LazyManager<RailConnectionHighlighter>.Current;
                int frontIndex = _path.FrontIndex;
                _tmpToRemoveHashSet.UnionWith(_usedHighlighters.Keys);
                for (int i = _path.RearIndex; i < frontIndex; i++)
                {
                    if (_path[i] is RailConnection conn)
                    {
                        if (!_usedHighlighters.ContainsKey(conn))
                        {
                            _tmpToAddHashSet.Add(conn);
                        }
                        else
                        {
                            _tmpToRemoveHashSet.Remove(conn);
                        }
                    }
                }

                foreach (RailConnection connection in _tmpToRemoveHashSet)
                {   
                    if (!connection.Track.IsBuilt)
                        continue;
                    _usedHighlighters[connection].gameObject.SetActive(false);
                    _usedHighlighters.Remove(connection);
                }
                
                foreach (RailConnection conn in _tmpToAddHashSet)
                {
                    if (!conn.Track.IsBuilt)
                        continue;
                    _usedHighlighters.Add(conn, hlMan.ForOneTrack(conn.Track, _color, 0.2f));
                }

//                FileLog.Log("Updated highlight in {0} ms".Format((sw.ElapsedTicks / 10000f).ToString("N4")));

                IsDirty = false;
                _lastUpdated = Time.time;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vehicle), "UpdatePathIntenal")]
        private static void Vehicle_UpdatePathIntenal_pof(Vehicle __instance)
        {
            if (__instance is Train train)
            {
                Current.UpdatePath(train, true);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrackUnit), "UpdatePosition")]
        private static void TrackUnit_UpdatePosition_pof(TrackUnit __instance)
        {
            if (__instance is Train train)
            {
                Current.UpdatePath(train);
            }
        }
    }

}