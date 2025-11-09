using UnityEngine;
using Unity.Collections;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// GPU-accelerated terrain generation
    /// Layer 1: Generates height, moisture, and temperature maps
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader terrainComputeShader;

        [Header("Generation Parameters")]
        [SerializeField] private int seed = 12345;
        [SerializeField] private float scale = 0.01f;
        [SerializeField] private int octaves = 6;
        [SerializeField] private float persistence = 0.5f;
        [SerializeField] private float lacunarity = 2.0f;
        [SerializeField] private float heightMultiplier = 100f;

        [Header("Dual GPU Support")]
        [SerializeField] private bool useDualGPU = true;

        private int kernelGenerate;
        private int kernelApplyEvents;

        private ComputeBuffer heightBuffer;
        private ComputeBuffer moistureBuffer;
        private ComputeBuffer temperatureBuffer;
        private ComputeBuffer eventsBuffer;

        private int chunkSize;
        private int chunkResolution;

        public void Initialize(int chunkSize, int chunkResolution)
        {
            this.chunkSize = chunkSize;
            this.chunkResolution = chunkResolution;

            // Load compute shader if not assigned
            if (terrainComputeShader == null)
            {
                terrainComputeShader = Resources.Load<ComputeShader>("Shaders/Compute/TerrainGeneration");

                if (terrainComputeShader == null)
                {
                    Debug.LogError("TerrainGeneration compute shader not found! Make sure it's in Resources/Shaders/Compute/");
                    return;
                }
            }

            kernelGenerate = terrainComputeShader.FindKernel("GenerateTerrain");
            kernelApplyEvents = terrainComputeShader.FindKernel("ApplyWorldEvents");
        }

        public void GenerateBaseTerrain(ChunkData chunk, int gpuIndex = 0)
        {
            if (terrainComputeShader == null)
            {
                Debug.LogError("TerrainGeneration compute shader not initialized!");
                return;
            }

            int resolution = chunk.resolution;
            int bufferSize = resolution * resolution;

            // Create compute buffers
            heightBuffer = new ComputeBuffer(bufferSize, sizeof(float));
            moistureBuffer = new ComputeBuffer(bufferSize, sizeof(float));
            temperatureBuffer = new ComputeBuffer(bufferSize, sizeof(float));

            // Set shader parameters
            terrainComputeShader.SetInt("Resolution", resolution);
            terrainComputeShader.SetInt("ChunkX", chunk.coord.x);
            terrainComputeShader.SetInt("ChunkZ", chunk.coord.z);
            terrainComputeShader.SetInt("ChunkSize", chunkSize);
            terrainComputeShader.SetInt("Seed", seed);
            terrainComputeShader.SetFloat("Scale", scale);
            terrainComputeShader.SetInt("Octaves", octaves);
            terrainComputeShader.SetFloat("Persistence", persistence);
            terrainComputeShader.SetFloat("Lacunarity", lacunarity);
            terrainComputeShader.SetFloat("HeightMultiplier", heightMultiplier);

            // Bind buffers
            terrainComputeShader.SetBuffer(kernelGenerate, "HeightMap", heightBuffer);
            terrainComputeShader.SetBuffer(kernelGenerate, "MoistureMap", moistureBuffer);
            terrainComputeShader.SetBuffer(kernelGenerate, "TemperatureMap", temperatureBuffer);

            // Calculate thread groups
            int threadGroupsX = Mathf.CeilToInt(resolution / 8f);
            int threadGroupsY = Mathf.CeilToInt(resolution / 8f);

            // Dispatch compute shader
            // Note: Unity doesn't directly support multi-GPU, but this sets up the structure
            // For true dual-GPU, you'd need a custom rendering plugin or DirectX/Vulkan integration
            if (useDualGPU && gpuIndex == 1)
            {
                // In a real implementation, this would dispatch to GPU 1
                // For now, we just dispatch normally
                // TODO: Implement custom multi-GPU support via native plugin
            }

            terrainComputeShader.Dispatch(kernelGenerate, threadGroupsX, threadGroupsY, 1);

            // Read back results
            float[] heightData = new float[bufferSize];
            float[] moistureData = new float[bufferSize];
            float[] temperatureData = new float[bufferSize];

            heightBuffer.GetData(heightData);
            moistureBuffer.GetData(moistureData);
            temperatureBuffer.GetData(temperatureData);

            // Copy to chunk data
            NativeArray<float>.Copy(heightData, chunk.heightMap);
            NativeArray<float>.Copy(moistureData, chunk.moistureMap);
            NativeArray<float>.Copy(temperatureData, chunk.temperatureMap);

            // Apply world events if any
            if (chunk.activeEvents.Length > 0)
            {
                ApplyWorldEvents(chunk);
            }

            // Cleanup buffers
            heightBuffer.Release();
            moistureBuffer.Release();
            temperatureBuffer.Release();
        }

        private void ApplyWorldEvents(ChunkData chunk)
        {
            int resolution = chunk.resolution;
            int bufferSize = resolution * resolution;

            // Recreate buffers with current data
            heightBuffer = new ComputeBuffer(bufferSize, sizeof(float));
            moistureBuffer = new ComputeBuffer(bufferSize, sizeof(float));
            temperatureBuffer = new ComputeBuffer(bufferSize, sizeof(float));

            heightBuffer.SetData(chunk.heightMap);
            moistureBuffer.SetData(chunk.moistureMap);
            temperatureBuffer.SetData(chunk.temperatureMap);

            // Create events buffer
            int eventStructSize = sizeof(int) + sizeof(float) * 6; // type + float3 + 3 floats
            eventsBuffer = new ComputeBuffer(chunk.activeEvents.Length, eventStructSize);
            eventsBuffer.SetData(chunk.activeEvents);

            // Set shader parameters
            terrainComputeShader.SetInt("Resolution", resolution);
            terrainComputeShader.SetInt("ChunkX", chunk.coord.x);
            terrainComputeShader.SetInt("ChunkZ", chunk.coord.z);
            terrainComputeShader.SetInt("ChunkSize", chunkSize);

            // Bind buffers
            terrainComputeShader.SetBuffer(kernelApplyEvents, "HeightMap", heightBuffer);
            terrainComputeShader.SetBuffer(kernelApplyEvents, "MoistureMap", moistureBuffer);
            terrainComputeShader.SetBuffer(kernelApplyEvents, "TemperatureMap", temperatureBuffer);
            terrainComputeShader.SetBuffer(kernelApplyEvents, "ActiveEvents", eventsBuffer);

            // Calculate thread groups
            int threadGroupsX = Mathf.CeilToInt(resolution / 8f);
            int threadGroupsY = Mathf.CeilToInt(resolution / 8f);

            // Dispatch
            terrainComputeShader.Dispatch(kernelApplyEvents, threadGroupsX, threadGroupsY, 1);

            // Read back modified data
            float[] heightData = new float[bufferSize];
            float[] moistureData = new float[bufferSize];

            heightBuffer.GetData(heightData);
            moistureBuffer.GetData(moistureData);

            NativeArray<float>.Copy(heightData, chunk.heightMap);
            NativeArray<float>.Copy(moistureData, chunk.moistureMap);

            // Cleanup
            heightBuffer.Release();
            moistureBuffer.Release();
            temperatureBuffer.Release();
            eventsBuffer.Release();
        }

        private void OnDestroy()
        {
            // Cleanup any remaining buffers
            if (heightBuffer != null) heightBuffer.Release();
            if (moistureBuffer != null) moistureBuffer.Release();
            if (temperatureBuffer != null) temperatureBuffer.Release();
            if (eventsBuffer != null) eventsBuffer.Release();
        }

        // Public API for customization
        public void SetSeed(int newSeed)
        {
            seed = newSeed;
        }

        public void SetScale(float newScale)
        {
            scale = newScale;
        }

        public void SetHeightMultiplier(float multiplier)
        {
            heightMultiplier = multiplier;
        }
    }
}
