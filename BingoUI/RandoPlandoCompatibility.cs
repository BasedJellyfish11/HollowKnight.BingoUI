using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModCommon.Util;
using UnityEngine;

namespace BingoUI
{
    using System;
    using System.Reflection;
    using MonoMod.RuntimeDetour;
    
    
    /**Split into two classes because GetMethod iterates over the different methods in a class.
     * If it was to find a Plando/Rando method without the dll being present it would die, even if it's not the requested method
     */
    
    public class RandoPlandoCompatibility
    {
        
        private RandoCompatibility _rando;
        private PlandoCompatibility _plando;

        private static readonly Dictionary<string, Vector2> CorniferPositions = new Dictionary<string, Vector2>()
        {
            ["Cliffs_01"] = new Vector2(125.6f, 91.8f),
            ["Crossroads_33"] = new Vector2(36.3f, 34.7f),
            ["Fungus1_06"] = new Vector2(160.3f, 3.7f),
            ["Fungus3_25"] = new Vector2(36.4f, 32.7f),
            ["Fungus2_18"] = new Vector2(9.7f, 35.7f),
            ["Ruins1_31"] = new Vector2(35.0f, 15.7f),
            ["Waterways_09"] = new Vector2(25.1f, 30.7f),
            ["Abyss_04"] = new Vector2(68.4f, 41.7f),
            ["Deepnest_East_03"] = new Vector2(7.6f, 60.8f),
            ["RestingGrounds_09"] = new Vector2(8.4f, 5.4f),
            ["Fungus1_24"] = new Vector2(53.4f, 22.7f),
            //TODO deepnest
            
            

        };
        
        public delegate void OnCorniferLocationHandler(string scene);
        public static OnCorniferLocationHandler OnCorniferLocation = (scene => { });
        public delegate void OnGrubLocationHandler(string scene);
        public static OnGrubLocationHandler OnGrubLocation = (scene => { });
        
        public RandoPlandoCompatibility()
        {
            _rando = new RandoCompatibility();
            _plando = new PlandoCompatibility();
        }
        
        
        private class RandoCompatibility
        {
            private Hook _setIntHook;
            private Hook _corniferLocationHook;

            
            public RandoCompatibility()
            {
                
                HookRando();
            }

            private void HookRando()
            {
                
                //These GetMethod is the reason why this is split up into two private subclasses
                
                Type giveItemActions = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");

                if (giveItemActions == null) return;

                BingoUI.Log("Hooking Rando");
                
                BingoUI.Log("Hooking GiveItemActions");
                _setIntHook = new Hook
                (
                    giveItemActions.GetMethod("GiveItem",BindingFlags.Public | BindingFlags.Static),
                    typeof(RandoCompatibility).GetMethod(nameof(FixRando))
                );

                Type createNewShiny = Type.GetType("RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0");

                BingoUI.Log("Hooking CreateNewShiny for Cornifer locations");
                
                _corniferLocationHook = new Hook
                (
                    createNewShiny.GetMethod("Process",BindingFlags.Public | BindingFlags.Instance),
                    typeof(RandoCompatibility).GetMethod(nameof(PatchRandoCornifer)), this
                );
            }

            public void PatchRandoCornifer(Action<RandomizerMod.Actions.RandomizerAction, string, Object> orig, 
                RandomizerMod.Actions.RandomizerAction self, string scene, Object changeObj)
            {
                
                orig(self, scene, changeObj);
                
                if(!CorniferPositions.Keys.Contains(scene))
                    return;
                
                Type createNewShiny = Type.GetType("RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0");
                float x = (float)createNewShiny.GetField("_x", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                float y = (float)createNewShiny.GetField("_y", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
               
                if( (CorniferPositions[scene] -  new Vector2(x, y)).magnitude > 3.0f)
                    return;
               
                BingoUI.Log("Patching rando cornifer");
               
                GameObject cornifer = GameObject.Find((string) createNewShiny
                    .GetField("_newShinyName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self));
                PlayMakerFSM shinyControl = cornifer.LocateMyFSM("Shiny Control");
                shinyControl.InsertMethod("Hero Down", 0, () => OnCorniferLocation.Invoke(scene));

            }
            public static void FixRando(Action<RandomizerMod.GiveItemActions.GiveAction, string, string, int> orig, RandomizerMod.GiveItemActions.GiveAction action, string item, string location, int geo)
            {
                orig(action, item, location, geo);
            
                Type logicManager = Type.GetType("RandomizerMod.Randomization.LogicManager, RandomizerMod3.0");
                MethodInfo getItemDef = logicManager.GetMethod("GetItemDef", BindingFlags.Static | BindingFlags.Public);
                Object reqDef = getItemDef.Invoke(null,new object[]{location});
                string pool = (string) reqDef.GetType().GetField("pool").GetValue(reqDef);
                
                if(pool == "Grub")
                    OnGrubLocation.Invoke(location);

                //Literally just let rando do its thing and increment or whatever, then call SetInt so the hook runs
            
                PlayerData pd = PlayerData.instance;
                switch (action)
                {
                    case RandomizerMod.GiveItemActions.GiveAction.WanderersJournal:
                        pd.SetInt("trinket1", pd.trinket1);
                        break;
                    case RandomizerMod.GiveItemActions.GiveAction.HallownestSeal:
                        pd.SetInt("trinket2", pd.trinket2);
                        break;
                    case RandomizerMod.GiveItemActions.GiveAction.KingsIdol:
                        pd.SetInt("trinket3", pd.trinket3);
                        break;
                    case RandomizerMod.GiveItemActions.GiveAction.ArcaneEgg:
                        pd.SetInt("trinket4", pd.trinket4);
                        break;
                    case RandomizerMod.GiveItemActions.GiveAction.Grub:
                        pd.SetInt("grubsCollected", pd.grubsCollected);
                        break;


                }
            }

        }
        
        private class PlandoCompatibility
        {
            
            private readonly GameObject _coroutineStarterObject;
            private readonly NonBouncer _coroutineStarter;

            private Hook _corniferLocationHook;

            public PlandoCompatibility()
            {
                CheckPlando();
            
                _coroutineStarterObject = new GameObject();
                _coroutineStarter = _coroutineStarterObject.AddComponent<NonBouncer>();
                UnityEngine.Object.DontDestroyOnLoad(_coroutineStarterObject);
            }
            public void CheckPlando()
            {
                /*Hooking plando is a huge hassle, because it has an internal hook we have to use
                 *(and so need to split the actual hooking up into two in order for it to not die), and
                 *because said hook is called before it actually does its thing so it needs to be delayed by a coroutine, which needs to not reference 
                 *plando, else when it gets compiled into a class it dies
                 * So Check=>Hook=>Start routine => delay => Actually do the hook stuff
                */
            
                Type t = Type.GetType("ItemChanger.GiveItemActions, ItemChanger");

            
                if (t == null) return;

                BingoUI.Log("Hooking Plando");

                HookPlando();


            }

        
            private void HookPlando()
            {
                ItemChanger.GiveItemActions.OnGiveItem += FixPlandoDelayStarter;
                
                Type createNewShiny = Type.GetType("ItemChanger.Actions.CreateNewShiny, ItemChanger");

                BingoUI.Log("Hooking CreateNewShiny for Cornifer locations");
                
                _corniferLocationHook = new Hook
                (
                    createNewShiny.GetMethod("Process",BindingFlags.Public | BindingFlags.Instance),
                    typeof(PlandoCompatibility).GetMethod(nameof(PatchPlandoCornifer)), this
                );
                
            }

        
            private void FixPlandoDelayStarter(ItemChanger.Item item, ItemChanger.Location location)
            {
                _coroutineStarter.StartCoroutine(FixPlandoDelay(item, location));
            }



            private IEnumerator FixPlandoDelay(Object item, Object location)
            {
                //The hook is called at the start of the method instead of when the item is actually given, so wait for plando to do its thing
                //Parameter has to be Object else it explodes because of ItemChanger parameter in the coroutine compiler generated class
                
                yield return null;

                FixPlando(item, location);
            }
        
            private void FixPlando(Object item, Object location){
            

                ItemChanger.Item castedItem = (ItemChanger.Item)item;
                ItemChanger.Location castedLocation = (ItemChanger.Location) location;
                if(castedLocation.pool == ItemChanger.Location.LocationPool.Grub)
                    OnGrubLocation.Invoke(castedLocation.sceneName);
            
                PlayerData pd = PlayerData.instance;
                ItemChanger.Item.GiveAction action = castedItem.action;
                switch (action)
                {
                    case ItemChanger.Item.GiveAction.WanderersJournal:
                        pd.SetInt("trinket1", pd.trinket1);
                        break;
                    case ItemChanger.Item.GiveAction.HallownestSeal:
                        pd.SetInt("trinket2", pd.trinket2);
                        break;
                    case ItemChanger.Item.GiveAction.KingsIdol:
                        pd.SetInt("trinket3", pd.trinket3);
                        break;
                    case ItemChanger.Item.GiveAction.ArcaneEgg:
                        pd.SetInt("trinket4", pd.trinket4);
                        break;
                    case ItemChanger.Item.GiveAction.Grub:
                        pd.SetInt("grubsCollected", pd.grubsCollected);
                        break;
                
                }

            }
            
            public void PatchPlandoCornifer(Action<ItemChanger.Actions.RandomizerAction, string, Object> orig, 
                ItemChanger.Actions.RandomizerAction self, string scene, Object changeObj)
            {
                
                orig(self, scene, changeObj);
                
                if(!CorniferPositions.Keys.Contains(scene))
                    return;
                
                Type createNewShiny = Type.GetType("ItemChanger.Actions.CreateNewShiny, ItemChanger");
                float x = (float)createNewShiny.GetField("_x", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                float y = (float)createNewShiny.GetField("_y", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
               
                if( (CorniferPositions[scene] -  new Vector2(x, y)).magnitude > 3.0f)
                    return;
               
                BingoUI.Log("Patching plando cornifer");
               
                GameObject cornifer = GameObject.Find((string) createNewShiny
                    .GetField("_newShinyName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self));
                PlayMakerFSM shinyControl = cornifer.LocateMyFSM("Shiny Control");
                shinyControl.InsertMethod("Hero Down", 0, () => OnCorniferLocation.Invoke(scene));

            }
        }
        
        
    }
    
    
}