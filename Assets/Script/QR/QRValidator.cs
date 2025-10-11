using ZXing;
using UnityEngine;
using Unity.Netcode;

public class QRValidator : MonoBehaviour
{
    public PlayerInventory inventory;

    public void ValidateQR(string data)
    {
        CardData card = JsonUtility.FromJson<CardData>(data);
        inventory.AddCard(card);
    }
}
