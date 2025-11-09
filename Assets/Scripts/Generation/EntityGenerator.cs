using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// Entity population system
    /// Layer 6: Populates NPCs, monsters, and loot
    /// </summary>
    public class EntityGenerator : MonoBehaviour
    {
        [Header("Entity Settings")]
        [SerializeField] private int entitySeed = 99999;
        [SerializeField] private int maxEntitiesPerChunk = 64;

        [Header("Spawn Density")]
        [SerializeField] private float npcDensity = 0.3f;
        [SerializeField] private float monsterDensity = 0.5f;
        [SerializeField] private float lootDensity = 0.2f;

        [Header("Threat Scaling")]
        [SerializeField] private float baseThreatLevel = 1.0f;
        [SerializeField] private float distanceThreatMultiplier = 0.1f; // Threat increases with distance from origin

        public void PopulateEntities(ChunkData chunk)
        {
            // Create job for entity spawning
            var entityJob = new EntitySpawnJob
            {
                biomeMap = chunk.biomeMap,
                heightMap = chunk.heightMap,
                resolution = chunk.resolution,
                chunkX = chunk.coord.x,
                chunkZ = chunk.coord.z,
                entitySeed = entitySeed,
                maxEntities = maxEntitiesPerChunk,
                npcDensity = npcDensity,
                monsterDensity = monsterDensity,
                lootDensity = lootDensity,
                baseThreatLevel = baseThreatLevel,
                distanceThreatMultiplier = distanceThreatMultiplier,
                hasSettlement = chunk.hasSettlement,
                hasDungeon = chunk.hasDungeon,
                dungeonDepth = chunk.dungeonDepth,
                entitySpawns = chunk.entitySpawns
            };

            JobHandle jobHandle = entityJob.Schedule();
            jobHandle.Complete();
        }

        [BurstCompile]
        private struct EntitySpawnJob : IJob
        {
            [ReadOnly] public NativeArray<BiomeType> biomeMap;
            [ReadOnly] public NativeArray<float> heightMap;

            public int resolution;
            public int chunkX;
            public int chunkZ;
            public int entitySeed;
            public int maxEntities;

            public float npcDensity;
            public float monsterDensity;
            public float lootDensity;

            public float baseThreatLevel;
            public float distanceThreatMultiplier;

            public bool hasSettlement;
            public bool hasDungeon;
            public int dungeonDepth;

            [WriteOnly] public NativeArray<EntitySpawnData> entitySpawns;

            public void Execute()
            {
                Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                    (uint)(entitySeed + chunkX * 2654435761 + chunkZ)
                );

                int entityCount = 0;

                // Calculate threat level based on distance from origin
                float distanceFromOrigin = math.sqrt(chunkX * chunkX + chunkZ * chunkZ);
                float chunkThreatLevel = baseThreatLevel + distanceFromOrigin * distanceThreatMultiplier;

                // Settlement entities (NPCs)
                if (hasSettlement)
                {
                    entityCount = SpawnSettlementEntities(ref rng, entityCount, chunkThreatLevel);
                }

                // Dungeon entities (monsters and loot)
                if (hasDungeon)
                {
                    entityCount = SpawnDungeonEntities(ref rng, entityCount, chunkThreatLevel);
                }

                // Ambient entities (wildlife, random encounters)
                entityCount = SpawnAmbientEntities(ref rng, entityCount, chunkThreatLevel);

                // Loot spawns
                entityCount = SpawnLoot(ref rng, entityCount, chunkThreatLevel);
            }

            private int SpawnSettlementEntities(ref Unity.Mathematics.Random rng, int startIndex, float threatLevel)
            {
                // NPCs in settlements - friendly traders, guards, civilians
                int npcCount = rng.NextInt(5, 20); // More NPCs in settlements
                int index = startIndex;

                for (int i = 0; i < npcCount && index < maxEntities; i++)
                {
                    // Find valid spawn position
                    int2 pos = GetRandomValidPosition(ref rng, BiomeType.Grassland, true);
                    if (pos.x < 0) continue; // Invalid position

                    EntitySpawnData entity = new EntitySpawnData
                    {
                        position = new float3(pos.x, heightMap[pos.x + pos.y * resolution], pos.y),
                        entityTypeID = rng.NextInt(1000, 1100), // NPC ID range
                        threatLevel = 0f, // Friendly NPCs
                        isLoot = false
                    };

                    entitySpawns[index] = entity;
                    index++;
                }

                return index;
            }

            private int SpawnDungeonEntities(ref Unity.Mathematics.Random rng, int startIndex, float threatLevel)
            {
                // Monsters near dungeon entrance, scaled by depth
                float dungeonThreat = threatLevel + dungeonDepth * 0.5f;
                int monsterCount = rng.NextInt(dungeonDepth * 2, dungeonDepth * 5);
                int index = startIndex;

                for (int i = 0; i < monsterCount && index < maxEntities; i++)
                {
                    int2 pos = GetRandomValidPosition(ref rng, BiomeType.Mountain, false);
                    if (pos.x < 0) continue;

                    EntitySpawnData entity = new EntitySpawnData
                    {
                        position = new float3(pos.x, heightMap[pos.x + pos.y * resolution], pos.y),
                        entityTypeID = rng.NextInt(2000, 2100), // Monster ID range
                        threatLevel = dungeonThreat * rng.NextFloat(0.8f, 1.2f),
                        isLoot = false
                    };

                    entitySpawns[index] = entity;
                    index++;
                }

                return index;
            }

            private int SpawnAmbientEntities(ref Unity.Mathematics.Random rng, int startIndex, float threatLevel)
            {
                // Ambient creatures based on biome
                int ambientCount = (int)(resolution * resolution * monsterDensity * 0.01f);
                int index = startIndex;

                for (int i = 0; i < ambientCount && index < maxEntities; i++)
                {
                    int2 pos = GetRandomValidPosition(ref rng, BiomeType.Grassland, false);
                    if (pos.x < 0) continue;

                    BiomeType biome = biomeMap[pos.x + pos.y * resolution];
                    int entityType = GetBiomeEntityType(biome, ref rng);

                    // Determine if hostile or peaceful
                    bool isHostile = rng.NextFloat() > 0.6f;

                    EntitySpawnData entity = new EntitySpawnData
                    {
                        position = new float3(pos.x, heightMap[pos.x + pos.y * resolution], pos.y),
                        entityTypeID = entityType,
                        threatLevel = isHostile ? threatLevel * rng.NextFloat(0.5f, 1.5f) : 0f,
                        isLoot = false
                    };

                    entitySpawns[index] = entity;
                    index++;
                }

                return index;
            }

            private int SpawnLoot(ref Unity.Mathematics.Random rng, int startIndex, float threatLevel)
            {
                // Random loot spawns
                int lootCount = (int)(resolution * resolution * lootDensity * 0.01f);
                int index = startIndex;

                for (int i = 0; i < lootCount && index < maxEntities; i++)
                {
                    int2 pos = GetRandomValidPosition(ref rng, BiomeType.Grassland, false);
                    if (pos.x < 0) continue;

                    EntitySpawnData entity = new EntitySpawnData
                    {
                        position = new float3(pos.x, heightMap[pos.x + pos.y * resolution], pos.y),
                        entityTypeID = rng.NextInt(3000, 3100), // Loot ID range
                        threatLevel = 0f,
                        isLoot = true
                    };

                    entitySpawns[index] = entity;
                    index++;
                }

                return index;
            }

            private int2 GetRandomValidPosition(ref Unity.Mathematics.Random rng, BiomeType preferredBiome, bool requireFlat)
            {
                // Try to find valid spawn position
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int x = rng.NextInt(0, resolution);
                    int y = rng.NextInt(0, resolution);
                    int index = x + y * resolution;

                    BiomeType biome = biomeMap[index];

                    // Skip water
                    if (biome == BiomeType.Ocean)
                        continue;

                    // Check flatness if required
                    if (requireFlat)
                    {
                        // Simple flatness check - would need neighbor sampling for accuracy
                        // For now, just avoid extreme heights
                        if (heightMap[index] > 80f)
                            continue;
                    }

                    return new int2(x, y);
                }

                return new int2(-1, -1); // Invalid
            }

            private int GetBiomeEntityType(BiomeType biome, ref Unity.Mathematics.Random rng)
            {
                // Return entity type ID based on biome
                switch (biome)
                {
                    case BiomeType.Forest:
                    case BiomeType.Rainforest:
                        return rng.NextInt(2100, 2110); // Forest creatures

                    case BiomeType.Desert:
                        return rng.NextInt(2110, 2120); // Desert creatures

                    case BiomeType.Mountain:
                        return rng.NextInt(2120, 2130); // Mountain creatures

                    case BiomeType.Taiga:
                        return rng.NextInt(2130, 2140); // Cold creatures

                    case BiomeType.Savanna:
                        return rng.NextInt(2140, 2150); // Savanna creatures

                    default:
                        return rng.NextInt(2150, 2200); // Generic creatures
                }
            }
        }

        // Utility: Get entity name from ID
        public static string GetEntityName(int entityTypeID)
        {
            if (entityTypeID >= 1000 && entityTypeID < 1100) return "NPC";
            if (entityTypeID >= 2000 && entityTypeID < 2100) return "Dungeon Monster";
            if (entityTypeID >= 2100 && entityTypeID < 2110) return "Forest Creature";
            if (entityTypeID >= 2110 && entityTypeID < 2120) return "Desert Creature";
            if (entityTypeID >= 2120 && entityTypeID < 2130) return "Mountain Creature";
            if (entityTypeID >= 2130 && entityTypeID < 2140) return "Arctic Creature";
            if (entityTypeID >= 2140 && entityTypeID < 2150) return "Savanna Creature";
            if (entityTypeID >= 2150 && entityTypeID < 2200) return "Wildlife";
            if (entityTypeID >= 3000 && entityTypeID < 3100) return "Treasure";

            return "Unknown Entity";
        }
    }
}
