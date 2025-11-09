using UnityEngine;
using System.Collections.Generic;
using TerrariumGen.ChunkSystem;
using TerrariumGen.Data;

namespace TerrariumGen.Systems
{
    /// <summary>
    /// Main game manager that coordinates chunk system and visualization
    /// This is the entry point for the Terrarium Gen system
    /// </summary>
    public class TerrariumGameManager : MonoBehaviour
    {
        [Header("Core Systems")]
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private WorldEventManager worldEventManager;

        [Header("Visualization")]
        [SerializeField] private GameObject chunkMeshPrefab;
        [SerializeField] private Transform chunksParent;

        [Header("Player/Camera")]
        [SerializeField] private Transform playerCamera;
        [SerializeField] private float cameraSpeed = 10f;
        [SerializeField] private float cameraTurnSpeed = 100f;

        [Header("Debug Info")]
        [SerializeField] private bool showDebugInfo = true;

        private Dictionary<ChunkCoord, GameObject> chunkVisuals = new Dictionary<ChunkCoord, GameObject>();
        private HashSet<ChunkCoord> visualizedChunks = new HashSet<ChunkCoord>();

        private void Start()
        {
            InitializeSystems();
            SetupCamera();
        }

        private void InitializeSystems()
        {
            // Create chunks parent if not assigned
            if (chunksParent == null)
            {
                chunksParent = new GameObject("Chunks").transform;
            }

            // Find or create chunk manager
            if (chunkManager == null)
            {
                chunkManager = FindObjectOfType<ChunkManager>();

                if (chunkManager == null)
                {
                    GameObject managerObj = new GameObject("ChunkManager");
                    chunkManager = managerObj.AddComponent<ChunkManager>();
                }
            }

            // Find or create world event manager
            if (worldEventManager == null)
            {
                worldEventManager = FindObjectOfType<WorldEventManager>();

                if (worldEventManager == null)
                {
                    GameObject eventManagerObj = new GameObject("WorldEventManager");
                    worldEventManager = eventManagerObj.AddComponent<WorldEventManager>();
                }
            }

            Debug.Log("TerrariumGen systems initialized!");
        }

        private void SetupCamera()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main.transform;
            }

            // Position camera at a good starting height
            if (playerCamera != null)
            {
                playerCamera.position = new Vector3(0, 50, 0);
                playerCamera.rotation = Quaternion.Euler(45, 0, 0);
            }
        }

        private void Update()
        {
            HandleCameraMovement();
            UpdateChunkVisuals();
        }

        private void HandleCameraMovement()
        {
            if (playerCamera == null) return;

            // WASD movement
            Vector3 movement = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) movement += playerCamera.forward;
            if (Input.GetKey(KeyCode.S)) movement -= playerCamera.forward;
            if (Input.GetKey(KeyCode.A)) movement -= playerCamera.right;
            if (Input.GetKey(KeyCode.D)) movement += playerCamera.right;

            // Vertical movement
            if (Input.GetKey(KeyCode.Q)) movement += Vector3.down;
            if (Input.GetKey(KeyCode.E)) movement += Vector3.up;

            // Apply movement
            playerCamera.position += movement.normalized * cameraSpeed * Time.deltaTime;

            // Mouse look (hold right click)
            if (Input.GetMouseButton(1))
            {
                float mouseX = Input.GetAxis("Mouse X") * cameraTurnSpeed * Time.deltaTime;
                float mouseY = Input.GetAxis("Mouse Y") * cameraTurnSpeed * Time.deltaTime;

                playerCamera.Rotate(Vector3.up, mouseX, Space.World);
                playerCamera.Rotate(Vector3.right, -mouseY, Space.Self);
            }
        }

        private void UpdateChunkVisuals()
        {
            if (chunkManager == null) return;

            // Get all active chunks
            foreach (ChunkData chunk in chunkManager.GetActiveChunks())
            {
                // Only visualize fully generated chunks
                if (chunk.state == GenerationState.Complete && !visualizedChunks.Contains(chunk.coord))
                {
                    CreateChunkVisual(chunk);
                    visualizedChunks.Add(chunk.coord);
                }
            }

            // Clean up old visuals (chunks that are no longer active)
            List<ChunkCoord> toRemove = new List<ChunkCoord>();

            foreach (var coord in visualizedChunks)
            {
                if (!chunkManager.IsChunkLoaded(coord))
                {
                    toRemove.Add(coord);
                }
            }

            foreach (var coord in toRemove)
            {
                RemoveChunkVisual(coord);
                visualizedChunks.Remove(coord);
            }
        }

        private void CreateChunkVisual(ChunkData chunk)
        {
            // Create chunk mesh object
            GameObject chunkObj;

            if (chunkMeshPrefab != null)
            {
                chunkObj = Instantiate(chunkMeshPrefab, chunksParent);
            }
            else
            {
                chunkObj = new GameObject($"Chunk_{chunk.coord.x}_{chunk.coord.z}");
                chunkObj.transform.SetParent(chunksParent);
                chunkObj.AddComponent<MeshFilter>();
                chunkObj.AddComponent<MeshRenderer>();
            }

            // Position chunk
            Vector3 worldPos = chunk.coord.ToWorldPosition(64); // Assuming chunk size of 64
            chunkObj.transform.position = worldPos;

            // Generate mesh
            ChunkMeshGenerator meshGen = chunkObj.GetComponent<ChunkMeshGenerator>();
            if (meshGen == null)
            {
                meshGen = chunkObj.AddComponent<ChunkMeshGenerator>();
            }

            meshGen.GenerateMesh(chunk, 64);

            chunkVisuals[chunk.coord] = chunkObj;
        }

        private void RemoveChunkVisual(ChunkCoord coord)
        {
            if (chunkVisuals.TryGetValue(coord, out GameObject chunkObj))
            {
                Destroy(chunkObj);
                chunkVisuals.Remove(coord);
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            // Display debug information
            GUILayout.BeginArea(new Rect(10, 10, 300, 400));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>TerrariumGen Debug Info</b>");
            GUILayout.Space(10);

            if (chunkManager != null)
            {
                int activeChunks = 0;
                foreach (var _ in chunkManager.GetActiveChunks()) activeChunks++;

                GUILayout.Label($"Active Chunks: {activeChunks}");
                GUILayout.Label($"Visualized Chunks: {visualizedChunks.Count}");
            }

            if (playerCamera != null)
            {
                GUILayout.Label($"Camera Position: {playerCamera.position:F1}");

                ChunkCoord currentChunk = ChunkCoord.FromWorldPosition(playerCamera.position, 64);
                GUILayout.Label($"Current Chunk: ({currentChunk.x}, {currentChunk.z})");
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Controls:</b>");
            GUILayout.Label("WASD - Move Camera");
            GUILayout.Label("Q/E - Move Up/Down");
            GUILayout.Label("Right Click + Mouse - Look");
            GUILayout.Label("1 - Magical Explosion");
            GUILayout.Label("2 - Crystallize Terrain");
            GUILayout.Label("3 - Faction Influence");
            GUILayout.Label("4 - Natural Disaster");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // Public API
        public ChunkManager GetChunkManager() => chunkManager;
        public WorldEventManager GetWorldEventManager() => worldEventManager;
    }
}
