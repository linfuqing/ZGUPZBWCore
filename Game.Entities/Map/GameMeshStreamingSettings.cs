using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using ZG;
using static ZG.MeshStreamingUtility;

[CreateAssetMenu(fileName = "Game Mesh Streaming Settings", menuName = "Game/Mesh Streaming Settings")]
public class GameMeshStreamingSettings : MeshInstanceStreamingSettings<GameMeshStreamingSettings.Vertex, GameMeshStreamingSettings.MeshWrapper>
{
    public struct Vertex : IVertex
    {
        public float4 position;

        public float4 normal { get; set; }

        //public float4 tangent { get; set; }

        public Vertex(
            float3 position, 
            float3 normal, 
            //float4 tangent, 
            in float4x4 matrix)
        {
            this.position = math.float4(math.transform(matrix, position), 1.0f);

            this.normal = math.float4(math.rotate(matrix, normal), 0.0f);

            //this.tangent = 0.0f;// math.mul(matrix, tangent);
        }

        public void Clear()
        {
            position.w = 0.0f;
        }

        float3 IVertex.position => position.xyz;
    }

    public struct MeshWrapper : IMeshWrapper<Triangle<Vertex>>
    {
        public void GetPolygons(int subMesh, in float4x4 matrix, in Mesh.MeshData mesh, ref NativeList<Triangle<Vertex>> values)
        {
            int vertexCount = mesh.vertexCount;
            using (var positions = new NativeArray<Vector3>(vertexCount, Allocator.Temp))
            using (var normals = new NativeArray<Vector3>(vertexCount, Allocator.Temp))
            //using (var tangents = new NativeArray<Vector4>(vertexCount, Allocator.Temp))
            {
                mesh.GetVertices(positions);
                mesh.GetNormals(normals);
                //mesh.GetTangents(tangents);

                int3 index;
                var subMeshDesc = mesh.GetSubMesh(subMesh);
                UnityEngine.Assertions.Assert.AreEqual(MeshTopology.Triangles, subMeshDesc.topology);
                Triangle<Vertex> triangle;
                switch (mesh.indexFormat)
                {
                    case UnityEngine.Rendering.IndexFormat.UInt16:
                        var indices16 = mesh.GetIndexData<ushort>();
                        for (int i = 0; i < subMeshDesc.indexCount; i += 3)
                        {
                            index.x = indices16[subMeshDesc.indexStart + i + 0] + subMeshDesc.baseVertex;
                            index.y = indices16[subMeshDesc.indexStart + i + 1] + subMeshDesc.baseVertex;
                            index.z = indices16[subMeshDesc.indexStart + i + 2] + subMeshDesc.baseVertex;

                            triangle.x = new Vertex(positions[index.x], normals[index.x], /*tangents[index.x], */matrix);
                            triangle.y = new Vertex(positions[index.y], normals[index.y], /*tangents[index.y], */matrix);
                            triangle.z = new Vertex(positions[index.z], normals[index.z], /*tangents[index.z], */matrix);

                            values.Add(triangle);
                        }

                        break;
                    case UnityEngine.Rendering.IndexFormat.UInt32:
                        var indices32 = mesh.GetIndexData<int>();
                        for (int i = 0; i < subMeshDesc.indexCount; i += 3)
                        {
                            index.x = indices32[subMeshDesc.indexStart + i + 0] + subMeshDesc.baseVertex;
                            index.y = indices32[subMeshDesc.indexStart + i + 1] + subMeshDesc.baseVertex;
                            index.z = indices32[subMeshDesc.indexStart + i + 2] + subMeshDesc.baseVertex;

                            triangle.x = new Vertex(positions[index.x], normals[index.x], /*tangents[index.x], */matrix);
                            triangle.y = new Vertex(positions[index.y], normals[index.y], /*tangents[index.y], */matrix);
                            triangle.z = new Vertex(positions[index.z], normals[index.z], /*tangents[index.z], */matrix);

                            values.Add(triangle);
                        }
                        break; 
                }
            }
        }
    }

    private MeshWrapper __meshWrapper;

    public override Mesh CreateMesh(MeshInstanceStreamingDatabase database)
    {
        int vertexCount = database.vertexCount;
        var vertices = MeshStreamingSharedData<Vertex>.GetData((int)database.vertexOffset, database.vertexCount);

        var positions = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; ++i)
            positions[i] = vertices[i].position.xyz;

        var normals = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; ++i)
            normals[i] = vertices[i].normal.xyz;

        var indices = new int[vertexCount];
        for (int i = 0; i < vertexCount; ++i)
            indices[i] = i;

        var mesh = new Mesh();
        mesh.vertices = positions;
        mesh.normals = normals;

        mesh.triangles = indices;

        mesh.RecalculateBounds();

        return mesh;
    }

#if UNITY_EDITOR
    public override ref MeshWrapper meshWrapper => ref __meshWrapper;
#endif
}
