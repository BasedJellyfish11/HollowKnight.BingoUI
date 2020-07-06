using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Modding;
using MonoMod.RuntimeDetour;
using RandomizerMod;
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

        public override ModSettings SaveSettings
        {
            get
            {
                /*
                Log("getting settings");
                foreach (KeyValuePair<MapZone, int> a in _settings.areaGrubs)
                {
                    Log(a.Key + " " + a.Value);
                }
                */
                return _settings;
            }
            set
            {
                
                //Log("setting settings");
                
                _settings = value as SaveSettings;
                /*
                Log(value);
                foreach (FieldInfo fieldInfo in value.GetType().GetFields())
                {
                    Log(fieldInfo.Name +" ");
                }
                foreach (KeyValuePair<MapZone,int> a in (GrubMap)value.GetType().GetField("areaGrubs").GetValue(value))
                {
                    Log(a.Key + " " + a.Value);
                }
                
                value.GetType().GetField("areaGrubs").GetValue(value);
                */



            }
        }


        public SaveSettings _settings = new SaveSettings();

        private Hook _rando;

        
        
        
        public override void Initialize()
        {
            
            //Hooks
            ModHooks.Instance.SetPlayerIntHook += UpdateIntCanvas;
            ModHooks.Instance.SetPlayerBoolHook += UpdateBoolCanvas;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += ResetSettings;
            
            
            Log("Creating Canvases");
            

            //Define anchor minimum and maximum so we can modify them in a loop and display the images systematically
            Vector2 anchorMin = new Vector2(0.25f,0f);
            Vector2 anchorMax = new Vector2(0.32f, 0.07f);
            
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
            
            //Hook rando due to it not using SetInt like everything else and instead calling trinket++ etc
            Type t = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");

            if (t == null) return;

            Log("Hooking Rando");

            _rando = new Hook
            (
                t.GetMethod("GiveItem",BindingFlags.Public | BindingFlags.Static),
                typeof(BingoUI).GetMethod(nameof(FixRando))
            );
        }

        public static void FixRando(Action<GiveItemActions.GiveAction, string, string, int> orig, GiveItemActions.GiveAction action, string item, string location, int geo)
        {
            orig(action, item, location, geo);
            
            //Literally just let rando do its thing and increment or whatever, then call SetInt so the hook runs
            
            PlayerData pd = PlayerData.instance;
            switch (action)
            {
                case GiveItemActions.GiveAction.WanderersJournal:
                    pd.SetInt("trinket1", pd.trinket1);
                    break;
                case GiveItemActions.GiveAction.HallownestSeal:
                    pd.SetInt("trinket2", pd.trinket2);
                    break;
                case GiveItemActions.GiveAction.KingsIdol:
                    pd.SetInt("trinket3", pd.trinket3);
                    break;
                case GiveItemActions.GiveAction.ArcaneEgg:
                    pd.SetInt("trinket4", pd.trinket4);
                    break;
                case GiveItemActions.GiveAction.Grub:
                    pd.SetInt("grubsCollected", pd.grubsCollected);
                    break;


            }
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
                    GameManager.instance.StartCoroutine(FadeCanvas(CanvasGroups[intname]));
                    break;
                
                case "rancidEggs":
                    Log("Updating rancid eggs");
                    
                    TextPanels["regg"].GetComponent<Text>().text = $"{pd.rancidEggs}({pd.rancidEggs + pd.jinnEggsSold})";
                    GameManager.instance.StartCoroutine(FadeCanvas((CanvasGroups["regg"])));
                    break;

                case "grubsCollected":
                    Log("Updating grubs");
                    
                    _settings.SetInt(_settings.GetInt(null,pd.mapZone.ToString()) +1, pd.mapZone.ToString());
                    //_settings.areaGrubs[pd.mapZone]++; //add the rescued grub to the amount of grubs rescued in the area
                    
                    TextPanels["grub"].GetComponent<Text>().text = $"{pd.grubsCollected}({_settings.GetInt(null,pd.mapZone.ToString())})";
                    GameManager.instance.StartCoroutine(FadeCanvas( CanvasGroups["grub"]));
                    break;
                
                case "killsSlashSpider":
                    Log("Updating devouts");
                    
                    //The kills start at 15 and count down since it's amount of kills left to get full journal
                    TextPanels["devout"].GetComponentInChildren<Text>().text = $"{15 - pd.killsSlashSpider}";;
                    GameManager.instance.StartCoroutine(FadeCanvas( CanvasGroups["devout"]));
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
                    GameManager.instance.StartCoroutine(FadeCanvas(CanvasGroups["cornifer"]));
                    break;
                case var _ when originalset.StartsWith("map"):
                    amount = CountMapBools();
                    TextPanels["maps"].GetComponentInChildren<Text>().text = amount.ToString();
                    GameManager.instance.StartCoroutine(FadeCanvas(CanvasGroups["maps"]));
                    break;
            }

        }



        private static IEnumerator FadeCanvas(CanvasGroup canvasGroup)
        {
            GameManager.instance.StartCoroutine(CanvasUtil.FadeInCanvasGroup(canvasGroup));

            yield return new WaitForSeconds(5f);

            GameManager.instance.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(canvasGroup));
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
            //API bug

            if (arg1.name == Constants.MENU_SCENE)
            {
                
                _settings = new SaveSettings();
                foreach (CanvasGroup cg in CanvasGroups.Values)
                {
                    GameManager.instance.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(cg));
                }
            }
        }
        
    }
}