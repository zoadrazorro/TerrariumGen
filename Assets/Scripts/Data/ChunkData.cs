using UnityEngine;
using Unity.Collections;
using System;

namespace TerrariumGen.Data
{
    /// <summary>
    /// Core data structure for a world chunk
    /// Contains all generation layers and metadata
    /// </summary>
    [Serializable]
    public struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int x;
        public int z;

        public ChunkCoord(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public bool Equals(ChunkCoord other)
        {
            return x == other.x && z == other.z;
        }

        public override int GetHashCode()
        {
            return (x * 397) ^ z;
        }

        public static ChunkCoord FromWorldPosition(Vector3 worldPos, int chunkSize)
        {
            return new ChunkCoord(
                Mathf.FloorToInt(worldPos.x / chunkSize),
                Mathf.FloorToInt(worldPos.z / chunkSize)
            );
        }

        public Vector3 ToWorldPosition(int chunkSize)
        {
            return new Vector3(x * chunkSize, 0, z * chunkSize);
        }
    }

    /// <summary>
    /// Level of detail for chunk generation
    /// </summary>
    public enum ChunkLOD
    {
        Full = 0,      // Immediate surroundings - full detail
        High = 1,      // 1 chunk ring - high detail
        Medium = 2,    // 2-3 chunk rings - medium detail
        Low = 3        // 4+ chunk rings - low detail, terrain only
    }

    /// <summary>
    /// Generation state of a chunk
    /// </summary>
    public enum GenerationState
    {
        None = 0,
        Queued = 1,
        BaseTerrain = 2,      // Layer 1: Height/moisture/temperature
        Biomes = 3,            // Layer 2: Biome assignment
        Features = 4,          // Layer 3: Rivers, roads, POIs
        Settlements = 5,       // Layer 4: Towns and settlements
        Dungeons = 6,          // Layer 5: Dungeon structures
        Entities = 7,          // Layer 6: NPCs, monsters, loot
        Complete = 8
    }

    /// <summary>
    /// Biome types based on temperature and moisture
    /// </summary>
    public enum BiomeType
    {
        Ocean,
        Beach,
        Desert,
        Savanna,
        Grassland,
        Forest,
        Rainforest,
        Taiga,
        Tundra,
        Snow,
        Mountain
    }

    /// <summary>
    /// Complete chunk data with all generation layers
    /// </summary>
    public class ChunkData : IDisposable
    {
        public ChunkCoord coord;
        public ChunkLOD lod;
        public GenerationState state;
        public float lastAccessTime;
        public bool isDirty; // Modified by dynamic events

        // Layer 1: Base terrain data (GPU generated)
        public NativeArray<float> heightMap;
        public NativeArray<float> moistureMap;
        public NativeArray<float> temperatureMap;

        // Layer 2: Biome data
        public NativeArray<BiomeType> biomeMap;

        // Layer 3: Feature data
        public bool hasRiver;
        public bool hasRoad;
        public NativeArray<Vector2Int> pointsOfInterest;

        // Layer 4: Settlement data
        public bool hasSettlement;
        public int settlementSize; // 0 = none, 1-5 = village to city

        // Layer 5: Dungeon data
        public bool hasDungeon;
        public int dungeonDepth;

        // Layer 6: Entity data
        public NativeArray<EntitySpawnData> entitySpawns;

        // Dynamic world events
        public NativeArray<WorldEvent> activeEvents;

        // Resolution based on LOD
        public int resolution;

        public ChunkData(ChunkCoord coord, int resolution, ChunkLOD lod = ChunkLOD.Full)
        {
            this.coord = coord;
            this.resolution = resolution;
            this.lod = lod;
            this.state = GenerationState.None;
            this.lastAccessTime = Time.time;
            this.isDirty = false;

            // Allocate native arrays
            int mapSize = resolution * resolution;
            heightMap = new NativeArray<float>(mapSize, Allocator.Persistent);
            moistureMap = new NativeArray<float>(mapSize, Allocator.Persistent);
            temperatureMap = new NativeArray<float>(mapSize, Allocator.Persistent);
            biomeMap = new NativeArray<BiomeType>(mapSize, Allocator.Persistent);
            pointsOfInterest = new NativeArray<Vector2Int>(16, Allocator.Persistent);
            entitySpawns = new NativeArray<EntitySpawnData>(64, Allocator.Persistent);
            activeEvents = new NativeArray<WorldEvent>(8, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (heightMap.IsCreated) heightMap.Dispose();
            if (moistureMap.IsCreated) moistureMap.Dispose();
            if (temperatureMap.IsCreated) temperatureMap.Dispose();
            if (biomeMap.IsCreated) biomeMap.Dispose();
            if (pointsOfInterest.IsCreated) pointsOfInterest.Dispose();
            if (entitySpawns.IsCreated) entitySpawns.Dispose();
            if (activeEvents.IsCreated) activeEvents.Dispose();
        }
    }

    /// <summary>
    /// Entity spawn data for NPCs, monsters, loot
    /// </summary>
    [Serializable]
    public struct EntitySpawnData
    {
        public Vector3 position;
        public int entityTypeID;
        public float threatLevel;
        public bool isLoot;
    }

    /// <summary>
    /// Dynamic world event data
    /// </summary>
    [Serializable]
    public struct WorldEvent
    {
        public enum EventType
        {
            None,
            MagicalExplosion,
            CrystalizedTerrain,
            FactionInfluence,
            NaturalDisaster
        }

        public EventType type;
        public Vector3 epicenter;
        public float radius;
        public float intensity;
        public float timestamp;
    }
}
