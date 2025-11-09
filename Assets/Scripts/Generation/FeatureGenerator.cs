using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// Feature placement system
    /// Layer 3: Places rivers, roads, and points of interest
    /// </summary>
    public class FeatureGenerator : MonoBehaviour
    {
        [Header("River Settings")]
        [SerializeField] private float riverThreshold = 0.7f;
        [SerializeField] private int riverSeed = 54321;

        [Header("Road Settings")]
        [SerializeField] private float roadProbability = 0.15f;
        [SerializeField] private int roadSeed = 98765;

        [Header("POI Settings")]
        [SerializeField] private int maxPOIsPerChunk = 8;
        [SerializeField] private int poiSeed = 11111;

        public void PlaceFeatures(ChunkData chunk)
        {
            // Schedule feature detection job
            var featureJob = new FeaturePlacementJob
            {
                heightMap = chunk.heightMap,
                moistureMap = chunk.moistureMap,
                biomeMap = chunk.biomeMap,
                resolution = chunk.resolution,
                chunkX = chunk.coord.x,
                chunkZ = chunk.coord.z,
                riverThreshold = riverThreshold,
                riverSeed = riverSeed,
                roadSeed = roadSeed,
                poiSeed = poiSeed,
                maxPOIs = maxPOIsPerChunk,
                hasRiver = new NativeArray<int>(1, Allocator.TempJob),
                hasRoad = new NativeArray<int>(1, Allocator.TempJob),
                pointsOfInterest = new NativeList<int2>(maxPOIsPerChunk, Allocator.TempJob)
            };

            JobHandle jobHandle = featureJob.Schedule();
            jobHandle.Complete();

            // Copy results to chunk
            chunk.hasRiver = featureJob.hasRiver[0] == 1;
            chunk.hasRoad = featureJob.hasRoad[0] == 1;

            for (int i = 0; i < featureJob.pointsOfInterest.Length && i < chunk.pointsOfInterest.Length; i++)
            {
                int2 poi = featureJob.pointsOfInterest[i];
                chunk.pointsOfInterest[i] = new Vector2Int(poi.x, poi.y);
            }

            // Cleanup
            featureJob.hasRiver.Dispose();
            featureJob.hasRoad.Dispose();
            featureJob.pointsOfInterest.Dispose();
        }

        [BurstCompile]
        private struct FeaturePlacementJob : IJob
        {
            [ReadOnly] public NativeArray<float> heightMap;
            [ReadOnly] public NativeArray<float> moistureMap;
            [ReadOnly] public NativeArray<BiomeType> biomeMap;

            public int resolution;
            public int chunkX;
            public int chunkZ;

            public float riverThreshold;
            public int riverSeed;
            public int roadSeed;
            public int poiSeed;
            public int maxPOIs;

            [WriteOnly] public NativeArray<int> hasRiver;
            [WriteOnly] public NativeArray<int> hasRoad;
            public NativeList<int2> pointsOfInterest;

            public void Execute()
            {
                // Initialize random
                Unity.Mathematics.Random riverRng = new Unity.Mathematics.Random((uint)(riverSeed + chunkX * 1000 + chunkZ));
                Unity.Mathematics.Random roadRng = new Unity.Mathematics.Random((uint)(roadSeed + chunkX * 1000 + chunkZ));
                Unity.Mathematics.Random poiRng = new Unity.Mathematics.Random((uint)(poiSeed + chunkX * 1000 + chunkZ));

                // River detection
                hasRiver[0] = DetectRiver(riverRng) ? 1 : 0;

                // Road placement
                hasRoad[0] = PlaceRoad(roadRng) ? 1 : 0;

                // POI placement
                PlacePOIs(poiRng);
            }

            private bool DetectRiver(Unity.Mathematics.Random rng)
            {
                // Rivers flow through low areas with high moisture
                int riverCandidates = 0;
                float avgMoisture = 0f;

                for (int i = 0; i < heightMap.Length; i++)
                {
                    if (moistureMap[i] > riverThreshold)
                    {
                        riverCandidates++;
                        avgMoisture += moistureMap[i];
                    }
                }

                if (riverCandidates > resolution * 2) // Enough moisture
                {
                    avgMoisture /= riverCandidates;

                    // River probability based on moisture and random
                    float riverChance = avgMoisture * rng.NextFloat();
                    return riverChance > 0.6f;
                }

                return false;
            }

            private bool PlaceRoad(Unity.Mathematics.Random rng)
            {
                // Roads prefer flat terrain and certain biomes
                int roadableTiles = 0;

                for (int i = 0; i < biomeMap.Length; i++)
                {
                    BiomeType biome = biomeMap[i];

                    // Roads avoid water and mountains
                    if (biome != BiomeType.Ocean &&
                        biome != BiomeType.Mountain &&
                        biome != BiomeType.Snow)
                    {
                        roadableTiles++;
                    }
                }

                float roadableRatio = roadableTiles / (float)biomeMap.Length;

                if (roadableRatio > 0.5f)
                {
                    return rng.NextFloat() < roadableRatio * 0.3f;
                }

                return false;
            }

            private void PlacePOIs(Unity.Mathematics.Random rng)
            {
                // Place random points of interest
                // These could be: ruins, caves, resource nodes, landmarks, etc.

                int poiCount = rng.NextInt(0, maxPOIs);

                for (int i = 0; i < poiCount; i++)
                {
                    int x = rng.NextInt(0, resolution);
                    int y = rng.NextInt(0, resolution);
                    int index = x + y * resolution;

                    // Only place POI on valid terrain
                    BiomeType biome = biomeMap[index];
                    if (biome != BiomeType.Ocean)
                    {
                        pointsOfInterest.Add(new int2(x, y));
                    }
                }
            }
        }

        // Utility: Get POI type based on biome
        public static string GetPOIType(BiomeType biome, int seed)
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)seed);
            float roll = rng.NextFloat();

            switch (biome)
            {
                case BiomeType.Desert:
                    return roll > 0.5f ? "Oasis" : "Ancient Ruins";

                case BiomeType.Forest:
                case BiomeType.Rainforest:
                    return roll > 0.6f ? "Druid Circle" : roll > 0.3f ? "Hidden Grove" : "Old Shrine";

                case BiomeType.Mountain:
                    return roll > 0.5f ? "Cave Entrance" : "Mountain Peak";

                case BiomeType.Grassland:
                case BiomeType.Savanna:
                    return roll > 0.7f ? "Standing Stones" : roll > 0.4f ? "Abandoned Camp" : "Wildflower Field";

                case BiomeType.Taiga:
                    return roll > 0.5f ? "Hunter's Lodge" : "Frozen Lake";

                case BiomeType.Beach:
                    return roll > 0.5f ? "Shipwreck" : "Tide Pools";

                default:
                    return "Mystery Location";
            }
        }
    }
}
