// CardCollection.cs - YENÝ
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Card Collection", menuName = "Card/Card Collection")]
public class CardCollection : ScriptableObject
{
    public List<CardData> allCards;
}