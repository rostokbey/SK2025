using System;
using System.Collections.Generic;

[Serializable]
public class DeckSaveData
{
    // 5 destenin her biri için kart ID'lerini tutan listeler
    public List<string> deck1_CardIds = new List<string>();
    public List<string> deck2_CardIds = new List<string>();
    public List<string> deck3_CardIds = new List<string>();
    public List<string> deck4_CardIds = new List<string>();
    public List<string> deck5_CardIds = new List<string>();
}