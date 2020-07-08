using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using ModCommon;
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
    public class BingoUI : Mod
    {
        private static readonly Dictionary<string, CanvasGroup> CanvasGroups = new Dictionary<string, CanvasGroup>();
        private static readonly Dictionary<string, GameObject> TextPanels = new Dictionary<string, GameObject>();
        private GameObject _coroutineStarterObject;
        private NonBouncer _coroutineStarter;

        public override ModSettings SaveSettings
        {
            get => _settings;
            set => _settings = value as SaveSettings;
        }

        private SaveSettings _settings = new SaveSettings();

        private RandoPlandoCompatibility _randoPlandoCompatibility;

        private ILHook _dreamPlantHook;

        public override void Initialize()
        {
            //Hooks
            ModHooks.Instance.SetPlayerIntHook += UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook += UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += PatchCornifer;

            //Hook rando/plando due to it not using SetInt like everything else and instead calling trinket++ etc
            _randoPlandoCompatibility = new RandoPlandoCompatibility();
            RandoPlandoCompatibility.OnCorniferLocation += UpdateCornifer;
            RandoPlandoCompatibility.OnGrubLocation += DummyGrubSet;


            _dreamPlantHook = new ILHook
            (
                typeof(DreamPlant).GetNestedType("<CheckOrbs>c__Iterator0", BindingFlags.NonPublic).GetMethod("MoveNext"),
                TrackTrees
            );


            _coroutineStarterObject = new GameObject();
            _coroutineStarter = _coroutineStarterObject.AddComponent<NonBouncer>();
            UnityEngine.Object.DontDestroyOnLoad(_coroutineStarterObject);

            Log("Creating Canvases");

            //Define anchor minimum and maximum so we can modify them in a loop and display the images systematically
            Vector2 anchorMin = new Vector2(0.08f, 0f);
            Vector2 anchorMax = new Vector2(0.15f, 0.07f);


            //Create the canvas, make it not disappear on load
            GameObject canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceCamera, new Vector2(1920, 1080));
            UnityEngine.Object.DontDestroyOnLoad(canvas);

            foreach (KeyValuePair<string, Sprite> pair in SeanprCore.ResourceHelper.GetSprites())
            {
                //Get file name without extension as key
                string[] a = pair.Key.Split('.');
                string key = a[a.Length - 1];

                //Create the image
                GameObject canvasSprite = CanvasUtil.CreateImagePanel
                (
                    canvas,
                    pair.Value,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, anchorMin, anchorMax)
                );

                //Add a canvas group so we can fade it in and out
                canvasSprite.AddComponent<CanvasGroup>();
                CanvasGroup canvasGroup = canvasSprite.GetComponent<CanvasGroup>();
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                canvasGroup.alpha = 0f;

                //Add the group to the map to access it easier
                CanvasGroups.Add(key, canvasGroup);


                //Create text, parented to the image so it gets centered on it
                GameObject text = CanvasUtil.CreateTextPanel
                (
                    canvasSprite,
                    "0",
                    20,
                    TextAnchor.LowerCenter,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one)
                );
                text.AddComponent<Outline>();

                //Increment the anchors so the next image isn't on top
                anchorMin += new Vector2(0.07f, 0);
                anchorMax += new Vector2(0.07f, 0);

                //Easy access to the text panel
                TextPanels.Add(key, text);

                Log("Canvas with key " + key + " created");
            }

            Log("Canvas creation done");
        }

        private void DummyGrubSet(string location)
        {
            PlayerData.instance.SetInt("grubsCollected", PlayerData.instance.grubsCollected);
        }


        /**
         * Check if the Int that changed is something to track, and if so display the canvas and the updated number
         */
        private void UpdateIntCanvas(string intname, int value)
        {
            PlayerData pd = PlayerData.instance;

            //Make sure to set the value
            pd.SetIntInternal(intname, value);

            switch (intname)
            {
                //Relics.
                case var _ when intname.StartsWith("trinket"):
                    Log("Updating " + intname);
                    int amount = pd.GetInt(intname);
                    int sold = pd.GetInt("sold" + char.ToUpper(intname[0]) + intname.Substring(1));

                    TextPanels[intname].GetComponent<Text>().text = $"{amount}({amount + sold})";

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups[intname]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups[intname]));
                    break;

                //Lifeblood
                case "healthBlue":
                    if (pd.healthBlue <= 6)
                        break;
                    Log("Updating Lifeblood");

                    TextPanels["lifeblood"].GetComponent<Text>().text = $"{pd.healthBlue}";

                    _coroutineStarter.StopCoroutine(FadeCanvas((CanvasGroups["lifeblood"])));
                    _coroutineStarter.StartCoroutine(FadeCanvas((CanvasGroups["lifeblood"])));
                    break;

                //eggs
                case "jinnEggsSold":
                case "rancidEggs":
                    Log("Updating rancid eggs");

                    TextPanels["regg"].GetComponent<Text>().text = $"{pd.rancidEggs}({pd.rancidEggs + pd.jinnEggsSold})";

                    _coroutineStarter.StopCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    _coroutineStarter.StartCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    break;

                //grubs
                case "grubsCollected":
                {
                    Log("Updating grubs");

                    MapZone mapZone = SanitizeMapzone(GameManager.instance.sm.mapZone);

                    string toStr = mapZone.ToString();

                    if (_settings.AreaGrubs.TryGetValue(toStr, out int _))
                        _settings.AreaGrubs[toStr] += 1;
                    else
                        _settings.AreaGrubs[toStr] = 1;

                    TextPanels["grub"].GetComponent<Text>().text = $"{pd.grubsCollected}({_settings.AreaGrubs[toStr]})";

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["grub"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["grub"]));

                    break;
                }

                case "killsSlashSpider":
                {
                    Log("Updating devouts");

                    //The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels["devout"].GetComponentInChildren<Text>().text = $"{15 - pd.killsSlashSpider}";

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["devout"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["devout"]));
                    break;
                }

                case "ore":
                case "nailSmithUpgrades":
                {
                    Log("Updating pale ore");

                    int oreFromUpgrades = (pd.nailSmithUpgrades * (pd.nailSmithUpgrades - 1)) / 2;

                    TextPanels["ore"].GetComponentInChildren<Text>().text = $"{pd.ore} ({pd.ore + oreFromUpgrades})";

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["ore"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["ore"]));
                    break;
                }
            }
        }


        private void UpdateBoolCanvas(string orig, bool value)
        {
            PlayerData pd = PlayerData.instance;

            pd.SetBoolInternal(orig, value);

            if (!orig.StartsWith("map")) return;

            int amount = CountMapBools();

            TextPanels["maps"].GetComponentInChildren<Text>().text = amount.ToString();

            _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["maps"]));
            _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["maps"]));
        }


        private void UpdateNonPdCanvas(NonPdEnums enums)
        {
            switch (enums)
            {
                case NonPdEnums.DreamPlant:
                    Log("Updating dream trees");

                    //The kills start at 15 and count down since it's amount of kills left to get full journal

                    _settings.DreamTreesCompleted++;
                    TextPanels["DreamPlant"].GetComponentInChildren<Text>().text = $"{_settings.DreamTreesCompleted}";

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["DreamPlant"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["DreamPlant"]));
                    break;

                case NonPdEnums.Cornifer:
                    Log("Updating cornifer");

                    TextPanels["cornifer"].GetComponentInChildren<Text>().text = CountCorniferBools().ToString();

                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    break;
            }
        }

        private IEnumerator FadeCanvas(CanvasGroup canvasGroup)
        {
            _coroutineStarter.StartCoroutine(CanvasUtil.FadeInCanvasGroup(canvasGroup));

            yield return new WaitForSeconds(4f);

            _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
        }

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
            //Finds both kinds of cornifer locations in a possible scene and patches the FSM to increase the cornifer locations counter
            GameObject[] cornifers = new GameObject[2];
            cornifers[0] = GameObject.Find("Cornifer");
            cornifers[1] = GameObject.Find("Cornifer Card");

            foreach (GameObject cornifer in cornifers)
            {
                if (cornifer == null)
                    continue;
                Log("Patching cornifer");
                PlayMakerFSM fsm = cornifer.LocateMyFSM("Conversation Control");
                fsm.InsertMethod
                (
                    "Box Down",
                    0,
                    () => { UpdateCornifer(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name); }
                );
            }
        }

        public override string GetVersion()
        {
            return "1.1";
        }

        public new static void Log(object message)
        {
            Logger.Log("[BingoUI] - " + message);
        }

        #region UtilityMethods

        private static int CountMapBools()
        {
            IEnumerable<FieldInfo> mapBools = typeof(PlayerData)
                                              .GetFields(BindingFlags.Public | BindingFlags.Instance)
                                              .Where(x => x.FieldType == typeof(bool) && x.Name.StartsWith("map") && x.Name != nameof(PlayerData.mapAllRooms));

            return mapBools.Count(fi => (bool) fi.GetValue(PlayerData.instance));
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

        private MapZone SanitizeMapzone(MapZone mapZone)
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
                default: return mapZone;
            }
        }

        #endregion
    }
}