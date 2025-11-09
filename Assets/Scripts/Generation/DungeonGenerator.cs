using UnityEngine;
using Unity.Mathematics;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// Dungeon generation system
    /// Layer 5: Generates dungeon entrances and metadata
    /// </summary>
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Dungeon Settings")]
        [SerializeField] private int dungeonSeed = 88888;
        [SerializeField] private float baseDungeonChance = 0.08f;

        [Header("Dungeon Depth")]
        [SerializeField] private int minDepth = 1;
        [SerializeField] private int maxDepth = 10;

        public void GenerateDungeons(ChunkData chunk)
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                (uint)(dungeonSeed + chunk.coord.x * 1337 + chunk.coord.z)
            );

            // Calculate dungeon spawn chance based on terrain
            float dungeonChance = CalculateDungeonChance(chunk, rng);

            float dungeonRoll = rng.NextFloat();
            if (dungeonRoll < dungeonChance)
            {
                chunk.hasDungeon = true;

                // Determine dungeon depth
                chunk.dungeonDepth = DetermineDungeonDepth(chunk, rng);

                Debug.Log($"Dungeon generated at chunk ({chunk.coord.x}, {chunk.coord.z}) - Depth: {chunk.dungeonDepth}");
            }
            else
            {
                chunk.hasDungeon = false;
                chunk.dungeonDepth = 0;
            }
        }

        private float CalculateDungeonChance(ChunkData chunk, Unity.Mathematics.Random rng)
        {
            float chance = baseDungeonChance;

            // Dungeons are more likely in certain biomes
            int mountainTiles = 0;
            int forestTiles = 0;
            int desertTiles = 0;
            int totalValidTiles = 0;

            for (int i = 0; i < chunk.biomeMap.Length; i++)
            {
                BiomeType biome = chunk.biomeMap[i];

                switch (biome)
                {
                    case BiomeType.Mountain:
                        mountainTiles++;
                        totalValidTiles++;
                        break;

                    case BiomeType.Forest:
                    case BiomeType.Rainforest:
                    case BiomeType.Taiga:
                        forestTiles++;
                        totalValidTiles++;
                        break;

                    case BiomeType.Desert:
                        desertTiles++;
                        totalValidTiles++;
                        break;

                    case BiomeType.Grassland:
                    case BiomeType.Savanna:
                        totalValidTiles++;
                        break;
                }
            }

            if (totalValidTiles == 0)
                return 0f;

            // Mountains have highest dungeon chance (caves)
            float mountainRatio = mountainTiles / (float)chunk.biomeMap.Length;
            chance += mountainRatio * 0.3f;

            // Forests have moderate chance (ruins, old structures)
            float forestRatio = forestTiles / (float)chunk.biomeMap.Length;
            chance += forestRatio * 0.15f;

            // Deserts have moderate chance (ancient tombs)
            float desertRatio = desertTiles / (float)chunk.biomeMap.Length;
            chance += desertRatio * 0.2f;

            // Don't place dungeons too close to settlements
            if (chunk.hasSettlement)
            {
                chance *= 0.3f;
            }

            // Magical explosions might create dungeons
            for (int i = 0; i < chunk.activeEvents.Length; i++)
            {
                if (chunk.activeEvents[i].type == WorldEvent.EventType.MagicalExplosion)
                {
                    chance += chunk.activeEvents[i].intensity * 0.4f;
                }
            }

            return Mathf.Clamp01(chance);
        }

        private int DetermineDungeonDepth(ChunkData chunk, Unity.Mathematics.Random rng)
        {
            // Depth influenced by terrain and events

            // Base depth from random
            int depth = rng.NextInt(minDepth, maxDepth + 1);

            // Mountain dungeons tend to be deeper (caves go down)
            int mountainTiles = 0;
            for (int i = 0; i < chunk.biomeMap.Length; i++)
            {
                if (chunk.biomeMap[i] == BiomeType.Mountain)
                    mountainTiles++;
            }

            float mountainRatio = mountainTiles / (float)chunk.biomeMap.Length;
            if (mountainRatio > 0.5f)
            {
                depth += rng.NextInt(1, 4);
            }

            // Magical events create deeper dungeons
            for (int i = 0; i < chunk.activeEvents.Length; i++)
            {
                if (chunk.activeEvents[i].type == WorldEvent.EventType.MagicalExplosion)
                {
                    depth += (int)(chunk.activeEvents[i].intensity * 5f);
                }
            }

            return Mathf.Clamp(depth, minDepth, maxDepth);
        }

        public static string GetDungeonType(ChunkData chunk, int seed)
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)seed);

            // Determine dungeon type based on dominant biome
            BiomeType dominantBiome = GetDominantBiome(chunk);

            switch (dominantBiome)
            {
                case BiomeType.Mountain:
                    return rng.NextFloat() > 0.5f ? "Mountain Cave" : "Dwarven Ruins";

                case BiomeType.Forest:
                case BiomeType.Rainforest:
                    return rng.NextFloat() > 0.5f ? "Ancient Grove Sanctum" : "Overgrown Catacombs";

                case BiomeType.Desert:
                    return rng.NextFloat() > 0.5f ? "Lost Tomb" : "Desert Pyramid";

                case BiomeType.Taiga:
                    return "Frozen Barrow";

                case BiomeType.Grassland:
                case BiomeType.Savanna:
                    return rng.NextFloat() > 0.5f ? "Bandit Hideout" : "Old Cellar";

                default:
                    return "Mysterious Dungeon";
            }
        }

        private static BiomeType GetDominantBiome(ChunkData chunk)
        {
            // Count biome occurrences
            int[] biomeCounts = new int[System.Enum.GetValues(typeof(BiomeType)).Length];

            for (int i = 0; i < chunk.biomeMap.Length; i++)
            {
                biomeCounts[(int)chunk.biomeMap[i]]++;
            }

            // Find most common
            int maxCount = 0;
            BiomeType dominant = BiomeType.Grassland;

            for (int i = 0; i < biomeCounts.Length; i++)
            {
                if (biomeCounts[i] > maxCount)
                {
                    maxCount = biomeCounts[i];
                    dominant = (BiomeType)i;
                }
            }

            return dominant;
        }
    }
}
