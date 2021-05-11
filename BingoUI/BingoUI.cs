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
        private static Dictionary<KeyEnums, CanvasGroup> CanvasGroups;
        private static Dictionary<KeyEnums, Text> TextPanels;
        private static Dictionary<KeyEnums, DateTime> NextCanvasFade;

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

        internal static SaveSettings _settings = new SaveSettings();
        private static GlobalSettings _globalSettings = new GlobalSettings();

        private RandoPlandoCompatibility _randoPlandoCompatibility;

        private ILHook _dreamPlantHook;

        private bool? _grubsRandomized;

        public override void Initialize()
        {
            
            CanvasGroups = new Dictionary<KeyEnums, CanvasGroup>();
            TextPanels = new Dictionary<KeyEnums, Text>();
            NextCanvasFade = new Dictionary<KeyEnums, DateTime>();
            // Hooks
            ModHooks.Instance.SetPlayerIntHook += UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook += UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += PatchCornifer;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += ResetGrubsFlag;
            On.UIManager.GoToPauseMenu += OnPause;
            On.UIManager.UIClosePauseMenu += OnUnpause;
            On.UIManager.ReturnToMainMenu += OnUnpauseQuitGame;
            On.HealthManager.SendDeathEvent += UpdateUniqueKills;
            
            // GeoTracker stuff. Taken straight up from Serena's code, with no changes, so some of it might be redundant idk
            
            
            On.GeoCounter.Update +=  GeoTracker.UpdateGeoText;
            On.GeoCounter.TakeGeo += GeoTracker.CheckGeoSpent;

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

            Dictionary<string, Sprite> sprites = SereCore.ResourceHelper.GetSprites();
            // Define anchor minimum and maximum so we can modify them in a loop and display the images systematically
            Vector2 anchorMin = new Vector2(0f, 0.01f);
            Vector2 anchorMax = new Vector2(1f/15f, 0.1f);

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
                CanvasGroups.Add((KeyEnums)Enum.Parse(typeof(KeyEnums),key), canvasGroup);


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
                Vector2 sum = anchorMax.x >= 0.95f ? new Vector2(0, 0.11f) : new Vector2(1f/15,0);
                anchorMin += sum;
                anchorMax += sum;

                // Easy access to the text panel
                TextPanels.Add((KeyEnums)Enum.Parse(typeof(KeyEnums), key), text.GetComponent<Text>());
                
                // Set a minimum cooldown between fades
                NextCanvasFade.Add((KeyEnums)Enum.Parse(typeof(KeyEnums), key), DateTime.MinValue);

                Log("Canvas with key " + key + " created");
            }

            Log("Canvas creation done");
        }

        public void Unload()
        {
            ModHooks.Instance.SetPlayerIntHook -= UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook -= UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= PatchCornifer;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= ResetGrubsFlag;
            On.UIManager.GoToPauseMenu -= OnPause;
            On.UIManager.UIClosePauseMenu -= OnUnpause;
            On.UIManager.ReturnToMainMenu -= OnUnpauseQuitGame;
            On.HealthManager.SendDeathEvent -= UpdateUniqueKills;


            _randoPlandoCompatibility?.Dispose();
            
            // ReSharper disable DelegateSubtraction
            RandoPlandoCompatibility.OnCorniferLocation -= UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation -= DummyRandoAreaGrubSet;
            RandoPlandoCompatibility.OnGrubObtain -= OnRandoGrubObtain;
            // ReSharper enable DelegateSubtraction

            _dreamPlantHook?.Dispose();
            
            UnityEngine.Object.Destroy(_canvas);
            UnityEngine.Object.Destroy(_coroutineStarter.gameObject);
        }

        private void ResetGrubsFlag(Scene from, Scene to) {
            // Invalidate the cache, as the player may be switching to a different
            // save file.
            if (to.name == "Menu_Title" || to.name == "Quit_To_Menu") {
                _grubsRandomized = null;
            }
        }

        private bool GrubsRandomized() {
            if (_grubsRandomized.HasValue) {
                return _grubsRandomized.Value;
            }
            var rando = Type.GetType("RandomizerMod.RandomizerMod, RandomizerMod3.0");
            if (rando == null) {
                return false;
            }
            var settingsType = Type.GetType("RandomizerMod.SaveSettings, RandomizerMod3.0");
            if (settingsType == null) {
                return false;
            }
            var randoInstance = rando.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            if (randoInstance == null) {
                return false;
            }
            var settings = rando.GetProperty("Settings").GetValue(randoInstance, null);
            if (settings == null) {
                return false;
            }
            var r = (bool)settingsType.GetProperty("RandomizeGrubs").GetValue(settings, null);
            // Cache the value so we don't have to run all this reflection again next time the
            // player breaks a grub jar.
            _grubsRandomized = r;
            return r;
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

                    UpdateCanvas((KeyEnums)Enum.Parse(typeof(KeyEnums), intname));
                    break;

                // Lifeblood
                case nameof(pd.healthBlue) when pd.healthBlue > 6 || CanvasGroups[KeyEnums.lifeblood].gameObject.activeSelf:
                    
                    UpdateCanvas(KeyEnums.lifeblood);
                    break;
                
                // eggs
                case nameof(pd.jinnEggsSold):
                case nameof(pd.rancidEggs):
                    
                    UpdateCanvas(KeyEnums.regg);
                    break;

                // grubs
                case nameof(pd.grubsCollected):
                
                    UpdateGrubs(!GrubsRandomized());
                    break;

                case nameof(pd.nailSmithUpgrades): // Update on upgrades, else it shows as if we "lost" ore
                    if(pd.nailSmithUpgrades == 1)  // However the first upgrade costs no ore so it makes no sense to update on it
                        break;
                    goto case nameof(pd.ore); // C# doesn't allow switch fallthrough. I hate goto so much
                case nameof(pd.ore):
                    
                    UpdateCanvas(KeyEnums.ore);
                    break;
                
                case nameof(pd.charmSlots):
                  
                    UpdateCanvas(KeyEnums.notches);
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

            TextPanels[KeyEnums.grub].text = $"{pd.grubsCollected}({_settings.AreaGrubs[mapZone]})";
            
            if (DateTime.Now < NextCanvasFade[KeyEnums.grub])
                return;
            _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups[KeyEnums.grub]));
            NextCanvasFade[KeyEnums.grub] = DateTime.Now.AddSeconds(0.5f);
        }

        private void UpdateBoolCanvas(string orig, bool value)
        {

            PlayerData pd = PlayerData.instance;
            
            pd.SetBoolInternal(orig, value);

            switch (orig)
            {

                case var _ when orig.StartsWith("map"):
                    
                    UpdateCanvas(KeyEnums.maps);
                    break;
                
                case var _ when orig.StartsWith("gotCharm"):
                    
                    UpdateCanvas(KeyEnums.charms);
                    break;
                
                case var _ when orig.StartsWith("hasPin"):

                    UpdateCanvas(KeyEnums.pins);
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
                    UpdateCanvas(KeyEnums.DreamPlant);
                    break;

                case NonPdEnums.Cornifer:
                    
                    UpdateCanvas(KeyEnums.cornifer);
                    break;
            }
        }
        
        private void UpdateCanvas(KeyEnums key)
        {
            Log("Updating " + key);

            string oldText = TextPanels[key].text;
            UpdateText(key);
            Log($" Old text was {oldText} new text is {TextPanels[key].text}");
            
            if(DateTime.Now < NextCanvasFade[key] || oldText == TextPanels[key].text)
                return;
            _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups[key]));
            NextCanvasFade[key] = DateTime.Now.AddSeconds(0.5f);

        }
        
        
        private void UpdateText(KeyEnums key)
        {
            PlayerData pd = PlayerData.instance;

            switch (key)
            {
                case var _ when key.ToString().StartsWith("trinket"):
                    int amount = pd.GetInt(key.ToString());
                    int sold = pd.GetInt("sold" + char.ToUpper(key.ToString()[0]) + key.ToString().Substring(1));

                    TextPanels[key].text = $"{amount}({amount + sold})";
                    break;
                
                case KeyEnums.lifeblood:
                    TextPanels[KeyEnums.lifeblood].text = pd.joniHealthBlue != 0? $"{pd.healthBlue + 1}": $"{pd.healthBlue}" ;
                    break;
                
                case KeyEnums.regg:
                    TextPanels[KeyEnums.regg].text = $"{pd.rancidEggs}({pd.rancidEggs + pd.jinnEggsSold})";
                    break;
                
                case KeyEnums.grub:
                    UpdateGrubs(false); // This will update the text without incrementing the area grubs
                    break;
                
                case KeyEnums.devout:
                    // The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels[KeyEnums.devout].text = _settings.Devouts.Count.ToString();
                    break;
                
                case KeyEnums.ghs:
                    // Same behaviour as above
                    TextPanels[KeyEnums.ghs].text = _settings.GreatHuskSentries.Count.ToString();
                    break;
                
                case KeyEnums.ore:
                    int oreFromUpgrades = (pd.nailSmithUpgrades * (pd.nailSmithUpgrades - 1)) / 2; // This equation is stolen from Yusuf
                    TextPanels[KeyEnums.ore].text = $"{pd.ore}({pd.ore + oreFromUpgrades})";
                    break;
                
                case KeyEnums.notches:
                    TextPanels[KeyEnums.notches].text = pd.charmSlots.ToString();
                    break;
                
                case KeyEnums.maps:
                    TextPanels[KeyEnums.maps].text = CountMapBools().ToString();
                    break;
                
                case KeyEnums.charms:
                    pd.CountCharms(); // Update charms owned first by calling this method. Especially useful since some mods seem to increment the number on half a kingsoul and this will undo that
                    TextPanels[KeyEnums.charms].text = pd.charmsOwned.ToString();
                    break;
                
                case KeyEnums.pins:
                    TextPanels[KeyEnums.pins].text = CountPinBools().ToString();
                    break;
                
                case KeyEnums.DreamPlant:
                    TextPanels[KeyEnums.DreamPlant].text = $"{_settings.DreamTreesCompleted}";
                    break;
                
                case KeyEnums.cornifer:
                    TextPanels[KeyEnums.cornifer].text = CountCorniferBools().ToString();
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
        
        private void UpdateUniqueKills(On.HealthManager.orig_SendDeathEvent orig, HealthManager self)
        {
            
            orig(self);

            var tuples = new (string, HashSet<(string, string)>, KeyEnums)[]
            {
                ("Slash Spider", _settings.Devouts, KeyEnums.devout),
                ("Great Shield Zombie", _settings.GreatHuskSentries, KeyEnums.ghs),
            };

            foreach ((string, HashSet<(string, string)>, KeyEnums) tuple in tuples)
            {
                if (self.gameObject.name.StartsWith(tuple.Item1) && tuple.Item2.Add((UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, self.gameObject.name)))
                    UpdateCanvas(tuple.Item3);
            }


        }

        private IEnumerator OnPause(On.UIManager.orig_GoToPauseMenu orig, UIManager uiManager)
        {
            yield return orig(uiManager);

            if (_globalSettings.alwaysDisplay)
                yield break;
            
            // Update and display every image
            foreach (KeyValuePair<KeyEnums,CanvasGroup> pair in CanvasGroups)
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
        
        private void DummyRandoAreaGrubSet(string location)
        {
            // Increments area grubs. Hooked to checking a grub location in rando, dead otherwise
            UpdateGrubs(true);
        }

        #endregion
    }
}