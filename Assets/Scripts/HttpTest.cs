using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class PokemonAppManager : MonoBehaviour
{
    [Header("APIs Config")]
    [SerializeField] private string usersApiUrl = "https://my-json-server.typicode.com/Nicofon1/SII_entrega1/users";
    [SerializeField] private string pokeApiUrl = "https://pokeapi.co/api/v2/pokemon/";

    [Header("Pantallas (Menús)")]
    [SerializeField] private GameObject menuPrincipal;
    [SerializeField] private GameObject menuCards;

    [Header("Menu Principal - Entrenadores")]
    [SerializeField] private TrainerCardUI[] trainerCards;

    [Header("Menu Cards - Visualización de Deck")]
    [SerializeField] private PokemonCardUI leftCard;    // Card (4)
    [SerializeField] private PokemonCardUI centerCard;  // Card (5)
    [SerializeField] private PokemonCardUI rightCard;   // Card (3)
    [SerializeField] private Button btnPrev;            // Botón Flecha Izquierda
    [SerializeField] private Button btnNext;            // Botón Flecha Derecha
    [SerializeField] private Button btnBackToTrainers;  // Botón para volver

    private List<UserInfo> trainersList = new List<UserInfo>();
    private UserInfo selectedTrainer;
    private int currentDeckCenterIndex = 0;

    void Start()
    {
        if (btnPrev != null) btnPrev.onClick.AddListener(PrevPokemon);
        if (btnNext != null) btnNext.onClick.AddListener(NextPokemon);
        if (btnBackToTrainers != null) btnBackToTrainers.onClick.AddListener(ShowMenuPrincipal);

        ShowMenuPrincipal();
        StartCoroutine(FetchUsers());
    }

    #region Gestión de Pantallas

    public void ShowMenuPrincipal()
    {
        if (menuPrincipal != null) menuPrincipal.SetActive(true);
        if (menuCards != null) menuCards.SetActive(false);
    }

    public void ShowMenuCards(UserInfo trainer)
    {
        selectedTrainer = trainer;
        currentDeckCenterIndex = 0;

        if (menuPrincipal != null) menuPrincipal.SetActive(false);
        if (menuCards != null) menuCards.SetActive(true);

        UpdateDeckView();
    }

    #endregion

    #region Peticiones API - Usuarios (Entrenadores)

    IEnumerator FetchUsers()
    {
        Debug.Log($"<color=cyan>[API REQUEST]</color> Consultando entrenadores en: {usersApiUrl}");

        using (UnityWebRequest req = UnityWebRequest.Get(usersApiUrl))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[ERROR API USUARIOS]</color> Código: {req.responseCode} | Detalle: {req.error} | URL: {usersApiUrl}");
            }
            else
            {
                Debug.Log($"<color=green>[OK API USUARIOS]</color> Datos recibidos: {req.downloadHandler.text}");

                string jsonWrapped = "{\"users\":" + req.downloadHandler.text + "}";
                UserListWrapper wrapper = JsonUtility.FromJson<UserListWrapper>(jsonWrapped);
                trainersList = new List<UserInfo>(wrapper.users);

                SetupTrainerCardsUI();
            }
        }
    }

    private void SetupTrainerCardsUI()
    {
        for (int i = 0; i < trainerCards.Length; i++)
        {
            if (i < trainersList.Count)
            {
                UserInfo trainer = trainersList[i];
                trainerCards[i].cardRoot.SetActive(true);

                // Asignar Nombre y Región
                if (trainerCards[i].nameText != null)
                    trainerCards[i].nameText.text = trainer.username;

                if (trainerCards[i].regionText != null)
                    trainerCards[i].regionText.text = trainer.region;

                // Cargar imagen del entrenador
                StartCoroutine(DownloadImage(trainer.img, trainerCards[i].avatarImage, $"Entrenador ({trainer.username})"));

                // Configurar click
                trainerCards[i].actionButton.onClick.RemoveAllListeners();
                trainerCards[i].actionButton.onClick.AddListener(() => ShowMenuCards(trainer));
            }
            else
            {
                trainerCards[i].cardRoot.SetActive(false);
            }
        }
    }

    #endregion

    #region Peticiones API - Pokémon & Deck

    private void UpdateDeckView()
    {
        if (selectedTrainer == null || selectedTrainer.deck == null || selectedTrainer.deck.Length == 0)
        {
            Debug.LogWarning("[DECK] El entrenador seleccionado no tiene deck configurado.");
            return;
        }

        int deckCount = selectedTrainer.deck.Length;
        int leftIdx = (currentDeckCenterIndex - 1 + deckCount) % deckCount;
        int centerIdx = currentDeckCenterIndex;
        int rightIdx = (currentDeckCenterIndex + 1) % deckCount;

        StartCoroutine(FetchAndDisplayPokemon(selectedTrainer.deck[leftIdx], leftCard));
        StartCoroutine(FetchAndDisplayPokemon(selectedTrainer.deck[centerIdx], centerCard));
        StartCoroutine(FetchAndDisplayPokemon(selectedTrainer.deck[rightIdx], rightCard));
    }

    public void NextPokemon()
    {
        if (selectedTrainer == null) return;
        currentDeckCenterIndex = (currentDeckCenterIndex + 1) % selectedTrainer.deck.Length;
        UpdateDeckView();
    }

    public void PrevPokemon()
    {
        if (selectedTrainer == null) return;
        currentDeckCenterIndex = (currentDeckCenterIndex - 1 + selectedTrainer.deck.Length) % selectedTrainer.deck.Length;
        UpdateDeckView();
    }

    IEnumerator FetchAndDisplayPokemon(int pokemonId, PokemonCardUI uiCard)
    {
        string url = pokeApiUrl + pokemonId;

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[ERROR POKEAPI]</color> ID: {pokemonId} | Código: {req.responseCode} | Detalle: {req.error}");
            }
            else
            {
                PokemonData data = JsonUtility.FromJson<PokemonData>(req.downloadHandler.text);

                // 1. Nombre
                if (uiCard.nameText != null)
                {
                    uiCard.nameText.text = char.ToUpper(data.name[0]) + data.name.Substring(1);
                }

                // 2. "#{id} tipo: {tipos}"
                if (uiCard.infoText != null)
                {
                    List<string> typesList = new List<string>();
                    foreach (var slot in data.types)
                    {
                        if (slot != null && slot.type != null)
                        {
                            typesList.Add(slot.type.name);
                        }
                    }
                    string typesString = string.Join(" / ", typesList);
                    uiCard.infoText.text = $"#{data.id} tipo: {typesString}";
                }

                // 3. Sprite Oficial
                if (data.sprites != null && !string.IsNullOrEmpty(data.sprites.front_default))
                {
                    StartCoroutine(DownloadImage(data.sprites.front_default, uiCard.pokemonImage, $"Pokemon ({data.name})"));
                }
            }
        }
    }

    #endregion

    #region Utilidad de Descarga de Imágenes

    IEnumerator DownloadImage(string url, Image targetImage, string tag = "Asset")
    {
        if (string.IsNullOrEmpty(url) || targetImage == null)
        {
            Debug.LogWarning($"<color=yellow>[IMAGE CANCEL]</color> URL vacía o Image component no asignado para {tag}");
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            // Evita bloqueos 403 Forbidden comunes en servidores como Wikia/Google CDN
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[ERROR IMAGEN - {tag}]</color> Status: {req.responseCode} | Error: {req.error}\nURL: {url}");
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(req);
                if (texture != null)
                {
                    Sprite newSprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    targetImage.sprite = newSprite;
                    Debug.Log($"<color=green>[OK IMAGEN]</color> Cargada correctamente: {tag}");
                }
                else
                {
                    Debug.LogError($"<color=red>[ERROR TEXTURA]</color> No se pudo convertir la respuesta en textura para {tag}");
                }
            }
        }
    }

    #endregion
}

#region Estructuras de Datos

[Serializable]
public class TrainerCardUI
{
    public GameObject cardRoot;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI regionText; // Texto secundario para la región
    public Image avatarImage;
    public Button actionButton;
}

[Serializable]
public class PokemonCardUI
{
    public GameObject cardRoot;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI infoText;   // Texto para "#{id} tipo: {tipo}"
    public Image pokemonImage;
}

[Serializable]
public class UserListWrapper
{
    public UserInfo[] users;
}

[Serializable]
public class UserInfo
{
    public int id;
    public string username;
    public string region;
    public string img;
    public int[] deck;
}

[Serializable]
public class PokemonData
{
    public int id;
    public string name;
    public PokemonSprites sprites;
    public PokemonTypeSlot[] types;
}

[Serializable]
public class PokemonTypeSlot
{
    public int slot;
    public PokemonTypeDetails type;
}

[Serializable]
public class PokemonTypeDetails
{
    public string name;
    public string url;
}

[Serializable]
public class PokemonSprites
{
    public string front_default;
}

#endregion