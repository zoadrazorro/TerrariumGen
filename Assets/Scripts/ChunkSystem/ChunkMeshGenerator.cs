using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using TerrariumGen.Data;
using TerrariumGen.Generation;

namespace TerrariumGen.ChunkSystem
{
    /// <summary>
    /// Generates mesh visualization for chunks
    /// Uses Job System for parallel mesh generation
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ChunkMeshGenerator : MonoBehaviour
    {
        [Header("Mesh Settings")]
        [SerializeField] private bool generateCollider = true;
        [SerializeField] private bool smoothNormals = true;
        [SerializeField] private bool colorByBiome = true;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        private ChunkData chunkData;
        private int chunkSize;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            if (generateCollider)
            {
                meshCollider = gameObject.AddComponent<MeshCollider>();
            }

            // Create default material if none exists
            if (meshRenderer.sharedMaterial == null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                meshRenderer.sharedMaterial = mat;
            }
        }

        public void GenerateMesh(ChunkData chunk, int chunkSize)
        {
            this.chunkData = chunk;
            this.chunkSize = chunkSize;

            if (chunk.state != GenerationState.Complete)
            {
                Debug.LogWarning($"Chunk {chunk.coord.x},{chunk.coord.z} not fully generated yet!");
                return;
            }

            // Create mesh generation job
            int resolution = chunk.resolution;
            int vertexCount = resolution * resolution;
            int triangleCount = (resolution - 1) * (resolution - 1) * 6;

            NativeArray<Vector3> vertices = new NativeArray<Vector3>(vertexCount, Allocator.TempJob);
            NativeArray<Vector3> normals = new NativeArray<Vector3>(vertexCount, Allocator.TempJob);
            NativeArray<Vector2> uvs = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);
            NativeArray<Color> colors = new NativeArray<Color>(vertexCount, Allocator.TempJob);
            NativeArray<int> triangles = new NativeArray<int>(triangleCount, Allocator.TempJob);

            // Generate vertices job
            var vertexJob = new GenerateVerticesJob
            {
                heightMap = chunk.heightMap,
                biomeMap = chunk.biomeMap,
                resolution = resolution,
                chunkSize = chunkSize,
                colorByBiome = colorByBiome,
                vertices = vertices,
                uvs = uvs,
                colors = colors
            };

            JobHandle vertexHandle = vertexJob.Schedule(vertexCount, 64);

            // Generate triangles job
            var triangleJob = new GenerateTrianglesJob
            {
                resolution = resolution,
                triangles = triangles
            };

            JobHandle triangleHandle = triangleJob.Schedule();

            // Wait for both jobs
            JobHandle.CompleteAll(ref vertexHandle, ref triangleHandle);

            // Calculate normals job (after vertices are ready)
            var normalJob = new CalculateNormalsJob
            {
                vertices = vertices,
                triangles = triangles,
                normals = normals,
                smoothNormals = smoothNormals
            };

            JobHandle normalHandle = normalJob.Schedule(vertexCount, 64);
            normalHandle.Complete();

            // Create mesh
            Mesh mesh = new Mesh
            {
                name = $"Chunk_{chunk.coord.x}_{chunk.coord.z}"
            };

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles.ToArray(), 0);

            meshFilter.mesh = mesh;

            if (generateCollider && meshCollider != null)
            {
                meshCollider.sharedMesh = mesh;
            }

            // Cleanup
            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();
            colors.Dispose();
            triangles.Dispose();
        }

        [BurstCompile]
        private struct GenerateVerticesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heightMap;
            [ReadOnly] public NativeArray<BiomeType> biomeMap;

            public int resolution;
            public int chunkSize;
            public bool colorByBiome;

            [WriteOnly] public NativeArray<Vector3> vertices;
            [WriteOnly] public NativeArray<Vector2> uvs;
            [WriteOnly] public NativeArray<Color> colors;

            public void Execute(int index)
            {
                int x = index % resolution;
                int z = index / resolution;

                // Calculate vertex position
                float xPos = (x / (float)(resolution - 1)) * chunkSize;
                float zPos = (z / (float)(resolution - 1)) * chunkSize;
                float height = heightMap[index];

                vertices[index] = new Vector3(xPos, height, zPos);

                // UV coordinates
                uvs[index] = new Vector2(x / (float)(resolution - 1), z / (float)(resolution - 1));

                // Color based on biome or height
                if (colorByBiome)
                {
                    colors[index] = GetBiomeColor(biomeMap[index]);
                }
                else
                {
                    // Gradient based on height
                    float t = height / 100f; // Assuming max height of 100
                    colors[index] = Color.Lerp(Color.green, Color.white, t);
                }
            }

            private Color GetBiomeColor(BiomeType biome)
            {
                // Use the BiomeGenerator's color mapping
                switch (biome)
                {
                    case BiomeType.Ocean: return new Color(0.1f, 0.3f, 0.8f);
                    case BiomeType.Beach: return new Color(0.9f, 0.9f, 0.7f);
                    case BiomeType.Desert: return new Color(0.9f, 0.8f, 0.5f);
                    case BiomeType.Savanna: return new Color(0.7f, 0.7f, 0.4f);
                    case BiomeType.Grassland: return new Color(0.5f, 0.8f, 0.3f);
                    case BiomeType.Forest: return new Color(0.2f, 0.6f, 0.2f);
                    case BiomeType.Rainforest: return new Color(0.1f, 0.5f, 0.2f);
                    case BiomeType.Taiga: return new Color(0.3f, 0.5f, 0.4f);
                    case BiomeType.Tundra: return new Color(0.7f, 0.8f, 0.7f);
                    case BiomeType.Snow: return new Color(0.95f, 0.95f, 1.0f);
                    case BiomeType.Mountain: return new Color(0.5f, 0.5f, 0.5f);
                    default: return Color.magenta;
                }
            }
        }

        [BurstCompile]
        private struct GenerateTrianglesJob : IJob
        {
            public int resolution;

            [WriteOnly] public NativeArray<int> triangles;

            public void Execute()
            {
                int triangleIndex = 0;

                for (int z = 0; z < resolution - 1; z++)
                {
                    for (int x = 0; x < resolution - 1; x++)
                    {
                        int vertexIndex = x + z * resolution;

                        // First triangle
                        triangles[triangleIndex + 0] = vertexIndex;
                        triangles[triangleIndex + 1] = vertexIndex + resolution;
                        triangles[triangleIndex + 2] = vertexIndex + 1;

                        // Second triangle
                        triangles[triangleIndex + 3] = vertexIndex + 1;
                        triangles[triangleIndex + 4] = vertexIndex + resolution;
                        triangles[triangleIndex + 5] = vertexIndex + resolution + 1;

                        triangleIndex += 6;
                    }
                }
            }
        }

        [BurstCompile]
        private struct CalculateNormalsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector3> vertices;
            [ReadOnly] public NativeArray<int> triangles;

            public bool smoothNormals;

            [WriteOnly] public NativeArray<Vector3> normals;

            public void Execute(int index)
            {
                if (smoothNormals)
                {
                    // Calculate smooth normals by averaging adjacent triangle normals
                    // This is a simplified version - full implementation would need triangle adjacency data
                    normals[index] = Vector3.up; // Placeholder
                }
                else
                {
                    // Flat normals (just point up for terrain)
                    normals[index] = Vector3.up;
                }
            }
        }

        private void OnDestroy()
        {
            // Cleanup mesh
            if (meshFilter != null && meshFilter.mesh != null)
            {
                Destroy(meshFilter.mesh);
            }
        }
    }
}
