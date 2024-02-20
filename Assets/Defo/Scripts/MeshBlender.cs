using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
internal class MeshBlender : MonoBehaviour
{
    public static bool EnableSmoothInput { get; private set; }

    private const bool THROW_BAD_RATIO_EXCEPTION = false;

    [SerializeField] private bool _swap;
    [Space]
    [SerializeField] private bool _enableUVBlending;
    [Space]
    [Range(0, 1)]
    [SerializeField] private float _blend;
    [Space]
    [SerializeField] private MeshFilter _srcTmpl;
    [SerializeField] private MeshFilter _dstTmpl;
    [Space]
    [SerializeField] private MeshCollider _otherMeshCollider;
    [Space]
    [SerializeField] private ComputeShader _computeShader;

    private float _lastBlend;
    private bool _lastSwap;

    private Mesh _src;
    private Mesh _dst;
    private Mesh _dumDst;

    private MeshFilter cachedMeshFilter;
    private MeshFilter CachedMeshFilter => cachedMeshFilter = cachedMeshFilter != null ? cachedMeshFilter : GetComponent<MeshFilter>();

    private MeshCollider cachedMeshCollider;
    private MeshCollider CachedMeshCollider => cachedMeshCollider = cachedMeshCollider != null ? cachedMeshCollider : GetComponent<MeshCollider>();

    private Mesh Mesh { get => CachedMeshFilter.mesh; set => CachedMeshFilter.mesh = value; }

    [Header("Jobs")]
    private NativeArray<Vector3> _blendedVerts;
    private NativeArray<Vector3> _srcVerts;
    private NativeArray<Vector3> _dstVerts;

    private NativeArray<Vector2> _blendedUVs;
    private NativeArray<Vector2> _srcUVs;
    private NativeArray<Vector2> _dstUVs;

    private NativeArray<bool> _toBlend;
    private NativeArray<float> _blendValues;

    private BlendVertsJob blendVertsJob;
    private BlendUVsJob blendUVsJob;
    private BlendVertsIfJob blendVertsIfJob;
    private BlendUVsIfJob blendUVsIfJob;

    [Header("CS")]
    private ComputeBuffer _blendedVertsCB;
    private ComputeBuffer _srcVertsCB;
    private ComputeBuffer _dstVertsCB;

    private ComputeBuffer _blendedUVsCB;
    private ComputeBuffer _srcUVsCB;
    private ComputeBuffer _dstUVsCB;

    private Vector3[] _blendedVertsCSResult;
    private Vector2[] _blendedUVsCSResult;

    private void Awake() => SetupBlending();
    private void Update() => HandleBlending();

    private void SetupBlending()
    {
        _src = BakeMesh(_srcTmpl);
        _dst = BakeMesh(_dstTmpl);

        _dumDst = DummifyMesh(_src, _dst);

        AssignMesh(_src);

        SetupJobs();
        SetupCS();

        void SetupJobs()
        {
            _blendedVerts = new NativeArray<Vector3>(Mesh.vertices, Allocator.Persistent);
            _srcVerts = new NativeArray<Vector3>(_src.vertices, Allocator.Persistent);
            _dstVerts = new NativeArray<Vector3>(_dumDst.vertices, Allocator.Persistent);

            _blendedUVs = new NativeArray<Vector2>(Mesh.uv, Allocator.Persistent);
            _srcUVs = new NativeArray<Vector2>(_src.uv, Allocator.Persistent);
            _dstUVs = new NativeArray<Vector2>(_dumDst.uv, Allocator.Persistent);

            _toBlend = new NativeArray<bool>(Mesh.vertices.Length, Allocator.Persistent);
            _blendValues = new NativeArray<float>(Mesh.vertices.Length, Allocator.Persistent);

            blendVertsJob = new BlendVertsJob
            {
                Verts = _blendedVerts,
                SrcVerts = _srcVerts,
                DstVerts = _dstVerts
            };

            blendUVsJob = new BlendUVsJob
            {
                UVs = _blendedUVs,
                SrcUVs = _srcUVs,
                DstUVs = _dstUVs
            };

            blendVertsIfJob = new BlendVertsIfJob
            {
                Verts = _blendedVerts,
                SrcVerts = _srcVerts,
                DstVerts = _dstVerts
            };

            blendUVsIfJob = new BlendUVsIfJob
            {
                UVs = _blendedUVs,
                SrcUVs = _srcUVs,
                DstUVs = _dstUVs
            };
        }

        void SetupCS()
        {
            _blendedVertsCB = new ComputeBuffer(_blendedVerts.Length, sizeof(float) * 3);
            _srcVertsCB = new ComputeBuffer(_srcVerts.Length, sizeof(float) * 3);
            _dstVertsCB = new ComputeBuffer(_dstVerts.Length, sizeof(float) * 3);

            _blendedUVsCB = new ComputeBuffer(_blendedUVs.Length, sizeof(float) * 2);
            _srcUVsCB = new ComputeBuffer(_srcUVs.Length, sizeof(float) * 2);
            _dstUVsCB = new ComputeBuffer(_dstUVs.Length, sizeof(float) * 2);

            _srcVertsCB.SetData(_srcVerts);
            _dstVertsCB.SetData(_dstVerts);

            _srcUVsCB.SetData(_srcUVs);
            _dstUVsCB.SetData(_dstUVs);

            _computeShader.SetBuffer(0, "Verts", _blendedVertsCB);
            _computeShader.SetBuffer(0, "SrcVerts", _srcVertsCB);
            _computeShader.SetBuffer(0, "DstVerts", _dstVertsCB);

            _computeShader.SetBuffer(0, "UVs", _blendedUVsCB);
            _computeShader.SetBuffer(0, "SrcUVs", _srcUVsCB);
            _computeShader.SetBuffer(0, "DstUVs", _dstUVsCB);

            _blendedVertsCSResult = new Vector3[_blendedVerts.Length];
            _blendedUVsCSResult = new Vector2[_blendedUVs.Length];
        }
    }

    private Mesh BakeMesh(MeshFilter meshFilter)
    {
        var mesh = new Mesh();
        SetArrays();
        return mesh;

        void SetArrays()
        {
            var verts = new List<Vector3>();

            foreach (var vert in meshFilter.mesh.vertices)
                verts.Add(Matrix4x4.TRS(Vector3.zero, meshFilter.transform.localRotation, meshFilter.transform.localScale).MultiplyPoint(vert));

            mesh.vertices = verts.ToArray();
            mesh.triangles = meshFilter.mesh.triangles;
            mesh.uv = meshFilter.mesh.uv;
        }
    }

    private Mesh DummifyMesh(Mesh mesh, Mesh model)
    {
        if (THROW_BAD_RATIO_EXCEPTION && (float)mesh.vertices.Length / model.vertices.Length <= 1) throw new Exception("Bad Ratio");

        var dummy = new Mesh();
        SetArraysClosestPointOptimized();
        return dummy;

        void SetArraysClosestRaycastNormals()
        {
            mesh.RecalculateNormals();

            var _ = CachedMeshCollider.sharedMesh;
            CachedMeshCollider.sharedMesh = model;

            var hits = new List<Vector3>();
            var verts = new List<Vector3>();
            for (var i = 0; i < mesh.vertices.Length; i++)
                verts.Add(ProjectVertex(i));

            for (var i = 0; i < verts.Count; i++)
                verts[i] = hits.OrderBy(x => (x - verts[i]).sqrMagnitude).First();

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;

            CachedMeshCollider.sharedMesh = _;

            Vector3 ProjectVertex(int i)
            {
                if (Physics.Raycast(transform.TransformPoint(mesh.vertices[i]), mesh.vertices[i] - mesh.normals[i], out var hit))
                {
                    hits.Add(transform.InverseTransformPoint(hit.point));
                    return transform.InverseTransformPoint(hit.point);
                }
                else return mesh.vertices[i];
            }
        }

        void SetArraysClosestPoint()
        {
            var verts = new List<Vector3>();
            foreach (var vert in mesh.vertices)
                verts.Add(model.vertices.OrderBy(x => (x - vert).sqrMagnitude).First());

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;
        }

        void SetArraysClosestPointCollider()
        {
            var _ = CachedMeshCollider.sharedMesh;
            var __ = CachedMeshCollider.convex;
            CachedMeshCollider.sharedMesh = model;
            CachedMeshCollider.convex = true;

            var verts = new List<Vector3>();
            foreach (var vert in mesh.vertices)
                verts.Add(transform.InverseTransformPoint(CachedMeshCollider.ClosestPoint(transform.TransformPoint(vert))));

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;

            CachedMeshCollider.sharedMesh = _;
            CachedMeshCollider.convex = __;
        }

        void SetArraysClosestPointPhysics()
        {
            var _ = CachedMeshCollider.sharedMesh;
            var __ = CachedMeshCollider.convex;
            CachedMeshCollider.sharedMesh = model;
            CachedMeshCollider.convex = true;

            var verts = new List<Vector3>();
            foreach (var vert in mesh.vertices)
                verts.Add(
                    transform.InverseTransformPoint(
                        Physics.ClosestPoint(transform.TransformPoint(vert), CachedMeshCollider, transform.position, transform.rotation)
                    )
                );

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;

            CachedMeshCollider.sharedMesh = _;
            CachedMeshCollider.convex = __;
        }

        void SetArraysClosestRaycastNormals_ClosestPoint()
        {
            mesh.RecalculateNormals();

            var _ = CachedMeshCollider.sharedMesh;
            CachedMeshCollider.sharedMesh = model;

            var verts = new List<Vector3>();
            for (var i = 0; i < mesh.vertices.Length; i++)
                verts.Add(ProjectVertex(i));

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;

            CachedMeshCollider.sharedMesh = _;

            Vector3 ProjectVertex(int i)
            {
                if (!Physics.Raycast(transform.TransformPoint(mesh.vertices[i]), mesh.vertices[i] - mesh.normals[i], out var hit))
                    return model.vertices.OrderBy(x => (x - mesh.vertices[i]).sqrMagnitude).First();

                return transform.InverseTransformPoint(hit.point);
            }
        }

        void SetArraysClosestRaycastNormals_ClosestPointCollider()
        {
            mesh.RecalculateNormals();

            var _ = CachedMeshCollider.sharedMesh;
            var __ = CachedMeshCollider.convex;
            CachedMeshCollider.sharedMesh = model;

            var verts = new List<Vector3>();
            for (var i = 0; i < mesh.vertices.Length; i++)
                verts.Add(ProjectVertex(i));

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;

            CachedMeshCollider.sharedMesh = _;
            CachedMeshCollider.convex = __;

            Vector3 ProjectVertex(int i)
            {
                CachedMeshCollider.convex = __;

                if (!Physics.Raycast(transform.TransformPoint(mesh.vertices[i]), mesh.vertices[i] - mesh.normals[i], out var hit))
                {
                    CachedMeshCollider.convex = true;

                    return transform.InverseTransformPoint(CachedMeshCollider.ClosestPoint(transform.TransformPoint(mesh.vertices[i])));
                }

                return transform.InverseTransformPoint(hit.point);
            }
        }

        void SetArraysClosestPointOtherCollider()
        {
            var verts = new List<Vector3>();
            foreach (var vert in mesh.vertices)
                verts.Add(transform.InverseTransformPoint(_otherMeshCollider.ClosestPoint(transform.TransformPoint(vert))));

            dummy.vertices = verts.ToArray();
            dummy.triangles = mesh.triangles;
            dummy.uv = mesh.uv;
        }

        void SetArraysClosestPointOptimized()
        {
            var verts = new Vector3[mesh.vertices.Length];
            var uvs = new Vector2[mesh.uv.Length];
            var i = 0;
            foreach (var meshVert in mesh.vertices)
            {
                var closestModelVertIndex = 0;
                var closestModelVert = Vector3.zero;
                var lastSqrMag = Mathf.Infinity;
                var j = 0;
                foreach (var modelVert in model.vertices)
                {
                    var sqrMag = (modelVert - meshVert).sqrMagnitude;
                    if (sqrMag < lastSqrMag)
                    {
                        lastSqrMag = sqrMag;
                        closestModelVertIndex = j;
                        closestModelVert = modelVert;
                    }
                    j++;
                }
                verts[i] = closestModelVert;
                uvs[i] = model.uv[closestModelVertIndex];
                i++;
            }

            dummy.vertices = verts;
            dummy.triangles = mesh.triangles;
            dummy.uv = uvs;
        }
    }

    private void AssignMesh(Mesh mesh)
    {
        UpdateMesh();
        Recalculate();

        void UpdateMesh() => Mesh = mesh;
    }

    private void Recalculate()
    {
        Mesh.RecalculateNormals();

        UpdateCollider();

        void UpdateCollider() => CachedMeshCollider.sharedMesh = Mesh;
    }

    private void HandleBlending()
    {
        if (_blend != _lastBlend && !_swap)
        {
            Blend();
            Recalculate();
        }

        if (_swap != _lastSwap)
        {
            Mesh = _swap ? _dst : _src;
            if (!_swap) ProgressiveBlend();
            Recalculate();
        }

        _lastBlend = _blend;
        _lastSwap = _swap;

        void Blend()
        {
            BlendJobs();

            for (var i = _blendValues.Length - 1; i >= 0; i--)
                _blendValues[i] = _blend;
        }

        void BlendJobs()
        {
            blendVertsJob.Blend = _blend;
            blendVertsJob.Schedule(_blendedVerts.Length, 1).Complete();

            Mesh.vertices = _blendedVerts.ToArray();

            blendUVsJob.Blend = _enableUVBlending ? _blend : 0;
            blendUVsJob.Schedule(_blendedUVs.Length, 1).Complete();

            Mesh.uv = _blendedUVs.ToArray();
        }

        void BlendCS()
        {
            _computeShader.SetFloat("Blend", _blend);

            _computeShader.Dispatch(0, _blendedVertsCB.count, 1, 1);

            _blendedVertsCB.GetData(_blendedVertsCSResult);
            Mesh.vertices = _blendedVertsCSResult;

            if (!_enableUVBlending) return;

            _blendedUVsCB.GetData(_blendedUVsCSResult);
            Mesh.uv = _blendedUVsCSResult;
        }
    }

    public void ReceiveInput(in Vector3 point, float radius, float force)
    {
        if (_swap) return;

        var i = 0;
        foreach (var vert in Mesh.vertices)
        {
            var sqrMag = (point - transform.TransformPoint(vert)).sqrMagnitude;

            var blend = true;
            var value = force / (sqrMag / (radius * radius));

            _toBlend[i] = blend;
            if (blend)
                _blendValues[i] += value;

            i++;
        }

        ProgressiveBlend();
        Recalculate();
    }

    private void ProgressiveBlend()
    {
        blendVertsIfJob.ToBlend = _toBlend;
        blendVertsIfJob.BlendValues = _blendValues;
        blendVertsIfJob.Schedule(_blendedVerts.Length, 1).Complete();

        Mesh.vertices = _blendedVerts.ToArray();

        if (!_enableUVBlending) return;

        blendUVsIfJob.ToBlend = _toBlend;
        blendUVsIfJob.BlendValues = _blendValues;
        blendUVsIfJob.Schedule(_blendedUVs.Length, 1).Complete();

        Mesh.uv = _blendedUVs.ToArray();
    }

    private void OnGUI()
    {
        GUILayout.BeginVertical("Box");
        _swap = GUILayout.Toggle(_swap, "Swap Mesh");
        EnableSmoothInput = GUILayout.Toggle(EnableSmoothInput, "Enable Smooth Input");
        _enableUVBlending = GUILayout.Toggle(_enableUVBlending, "Enable UV Blending\n(Experimental)");
        GUILayout.Space(10);
        GUILayout.Label("Manual Linear Blend");
        _blend = GUILayout.HorizontalSlider(_blend, 0, 1);
        GUILayout.Space(20);
        GUILayout.EndVertical();

        GUILayout.BeginVertical("Box");
        GUILayout.Label("Left Click: Forge");
        GUILayout.Label("Right Click: Mouse Look");
        GUILayout.Label("Middle Mouse: Reset View");
        GUILayout.EndVertical();

        GUILayout.BeginVertical("Box");
        GUILayout.Label("Audio Volume");
        AudioListener.volume = GUILayout.HorizontalSlider(AudioListener.volume, 0, 1);
        GUILayout.EndVertical();
    }
}

internal struct BlendVertsJob : IJobParallelFor
{
    public NativeArray<Vector3> Verts;

    [ReadOnly] public NativeArray<Vector3> SrcVerts;
    [ReadOnly] public NativeArray<Vector3> DstVerts;

    [ReadOnly] public float Blend;

    public void Execute(int i) => Verts[i] = Vector3.Lerp(SrcVerts[i], DstVerts[i], Blend);
}

internal struct BlendUVsJob : IJobParallelFor
{
    public NativeArray<Vector2> UVs;

    [ReadOnly] public NativeArray<Vector2> SrcUVs;
    [ReadOnly] public NativeArray<Vector2> DstUVs;

    [ReadOnly] public float Blend;

    public void Execute(int i) => UVs[i] = Vector2.Lerp(SrcUVs[i], DstUVs[i], Blend);
}

internal struct BlendVertsIfJob : IJobParallelFor
{
    public NativeArray<Vector3> Verts;

    [ReadOnly] public NativeArray<Vector3> SrcVerts;
    [ReadOnly] public NativeArray<Vector3> DstVerts;

    [ReadOnly] public NativeArray<bool> ToBlend;
    [ReadOnly] public NativeArray<float> BlendValues;

    public void Execute(int i)
    {
        if (ToBlend[i])
            Verts[i] = Vector3.Lerp(SrcVerts[i], DstVerts[i], BlendValues[i]);
    }
}

internal struct BlendUVsIfJob : IJobParallelFor
{
    public NativeArray<Vector2> UVs;

    [ReadOnly] public NativeArray<Vector2> SrcUVs;
    [ReadOnly] public NativeArray<Vector2> DstUVs;

    [ReadOnly] public NativeArray<bool> ToBlend;
    [ReadOnly] public NativeArray<float> BlendValues;

    public void Execute(int i)
    {
        if (ToBlend[i])
            UVs[i] = Vector3.Lerp(SrcUVs[i], DstUVs[i], BlendValues[i]);
    }
}