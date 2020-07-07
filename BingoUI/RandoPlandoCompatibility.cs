using System.Collections;
using UnityEngine;

namespace BingoUI
{
    using System;
    using System.Reflection;
    using MonoMod.RuntimeDetour;
    
    
    /**Split into two classes because GetMethod, used to hook rando, iterates over the different methods in a class.
     * If it was to find a Plando method it would die, and trying to respect a method order in the file of Rando methods before Plando ones seemed pretty bad
     */
    
    public class RandoPlandoCompatibility
    {
        
        private RandoCompatibility _rando;
        private PlandoCompatibility _plando;
        
        
        public RandoPlandoCompatibility()
        {
            _rando = new RandoCompatibility();
            _plando = new PlandoCompatibility();
        }
        
        
        private class RandoCompatibility
        {
            private Hook _randoHook;
            public RandoCompatibility()
            {
                
                HookRando();
            }

            private void HookRando()
            {
                Type t = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");

                if (t == null) return;

                BingoUI.Log("Hooking Rando");

                
                //This GetMethod is the reason why this is split up into two private subclasses
                
                _randoHook = new Hook
                (
                    t.GetMethod("GiveItem",BindingFlags.Public | BindingFlags.Static),
                    typeof(RandoCompatibility).GetMethod(nameof(FixRando))
                );
            }
        
            public static void FixRando(Action<RandomizerMod.GiveItemActions.GiveAction, string, string, int> orig, RandomizerMod.GiveItemActions.GiveAction action, string item, string location, int geo)
            {
                orig(action, item, location, geo);
            
                Type t = Type.GetType("RandomizerMod.Randomization.LogicManager, RandomizerMod3.0");
                BingoUI.Log("a");
                BingoUI.Log(t.Name);
                MethodInfo m = t.GetMethod("GetItemDef", BindingFlags.Static | BindingFlags.Public);
                BingoUI.Log("b");
                Object a = m.Invoke(null,new object[]{item});
                BingoUI.Log("c");
                BingoUI.Log(a.GetType().GetField("pool").GetValue(a));

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
            }

        
            private void FixPlandoDelayStarter(ItemChanger.Item item, ItemChanger.Location location)
            {
                _coroutineStarter.StartCoroutine(FixPlandoDelay(item));
            }



            private IEnumerator FixPlandoDelay(Object item)
            {
                //The hook is called at the start of the method instead of when the item is actually given, so wait for plando to do its thing
                yield return null;

                FixPlando(item);
            }
        
            private void FixPlando(Object item){
            

                ItemChanger.Item castedItem = (ItemChanger.Item)item;
            
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
        }
    }
}