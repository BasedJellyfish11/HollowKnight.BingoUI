using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using GlobalEnums;
using JetBrains.Annotations;
using Modding;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Logger = Modding.Logger;
using ModCommon.Util;

namespace BingoUI
{
    public class BingoUI : Mod, ITogglableMod
    {
        private static readonly Dictionary<string, CanvasGroup> CanvasGroups = new Dictionary<string, CanvasGroup>();
        private static readonly Dictionary<string, Text> TextPanels = new Dictionary<string, Text>();
        private static readonly Dictionary<string, DateTime> NextCanvasFade = new Dictionary<string, DateTime>();

        private static readonly string[] mapPinsStrings =
        {
            nameof(PlayerData.hasPinBench),
            nameof(PlayerData.hasPinCocoon),
            nameof(PlayerData.hasPinGhost),
            nameof(PlayerData.hasPinShop),
            nameof(PlayerData.hasPinSpa),
            nameof(PlayerData.hasPinStag),
            nameof(PlayerData.hasPinTram),
            nameof(PlayerData.hasPinDreamPlant)
        };

        private GameObject _canvas;
        
        private NonBouncer _coroutineStarter;

        public override ModSettings SaveSettings
        {
            get => _settings;
            set => _settings = value as SaveSettings;
        }

        public override ModSettings GlobalSettings
        {
            get => _globalSettings; 
            set => _globalSettings = value as GlobalSettings;
        }

        private SaveSettings _settings = new SaveSettings();
        private GlobalSettings _globalSettings = new GlobalSettings();

        private RandoPlandoCompatibility _randoPlandoCompatibility;

        private ILHook _dreamPlantHook;

        public override void Initialize()
        {
            // Hooks
            ModHooks.Instance.SetPlayerIntHook += UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook += UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += PatchCornifer;
            On.UIManager.GoToPauseMenu += OnPause;
            On.UIManager.UIClosePauseMenu += OnUnpause;
            On.UIManager.ReturnToMainMenu += OnUnpauseQuitGame;

            // Hook rando/plando due to it not using SetInt like everything else and instead calling trinket++ etc
            _randoPlandoCompatibility = new RandoPlandoCompatibility();
            
            RandoPlandoCompatibility.OnCorniferLocation += UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation += DummyRandoAreaGrubSet;
            RandoPlandoCompatibility.OnGrubObtain += OnRandoGrubObtain;

            _dreamPlantHook = new ILHook
            (
                typeof(DreamPlant).GetNestedType("<CheckOrbs>c__Iterator0", BindingFlags.NonPublic).GetMethod("MoveNext"),
                TrackTrees
            );

            GameObject go = new GameObject();
            _coroutineStarter = go.AddComponent<NonBouncer>();
            UnityEngine.Object.DontDestroyOnLoad(go);

            Log("Creating Canvases");

            Dictionary<string, Sprite> sprites = SeanprCore.ResourceHelper.GetSprites();
            // Define anchor minimum and maximum so we can modify them in a loop and display the images systematically
            Vector2 anchorMin = new Vector2(0f, 0.01f);
            Vector2 anchorMax = new Vector2(1f/sprites.Count, 0.1f);

            // Create the canvas, make it not disappear on load
            _canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceCamera, new Vector2(1920, 1080));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);

            foreach (KeyValuePair<string, Sprite> pair in sprites)
            {
                // Get file name without extension as key
                string[] a = pair.Key.Split('.');
                string key = a[a.Length - 1];

                // Create the image
                GameObject canvasSprite = CanvasUtil.CreateImagePanel
                (
                    _canvas,
                    pair.Value,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, anchorMin, anchorMax)
                );

                // Add a canvas group so we can fade it in and out
                canvasSprite.AddComponent<CanvasGroup>();
                CanvasGroup canvasGroup = canvasSprite.GetComponent<CanvasGroup>();
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                if(!_globalSettings.alwaysDisplay)
                    canvasGroup.gameObject.SetActive(false);

                // Add the group to the map to access it easier
                CanvasGroups.Add(key, canvasGroup);


                // Create text, parented to the image so it gets centered on it
                GameObject text = CanvasUtil.CreateTextPanel
                (
                    canvasSprite,
                    "0",
                    23,
                    TextAnchor.LowerCenter,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one)
                );
                //// text.AddComponent<Outline>();
                text.GetComponent<Text>().color = Color.black;

                // Increment the anchors so the next image isn't on top. If it's all the way right, start drawing up
                Vector2 sum = anchorMax.x >= 1f ? new Vector2(0, 0.09f) : new Vector2(1f/sprites.Count,0);
                anchorMin += sum;
                anchorMax += sum;

                // Easy access to the text panel
                TextPanels.Add(key, text.GetComponent<Text>());
                
                NextCanvasFade.Add(key, DateTime.MinValue);

                Log("Canvas with key " + key + " created");
            }

            Log("Canvas creation done");
        }



        public void Unload()
        {
            ModHooks.Instance.SetPlayerIntHook -= UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook -= UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= PatchCornifer;
            On.UIManager.GoToPauseMenu -= OnPause;
            On.UIManager.UIClosePauseMenu -= OnUnpause;
            On.UIManager.ReturnToMainMenu -= OnUnpauseQuitGame;


            _randoPlandoCompatibility?.Dispose();
            
            // ReSharper disable DelegateSubtraction
            RandoPlandoCompatibility.OnCorniferLocation -= UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation -= DummyRandoAreaGrubSet;
            RandoPlandoCompatibility.OnGrubObtain -= OnRandoGrubObtain;
            // ReSharper enable DelegateSubtraction

            _dreamPlantHook?.Dispose();
            
            UnityEngine.Object.Destroy(_canvas);
        }


        private static void DummyRandoAreaGrubSet(string location)
        {
            PlayerData.instance.SetInt(nameof(PlayerData.instance.grubsCollected), PlayerData.instance.grubsCollected);
        }

        /**
         * Check if the Int that changed is something to track, and if so display the canvas and the updated number
         */
        private void UpdateIntCanvas(string intname, int value)
        {
            string text;
            PlayerData pd = PlayerData.instance;

            // Make sure to set the value
            pd.SetIntInternal(intname, value);
            
            switch (intname)
            {
                // Relics.
                case var _ when intname.StartsWith("trinket"): 
                    
                    Log("Updating " + intname);
                   
                    UpdateText(intname);

                    if(DateTime.Now < NextCanvasFade[intname])
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups[intname]));
                    NextCanvasFade[intname] = DateTime.Now.AddSeconds(0.5f);
                    
                    break;

                // Lifeblood
                case nameof(pd.healthBlue):
                    Log("Updating Lifeblood");

                    UpdateText("lifeblood");

                    if(DateTime.Now < NextCanvasFade["lifeblood"] || pd.healthBlue <= 6)
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas((CanvasGroups["lifeblood"])));
                    NextCanvasFade["lifeblood"] = DateTime.Now.AddSeconds(0.5f);

                    break;

                // eggs
                case nameof(pd.jinnEggsSold):
                case nameof(pd.rancidEggs):
                    
                    Log("Updating rancid eggs");

                    UpdateText("regg");
                    
                    if(DateTime.Now < NextCanvasFade["regg"])
                        break;
                    
                    _coroutineStarter.StartCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    NextCanvasFade["regg"] = DateTime.Now.AddSeconds(0.5f);

                    break;

                // grubs
                case nameof(pd.grubsCollected):
                
                    UpdateGrubs();

                    break;
                

                case nameof(pd.killsSlashSpider):
                
                    Log("Updating devouts");

                    UpdateText("devout");
                    
                    if(DateTime.Now < NextCanvasFade["devout"])
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["devout"]));
                    NextCanvasFade["devout"] = DateTime.Now.AddSeconds(0.5f);

                    break;
                
                
                case nameof(pd.nailSmithUpgrades):
                    if(pd.nailSmithUpgrades == 1)
                        break;
                    goto case nameof(pd.ore);
                case nameof(pd.ore):
                    Log("Updating pale ore");

                    UpdateText("ore");

                    if(DateTime.Now < NextCanvasFade["ore"])
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["ore"]));
                    NextCanvasFade["ore"] = DateTime.Now.AddSeconds(0.5f);

                    break;
                
                case nameof(pd.charmSlots):
                    Log("Updating charm notches");

                    text = TextPanels["notches"].text;
                    UpdateText("notches");
                    
                    if(DateTime.Now < NextCanvasFade["notches"] || text.Equals(TextPanels["notches"].text))
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["notches"]));
                    NextCanvasFade["notches"] = DateTime.Now.AddSeconds(0.5f);
                    break;
            }
        }

        // Grubs are annoying so they get their own method, courtesy of 56.
        private void UpdateGrubs(bool incArea = true)
        {
            PlayerData pd = PlayerData.instance;
            
            Log("Updating grubs");

            MapZone mapZone = SanitizeMapzone(GameManager.instance.sm.mapZone);

            if (incArea)
            {
                _settings.AreaGrubs[mapZone] += 1;
            }

            TextPanels["grub"].text = $"{pd.grubsCollected}({_settings.AreaGrubs[mapZone]})";
            
            if (DateTime.Now < NextCanvasFade["grub"])
                return;
            _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["grub"]));
            NextCanvasFade["grub"] = DateTime.Now.AddSeconds(0.5f);
        }

        private void UpdateBoolCanvas(string orig, bool value)
        {

            PlayerData pd = PlayerData.instance;
            string text;
            
            pd.SetBoolInternal(orig, value);

            switch (orig)
            {

                case var _ when orig.StartsWith("map"):
                    
                    Log("Updating maps");

                    text = TextPanels["maps"].text;
                    UpdateText("maps");
                    
                    if (DateTime.Now < NextCanvasFade["maps"] || text.Equals(TextPanels["maps"].text))
                        return;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["maps"]));
                    NextCanvasFade["maps"] = DateTime.Now.AddSeconds(0.5f);
                    break;
                
                case var _ when orig.StartsWith("gotCharm"):
                    
                    Log("Updating charm number");

                    text = TextPanels["charms"].text;
                    UpdateText("charms");
                    
                    if(DateTime.Now < NextCanvasFade["charms"] || text.Equals(TextPanels["charms"].text))
                        return;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["charms"]));
                    NextCanvasFade["charms"] = DateTime.Now.AddSeconds(0.5f);
                    break;
                
                case var _ when orig.StartsWith("hasPin"):

                    Log("Updating map pins");

                    text = TextPanels["pins"].text;
                    UpdateText("pins");
                    
                    if(DateTime.Now < NextCanvasFade["pins"] || text.Equals(TextPanels["pins"].text))
                        return;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["pins"]));
                    NextCanvasFade["pins"] = DateTime.Now.AddSeconds(0.5f);
                    break;
                    
            }

        }

        private void UpdateNonPdCanvas(NonPdEnums enums)
        {
            switch (enums)
            {
                case NonPdEnums.DreamPlant:

                    Log("Updating dream trees");

                    // The kills start at 15 and count down since it's amount of kills left to get full journal

                    _settings.DreamTreesCompleted++;
                    UpdateText("DreamPlant");

                    if(DateTime.Now < NextCanvasFade["DreamPlant"])
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["DreamPlant"]));
                    NextCanvasFade["DreamPlant"] = DateTime.Now.AddSeconds(0.5f);

                    break;

                case NonPdEnums.Cornifer:
                    
                    Log("Updating cornifer");

                    UpdateText("cornifer");

                    if(DateTime.Now < NextCanvasFade["cornifer"])
                        break;
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    NextCanvasFade["cornifer"] = DateTime.Now.AddSeconds(0.5f);

                    break;
            }
        }
        
        private void UpdateText(string key)
        {
            PlayerData pd = PlayerData.instance;

            switch (key)
            {
                case var _ when key.StartsWith("trinket"):
                    int amount = pd.GetInt(key);
                    int sold = pd.GetInt("sold" + char.ToUpper(key[0]) + key.Substring(1));

                    TextPanels[key].text = $"{amount}({amount + sold})";
                    break;
                
                case "lifeblood":
                    TextPanels["lifeblood"].text = $"{pd.healthBlue}";
                    break;
                
                case "regg":
                    TextPanels["regg"].text = $"{pd.rancidEggs}({pd.rancidEggs + pd.jinnEggsSold})";
                    break;
                
                case "grub":
                    UpdateGrubs(false);
                    break;
                
                case "devout":
                    // The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels["devout"].text = $"{15 - pd.killsSlashSpider}";
                    break;
                
                case "ore":
                    int oreFromUpgrades = (pd.nailSmithUpgrades * (pd.nailSmithUpgrades - 1)) / 2;
                    TextPanels["ore"].text = $"{pd.ore}({pd.ore + oreFromUpgrades})";
                    break;
                
                case "notches":
                    TextPanels["notches"].text = pd.charmSlots.ToString();
                    break;
                
                case "maps":
                    TextPanels["maps"].text = CountMapBools().ToString();
                    break;
                
                case "charms":
                    pd.CountCharms();
                    TextPanels["charms"].text = pd.charmsOwned.ToString();
                    break;
                
                case "pins":
                    TextPanels["pins"].text = CountPinBools().ToString();
                    break;
                
                case "DreamPlant":
                    TextPanels["DreamPlant"].GetComponentInChildren<Text>().text = $"{_settings.DreamTreesCompleted}";
                    break;
                
                case "cornifer":
                    TextPanels["cornifer"].GetComponentInChildren<Text>().text = CountCorniferBools().ToString();
                    break;

            }
        }

        private IEnumerator FadeCanvas(CanvasGroup canvasGroup)
        {
            if (_globalSettings.alwaysDisplay)
                yield break;

            if(!canvasGroup.gameObject.activeSelf) // Make an if in case it gets updated again while it's still displaying. This is also why we stop coroutine before calling it
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeInCanvasGroup(canvasGroup));

            yield return new WaitForSeconds(4f);

            _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
        }

        [UsedImplicitly]
        public void TrackTrees(ILContext il)
        {
            FieldInfo completedBool = typeof(DreamPlant).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance);
            ILCursor cursor = new ILCursor(il).Goto(0);

            while (cursor.TryGotoNext(instruction => instruction.MatchStfld(completedBool)))
            {
                cursor.Index++;
                cursor.EmitDelegate<Action>(() => { UpdateNonPdCanvas(NonPdEnums.DreamPlant); });
            }
        }

        private IEnumerator PatchCorniferDelay()
        {
            yield return null;
            // Finds all kinds of cornifer locations in a possible scene and patches the FSM to increase the cornifer locations counter
            GameObject[] cornifers = new GameObject[3];
            cornifers[0] = GameObject.Find("Cornifer");
            cornifers[1] = GameObject.Find("Cornifer Deepnest");
            cornifers[2] = GameObject.Find("Cornifer Card");

            foreach (GameObject cornifer in cornifers)
            {
                if (cornifer == null)
                    continue;
                Log("Patching cornifer");
                PlayMakerFSM fsm = cornifer.LocateMyFSM("Conversation Control");
                fsm.InsertMethod
                (
                    "Box Up",
                    fsm.GetState("Box Up").Actions.Length,
                    () => { UpdateCornifer(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name); }
                );
            }
        }
        
        private IEnumerator OnPause(On.UIManager.orig_GoToPauseMenu orig, UIManager uiManager)
        {
            yield return orig(uiManager);

            if (_globalSettings.alwaysDisplay)
                yield break;
            
            foreach (KeyValuePair<string,CanvasGroup> pair in CanvasGroups)
            {
                UpdateText(pair.Key);
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeInCanvasGroup(pair.Value));
            }

        }



        private void OnUnpause(On.UIManager.orig_UIClosePauseMenu origUIClosePauseMenu, UIManager self)
        {
            origUIClosePauseMenu(self);                    
            if(_globalSettings.alwaysDisplay)
                return;
            
            foreach (CanvasGroup canvasGroup in CanvasGroups.Values)
            {
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
            }
        }

        private IEnumerator OnUnpauseQuitGame(On.UIManager.orig_ReturnToMainMenu origReturnToMainMenu, UIManager self)
        {
            yield return origReturnToMainMenu(self);
            if(_globalSettings.alwaysDisplay)
                yield break;
            
            foreach (CanvasGroup canvasGroup in CanvasGroups.Values)
            {
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
            }
            
        }


        public override string GetVersion()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            
            string ver = "1.3";

            using SHA1 sha1 = SHA1.Create();
            using FileStream stream = File.OpenRead(asm.Location);

            byte[] hashBytes = sha1.ComputeHash(stream);
            
            string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            return $"{ver}-{hash.Substring(0, 6)}";
        }

        public new static void Log(object message)
        {
            Logger.Log($"[{nameof(BingoUI)}] - " + message);
        }

        #region UtilityMethods

        private static int CountMapBools()
        {
            IEnumerable<FieldInfo> mapBools = typeof(PlayerData)
                                              .GetFields(BindingFlags.Public | BindingFlags.Instance)
                                              .Where(x => x.FieldType == typeof(bool) 
                                                         && x.Name.StartsWith("map") 
                                                         && x.Name != nameof(PlayerData.mapAllRooms) 
                                                         && x.Name != nameof(PlayerData.mapDirtmouth));

            return mapBools.Count(fi => (bool) fi.GetValue(PlayerData.instance));
        }

        private static int CountPinBools()
        {
            return typeof(PlayerData).GetFields()
                                     .Where(field => mapPinsStrings.Contains(field.Name))
                                     .Count(fi => (bool) fi.GetValue(PlayerData.instance));
        }

        private void PatchCornifer(Scene arg0, LoadSceneMode arg1)
        {
            _coroutineStarter.StartCoroutine(PatchCorniferDelay());
        }

        private void UpdateCornifer(string sceneName)
        {
            if (_settings.Cornifers.ContainsKey(sceneName)) return;

            _settings.Cornifers[sceneName] = true;

            UpdateNonPdCanvas(NonPdEnums.Cornifer);
        }

        private int CountCorniferBools()
        {
            return _settings.Cornifers.Values.Count(c => c);
        }

        private static MapZone SanitizeMapzone(MapZone mapZone)
        {
            switch (mapZone)
            {
                case MapZone.CITY:
                case MapZone.LURIENS_TOWER:
                case MapZone.SOUL_SOCIETY:
                case MapZone.KINGS_STATION:
                    return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Ruins2_11"
                        ? MapZone.OUTSKIRTS
                        // This is Tower of Love, which is City but is considered KE for rando goal purposes
                        : MapZone.CITY; 
                case MapZone.CROSSROADS:
                case MapZone.SHAMAN_TEMPLE:
                    return MapZone.CROSSROADS;
                case MapZone.BEASTS_DEN:
                case MapZone.DEEPNEST:
                    return MapZone.DEEPNEST;
                case MapZone.FOG_CANYON:
                case MapZone.MONOMON_ARCHIVE:
                    return MapZone.FOG_CANYON;
                case MapZone.WASTES:
                case MapZone.QUEENS_STATION:
                    return MapZone.WASTES;
                case MapZone.OUTSKIRTS:
                case MapZone.HIVE:
                case MapZone.COLOSSEUM:
                    return MapZone.OUTSKIRTS;
                case MapZone.TOWN:
                case MapZone.KINGS_PASS:
                    return MapZone.TOWN;
                case MapZone.WATERWAYS:
                case MapZone.GODSEEKER_WASTE:
                    return MapZone.WATERWAYS;
                default:
                    return mapZone;
            }
        }
        
        private bool OnRandoGrubObtain()
        {
            // Show the canvas ourselves as we're preventing SetInt.
            UpdateGrubs(false);
                   
            // Make sure SetInt isn't called so the MapZone isn't incremented.
            return false;
        }

        #endregion
    }
}