using System.Collections.Generic;
using UnityEngine;

public static class CardDatabase
{
    public static List<CardData> AllCards = new List<CardData>()
    {
        new CardData("S01-001", "S�rpriz Kutusu", "�ansa ba�l� d��man� �ld�r�r, olmazsa HP %50 kaybeder") {
            type = CardData.CardType.Special, duration = 1
        },
        new CardData("S02-001", "Tuzak", "Hedefi 3 tur zindana atar") {
            type = CardData.CardType.Debuff, duration = 3, banish = true
        },
        new CardData("S03-001", "Klonlama", "2�6 klon olu�turur (3 tur)") {
            type = CardData.CardType.Special, duration = 3, summonCount = 3
        },
        new CardData("S04-001", "G��lendirme", "Hasar� 2 tur %50 art�r�r") {
            type = CardData.CardType.Buff, duration = 2, damage = 50
        },
        new CardData("S05-001", "Silah", "Ek silah g�c� kazand�r�r") {
            type = CardData.CardType.Buff, damage = 30
        },
        new CardData("S06-001", "Sihir", "Do�rudan b�y� hasar� verir") {
            type = CardData.CardType.Attack, damage = 40
        },
        new CardData("S07-001", "Yans�tma", "Gelen hasar� yans�t�r") {
            type = CardData.CardType.Defense, reflect = 1.0f, duration = 1
        },
        new CardData("S08-001", "Kritik Art�r�c�", "Kritik hasar� %100 art�r�r (1 tur)") {
            type = CardData.CardType.Buff, critDamage = 2.0f, duration = 1
        },
        new CardData("S09-001", "Kritik Vuru�", "Kritik �ans� %30 art�r�r (2 tur)") {
            type = CardData.CardType.Buff, critChance = 0.3f, duration = 2
        },
        new CardData("S10-001", "Kombine", "2 karakterin g��lerini birle�tirir") {
            type = CardData.CardType.Special, combine = true
        },
        new CardData("S11-001", "Engelleme", "Rakibin s�radaki kart�n� iptal eder") {
            type = CardData.CardType.Utility, duration = 1
        },
        new CardData("S12-001", "Susturma", "Pasif yetenekleri 2 tur kapat�r") {
            type = CardData.CardType.Debuff, duration = 2
        },
        new CardData("S13-001", "Savunma Kalkan�", "2 tur %50 hasar azalt�r") {
            type = CardData.CardType.Defense, duration = 2
        },
        new CardData("S14-001", "H�z", "S�ras�n� �ne �eker") {
            type = CardData.CardType.Buff, duration = 1
        },
        new CardData("S15-001", "�ifa", "HP %40 yeniler") {
            type = CardData.CardType.Buff, heal = 40
        },
        new CardData("S16-001", "Tak�m Ruhu", "T�m karakterlere %20 buff (2 tur)") {
            type = CardData.CardType.Buff, duration = 2
        },
        new CardData("S17-001", "Payla��lan Ac�", "Hasar t�m d��manlara b�l�n�r") {
            type = CardData.CardType.Special
        },
        new CardData("S18-001", "Zaman Durdurucu", "Tüm düşmanlar 1 tur boyunca saldıramaz")
        {
           type = CardData.CardType.Debuff,
           duration = 1,
           disableAttack = true
        },

        new CardData("S19-001", "Manevi Güç", "Ölen karakter ruh olarak d�ner") {
            type = CardData.CardType.Special, duration = 1
        },
        new CardData("S20-001", "Zehir", "Her tur %10 HP kaybettirir (3 tur)") {
            type = CardData.CardType.Debuff, duration = 3
        },
        new CardData("S21-001", "Yanma", "3 tur boyunca ate� hasar� verir") {
            type = CardData.CardType.Debuff, duration = 3, damage = 10
        },
        new CardData("S22-001", "Donma", "1 tur boyunca dondurur") {
            type = CardData.CardType.Debuff, duration = 1, stun = true
        },
        new CardData("S23-001", "Deprem", "T�m d��manlara alan hasar� verir") {
            type = CardData.CardType.Attack, damage = 20
        },
        new CardData("S24-001", "R�zgar Darbesi", "Hedefin s�ras�n� bozar") {
            type = CardData.CardType.Utility, duration = 1
        },
        new CardData("S25-001", "Karanl�k S�zle�me", "HP %30 feda, %100 ek hasar") {
            type = CardData.CardType.Special, damage = 100
        },
    };
}