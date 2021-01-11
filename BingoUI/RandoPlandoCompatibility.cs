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

// NOTE: Heavy comments due to hooking rando becoming the standard for modding and this file being referenced to new modders in #modding-development
namespace BingoUI
{
    /*
     * Split into two classes because GetMethod iterates over the different methods in a class.
     * If it was to find a Plando/Rando method during this iteration without the dll being present it would die due to not knowing how to evaluate the arguments or even the method itself,
     * even if it's not the requested method. This is only needed because we're hooking two different mods at once, so one can exist without the other
     */
    public class RandoPlandoCompatibility : IDisposable
    {
        private readonly RandoCompatibility _rando;
        private readonly PlandoCompatibility _plando;

        /* Shinies are generated through the Randomizer *Process* method ( https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/Actions/CreateNewShiny.cs#L25 ),
         *  which is very generic and knows nothing about the pool etc.
         *  BingoUI wants to know which shinies are cornifer for tracking Cornifer Locations, so we map them then when a shiny is created check if it's, in fact, a Cornifer shiny.
         *  This kinda sucks because I believe Process runs every time you enter a room, but it's the best way I've found to modify rando shinies (changing the shiny fsm for example, which is what we want in this case).
         */
        
        private static readonly Dictionary<string, Vector2> CorniferPositions = new Dictionary<string, Vector2>()  {
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
            ["Fungus2_25"] = new Vector2(49.2f, 23.9f),
            ["Deepnest_01b"] = new Vector2(7.2f, 4.9f)
        };

        // This is a bunch of event declarations so that we can run code on the main class whenever something we wanted to track and isn't covered by SetBool / SetInt is done by rando
        // In reality this is probably unneeded for any implementation that isn't BingoUI. However BingoUI wants to know cornifer locations and what items were Grubs previously
        public delegate void OnCorniferLocationHandler(string scene);

        public static OnCorniferLocationHandler OnCorniferLocation;

        public delegate void OnGrubLocationHandler(string scene);

        public static OnGrubLocationHandler OnGrubLocation;

        public delegate bool OnGrubObtainHandler();

        public static OnGrubObtainHandler OnGrubObtain;

        // This constructor will hook both Rando and Plando. Remember they are split into two classes due to the GetMethod issue mentioned at the start of the file
        public RandoPlandoCompatibility()
        {
            _rando = new RandoCompatibility();
            _plando = new PlandoCompatibility();
        }

        // Hooks are unmanaged so we need a explicit Dispose call
        public void Dispose()
        {
            _rando?.Dispose();
            _plando?.Dispose();
        }

        // This is the class in charge of Hooking rando to make it follow proper compatibility procedure. Note the Plando class will be very similar
        private class RandoCompatibility : IDisposable
        {
            // These two are the two hooks for the methods BingoUI wants
            private Hook _giveActionsSetIntHook;
            private Hook _corniferLocationHook;
            
            // This is just a fancy constructor
            public RandoCompatibility() => HookRando();

            // This method creates the hooks
            private void HookRando()
            {
                /*
                 * First we need to navigate the randomizer namespace to get the class and therefore the type
                 * This is done by using Type.GetType ("Namespace , AssemblyName")
                 * AssemblyName will always be RandomizerMod3.0 if it's rando you're hooking. If you're hooking another mod, it's the mod's name on the top left in the title screen
                 * Namespace is usually figured out through going to Github and checking the code of the mod
                 *
                 */
                
                /*
                 * As an example, here we'll obtain both GiveItemActions, and CreateNewShiny, found respectively on
                 * https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/GiveItemActions.cs
                 * (Namespace RandomizerMod + . + Class name => "RandomizerMod.GiveItemActions, RandomizerMod3.0")
                 * https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/Actions/CreateNewShiny.cs
                 * (Namespace RandomizerMod.Actions + . + Class name => ""RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0" )
                 */
                
                Type giveItemActions = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");
                Type createNewShiny = Type.GetType("RandomizerMod.Actions.CreateNewShiny, RandomizerMod3.0");
            
                // After doing this you have to check the types aren't null before interacting with them. This ensures the mod is actually present and loaded, instead of dying if
                // the user is using your mod without the hooked one
                
                if (giveItemActions == null || createNewShiny == null)
                    return;

                BingoUI.Log("Hooking Rando");

                BingoUI.Log("Hooking GiveItemActions");
                
                /*  This is the hook constructor.
                 *  It's Hook( Method you're hooking , Method you're replacing it with)
                 *  Here we will replace GiveItemAction's GiveItem() method with out own FixRando
                 *  Since this takes two MethodInfo, you need to fetch your own method through Reflection too, which is slightly sad imo
                 */
                _giveActionsSetIntHook = new Hook
                (
                    giveItemActions.GetMethod("GiveItem"), // This is this method https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/GiveItemActions.cs#L46
                    typeof(RandoCompatibility).GetMethod(nameof(FixRando)) // This is our own. Look below
                );

                BingoUI.Log("Hooking CreateNewShiny for Cornifer locations");

                _corniferLocationHook = new Hook
                (
                    createNewShiny.GetMethod("Process"), // This is this method https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/Actions/CreateNewShiny.cs#L25
                    typeof(RandoCompatibility).GetMethod(nameof(PatchRandoCornifer)) // This is our own. Look below
                );
                
            }
            
            /** This is the method BingoUI uses to modify the shinies rando creates ( in order to track the cornifer locations)
             *  For any one method that you're using as a Hook destination, the parameters have to always have the same structure:
             *  An Action (if the hooked method doesn't return anything) or a Function (if it does), with its own argument types between <>
             *  Then the arguments that Action / Function takes
             *  In this case *Process* from Rando is an Action (it's a void method).
             *  Because it's an instance method, it takes a RandomizerAction as first parameter (its own class)
             *  After that it takes the *scene* string and the *changeObj* Object
             *  Reminder the *Process* method is still at https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/Actions/CreateNewShiny.cs#L25
             */
            
            [UsedImplicitly]
            public static void PatchRandoCornifer
            (
                Action<RandomizerAction, string, object> orig,
                RandomizerAction self,
                string scene,
                object changeObj
            )
            {
                orig(self, scene, changeObj); // This is a call to the originally hooked method to make it run. If you don't do this the original method will never run, which is something BingoUI does not want (but you might)

                // Below this it's mostly things not related to hooking but to BingoUI execution so I won't bother adding comments. 
                
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
            
            /** This is the method BingoUI uses to make rando assigning values directly to fields rather than using the proper Reflection procedure work with other mods
             *  Just as above, for any one method that you're using as a Hook destination, the parameters have to always have the same structure:
             *  An Action (if the hooked method doesn't return anything) or a Function (if it does), with its own argument types between <>
             *  Then the arguments that Action / Function takes
             *  In this case *GiveItem* from Rando is an Action (it's a void method).
             *  Because it's a static method, it doesn't take an instance of itself as first parameter (the usual self). Instead it just goes straight to its own arguments
             *  Reminder the *GiveItem* method is still at https://github.com/homothetyhk/HollowKnight.RandomizerMod/blob/e75fce28f373e178d12dfda007f2a1aae19cfba3/RandomizerMod3.0/GiveItemActions.cs#L46
             */
            
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
                orig(action, item, location, geo); // This is a call to the originally hooked method to make it run. If you don't do this the original method will never run, which is something BingoUI does not want (but you might)
                
                // Below this it's mostly things not related to hooking but to BingoUI execution so comments won't be as in-depth. 

                // Obtain the reqDef, which is how Rando manages which pool objects belong to. This is used for Grub locations in our case
                
                Type logicManager = Type.GetType("RandomizerMod.Randomization.LogicManager, RandomizerMod3.0");

                MethodInfo getItemDef = logicManager?.GetMethod("GetItemDef");

                if (getItemDef == null)
                    return;

                object reqDef = null;

                try
                {
                    reqDef = getItemDef.Invoke(null, new object[] {location}); // Shops are made to throw so catch it if it's a shop
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
                
                string pool = (string) reqDef?.GetType().GetField("pool").GetValue(reqDef); // Finally obtain the pool

                if (pool == "Grub")
                    OnGrubLocation?.Invoke(location);

                // This part is the one that checks what rando assigned then runs SetInt or SetBool so that other mods catch on
                // Literally just let rando do its thing and increment or whatever, then call SetInt with the same value so the hooked things run
                PlayerData pd = PlayerData.instance;

                switch (action)
                {
                    case GiveItemActions.GiveAction.WanderersJournal:
                        pd.SetInt(nameof(pd.trinket1), pd.trinket1);
                        break;
                    case GiveItemActions.GiveAction.HallownestSeal:
                        pd.SetInt(nameof(pd.trinket2), pd.trinket2);
                        break;
                    case GiveItemActions.GiveAction.KingsIdol:
                        pd.SetInt(nameof(pd.trinket3), pd.trinket3);
                        break;
                    case GiveItemActions.GiveAction.ArcaneEgg:
                        pd.SetInt(nameof(pd.trinket4), pd.trinket4);
                        break;
                    case GiveItemActions.GiveAction.Grub:
                        if (OnGrubObtain?.Invoke() ?? true)
                            pd.SetInt(nameof(pd.grubsCollected), pd.grubsCollected);
                        break;
                    case GiveItemActions.GiveAction.Kingsoul:
                        pd.SetBool(nameof(pd.gotCharm_36), true);
                        break;
                }
            }

            public void Dispose()
            {
                _giveActionsSetIntHook?.Dispose();
                _corniferLocationHook?.Dispose();
                //// _lemmSellAllHook?.Dispose();
                
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
                        pd.SetInt(nameof(pd.trinket1), pd.trinket1);
                        break;
                    case Item.GiveAction.HallownestSeal:
                        pd.SetInt(nameof(pd.trinket2), pd.trinket2);
                        break;
                    case Item.GiveAction.KingsIdol:
                        pd.SetInt(nameof(pd.trinket3), pd.trinket3);
                        break;
                    case Item.GiveAction.ArcaneEgg:
                        pd.SetInt(nameof(pd.trinket4), pd.trinket4);
                        break;
                    case Item.GiveAction.Grub:
                        if (OnGrubObtain?.Invoke() ?? true)
                            pd.SetInt(nameof(pd.grubsCollected), pd.grubsCollected);
                        break;
                    case Item.GiveAction.Kingsoul:
                        pd.SetBool(nameof(pd.gotCharm_36), true);
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