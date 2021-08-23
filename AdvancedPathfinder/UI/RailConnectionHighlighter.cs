using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelTycoon;
using VoxelTycoon.Tracks.Rails;

namespace AdvancedPathfinder.UI
{
    public class RailConnectionHighlighter: LazyManager<RailConnectionHighlighter>
    {
        private readonly LazyDictionary<(Color color, float halfWidth), Material> _materials = new();

        private readonly List<Vector3> _tempNormals = new List<Vector3>();
        private readonly List<int> _tempTriangles = new List<int>();
        private readonly List<Vector2> _tempUvs = new List<Vector2>();
        private readonly List<Vector3> _tempVertices = new List<Vector3>();
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int HalfWidth = Shader.PropertyToID("_HalfWidth");
        private readonly Dictionary<Rail, List<Highlighter>> _cacheFull = new();  //cache for full width highlighters
        private readonly Dictionary<RailConnection, List<Highlighter>> _cacheHalf = new();  //cache for half width highlighters

        public Highlighter ForOneTrack([NotNull] Rail track, Color color, float halfWidth = 0.4f, string name = "TrackHighlight")
        {
            Highlighter result = GetOrCreateFullHighlighterObject(track);
            result.name = name;
            result.MeshRenderer.sharedMaterial = GetMaterial(color, halfWidth);
            result.AttachedObject = track;
            result.gameObject.SetActive(true);
            return result;
        }
        
        public Highlighter ForOneConnection(RailConnection connection, Color color, float halfWidth = 0.4f, string name = "TrackHighlight")
        {
            Highlighter result = GetOrCreateHalfHighlighterObject(connection);
            result.name = name;
            result.MeshRenderer.sharedMaterial = GetMaterial(color, halfWidth);
            result.AttachedObject = connection;
            result.gameObject.SetActive(true);
            return result;
        }

        public void UpdateColorAndWidth(Highlighter highlighter, Color color, float halfWidth = 0.4f)
        {
            highlighter.MeshRenderer.sharedMaterial = GetMaterial(color, halfWidth);
        }

        public void SafeHide(Highlighter highlighter)
        {
            if (highlighter != null && highlighter.isActiveAndEnabled)
                highlighter.gameObject.SetActive(false);
        }

        protected override void OnDeinitialize()
        {
            foreach (Material value in _materials.Values)
            {
                UnityEngine.Object.Destroy(value);
            }
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            _materials.ValueFactory = delegate((Color color, float halfWidth) x)
            {
                Material material = new(R.Materials.RailBlockMaterial);
                material.SetColor(Color1, x.color);
                material.SetFloat(HalfWidth, x.halfWidth);
                return material;
            };
        }

        private Highlighter GetOrCreateHalfHighlighterObject(RailConnection connection)
        {
            if (_cacheHalf.TryGetValue(connection, out List<Highlighter> cached))
            {
                foreach (Highlighter highlighter in cached)
                {
                    if (highlighter.FreeForUse)
                        return highlighter;
                }
            }
            else
            {
                cached = new List<Highlighter>();
                _cacheHalf.Add(connection, cached);
            }
            Highlighter result = CreateGameObject();
            result.MeshFilter.sharedMesh = GenerateMesh(connection, halfTrack: true);
            result.gameObject.SetActive(false);
            result.transform.parent = connection.Track.transform;
            cached.Add(result);

            return result;
        }

        private Highlighter GetOrCreateFullHighlighterObject(Rail track)
        {
            if (ReferenceEquals(track, null) || !track.IsBuilt)
                return null;
            if (_cacheFull.TryGetValue(track, out List<Highlighter> cached))
            {
                foreach (Highlighter highlighter in cached)
                {
                    if (highlighter.FreeForUse)
                        return highlighter;
                }
            }
            else
            {
                cached = new List<Highlighter>();
                _cacheFull.Add(track, cached);
            }
            Highlighter result = CreateGameObject();
            result.MeshFilter.sharedMesh = GenerateMesh(track.GetConnection(0));
            result.gameObject.SetActive(false);
            result.transform.parent = track.transform;
            cached.Add(result);

            return result;
        }
        
        private Mesh GenerateMesh(RailConnection connection, string meshName = "TrackHighlight", bool halfTrack = false)
        {
            try
            {
                GenerateMeshData(connection, halfTrack);
                Mesh mesh = new() {name = meshName};
                mesh.SetVertices(_tempVertices);
                mesh.SetNormals(_tempNormals);
                mesh.SetUVs(0, _tempUvs);
                mesh.SetTriangles(_tempTriangles, 0);
                return mesh;
            }
            finally
            {
                ClearTempLists();
            }
        }

        private Highlighter CreateGameObject(string name = "TrackHighlight")
        {
            GameObject obj = new (name)
            {
                layer = (int)Layer.Default
            };
            MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            Highlighter highliter = obj.AddComponent<Highlighter>();
            highliter.MeshFilter = meshFilter;
            highliter.MeshRenderer = meshRenderer;
            highliter.Destroyed = OnHighlighterDestroyed; 
            return highliter;
        }

        private void OnHighlighterDestroyed(Highlighter hl)
        {
            switch (hl.AttachedObject)
            {
                case Rail rail:
                {
                    if (_cacheFull.TryGetValue(rail, out List<Highlighter> highlighters))
                    {
                        highlighters.Remove(hl);
                        if (highlighters.Count == 0)
                            _cacheFull.Remove(rail);
                    }

                    break;
                }
                case RailConnection railConnection:
                {
                    if (_cacheHalf.TryGetValue(railConnection, out List<Highlighter> highlighters))
                    {
                        highlighters.Remove(hl);
                        if (highlighters.Count == 0)
                            _cacheHalf.Remove(railConnection);
                    }

                    break;
                }
            }
        }

        private void ClearTempLists()
        {
            _tempVertices.Clear();
            _tempNormals.Clear();
            _tempUvs.Clear();
            _tempTriangles.Clear();
        }

        private Material GetMaterial(Color color, float halfWidth)
        {
            return _materials[(color, halfWidth)];
        }
        
        private void GenerateMeshData(RailConnection railConnection, bool halfTrack = false)
        {
            Vector3 vector = new Vector3(0f, 0.01f, 0f);
            float num = (halfTrack ? (0.5f - 0.25f / railConnection.Length) : 1f);
            int num2 = Mathf.Max(railConnection.Path.Curve.Points.Length / ((!halfTrack) ? 1 : 2) - 1, 1);
            float num3 = num / (float)num2;
            for (int j = 0; j < num2; j++)
            {
                float t = (float)j * num3;
                Vector3 item = railConnection.Evaluate(t) + vector;
                Vector3 vector2 = railConnection.EvaluateRight(t);
                float t2 = (float)(j + 1) * num3;
                Vector3 item2 = railConnection.Evaluate(t2) + vector;
                Vector3 vector3 = railConnection.EvaluateRight(t2);
                int count = _tempVertices.Count;
                _tempVertices.Add(item);
                _tempVertices.Add(item2);
                _tempVertices.Add(item2);
                _tempVertices.Add(item);
                _tempNormals.Add(-vector2);
                _tempNormals.Add(-vector3);
                _tempNormals.Add(vector3);
                _tempNormals.Add(vector2);
                _tempUvs.Add(new Vector2(0f, 0f));
                _tempUvs.Add(new Vector2(0f, 1f));
                _tempUvs.Add(new Vector2(1f, 1f));
                _tempUvs.Add(new Vector2(1f, 0f));
                _tempTriangles.Add(count);
                _tempTriangles.Add(count + 1);
                _tempTriangles.Add(count + 2);
                _tempTriangles.Add(count);
                _tempTriangles.Add(count + 2);
                _tempTriangles.Add(count + 3);
            }
            
        }
    }
}