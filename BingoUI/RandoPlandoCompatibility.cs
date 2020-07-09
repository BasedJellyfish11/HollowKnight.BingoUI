using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemChanger;
using JetBrains.Annotations;
using ModCommon.Util;
using MonoMod.RuntimeDetour;
using RandomizerMod.Actions;
using UnityEngine;
using GiveItemActions = RandomizerMod.GiveItemActions;
using UObject = UnityEngine.Object;

namespace BingoUI
{
    /**
     * Split into two classes because GetMethod iterates over the different methods in a class.
     * If it was to find a Plando/Rando method without the dll being present it would die, even if it's not the requested method
     */
    public class RandoPlandoCompatibility : IDisposable
    {
        private readonly RandoCompatibility _rando;
        private readonly PlandoCompatibility _plando;

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
            // TODO deepnest
        };

        public delegate void OnCorniferLocationHandler(string scene);

        public static OnCorniferLocationHandler OnCorniferLocation;

        public delegate void OnGrubLocationHandler(string scene);

        public static OnGrubLocationHandler OnGrubLocation;

        public delegate bool OnGrubObtainHandler();

        public static OnGrubObtainHandler OnGrubObtain;

        public RandoPlandoCompatibility()
        {
            _rando = new RandoCompatibility();
            _plando = new PlandoCompatibility();
        }

        public void Dispose()
        {
            _rando?.Dispose();
            _plando?.Dispose();
        }

        private class RandoCompatibility : IDisposable
        {
            private Hook _setIntHook;
            private Hook _corniferLocationHook;

            public RandoCompatibility() => HookRando();

            private void HookRando()
            {
                /*
                 * MethodInfo::GetMethod issue mentioned above shown here.
                 */
                Type giveItemActions = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");
                Type createNewShiny = Type.GetType("RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0");

                if (giveItemActions == null || createNewShiny == null) 
                    return;

                BingoUI.Log("Hooking Rando");

                BingoUI.Log("Hooking GiveItemActions");

                _setIntHook = new Hook
                (
                    giveItemActions.GetMethod("GiveItem"),
                    typeof(RandoCompatibility).GetMethod(nameof(FixRando))
                );

                BingoUI.Log("Hooking CreateNewShiny for Cornifer locations");

                _corniferLocationHook = new Hook
                (
                    createNewShiny.GetMethod("Process"),
                    typeof(RandoCompatibility).GetMethod(nameof(PatchRandoCornifer))
                );
            }

            [UsedImplicitly]
            public static void PatchRandoCornifer
            (
                Action<RandomizerAction, string, object> orig,
                RandomizerAction self,
                string scene,
                object changeObj
            )
            {
                orig(self, scene, changeObj);

                if (!CorniferPositions.Keys.Contains(scene))
                    return;

                Type createNewShiny = Type.GetType("RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0");

                if (createNewShiny == null)
                    return;

                // ReSharper disable PossibleNullReferenceException
                float x = (float) createNewShiny.GetField("_x", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                float y = (float) createNewShiny.GetField("_y", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                // ReSharper enable PossibleNullReferenceException

                if ((CorniferPositions[scene] - new Vector2(x, y)).magnitude > 3.0f)
                    return;

                BingoUI.Log("Patching rando cornifer");

                GameObject cornifer = GameObject.Find
                (
                    (string) createNewShiny
                             .GetField("_newShinyName", BindingFlags.NonPublic | BindingFlags.Instance)?
                             .GetValue(self)
                );

                PlayMakerFSM shinyControl = cornifer.LocateMyFSM("Shiny Control");

                shinyControl.InsertMethod("Hero Down", 0, () => OnCorniferLocation.Invoke(scene));
            }

            [UsedImplicitly]
            public static void FixRando
            (
                Action<GiveItemActions.GiveAction, string, string, int> orig,
                GiveItemActions.GiveAction action,
                string item,
                string location,
                int geo
            )
            {
                orig(action, item, location, geo);
                

                Type logicManager = Type.GetType("RandomizerMod.Randomization.LogicManager, RandomizerMod3.0");

                MethodInfo getItemDef = logicManager?.GetMethod("GetItemDef");

                if (getItemDef == null)
                    return;

                object reqDef = null;

                try
                {
                    reqDef = getItemDef.Invoke(null, new object[] {location});
                }
                catch (TargetInvocationException e)
                {
                    // If it's not a shop, re-throw.
                    if (!(e.InnerException is KeyNotFoundException))
                    {
                        BingoUI.Log($"Inner exception was not KeyNotFoundException, instead was {e.InnerException}");

                        throw;
                    }
                }
                
                string pool = (string) reqDef?.GetType().GetField("pool").GetValue(reqDef);

                if (pool == "Grub")
                    OnGrubLocation?.Invoke(location);

                // Literally just let rando do its thing and increment or whatever, then call SetInt so the hook runs
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
                        if (OnGrubObtain?.Invoke() ?? true)
                            pd.SetInt("grubsCollected", pd.grubsCollected);
                        break;
                }
            }

            public void Dispose()
            {
                _setIntHook?.Dispose();
                _corniferLocationHook?.Dispose();
            }
        }

        private class PlandoCompatibility : IDisposable
        {
            private readonly NonBouncer _coroutineStarter;

            private Hook _corniferLocationHook;

            private bool _plandoFound;

            public PlandoCompatibility()
            {
                CheckPlando();

                var go = new GameObject();

                _coroutineStarter = go.AddComponent<NonBouncer>();

                UObject.DontDestroyOnLoad(go);
            }

            private void CheckPlando()
            {
                /*
                 * Hooking plando is a huge hassle, because it has an internal hook we have to use
                 * (and so need to split the actual hooking up into two in order for it to not die), and
                 * because said hook is called before it actually does its thing so it needs to be delayed by a coroutine, which needs to not reference 
                 * plando, else when it gets compiled into a class it dies
                 * So Check => Hook => Start routine => Delay => Actually do the hook stuff
                */

                Type t = Type.GetType("ItemChanger.GiveItemActions, ItemChanger");

                if (t == null) return;

                _plandoFound = true;

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
                    createNewShiny?.GetMethod("Process"),
                    typeof(PlandoCompatibility).GetMethod(nameof(PatchPlandoCornifer))
                );
            }

            private void FixPlandoDelayStarter(Item item, Location location)
            {
                _coroutineStarter.StartCoroutine(FixPlandoDelay(item, location));
            }

            private static IEnumerator FixPlandoDelay(object item, object location)
            {
                // The hook is called at the start of the method instead of when the item is actually given, so wait for plando to do its thing
                // Parameter has to be object else it explodes because of ItemChanger field in the coroutine compiler generated class

                yield return null;

                FixPlando(item, location);
            }

            private static void FixPlando(object item, object location)
            {
                var castedItem = (Item) item;
                var castedLocation = (Location) location;

                if (castedLocation.pool == Location.LocationPool.Grub)
                    OnGrubLocation?.Invoke(castedLocation.sceneName);

                PlayerData pd = PlayerData.instance;
                Item.GiveAction action = castedItem.action;

                switch (action)
                {
                    case Item.GiveAction.WanderersJournal:
                        pd.SetInt("trinket1", pd.trinket1);
                        break;
                    case Item.GiveAction.HallownestSeal:
                        pd.SetInt("trinket2", pd.trinket2);
                        break;
                    case Item.GiveAction.KingsIdol:
                        pd.SetInt("trinket3", pd.trinket3);
                        break;
                    case Item.GiveAction.ArcaneEgg:
                        pd.SetInt("trinket4", pd.trinket4);
                        break;
                    case Item.GiveAction.Grub:
                        if (OnGrubObtain?.Invoke() ?? true)
                            pd.SetInt("grubsCollected", pd.grubsCollected);
                        break;
                }
            }

            [UsedImplicitly]
            public static void PatchPlandoCornifer
            (
                Action<ItemChanger.Actions.RandomizerAction, string, object> orig,
                ItemChanger.Actions.RandomizerAction self,
                string scene,
                object changeObj
            )
            {
                orig(self, scene, changeObj);

                if (!CorniferPositions.Keys.Contains(scene))
                    return;

                Type createNewShiny = Type.GetType("ItemChanger.Actions.CreateNewShiny, ItemChanger");

                if (createNewShiny == null)
                    return;

                // ReSharper disable once PossibleNullReferenceException
                float x = (float) createNewShiny.GetField("_x", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(self);
                // ReSharper disable once PossibleNullReferenceException
                float y = (float) createNewShiny.GetField("_y", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(self);

                if ((CorniferPositions[scene] - new Vector2(x, y)).magnitude > 3.0f)
                    return;

                BingoUI.Log("Patching plando cornifer");

                GameObject cornifer = GameObject.Find
                (
                    (string) createNewShiny
                             .GetField("_newShinyName", BindingFlags.NonPublic | BindingFlags.Instance)
                             ?.GetValue(self)
                );

                PlayMakerFSM shinyControl = cornifer.LocateMyFSM("Shiny Control");
                shinyControl.InsertMethod("Hero Down", 0, () => OnCorniferLocation?.Invoke(scene));
            }

            private void UnhookPlando()
            {
                // ReSharper disable once DelegateSubtraction
                ItemChanger.GiveItemActions.OnGiveItem -= FixPlandoDelayStarter;
            }

            public void Dispose()
            {
                _coroutineStarter.StopAllCoroutines();

                UObject.Destroy(_coroutineStarter.gameObject);

                _corniferLocationHook?.Dispose();

                if (_plandoFound)
                    UnhookPlando();
            }
        }
    }
}