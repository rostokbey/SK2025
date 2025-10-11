using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStoryDatabase", menuName = "Card/Story Database")]
public class CharacterStoryDatabase : ScriptableObject
{
    [System.Serializable]
    public class StoryEntry
    {
        public string cardName; // QR'daki name (örnek: "Sonsuz_Savasci")
        [TextArea(5, 10)]
        public string story;
    }

    public List<StoryEntry> stories;

    public string GetStory(string cardName)
    {
        foreach (var entry in stories)
        {
            if (entry.cardName == cardName)
                return entry.story;
        }
        return "Bu karakterin hikayesi bulunamadý.";
    }
}
