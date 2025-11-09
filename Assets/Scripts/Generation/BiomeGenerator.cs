using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// Biome assignment system using Unity Job System + Burst Compiler
    /// Layer 2: Assigns biomes based on height, moisture, and temperature
    /// </summary>
    public class BiomeGenerator : MonoBehaviour
    {
        [Header("Biome Thresholds")]
        [SerializeField] private float oceanLevel = 0.3f;
        [SerializeField] private float beachLevel = 0.35f;
        [SerializeField] private float mountainLevel = 0.7f;
        [SerializeField] private float snowLevel = 0.85f;

        public void AssignBiomes(ChunkData chunk)
        {
            // Create and schedule biome assignment job
            var biomeJob = new BiomeAssignmentJob
            {
                heightMap = chunk.heightMap,
                moistureMap = chunk.moistureMap,
                temperatureMap = chunk.temperatureMap,
                biomeMap = chunk.biomeMap,
                resolution = chunk.resolution,
                oceanLevel = oceanLevel,
                beachLevel = beachLevel,
                mountainLevel = mountainLevel,
                snowLevel = snowLevel,
                heightMultiplier = 100f // Should match terrain generator
            };

            JobHandle jobHandle = biomeJob.Schedule(chunk.biomeMap.Length, 64);
            jobHandle.Complete(); // Wait for job to finish
        }

        /// <summary>
        /// Burst-compiled job for fast biome assignment
        /// </summary>
        [BurstCompile]
        private struct BiomeAssignmentJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heightMap;
            [ReadOnly] public NativeArray<float> moistureMap;
            [ReadOnly] public NativeArray<float> temperatureMap;

            [WriteOnly] public NativeArray<BiomeType> biomeMap;

            public int resolution;
            public float oceanLevel;
            public float beachLevel;
            public float mountainLevel;
            public float snowLevel;
            public float heightMultiplier;

            public void Execute(int index)
            {
                float height = heightMap[index];
                float moisture = moistureMap[index];
                float temperature = temperatureMap[index];

                // Normalize height for comparisons
                float normalizedHeight = height / heightMultiplier;

                BiomeType biome = BiomeType.Grassland; // Default

                // Height-based biomes (highest priority)
                if (normalizedHeight < oceanLevel)
                {
                    biome = BiomeType.Ocean;
                }
                else if (normalizedHeight < beachLevel)
                {
                    biome = BiomeType.Beach;
                }
                else if (normalizedHeight > mountainLevel)
                {
                    // High altitude biomes
                    if (normalizedHeight > snowLevel || temperature < 0.3f)
                    {
                        biome = BiomeType.Snow;
                    }
                    else
                    {
                        biome = BiomeType.Mountain;
                    }
                }
                else
                {
                    // Temperature and moisture-based biomes
                    biome = GetBiomeFromClimate(temperature, moisture);
                }

                biomeMap[index] = biome;
            }

            private BiomeType GetBiomeFromClimate(float temperature, float moisture)
            {
                // Whittaker biome diagram approximation
                // Temperature: 0 = cold, 1 = hot
                // Moisture: 0 = dry, 1 = wet

                if (temperature < 0.2f)
                {
                    // Cold biomes
                    if (moisture < 0.3f)
                        return BiomeType.Tundra;
                    else
                        return BiomeType.Taiga;
                }
                else if (temperature < 0.5f)
                {
                    // Temperate biomes
                    if (moisture < 0.3f)
                        return BiomeType.Grassland;
                    else if (moisture < 0.7f)
                        return BiomeType.Forest;
                    else
                        return BiomeType.Forest; // Temperate rainforest
                }
                else if (temperature < 0.8f)
                {
                    // Warm biomes
                    if (moisture < 0.3f)
                        return BiomeType.Savanna;
                    else if (moisture < 0.6f)
                        return BiomeType.Forest;
                    else
                        return BiomeType.Rainforest;
                }
                else
                {
                    // Hot biomes
                    if (moisture < 0.4f)
                        return BiomeType.Desert;
                    else if (moisture < 0.7f)
                        return BiomeType.Savanna;
                    else
                        return BiomeType.Rainforest;
                }
            }
        }

        // Utility: Get biome color for visualization
        public static Color GetBiomeColor(BiomeType biome)
        {
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
}
