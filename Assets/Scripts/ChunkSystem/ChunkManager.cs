using UnityEngine;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Jobs;
using Unity.Collections;
using TerrariumGen.Data;
using TerrariumGen.Generation;

namespace TerrariumGen.ChunkSystem
{
    /// <summary>
    /// Main chunk management system
    /// Handles dynamic loading/unloading and coordinates generation pipeline
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        [Header("Chunk Settings")]
        [SerializeField] private int chunkSize = 64;
        [SerializeField] private int chunkResolution = 128;
        [SerializeField] private Transform player;

        [Header("View Distance")]
        [SerializeField] private int viewDistanceFull = 2;      // Full detail chunks
        [SerializeField] private int viewDistanceHigh = 4;      // High detail chunks
        [SerializeField] private int viewDistanceMedium = 8;    // Medium detail chunks
        [SerializeField] private int viewDistanceLow = 16;      // Low detail chunks

        [Header("Performance")]
        [SerializeField] private int maxChunksPerFrame = 2;
        [SerializeField] private float cacheTimeout = 300f;     // 5 minutes
        [SerializeField] private int maxCachedChunks = 100;
        [SerializeField] private bool useDualGPU = true;

        [Header("Generation Pipeline")]
        [SerializeField] private TerrainGenerator terrainGenerator;
        [SerializeField] private BiomeGenerator biomeGenerator;
        [SerializeField] private FeatureGenerator featureGenerator;
        [SerializeField] private SettlementGenerator settlementGenerator;
        [SerializeField] private DungeonGenerator dungeonGenerator;
        [SerializeField] private EntityGenerator entityGenerator;

        // Active and cached chunks
        private Dictionary<ChunkCoord, ChunkData> activeChunks = new Dictionary<ChunkCoord, ChunkData>();
        private Dictionary<ChunkCoord, ChunkData> cachedChunks = new Dictionary<ChunkCoord, ChunkData>();

        // Generation queues (separate for dual GPU)
        private ConcurrentQueue<ChunkData> immediateQueue = new ConcurrentQueue<ChunkData>();  // GPU 0
        private ConcurrentQueue<ChunkData> preGenQueue = new ConcurrentQueue<ChunkData>();     // GPU 1

        // Current player chunk
        private ChunkCoord currentPlayerChunk;
        private ChunkCoord previousPlayerChunk;

        // Frustum culling
        private Plane[] frustumPlanes = new Plane[6];

        private void Start()
        {
            if (player == null)
            {
                player = Camera.main.transform;
            }

            InitializeGenerators();
            currentPlayerChunk = ChunkCoord.FromWorldPosition(player.position, chunkSize);
            GenerateInitialChunks();
        }

        private void InitializeGenerators()
        {
            if (terrainGenerator == null) terrainGenerator = gameObject.AddComponent<TerrainGenerator>();
            if (biomeGenerator == null) biomeGenerator = gameObject.AddComponent<BiomeGenerator>();
            if (featureGenerator == null) featureGenerator = gameObject.AddComponent<FeatureGenerator>();
            if (settlementGenerator == null) settlementGenerator = gameObject.AddComponent<SettlementGenerator>();
            if (dungeonGenerator == null) dungeonGenerator = gameObject.AddComponent<DungeonGenerator>();
            if (entityGenerator == null) entityGenerator = gameObject.AddComponent<EntityGenerator>();

            terrainGenerator.Initialize(chunkSize, chunkResolution);
        }

        private void Update()
        {
            UpdatePlayerChunk();
            UpdateChunkLoading();
            ProcessGenerationQueues();
            UpdateFrustumCulling();
            CleanupOldChunks();
        }

        private void UpdatePlayerChunk()
        {
            currentPlayerChunk = ChunkCoord.FromWorldPosition(player.position, chunkSize);

            // Player moved to new chunk
            if (!currentPlayerChunk.Equals(previousPlayerChunk))
            {
                OnPlayerChangedChunk();
                previousPlayerChunk = currentPlayerChunk;
            }
        }

        private void OnPlayerChangedChunk()
        {
            // Determine which chunks need loading/unloading
            UpdateVisibleChunks();
        }

        private void UpdateVisibleChunks()
        {
            HashSet<ChunkCoord> chunksToKeep = new HashSet<ChunkCoord>();

            // Calculate chunks at different LOD levels
            for (int lod = 0; lod < 4; lod++)
            {
                int viewDistance = GetViewDistanceForLOD((ChunkLOD)lod);

                for (int x = -viewDistance; x <= viewDistance; x++)
                {
                    for (int z = -viewDistance; z <= viewDistance; z++)
                    {
                        ChunkCoord coord = new ChunkCoord(
                            currentPlayerChunk.x + x,
                            currentPlayerChunk.z + z
                        );

                        chunksToKeep.Add(coord);

                        // Load or update chunk
                        if (!activeChunks.ContainsKey(coord))
                        {
                            LoadChunk(coord, (ChunkLOD)lod);
                        }
                        else
                        {
                            // Update LOD if needed
                            UpdateChunkLOD(coord, (ChunkLOD)lod);
                        }
                    }
                }
            }

            // Unload chunks outside view distance
            List<ChunkCoord> chunksToUnload = new List<ChunkCoord>();
            foreach (var coord in activeChunks.Keys)
            {
                if (!chunksToKeep.Contains(coord))
                {
                    chunksToUnload.Add(coord);
                }
            }

            foreach (var coord in chunksToUnload)
            {
                UnloadChunk(coord);
            }
        }

        private int GetViewDistanceForLOD(ChunkLOD lod)
        {
            switch (lod)
            {
                case ChunkLOD.Full: return viewDistanceFull;
                case ChunkLOD.High: return viewDistanceHigh;
                case ChunkLOD.Medium: return viewDistanceMedium;
                case ChunkLOD.Low: return viewDistanceLow;
                default: return viewDistanceFull;
            }
        }

        private void LoadChunk(ChunkCoord coord, ChunkLOD lod)
        {
            // Check cache first
            if (cachedChunks.TryGetValue(coord, out ChunkData cachedData))
            {
                cachedData.lastAccessTime = Time.time;
                cachedData.lod = lod;
                activeChunks[coord] = cachedData;
                cachedChunks.Remove(coord);
                return;
            }

            // Create new chunk
            int resolution = GetResolutionForLOD(lod);
            ChunkData chunk = new ChunkData(coord, resolution, lod);

            activeChunks[coord] = chunk;

            // Queue for generation based on distance
            float distanceToPlayer = Vector3.Distance(
                coord.ToWorldPosition(chunkSize),
                player.position
            );

            if (useDualGPU && distanceToPlayer > chunkSize * viewDistanceFull)
            {
                // Pre-generate on second GPU
                preGenQueue.Enqueue(chunk);
            }
            else
            {
                // Immediate generation on first GPU
                immediateQueue.Enqueue(chunk);
            }
        }

        private void UnloadChunk(ChunkCoord coord)
        {
            if (activeChunks.TryGetValue(coord, out ChunkData chunk))
            {
                activeChunks.Remove(coord);

                // Cache if not too many cached chunks
                if (cachedChunks.Count < maxCachedChunks)
                {
                    chunk.lastAccessTime = Time.time;
                    cachedChunks[coord] = chunk;
                }
                else
                {
                    // Dispose to free memory
                    chunk.Dispose();
                }
            }
        }

        private void UpdateChunkLOD(ChunkCoord coord, ChunkLOD newLOD)
        {
            if (activeChunks.TryGetValue(coord, out ChunkData chunk))
            {
                if (chunk.lod != newLOD)
                {
                    chunk.lod = newLOD;
                    chunk.isDirty = true;
                }
            }
        }

        private int GetResolutionForLOD(ChunkLOD lod)
        {
            switch (lod)
            {
                case ChunkLOD.Full: return chunkResolution;
                case ChunkLOD.High: return chunkResolution / 2;
                case ChunkLOD.Medium: return chunkResolution / 4;
                case ChunkLOD.Low: return chunkResolution / 8;
                default: return chunkResolution;
            }
        }

        private void UpdateChunkLoading()
        {
            // This is called every frame to smoothly load chunks
        }

        private void ProcessGenerationQueues()
        {
            int processedThisFrame = 0;

            // Process immediate queue (GPU 0)
            while (processedThisFrame < maxChunksPerFrame && immediateQueue.TryDequeue(out ChunkData chunk))
            {
                ProcessChunkGeneration(chunk, 0);
                processedThisFrame++;
            }

            // Process pre-gen queue (GPU 1)
            if (useDualGPU && preGenQueue.TryDequeue(out ChunkData preGenChunk))
            {
                ProcessChunkGeneration(preGenChunk, 1);
            }
        }

        private void ProcessChunkGeneration(ChunkData chunk, int gpuIndex)
        {
            // Multi-layer generation pipeline
            switch (chunk.state)
            {
                case GenerationState.None:
                case GenerationState.Queued:
                    terrainGenerator.GenerateBaseTerrain(chunk, gpuIndex);
                    chunk.state = GenerationState.BaseTerrain;
                    break;

                case GenerationState.BaseTerrain:
                    biomeGenerator.AssignBiomes(chunk);
                    chunk.state = GenerationState.Biomes;
                    break;

                case GenerationState.Biomes:
                    featureGenerator.PlaceFeatures(chunk);
                    chunk.state = GenerationState.Features;
                    break;

                case GenerationState.Features:
                    settlementGenerator.GenerateSettlements(chunk);
                    chunk.state = GenerationState.Settlements;
                    break;

                case GenerationState.Settlements:
                    dungeonGenerator.GenerateDungeons(chunk);
                    chunk.state = GenerationState.Dungeons;
                    break;

                case GenerationState.Dungeons:
                    entityGenerator.PopulateEntities(chunk);
                    chunk.state = GenerationState.Entities;
                    break;

                case GenerationState.Entities:
                    chunk.state = GenerationState.Complete;
                    OnChunkComplete(chunk);
                    break;
            }
        }

        private void OnChunkComplete(ChunkData chunk)
        {
            // Chunk is fully generated and ready for rendering
            Debug.Log($"Chunk {chunk.coord.x},{chunk.coord.z} generation complete!");
        }

        private void UpdateFrustumCulling()
        {
            // Update frustum planes for GPU culling
            frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        }

        private void CleanupOldChunks()
        {
            // Remove old cached chunks
            List<ChunkCoord> toRemove = new List<ChunkCoord>();
            float currentTime = Time.time;

            foreach (var kvp in cachedChunks)
            {
                if (currentTime - kvp.Value.lastAccessTime > cacheTimeout)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var coord in toRemove)
            {
                if (cachedChunks.TryGetValue(coord, out ChunkData chunk))
                {
                    chunk.Dispose();
                    cachedChunks.Remove(coord);
                }
            }
        }

        private void GenerateInitialChunks()
        {
            // Generate chunks around starting position
            UpdateVisibleChunks();
        }

        public void TriggerWorldEvent(WorldEvent worldEvent)
        {
            // Apply world event to affected chunks
            foreach (var kvp in activeChunks)
            {
                ChunkData chunk = kvp.Value;
                Vector3 chunkCenter = chunk.coord.ToWorldPosition(chunkSize) + new Vector3(chunkSize / 2f, 0, chunkSize / 2f);

                float distance = Vector3.Distance(chunkCenter, worldEvent.epicenter);

                if (distance <= worldEvent.radius)
                {
                    // Add event to chunk
                    for (int i = 0; i < chunk.activeEvents.Length; i++)
                    {
                        if (chunk.activeEvents[i].type == WorldEvent.EventType.None)
                        {
                            chunk.activeEvents[i] = worldEvent;
                            chunk.isDirty = true;
                            break;
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            // Cleanup all chunks
            foreach (var chunk in activeChunks.Values)
            {
                chunk.Dispose();
            }

            foreach (var chunk in cachedChunks.Values)
            {
                chunk.Dispose();
            }

            activeChunks.Clear();
            cachedChunks.Clear();
        }

        // Public API
        public ChunkData GetChunk(ChunkCoord coord)
        {
            activeChunks.TryGetValue(coord, out ChunkData chunk);
            return chunk;
        }

        public bool IsChunkLoaded(ChunkCoord coord)
        {
            return activeChunks.ContainsKey(coord);
        }

        public IEnumerable<ChunkData> GetActiveChunks()
        {
            return activeChunks.Values;
        }
    }
}
