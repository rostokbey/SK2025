// Assets/Script/Sezon/SeasonSettleSummary.cs
using System;

[Serializable]
public struct SeasonSettleSummary
{
    public bool hadChampion;
    public string championProfileId;
    public string championName;
    public int participants;

    public int medalsDelta;   // bu sezonda verilen madalya (genelde 1 veya 0)
    public int championMedalsAfter; // þampiyonun yeni toplam madalyasý

    public int resetPoints;   // reset sonrasý baþlangýç puaný (SeasonManager.startPoints)
    public string note;          // debug/mesaj
}
