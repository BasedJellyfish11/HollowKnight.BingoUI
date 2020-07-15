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

        // Excluding the pins we didn't want to count proved to be more of a pain than writing the ones we do want and doing .Contains()
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
            On.HealthManager.SendDeathEvent += UpdateDevouts;

            // Hook rando/plando due to it not using SetInt like everything else and instead calling trinket++ etc
            _randoPlandoCompatibility = new RandoPlandoCompatibility();
            
            RandoPlandoCompatibility.OnCorniferLocation += UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation += DummyRandoAreaGrubSet;
            RandoPlandoCompatibility.OnGrubObtain += OnRandoGrubObtain;
            

            // Make dream trees send a delegate notifying that the tree is done when it sets the "completed" bool
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
                text.GetComponent<Text>().color = Color.black;

                // Increment the anchors so the next image isn't on top. If it's all the way right, start drawing up
                // This makes no sense with the new way of diving 1/sprites.Count, but it's kept in case that way starts failing due to space constraints
                Vector2 sum = anchorMax.x >= 1f ? new Vector2(0, 0.09f) : new Vector2(1f/sprites.Count,0);
                anchorMin += sum;
                anchorMax += sum;

                // Easy access to the text panel
                TextPanels.Add(key, text.GetComponent<Text>());
                
                // Set a minimum cooldown between fades
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
            On.HealthManager.SendDeathEvent -= UpdateDevouts;


            _randoPlandoCompatibility?.Dispose();
            
            // ReSharper disable DelegateSubtraction
            RandoPlandoCompatibility.OnCorniferLocation -= UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation -= DummyRandoAreaGrubSet;
            RandoPlandoCompatibility.OnGrubObtain -= OnRandoGrubObtain;
            // ReSharper enable DelegateSubtraction

            _dreamPlantHook?.Dispose();
            
            UnityEngine.Object.Destroy(_canvas);
            UnityEngine.Object.Destroy(_coroutineStarter.transform.parent.gameObject);
        }

        /**
         * Check if the Int that changed is something to track, and if so display the canvas and the updated number
         */
        private void UpdateIntCanvas(string intname, int value)
        {
            PlayerData pd = PlayerData.instance;

            // Make sure to set the value
            pd.SetIntInternal(intname, value);
            
            switch (intname)
            {
                // Relics.
                case var _ when intname.StartsWith("trinket"):

                    UpdateCanvas(intname);
                    break;

                // Lifeblood
                case nameof(pd.healthBlue):
                    
                    if(pd.healthBlue <= 6)
                        UpdateCanvas("lifeblood");
                    break;
                
                // eggs
                case nameof(pd.jinnEggsSold):
                case nameof(pd.rancidEggs):
                    
                    UpdateCanvas("regg");
                    break;

                // grubs
                case nameof(pd.grubsCollected):
                
                    UpdateGrubs();

                    break;

                case nameof(pd.nailSmithUpgrades): // Update on upgrades, else it shows as if we "lost" ore
                    if(pd.nailSmithUpgrades == 1)  // However the first upgrade costs no ore so it makes no sense to update on it
                        break;
                    goto case nameof(pd.ore); // C# doesn't allow switch fallthrough. I hate goto so much
                case nameof(pd.ore):
                    
                    UpdateCanvas("ore");
                    break;
                
                case nameof(pd.charmSlots):
                  
                    UpdateCanvas("notches");
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
            
            pd.SetBoolInternal(orig, value);

            switch (orig)
            {

                case var _ when orig.StartsWith("map"):
                    
                    UpdateCanvas("maps");
                    break;
                
                case var _ when orig.StartsWith("gotCharm"):
                    
                    UpdateCanvas("charms");
                    break;
                
                case var _ when orig.StartsWith("hasPin"):

                    UpdateCanvas("pins");
                    break;
                
            }

        }

        // Honestly now that the generic UpdateCanvas(key) exists, this is very useless. Might phase it out in a later update
        private void UpdateNonPdCanvas(NonPdEnums enums)
        {
            switch (enums)
            {
                case NonPdEnums.DreamPlant:

                    _settings.DreamTreesCompleted++; 
                    UpdateCanvas("DreamPlant");
                    break;

                case NonPdEnums.Cornifer:
                    
                    UpdateCanvas("cornifer");
                    break;
            }
        }
        
        private void UpdateCanvas(string key)
        {
            Log("Updating " + key);

            string oldText = TextPanels[key].text;
            UpdateText(key);
            Log($" old text was {oldText} new text is {TextPanels[key].text}");
            
            if(DateTime.Now < NextCanvasFade[key] || oldText == TextPanels[key].text)
                return;
            _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups[key]));
            NextCanvasFade[key] = DateTime.Now.AddSeconds(0.5f);

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
                    UpdateGrubs(false); // This will update the text without incrementing the area grubs
                    break;
                
                case "devout":
                    // The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels["devout"].text = _settings.Devouts.Count.ToString();
                    break;
                
                case "ore":
                    int oreFromUpgrades = (pd.nailSmithUpgrades * (pd.nailSmithUpgrades - 1)) / 2; // This equation is stolen from Yusuf
                    TextPanels["ore"].text = $"{pd.ore}({pd.ore + oreFromUpgrades})";
                    break;
                
                case "notches":
                    TextPanels["notches"].text = pd.charmSlots.ToString();
                    break;
                
                case "maps":
                    TextPanels["maps"].text = CountMapBools().ToString();
                    break;
                
                case "charms":
                    pd.CountCharms(); // Update charms owned first by calling this method. Especially useful since some mods seem to increment the number on half a kingsoul and this will undo that
                    TextPanels["charms"].text = pd.charmsOwned.ToString();
                    break;
                
                case "pins":
                    TextPanels["pins"].text = CountPinBools().ToString();
                    break;
                
                case "DreamPlant":
                    TextPanels["DreamPlant"].text = $"{_settings.DreamTreesCompleted}";
                    break;
                
                case "cornifer":
                    TextPanels["cornifer"].text = CountCorniferBools().ToString();
                    break;

            }
        }

        private IEnumerator FadeCanvas(CanvasGroup canvasGroup)
        {
            if (_globalSettings.alwaysDisplay)
                yield break;

            if(!canvasGroup.gameObject.activeSelf) // Make an if in case it gets updated again while it's still displaying. 
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeInCanvasGroup(canvasGroup));

            yield return new WaitForSeconds(4f);

            _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
        }

        [UsedImplicitly]
        public void TrackTrees(ILContext il)
        {
            FieldInfo completedBool = typeof(DreamPlant).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance);
            ILCursor cursor = new ILCursor(il).Goto(0);

            while (cursor.TryGotoNext(instruction => instruction.MatchStfld(completedBool))) // Find the instruction that sets the tree as completed
            {
                cursor.Index++;
                cursor.EmitDelegate<Action>(() => { UpdateNonPdCanvas(NonPdEnums.DreamPlant); }); // Emit the updating delegate after the bool is set
            }
        }

        private IEnumerator PatchCorniferDelay()
        {
            yield return null;
            // Finds all kinds of cornifer locations in a possible scene and patches the FSM to increase the cornifer locations counter
            GameObject[] cornifers = new GameObject[3];
            cornifers[0] = GameObject.Find("Cornifer");
            cornifers[1] = GameObject.Find("Cornifer Deepnest"); // Why is this a separete object TC
            cornifers[2] = GameObject.Find("Cornifer Card");

            foreach (GameObject cornifer in cornifers)
            {
                if (cornifer == null)
                    continue;
                // Patch the FSM to emit an updating delegate once the dialog box first appears
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
            if (_settings.Cornifers.Add(sceneName)) // This would mean this location's cornifer has already been interacted with
                UpdateNonPdCanvas(NonPdEnums.Cornifer);
        }
        
        private void UpdateDevouts(On.HealthManager.orig_SendDeathEvent orig, HealthManager self)
        {
            
            orig(self);

            if (!self.gameObject.name.StartsWith("Slash Spider")) return;
            
            if(_settings.Devouts.Add((UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, self.gameObject.name)))
                UpdateCanvas("devout");


        }

        private IEnumerator OnPause(On.UIManager.orig_GoToPauseMenu orig, UIManager uiManager)
        {
            yield return orig(uiManager);

            if (_globalSettings.alwaysDisplay)
                yield break;
            
            // Update and display every image
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
            
            // Fade all the canvases, which we were displaying due to pause, out
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
            
            // Same thing as above, except apparently quitting to menu doesn't count as unpausing
            foreach (CanvasGroup canvasGroup in CanvasGroups.Values)
            {
                _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
            }
            
        }
        
        private int CountCorniferBools()
        {
            return _settings.Cornifers.Count;
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
        
        private static void DummyRandoAreaGrubSet(string location)
        {
            // Increments area grubs. Hooked to checking a grub location in rando, dead otherwise
            PlayerData.instance.SetInt(nameof(PlayerData.instance.grubsCollected), PlayerData.instance.grubsCollected);
        }
        

        #endregion
    }
}