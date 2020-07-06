using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using Modding;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Logger = Modding.Logger;

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
            get => SETTINGS; 
            set => SETTINGS = value as SaveSettings;
        }
        
        public SaveSettings SETTINGS = new SaveSettings();
        
        private RandoPlandoCompatibility _randoPlandoCompatibility;

        private ILHook _dreamPlantHook;

        public override void Initialize()
        {
            
            //Hooks
            ModHooks.Instance.SetPlayerIntHook += UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook += UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += ResetSettings;
            //Hook rando/plando due to it not using SetInt like everything else and instead calling trinket++ etc
            _randoPlandoCompatibility = new RandoPlandoCompatibility();
            _dreamPlantHook = new ILHook( typeof(DreamPlant).GetNestedType("<CheckOrbs>c__Iterator0", BindingFlags.NonPublic).GetMethod("MoveNext"),
                                                TrackTrees);
            
            
            _coroutineStarterObject = new GameObject();
            _coroutineStarter = _coroutineStarterObject.AddComponent<NonBouncer>();
            UnityEngine.Object.DontDestroyOnLoad(_coroutineStarterObject);
            Log("Creating Canvases");
            

            //Define anchor minimum and maximum so we can modify them in a loop and display the images systematically
            Vector2 anchorMin = new Vector2(0.15f,0f);
            Vector2 anchorMax = new Vector2(0.22f, 0.07f);
            
            foreach (KeyValuePair<string, Sprite> pair in SeanprCore.ResourceHelper.GetSprites())
            {
                //Get file name without extension as key
                string[] a = pair.Key.Split('.');
                string key = a[a.Length - 1];
                
                //Create a canvas and make it not show
                GameObject canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceCamera, new Vector2(1920, 1080));
                UnityEngine.Object.DontDestroyOnLoad(canvas);
                canvas.SetActive(false);
                
                //Add a canvas group so we can fade it in and out
                canvas.AddComponent<CanvasGroup>();
                CanvasGroup canvasGroup = canvas.GetComponent<CanvasGroup>();
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                
                //Add the group to the map to access it easier
                CanvasGroups.Add(key, canvasGroup);
                
                //Create the image
                GameObject canvasSprite = CanvasUtil.CreateImagePanel(canvas, pair.Value,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, anchorMin, anchorMax));
                
                //Create text, parented to the image so it gets centered on it
                GameObject text = CanvasUtil.CreateTextPanel(canvasSprite, "0", 20, TextAnchor.LowerCenter,
                    new CanvasUtil.RectData(Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one));
                text.AddComponent<Outline>();
                
                //Increment the anchors so the next image isn't on top
                anchorMin += new Vector2(0.07f, 0);
                anchorMax += new Vector2(0.07f,0);

                //Easy access to the text panel
                TextPanels.Add(key, text);
                
                Log("Canvas with key " + key + " created");
                
                
            }

            Log("Canvas creation done");
            
           
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
                
                case "jinnEggsSold": case "rancidEggs":
                    Log("Updating rancid eggs");
                    
                    TextPanels["regg"].GetComponent<Text>().text = $"{pd.rancidEggs}({pd.rancidEggs + pd.jinnEggsSold})";
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    _coroutineStarter.StartCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    break;
                

                case "grubsCollected":
                    Log("Updating grubs");
                    MapZone mapZone = SanitizeMapzone(GameManager.instance.sm.mapZone);
                    SETTINGS.SetInt(SETTINGS.GetInt(null,mapZone.ToString()) +1, mapZone.ToString());
                    //SETTINGS.areaGrubs[mapZone]++; //add the rescued grub to the amount of grubs rescued in the area
                    
                    TextPanels["grub"].GetComponent<Text>().text = $"{pd.grubsCollected}({SETTINGS.GetInt(null,mapZone.ToString())})";
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas( CanvasGroups["grub"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas( CanvasGroups["grub"]));
                    break;
                
                case "killsSlashSpider":
                    Log("Updating devouts");
                    
                    //The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels["devout"].GetComponentInChildren<Text>().text = $"{15 - pd.killsSlashSpider}";
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas( CanvasGroups["devout"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas( CanvasGroups["devout"]));
                    break;
                
                case "ore": case"nailSmithUpgrades":
                    Log("Updating pale ore");
                    int oreFromUpgrades = (pd.nailSmithUpgrades * (pd.nailSmithUpgrades - 1)) / 2;
                    TextPanels["ore"].GetComponentInChildren<Text>().text = $"{pd.ore} ({pd.ore + oreFromUpgrades})";
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas( CanvasGroups["ore"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas( CanvasGroups["ore"]));
                    break;
                    
            }
            
        }
        

        private void UpdateBoolCanvas(string originalset, bool value)
        {
            PlayerData pd = PlayerData.instance;
            pd.SetBoolInternal(originalset, value);
            int amount;

            switch (originalset)
            {
                case var _ when originalset.StartsWith("corn_") && originalset.EndsWith("Encountered"):
                    amount = CountCorniferBools();
                    TextPanels["cornifer"].GetComponentInChildren<Text>().text = amount.ToString();
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    break;
                case var _ when originalset.StartsWith("map"):
                    amount = CountMapBools();
                    TextPanels["maps"].GetComponentInChildren<Text>().text = amount.ToString();
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas(CanvasGroups["maps"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas(CanvasGroups["maps"]));
                    break;
            }

        }


        private void UpdateNonPdCanvas(NonPdEnums enums)
        {
            switch (enums)
            {
                case NonPdEnums.DreamPlant:
                    Log("Updating dream trees");
                    
                    //The kills start at 15 and count down since it's amount of kills left to get full journal
                    
                    SETTINGS.dreamTreesCompleted++;
                    TextPanels["DreamPlant"].GetComponentInChildren<Text>().text = $"{SETTINGS.dreamTreesCompleted}";
                    
                    _coroutineStarter.StopCoroutine(FadeCanvas( CanvasGroups["DreamPlant"]));
                    _coroutineStarter.StartCoroutine(FadeCanvas( CanvasGroups["DreamPlant"]));
                    break;
                    
            }
        }

        private  IEnumerator FadeCanvas(CanvasGroup canvasGroup)
        {
            _coroutineStarter.StartCoroutine(CanvasUtil.FadeInCanvasGroup(canvasGroup));

            yield return new WaitForSeconds(4f);

            _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
        }

        public void TrackTrees(ILContext il)
        {
            FieldInfo completedBool = typeof(DreamPlant).GetField("completed", BindingFlags.NonPublic|BindingFlags.Instance);
            ILCursor cursor = new ILCursor(il).Goto(0);

            while (cursor.TryGotoNext(instruction  => instruction.MatchStfld(completedBool)))
            {
                cursor.Index++;
                cursor.EmitDelegate<Action>(() => {
                    UpdateNonPdCanvas(NonPdEnums.DreamPlant);
                    
                });
            }
        }

        public override string GetVersion()
        {
            return "1.0";
        }
        
        public new static void Log(object message)
        {
            Logger.Log("[BingoUI] - " + message );
        }
        
       
        private void ResetSettings(Scene arg0, Scene arg1)
        {
            //API issue

            if (arg1.name == Constants.MENU_SCENE)
            {
                
                SETTINGS = new SaveSettings();
                foreach (CanvasGroup cg in CanvasGroups.Values)
                {
                    _coroutineStarter.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(cg));
                }
            }
        }

        #region UtilityMethods

        private static int CountMapBools()
        {
            //terrible
            
            int amount = 0;

            PlayerData pd = PlayerData.instance;
            
            if (pd.mapAbyss)
                amount++;
            if (pd.mapCity)
                amount++;
            if (pd.mapCliffs)
                amount++;
            if (pd.mapCrossroads)
                amount++;
            if (pd.mapDeepnest)
                amount++;
            if (pd.mapGreenpath)
                amount++;
            if (pd.mapMines)
                amount++;
            if (pd.mapOutskirts)
                amount++;
            if (pd.mapWaterways)
                amount++;
            if (pd.mapFogCanyon)
                amount++;
            if (pd.mapFungalWastes)
                amount++;
            if (pd.mapRestingGrounds)
                amount++;
            if (pd.mapRoyalGardens)
                amount++;

            return amount;
        }
        
        
        private static int CountCorniferBools()
        {
            int amount = 0;

            
            Type t = typeof(PlayerData);
            IEnumerable<FieldInfo> fieldInfos = t.GetFields().Where(info => info.Name.StartsWith("corn_") &&
                                                                            info.Name.EndsWith("Encountered"));

            foreach (FieldInfo fieldInfo in fieldInfos)
            {
                if ((bool) fieldInfo.GetValue(PlayerData.instance))
                    amount++;
            }

            
            return amount;
            
        }

        private MapZone SanitizeMapzone(MapZone mapZone)
        {
            switch (mapZone)
            {
                case MapZone.CITY: case MapZone.LURIENS_TOWER: case MapZone.SOUL_SOCIETY: case MapZone.KINGS_STATION:
                    return MapZone.CITY;
                case MapZone.CROSSROADS: case MapZone.SHAMAN_TEMPLE:
                    return MapZone.CROSSROADS;
                case MapZone.BEASTS_DEN: case MapZone.DEEPNEST:
                    return MapZone.DEEPNEST;
                case MapZone.FOG_CANYON: case MapZone.MONOMON_ARCHIVE:
                    return MapZone.FOG_CANYON;
                case MapZone.WASTES: case MapZone.QUEENS_STATION:
                    return MapZone.WASTES;
                case MapZone.OUTSKIRTS: case MapZone.HIVE: case MapZone.COLOSSEUM:
                    return MapZone.OUTSKIRTS;
                case MapZone.TOWN: case MapZone.KINGS_PASS:
                    return MapZone.TOWN;
                case MapZone.WATERWAYS: case MapZone.GODSEEKER_WASTE:
                    return MapZone.WATERWAYS;
                default: return mapZone;
            }
        }
        
        
        
        
        

        #endregion
    }
}