using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class PokemonAppManager : MonoBehaviour
{
    public enum PixelArtMode
    {
        /// <summary>Recorta el transparente, reescala x N entero (nearest) y ajusta al slot. Llena bien el espacio y se ve nítido.</summary>
        UpscaleAndFit,
        /// <summary>Escala entera exacta en píxeles de PANTALLA. 100% nítido, pero puede quedar más pequeño que el slot.</summary>
        IntegerScale
    }

    /// <summary>
    /// Set de sprites de PokeAPI. El número es la resolución del lienzo:
    /// a menor resolución, píxeles más gordos en pantalla.
    /// </summary>
    public enum PokemonSpriteStyle
    {
        Gen1RedBlue,              // 48px - máximo pixelado, solo 4 colores, FONDO BLANCO
        Gen2Crystal,              // 48px - máximo pixelado, FONDO BLANCO
        Gen3Emerald,              // 64px - RECOMENDADO: chunky, paleta completa, alfa limpio
        Gen3FireRedLeafGreen,     // 64px - igual que Emerald pero solo Kanto (1-386)
        Gen4DiamondPearl,         // 80px
        Gen4HeartGoldSoulSilver,  // 80px - el que mejor combina con los entrenadores de Showdown
        Gen5BlackWhite,           // 96px - equivale al front_default de PokeAPI
        OfficialArtwork           // 475px - NO es pixel art
    }

    [Header("APIs Config")]
    [SerializeField] private string usersApiUrl = "https://my-json-server.typicode.com/Nicofon1/SII_entrega1/users";
    [SerializeField] private string pokeApiUrl = "https://pokeapi.co/api/v2/pokemon/";

    [Header("Estilo de Sprites")]
    [Tooltip("Set de sprites para los Pokémon. Menor resolución = píxeles más gordos.")]
    [SerializeField] private PokemonSpriteStyle pokemonSpriteStyle = PokemonSpriteStyle.Gen3Emerald;

    [Tooltip("Cómo se adapta el pixel art al tamaño del contenedor.")]
    [SerializeField] private PixelArtMode pixelArtMode = PixelArtMode.UpscaleAndFit;

    [Tooltip("Recorta el borde transparente del sprite. Los sprites de Showdown y PokeAPI traen mucho relleno vacío.")]
    [SerializeField] private bool trimTransparentBorders = true;

    [Tooltip("Detecta y borra fondos sólidos opacos (necesario para los sets Gen I y Gen II, que vienen con fondo blanco).")]
    [SerializeField] private bool autoRemoveSolidBackground = true;

    [Tooltip("Fuerza el sprite a esta resolución máxima antes de ampliarlo. 0 = desactivado. Baja el valor para pixelar más (prueba 32 o 24).")]
    [Range(0, 128)]
    [SerializeField] private int forcePixelSize = 0;

    [Tooltip("Factor máximo de reescalado. Más alto = textura más pesada en memoria.")]
    [Range(1, 16)]
    [SerializeField] private int maxUpscaleFactor = 10;

    [Tooltip("ON = bordes de píxel totalmente duros. OFF = suaviza mínimamente el ajuste final al contenedor.")]
    [SerializeField] private bool sharpEdges = true;

    private const string SPRITES_ROOT =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/";

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

    // Texturas ya descargadas y preprocesadas (evita re-descargar al navegar con las flechas)
    private Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
    // Sprites ya construidos, indexados por url + parámetros de escalado
    private Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    // Tamaño original del Image en el Inspector (SetNativeSize lo sobrescribe)
    private Dictionary<Image, Vector2> originalSlotSizes = new Dictionary<Image, Vector2>();

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
                yield break;
            }

            Debug.Log($"<color=green>[OK API USUARIOS]</color> Datos recibidos: {req.downloadHandler.text}");

            // La DB propia devuelve {"users":[...]}, pero my-json-server con /users
            // devuelve el array pelado. Se soportan los dos formatos.
            string raw = req.downloadHandler.text.TrimStart();
            string json = raw.StartsWith("[") ? "{\"users\":" + raw + "}" : raw;

            UserListWrapper wrapper = JsonUtility.FromJson<UserListWrapper>(json);

            if (wrapper == null || wrapper.users == null)
            {
                Debug.LogError("<color=red>[PARSE]</color> No se pudo leer la lista de usuarios del JSON.");
                yield break;
            }

            trainersList = new List<UserInfo>(wrapper.users);
            SetupTrainerCardsUI();
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

                if (trainerCards[i].nameText != null)
                    trainerCards[i].nameText.text = trainer.username;

                if (trainerCards[i].regionText != null)
                    trainerCards[i].regionText.text = trainer.region;

                // Los sprites de Showdown son pixel art con mucho relleno transparente
                StartCoroutine(DownloadImage(
                    trainer.img,
                    trainerCards[i].avatarImage,
                    $"Entrenador ({trainer.username})",
                    pixelArt: true));

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

    /// <summary>Construye la URL del sprite según el estilo elegido.</summary>
    private string BuildSpriteUrl(int id, PokemonSpriteStyle style)
    {
        switch (style)
        {
            case PokemonSpriteStyle.Gen1RedBlue:
                return $"{SPRITES_ROOT}versions/generation-i/red-blue/{id}.png";
            case PokemonSpriteStyle.Gen2Crystal:
                return $"{SPRITES_ROOT}versions/generation-ii/crystal/{id}.png";
            case PokemonSpriteStyle.Gen3Emerald:
                return $"{SPRITES_ROOT}versions/generation-iii/emerald/{id}.png";
            case PokemonSpriteStyle.Gen3FireRedLeafGreen:
                return $"{SPRITES_ROOT}versions/generation-iii/firered-leafgreen/{id}.png";
            case PokemonSpriteStyle.Gen4DiamondPearl:
                return $"{SPRITES_ROOT}versions/generation-iv/diamond-pearl/{id}.png";
            case PokemonSpriteStyle.Gen4HeartGoldSoulSilver:
                return $"{SPRITES_ROOT}versions/generation-iv/heartgold-soulsilver/{id}.png";
            case PokemonSpriteStyle.OfficialArtwork:
                return $"{SPRITES_ROOT}other/official-artwork/{id}.png";
            case PokemonSpriteStyle.Gen5BlackWhite:
            default:
                return $"{SPRITES_ROOT}{id}.png";
        }
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
                yield break;
            }

            PokemonData data = JsonUtility.FromJson<PokemonData>(req.downloadHandler.text);

            // 1. Nombre
            if (uiCard.nameText != null)
                uiCard.nameText.text = char.ToUpper(data.name[0]) + data.name.Substring(1);

            // 2. "#{id} tipo: {tipos}"
            if (uiCard.infoText != null)
            {
                List<string> typesList = new List<string>();
                foreach (var slot in data.types)
                {
                    if (slot != null && slot.type != null)
                        typesList.Add(slot.type.name);
                }
                uiCard.infoText.text = $"#{data.id} tipo: {string.Join(" / ", typesList)}";
            }

            // 3. Imagen
            // Nota: JsonUtility no puede mapear las claves con guion del JSON de PokeAPI
            // ("official-artwork", "firered-leafgreen"), así que las URLs se arman por id.
            string styledUrl = BuildSpriteUrl(data.id, pokemonSpriteStyle);

            // Fallback: los sets de solo-Kanto no tienen a todos los Pokémon
            // (p. ej. Togepi #175 no existe en red-blue ni en FireRed/LeafGreen)
            string fallback = data.sprites != null ? data.sprites.front_default : null;

            StartCoroutine(DownloadImage(
                styledUrl,
                uiCard.pokemonImage,
                $"Pokemon ({data.name})",
                pixelArt: pokemonSpriteStyle != PokemonSpriteStyle.OfficialArtwork,
                fallbackUrl: fallback));
        }
    }

    #endregion

    #region Descarga de Imágenes

    IEnumerator DownloadImage(
        string url,
        Image targetImage,
        string tag = "Asset",
        bool pixelArt = false,
        string fallbackUrl = null)
    {
        if (targetImage == null)
        {
            Debug.LogError($"<color=red>[INSPECTOR VACÍO]</color> Falta asignar el componente 'Image' en el Inspector para: {tag}");
            yield break;
        }

        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning($"<color=yellow>[JSON VACÍO]</color> La URL de imagen llegó vacía o nula desde el JSON para: {tag}");
            yield break;
        }

        // Textura ya en cache
        if (textureCache.TryGetValue(url, out Texture2D cachedTex))
        {
            BuildAndApply(url, cachedTex, targetImage, pixelArt);
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[ERROR DESCARGA - {tag}]</color> Status: {req.responseCode} | Error: {req.error}\nURL: {url}");

                if (!string.IsNullOrEmpty(fallbackUrl))
                {
                    Debug.LogWarning($"<color=yellow>[FALLBACK - {tag}]</color> Ese sprite no existe en este set. Usando: {fallbackUrl}");
                    yield return StartCoroutine(DownloadImage(fallbackUrl, targetImage, tag + " (fallback)", pixelArt));
                }
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(req);
            if (texture == null) yield break;

            // Los sets Gen I y Gen II vienen con fondo blanco opaco en lugar de alfa
            if (pixelArt && autoRemoveSolidBackground && !HasTransparency(texture))
            {
                Debug.Log($"<color=cyan>[FONDO SÓLIDO]</color> Detectado en {tag}, recortando por color de borde.");
                texture = RemoveBorderBackground(texture);
            }

            textureCache[url] = texture;
            BuildAndApply(url, texture, targetImage, pixelArt);
        }
    }

    private void BuildAndApply(string url, Texture2D source, Image targetImage, bool pixelArt)
    {
        if (!pixelArt)
        {
            // Artwork de alta resolución: suavizado normal, sin trucos
            source.filterMode = FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;

            string key = url + "|smooth";
            if (!spriteCache.TryGetValue(key, out Sprite smooth))
            {
                smooth = MakeSprite(source);
                spriteCache[key] = smooth;
            }

            targetImage.sprite = smooth;
            targetImage.rectTransform.localScale = Vector3.one;
            targetImage.preserveAspect = true;
            return;
        }

        if (pixelArtMode == PixelArtMode.IntegerScale)
            ApplyIntegerScale(url, source, targetImage);
        else
            ApplyUpscaleAndFit(url, source, targetImage);
    }

    #endregion

    #region Modo A: Upscale entero + ajuste al slot (recomendado)

    /// <summary>
    /// Recorta el borde transparente, opcionalmente baja la resolución para pixelar más,
    /// reescala por un factor ENTERO con nearest-neighbor (bloques perfectos) y deja que
    /// el Image lo ajuste al contenedor. No toca el RectTransform, así que convive bien
    /// con Layout Groups.
    /// </summary>
    private void ApplyUpscaleAndFit(string url, Texture2D source, Image img)
    {
        RectInt crop = trimTransparentBorders
            ? GetOpaqueBounds(source)
            : new RectInt(0, 0, source.width, source.height);

        // Bajada de resolución opcional, para engordar los píxeles a voluntad
        int baseW = crop.width;
        int baseH = crop.height;

        if (forcePixelSize > 0 && Mathf.Max(baseW, baseH) > forcePixelSize)
        {
            float ratio = (float)forcePixelSize / Mathf.Max(baseW, baseH);
            baseW = Mathf.Max(1, Mathf.RoundToInt(baseW * ratio));
            baseH = Mathf.Max(1, Mathf.RoundToInt(baseH * ratio));
        }

        int factor = ComputeUpscaleFactor(img, Mathf.Max(baseW, baseH));

        string key = $"{url}|{baseW}x{baseH}|up{factor}|trim{(trimTransparentBorders ? 1 : 0)}|sharp{(sharpEdges ? 1 : 0)}";
        if (!spriteCache.TryGetValue(key, out Sprite sprite))
        {
            // Paso 1: recorte (+ bajada de resolución si aplica)
            Texture2D lowRes = ResampleNearest(source, crop, baseW, baseH);

            // Paso 2: ampliación por factor entero -> bloques perfectamente cuadrados
            Texture2D scaled = factor > 1 ? UpscaleInteger(lowRes, factor) : lowRes;

            scaled.filterMode = sharpEdges ? FilterMode.Point : FilterMode.Bilinear;
            scaled.wrapMode = TextureWrapMode.Clamp;

            sprite = MakeSprite(scaled);
            spriteCache[key] = sprite;
        }

        img.sprite = sprite;
        img.rectTransform.localScale = Vector3.one;
        img.preserveAspect = true;
    }

    private int ComputeUpscaleFactor(Image img, int spriteMaxDim)
    {
        if (spriteMaxDim <= 0) return 1;

        RectTransform rt = img.rectTransform;

        // lossyScale del padre incluye el scaleFactor del Canvas Scaler
        float parentScale = 1f;
        if (rt.parent != null) parentScale = Mathf.Abs(rt.parent.lossyScale.x);
        if (parentScale <= 0.0001f) parentScale = 1f;

        Vector2 slot = rt.rect.size;
        float availableScreenPx = Mathf.Min(slot.x, slot.y) * parentScale;

        // Si el layout aún no resolvió el rect, usar un valor razonable
        if (availableScreenPx <= 1f) availableScreenPx = 256f;

        int factor = Mathf.CeilToInt(availableScreenPx / spriteMaxDim);
        return Mathf.Clamp(factor, 1, maxUpscaleFactor);
    }

    #endregion

    #region Modo B: Escala entera exacta en pantalla

    /// <summary>
    /// Escala el sprite de forma que cada píxel del original ocupe un número ENTERO
    /// de píxeles de pantalla. Compensa el scaleFactor del Canvas Scaler y cualquier
    /// localScale de los padres. Nitidez perfecta, pero puede no llenar el contenedor.
    /// </summary>
    private void ApplyIntegerScale(string url, Texture2D source, Image img)
    {
        source.filterMode = FilterMode.Point;
        source.wrapMode = TextureWrapMode.Clamp;

        RectInt crop = trimTransparentBorders
            ? GetOpaqueBounds(source)
            : new RectInt(0, 0, source.width, source.height);

        string key = $"{url}|point|trim{(trimTransparentBorders ? 1 : 0)}";
        if (!spriteCache.TryGetValue(key, out Sprite sprite))
        {
            sprite = Sprite.Create(
                source,
                new Rect(crop.x, crop.y, crop.width, crop.height),
                new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            spriteCache[key] = sprite;
        }

        img.sprite = sprite;

        RectTransform rt = img.rectTransform;

        // Recuperar (o cachear) el tamaño que tenía el Image en el Inspector,
        // porque SetNativeSize() lo sobrescribe en la primera carga.
        if (!originalSlotSizes.TryGetValue(img, out Vector2 slot) || slot.x <= 0f || slot.y <= 0f)
        {
            slot = rt.rect.size;
            if ((slot.x <= 0f || slot.y <= 0f) && rt.parent is RectTransform parentRt)
                slot = parentRt.rect.size;

            originalSlotSizes[img] = slot;
        }

        img.preserveAspect = false;
        img.SetNativeSize();

        float parentScale = 1f;
        if (rt.parent != null) parentScale = Mathf.Abs(rt.parent.lossyScale.x);
        if (parentScale <= 0.0001f) parentScale = 1f;

        float availableScreenPx = Mathf.Min(slot.x, slot.y) * parentScale;
        float maxDim = Mathf.Max(crop.width, crop.height);

        int screenScale = 1;
        if (availableScreenPx > 0f && maxDim > 0f)
            screenScale = Mathf.Max(1, Mathf.FloorToInt(availableScreenPx / maxDim));

        // localScale tal que la escala resultante EN PANTALLA sea exactamente screenScale
        rt.localScale = Vector3.one * (screenScale / parentScale);
    }

    #endregion

    #region Utilidades de Textura

    private Sprite MakeSprite(Texture2D tex)
    {
        // FullRect evita que Unity recorte la malla y desalinee medio píxel
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
    }

    private bool HasTransparency(Texture2D tex)
    {
        Color32[] px = SafeGetPixels(tex);
        if (px == null) return true;

        for (int i = 0; i < px.Length; i++)
            if (px[i].a < 250) return true;

        return false;
    }

    /// <summary>
    /// Borra el fondo sólido de los sprites que no traen alfa (Gen I y Gen II).
    /// Usa flood fill desde los bordes, así que un color que también aparezca
    /// dentro del Pokémon no se borra por error.
    /// </summary>
    private Texture2D RemoveBorderBackground(Texture2D src, int tolerance = 12)
    {
        Color32[] px = SafeGetPixels(src);
        if (px == null) return src;

        int w = src.width, h = src.height;
        Color32 bg = px[0]; // esquina inferior izquierda
        bool[] visited = new bool[px.Length];
        Stack<int> stack = new Stack<int>();

        // Sembrar desde los cuatro bordes
        for (int x = 0; x < w; x++)
        {
            stack.Push(x);
            stack.Push((h - 1) * w + x);
        }
        for (int y = 0; y < h; y++)
        {
            stack.Push(y * w);
            stack.Push(y * w + (w - 1));
        }

        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            if (idx < 0 || idx >= px.Length || visited[idx]) continue;

            Color32 c = px[idx];
            if (Mathf.Abs(c.r - bg.r) > tolerance ||
                Mathf.Abs(c.g - bg.g) > tolerance ||
                Mathf.Abs(c.b - bg.b) > tolerance) continue;

            visited[idx] = true;
            px[idx] = new Color32(c.r, c.g, c.b, 0);

            int x = idx % w, y = idx / w;
            if (x > 0) stack.Push(idx - 1);
            if (x < w - 1) stack.Push(idx + 1);
            if (y > 0) stack.Push(idx - w);
            if (y < h - 1) stack.Push(idx + w);
        }

        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels32(px);
        result.Apply();
        return result;
    }

    /// <summary>Devuelve el rectángulo que contiene los píxeles no transparentes.</summary>
    private RectInt GetOpaqueBounds(Texture2D tex, byte alphaThreshold = 8)
    {
        Color32[] px = SafeGetPixels(tex);
        if (px == null) return new RectInt(0, 0, tex.width, tex.height);

        int w = tex.width, h = tex.height;
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (px[row + x].a > alphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0) return new RectInt(0, 0, w, h); // todo transparente
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Recorta una región y la remuestrea a destW x destH por vecino más cercano.</summary>
    private Texture2D ResampleNearest(Texture2D src, RectInt crop, int destW, int destH)
    {
        Color32[] srcPix = SafeGetPixels(src);
        int sw = src.width;

        Color32[] dstPix = new Color32[destW * destH];

        for (int y = 0; y < destH; y++)
        {
            int sy = crop.y + Mathf.Min(crop.height - 1, y * crop.height / destH);
            int srcRow = sy * sw;
            int dstRow = y * destW;

            for (int x = 0; x < destW; x++)
            {
                int sx = crop.x + Mathf.Min(crop.width - 1, x * crop.width / destW);
                dstPix[dstRow + x] = srcPix[srcRow + sx];
            }
        }

        Texture2D result = new Texture2D(destW, destH, TextureFormat.RGBA32, false);
        result.SetPixels32(dstPix);
        result.Apply();
        return result;
    }

    /// <summary>Amplía por factor entero: cada píxel se convierte en un bloque de factor x factor.</summary>
    private Texture2D UpscaleInteger(Texture2D src, int factor)
    {
        Color32[] srcPix = src.GetPixels32();
        int sw = src.width;
        int w = sw * factor;
        int h = src.height * factor;

        Color32[] dstPix = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            int srcRow = (y / factor) * sw;
            int dstRow = y * w;

            for (int x = 0; x < w; x++)
                dstPix[dstRow + x] = srcPix[srcRow + (x / factor)];
        }

        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels32(dstPix);
        result.Apply();
        return result;
    }

    private Color32[] SafeGetPixels(Texture2D tex)
    {
        try { return tex.GetPixels32(); }
        catch (UnityException)
        {
            Debug.LogWarning("[TEXTURA] No es legible, se omite el procesado de píxeles.");
            return null;
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