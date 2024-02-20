using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
internal class MeshDeformer : MonoBehaviour
{
    [SerializeField] private float _spring = 10;
    [SerializeField] private float _damping = 5;

    private float UniformScale => transform.localScale.x;

    private MeshFilter cachedMeshFilter;
    private MeshFilter CachedMeshFilter => cachedMeshFilter = cachedMeshFilter != null ? cachedMeshFilter : GetComponent<MeshFilter>();

    private Mesh Mesh => CachedMeshFilter.mesh;

    private List<Vector3> _originalVertices, _displacedVertices, _vertexVelocities;

    private void Awake()
    {
        _originalVertices = Mesh.vertices.ToList();
        _displacedVertices = Mesh.vertices.ToList();
        _vertexVelocities = Mesh.vertices.ToList();
    }

    private void Update() => UpdateMesh();

    private void UpdateMesh()
    {
        for (var i = 0; i < _displacedVertices.Count; i++)
            UpdateVertex(i);

        Mesh.vertices = _displacedVertices.ToArray();

        Mesh.RecalculateBounds();
        Mesh.RecalculateNormals();
        Mesh.RecalculateTangents();

        void UpdateVertex(int i)
        {
            var velocity = _vertexVelocities[i];
            var displacement = _displacedVertices[i] - _originalVertices[i];

            //Apply Uniform Scaling
            displacement *= UniformScale;
            //Apply Spring
            velocity -= _spring * Time.deltaTime * displacement;
            //Apply Damping
            velocity *= 1 - _damping * Time.deltaTime;

            _vertexVelocities[i] = velocity;
            _displacedVertices[i] += velocity * (Time.deltaTime / UniformScale);
        }
    }

    public void AddForceToMesh(in Vector3 point, float force)
    {
        for (var i = 0; i < _displacedVertices.Count; i++)
            AddForceToVertex(i, transform.InverseTransformPoint(point), force);

        void AddForceToVertex(int i, in Vector3 point, float force)
        {
            //TODEF:
            var vertexMass = 1;

            var pointToVertex = _displacedVertices[i] - point;

            //Apply Uniform Scaling
            pointToVertex *= UniformScale;

            var attenuatedForce = force / (1 + pointToVertex.sqrMagnitude);
            var acceleration = attenuatedForce / vertexMass;
            var velocity = acceleration * Time.deltaTime;

            _vertexVelocities[i] += pointToVertex.normalized * velocity;
        }
    }
}