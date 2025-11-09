using UnityEngine;
using Unity.Mathematics;
using TerrariumGen.Data;

namespace TerrariumGen.Generation
{
    /// <summary>
    /// Settlement generation system
    /// Layer 4: Generates towns and settlements based on terrain suitability
    /// Influenced by dynamic world events (faction influence)
    /// </summary>
    public class SettlementGenerator : MonoBehaviour
    {
        [Header("Settlement Settings")]
        [SerializeField] private int settlementSeed = 77777;
        [SerializeField] private float baseSettlementChance = 0.05f;
        [SerializeField] private float factionInfluenceMultiplier = 2.0f;

        [Header("Settlement Requirements")]
        [SerializeField] private float minFlatness = 0.7f; // How flat terrain needs to be
        [SerializeField] private float optimalMoisture = 0.5f;
        [SerializeField] private float optimalTemperature = 0.6f;

        public void GenerateSettlements(ChunkData chunk)
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                (uint)(settlementSeed + chunk.coord.x * 1000 + chunk.coord.z)
            );

            // Check if chunk is suitable for settlement
            float suitability = CalculateSettlementSuitability(chunk);

            // Apply faction influence from world events
            float factionBonus = GetFactionInfluence(chunk);
            suitability *= (1.0f + factionBonus * factionInfluenceMultiplier);

            // Determine if settlement should spawn
            float settlementRoll = rng.NextFloat();
            if (settlementRoll < suitability * baseSettlementChance)
            {
                chunk.hasSettlement = true;

                // Determine settlement size (1 = hamlet, 2 = village, 3 = town, 4 = city)
                chunk.settlementSize = DetermineSettlementSize(suitability, factionBonus, rng);

                Debug.Log($"Settlement generated at chunk ({chunk.coord.x}, {chunk.coord.z}) - Size: {chunk.settlementSize}");
            }
            else
            {
                chunk.hasSettlement = false;
                chunk.settlementSize = 0;
            }
        }

        private float CalculateSettlementSuitability(ChunkData chunk)
        {
            float totalSuitability = 0f;
            int validTiles = 0;

            // Analyze terrain for settlement suitability
            for (int i = 0; i < chunk.biomeMap.Length; i++)
            {
                BiomeType biome = chunk.biomeMap[i];

                // Skip unsuitable biomes
                if (biome == BiomeType.Ocean || biome == BiomeType.Mountain || biome == BiomeType.Snow)
                    continue;

                // Calculate tile suitability
                float tileSuitability = 1.0f;

                // Prefer flat terrain
                float flatness = CalculateFlatness(chunk, i);
                if (flatness < minFlatness)
                    continue;

                tileSuitability *= flatness;

                // Prefer moderate moisture (not too dry, not too wet)
                float moisture = chunk.moistureMap[i];
                float moistureScore = 1.0f - Mathf.Abs(moisture - optimalMoisture);
                tileSuitability *= moistureScore;

                // Prefer moderate temperature
                float temperature = chunk.temperatureMap[i];
                float tempScore = 1.0f - Mathf.Abs(temperature - optimalTemperature);
                tileSuitability *= tempScore;

                // Biome-specific bonuses
                tileSuitability *= GetBiomeSuitability(biome);

                totalSuitability += tileSuitability;
                validTiles++;
            }

            if (validTiles == 0)
                return 0f;

            float avgSuitability = totalSuitability / validTiles;

            // Bonus if chunk has a road
            if (chunk.hasRoad)
                avgSuitability *= 1.5f;

            // Bonus if chunk has river (but not too much water)
            if (chunk.hasRiver)
                avgSuitability *= 1.3f;

            return Mathf.Clamp01(avgSuitability);
        }

        private float CalculateFlatness(ChunkData chunk, int index)
        {
            // Calculate how flat the terrain is around this point
            int x = index % chunk.resolution;
            int y = index / chunk.resolution;

            float centerHeight = chunk.heightMap[index];
            float maxDiff = 0f;
            int samples = 0;

            // Check neighbors
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < chunk.resolution && ny >= 0 && ny < chunk.resolution)
                    {
                        int nIndex = nx + ny * chunk.resolution;
                        float diff = Mathf.Abs(chunk.heightMap[nIndex] - centerHeight);
                        maxDiff = Mathf.Max(maxDiff, diff);
                        samples++;
                    }
                }
            }

            // Normalize and invert (flatter = higher score)
            return 1.0f - Mathf.Clamp01(maxDiff / 10f);
        }

        private float GetBiomeSuitability(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Grassland: return 1.0f;   // Best for settlements
                case BiomeType.Forest: return 0.8f;      // Good
                case BiomeType.Savanna: return 0.7f;     // Good
                case BiomeType.Beach: return 0.6f;       // Coastal settlements
                case BiomeType.Desert: return 0.4f;      // Oasis settlements
                case BiomeType.Taiga: return 0.5f;       // Hardy settlements
                case BiomeType.Tundra: return 0.3f;      // Rare
                case BiomeType.Rainforest: return 0.5f;  // Jungle settlements
                default: return 0.1f;
            }
        }

        private float GetFactionInfluence(ChunkData chunk)
        {
            // Check for faction influence world events
            float totalInfluence = 0f;

            for (int i = 0; i < chunk.activeEvents.Length; i++)
            {
                if (chunk.activeEvents[i].type == WorldEvent.EventType.FactionInfluence)
                {
                    totalInfluence += chunk.activeEvents[i].intensity;
                }
            }

            return totalInfluence;
        }

        private int DetermineSettlementSize(float suitability, float factionBonus, Unity.Mathematics.Random rng)
        {
            // Higher suitability and faction influence = larger settlements
            float sizeRoll = rng.NextFloat();
            float modifiedRoll = sizeRoll * (1.0f / (suitability + factionBonus * 0.5f));

            if (modifiedRoll < 0.1f) return 5; // Metropolis (very rare)
            if (modifiedRoll < 0.3f) return 4; // City
            if (modifiedRoll < 0.5f) return 3; // Town
            if (modifiedRoll < 0.7f) return 2; // Village
            return 1; // Hamlet
        }

        public static string GetSettlementName(int chunkX, int chunkZ, int size, int seed)
        {
            // Generate a procedural settlement name
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)(seed + chunkX * 7919 + chunkZ * 7907));

            string[] prefixes = { "North", "South", "East", "West", "New", "Old", "Great", "Little" };
            string[] roots = { "ford", "ton", "ville", "burg", "dale", "field", "wood", "stone", "haven", "port" };
            string[] suffixes = { "", " City", " Town", " Village", " Hamlet" };

            string prefix = rng.NextFloat() > 0.5f ? prefixes[rng.NextInt(0, prefixes.Length)] : "";
            string root = roots[rng.NextInt(0, roots.Length)];
            string suffix = size > 3 ? suffixes[size - 3] : "";

            return $"{prefix}{root}{suffix}".Trim();
        }
    }
}
