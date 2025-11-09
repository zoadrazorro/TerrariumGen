using UnityEngine;
using TerrariumGen.Data;
using TerrariumGen.ChunkSystem;

namespace TerrariumGen.Systems
{
    /// <summary>
    /// Manages dynamic world events that affect chunk generation
    /// Events can be triggered by player actions or scripted events
    /// </summary>
    public class WorldEventManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ChunkManager chunkManager;

        [Header("Debug Controls")]
        [SerializeField] private bool enableDebugKeys = true;

        private void Update()
        {
            if (enableDebugKeys)
            {
                HandleDebugInput();
            }
        }

        private void HandleDebugInput()
        {
            // Debug keys to trigger events
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                TriggerMagicalExplosion(Camera.main.transform.position, 100f, 1.0f);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                TriggerCrystallizedTerrain(Camera.main.transform.position, 80f, 0.8f);
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                TriggerFactionInfluence(Camera.main.transform.position, 150f, 1.5f);
            }

            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                TriggerNaturalDisaster(Camera.main.transform.position, 120f, 1.2f);
            }
        }

        /// <summary>
        /// Trigger a magical explosion event
        /// Raises terrain and affects generation in the area
        /// </summary>
        public void TriggerMagicalExplosion(Vector3 position, float radius, float intensity)
        {
            WorldEvent worldEvent = new WorldEvent
            {
                type = WorldEvent.EventType.MagicalExplosion,
                epicenter = position,
                radius = radius,
                intensity = intensity,
                timestamp = Time.time
            };

            ApplyWorldEvent(worldEvent);

            Debug.Log($"Magical Explosion triggered at {position} with radius {radius}");
        }

        /// <summary>
        /// Trigger crystallized terrain event
        /// Creates crystalline patterns and reduces moisture
        /// </summary>
        public void TriggerCrystallizedTerrain(Vector3 position, float radius, float intensity)
        {
            WorldEvent worldEvent = new WorldEvent
            {
                type = WorldEvent.EventType.CrystalizedTerrain,
                epicenter = position,
                radius = radius,
                intensity = intensity,
                timestamp = Time.time
            };

            ApplyWorldEvent(worldEvent);

            Debug.Log($"Crystallized Terrain triggered at {position} with radius {radius}");
        }

        /// <summary>
        /// Trigger faction influence event
        /// Increases settlement generation chance in the area
        /// </summary>
        public void TriggerFactionInfluence(Vector3 position, float radius, float intensity)
        {
            WorldEvent worldEvent = new WorldEvent
            {
                type = WorldEvent.EventType.FactionInfluence,
                epicenter = position,
                radius = radius,
                intensity = intensity,
                timestamp = Time.time
            };

            ApplyWorldEvent(worldEvent);

            Debug.Log($"Faction Influence triggered at {position} with radius {radius}");
        }

        /// <summary>
        /// Trigger natural disaster event
        /// Creates chaotic terrain changes
        /// </summary>
        public void TriggerNaturalDisaster(Vector3 position, float radius, float intensity)
        {
            WorldEvent worldEvent = new WorldEvent
            {
                type = WorldEvent.EventType.NaturalDisaster,
                epicenter = position,
                radius = radius,
                intensity = intensity,
                timestamp = Time.time
            };

            ApplyWorldEvent(worldEvent);

            Debug.Log($"Natural Disaster triggered at {position} with radius {radius}");
        }

        private void ApplyWorldEvent(WorldEvent worldEvent)
        {
            if (chunkManager == null)
            {
                Debug.LogError("ChunkManager not assigned to WorldEventManager!");
                return;
            }

            // Pass event to chunk manager
            chunkManager.TriggerWorldEvent(worldEvent);

            // Visual feedback (optional)
            CreateEventVisual(worldEvent);
        }

        private void CreateEventVisual(WorldEvent worldEvent)
        {
            // Create a temporary visual effect at the event location
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.position = worldEvent.epicenter;
            marker.transform.localScale = Vector3.one * (worldEvent.radius * 0.1f);

            // Color based on event type
            Renderer renderer = marker.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Standard"));

            switch (worldEvent.type)
            {
                case WorldEvent.EventType.MagicalExplosion:
                    mat.color = new Color(1f, 0.3f, 1f, 0.5f);
                    break;

                case WorldEvent.EventType.CrystalizedTerrain:
                    mat.color = new Color(0.3f, 0.8f, 1f, 0.5f);
                    break;

                case WorldEvent.EventType.FactionInfluence:
                    mat.color = new Color(1f, 0.8f, 0.2f, 0.5f);
                    break;

                case WorldEvent.EventType.NaturalDisaster:
                    mat.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                    break;
            }

            renderer.material = mat;

            // Destroy after 5 seconds
            Destroy(marker, 5f);
        }

        // Public API for scripted events
        public void TriggerCustomEvent(WorldEvent.EventType type, Vector3 position, float radius, float intensity)
        {
            switch (type)
            {
                case WorldEvent.EventType.MagicalExplosion:
                    TriggerMagicalExplosion(position, radius, intensity);
                    break;

                case WorldEvent.EventType.CrystalizedTerrain:
                    TriggerCrystallizedTerrain(position, radius, intensity);
                    break;

                case WorldEvent.EventType.FactionInfluence:
                    TriggerFactionInfluence(position, radius, intensity);
                    break;

                case WorldEvent.EventType.NaturalDisaster:
                    TriggerNaturalDisaster(position, radius, intensity);
                    break;
            }
        }
    }
}
