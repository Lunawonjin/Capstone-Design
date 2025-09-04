using UnityEngine;

public class LayerAdjuster : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Transform closestNpc;
    private SpriteRenderer npcSpriteRenderer;

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        FindClosestNPC();

        if (closestNpc == null) return;

        npcSpriteRenderer = closestNpc.GetComponent<SpriteRenderer>();
        if (npcSpriteRenderer == null) return;

        if (transform.position.y < closestNpc.position.y)
        {
            spriteRenderer.sortingOrder = npcSpriteRenderer.sortingOrder + 1;
        }
        else
        {
            spriteRenderer.sortingOrder = npcSpriteRenderer.sortingOrder - 1;
        }
    }

    private void FindClosestNPC()
    {
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");

        float minDistance = float.MaxValue;
        Transform nearest = null;

        foreach (var npc in npcs)
        {
            float distance = Vector2.Distance(transform.position, npc.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = npc.transform;
            }
        }

        closestNpc = nearest;
    }
}
