
using System.Collections;
using UnityEngine;

namespace BingoUI
{
    using System;
    using System.Reflection;
    using MonoMod.RuntimeDetour;
    public class RandoPlandoCompatibility
    {
        
        private Hook _rando;
        
        private readonly GameObject _coroutineStarterObject;
        private readonly NonBouncer _coroutineStarter;
        public RandoPlandoCompatibility()
        {
            HookRando();
            CheckPlando();
            
            _coroutineStarterObject = new GameObject();
            _coroutineStarter = _coroutineStarterObject.AddComponent<NonBouncer>();
            UnityEngine.Object.DontDestroyOnLoad(_coroutineStarterObject);
        }
        
        
        public void HookRando()
        {
            Type t = Type.GetType("RandomizerMod.GiveItemActions, RandomizerMod3.0");

            if (t == null) return;

            BingoUI.Log("Hooking Rando");

            _rando = new Hook
            (
                t.GetMethod("GiveItem",BindingFlags.Public | BindingFlags.Static),
                typeof(RandoPlandoCompatibility).GetMethod(nameof(FixRando))
            );
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


        public static void FixRando(Action<RandomizerMod.GiveItemActions.GiveAction, string, string, int> orig, RandomizerMod.GiveItemActions.GiveAction action, string item, string location, int geo)
        {
            orig(action, item, location, geo);
            
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
}
