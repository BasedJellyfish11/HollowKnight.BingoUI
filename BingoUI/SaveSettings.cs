using System;
using GlobalEnums;
using Modding;
using SeanprCore;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BingoUI
{
    [Serializable]
    public class SaveSettings : ModSettings
    {
        //SaveSettings doesn't save a Dictionary or an array correctly (or I just can't manage it) so I can't do the original idea of Dictionary<MapZone,int>.
        //Instead here's 80 million ints named as the MapZones, because those do save correctly

        //This is a fucking cry for help btw. Please make the Dictionary work or just tell me how to

        public int CLIFFS;
        public int TOWN;
        public int CROSSROADS;
        public int GREEN_PATH;
        public int ROYAL_GARDENS;
        public int FOG_CANYON;
        public int WASTES;
        public int DEEPNEST;
        public int MINES;
        public int RESTING_GROUNDS;
        public int CITY;
        public int ABYSS;
        public int WHITE_PALACE;
        public int WATERWAYS;
      
        public int dreamTreesCompleted;

        
        [SerializeField]
        public GrubMap areaGrubs = new GrubMap();
        
        [SerializeField]
        public CorniferMap cornifers = new CorniferMap();
    }
    
    [Serializable]
    public class GrubMap : SerializableDictionary<MapZone, int>
    {
        public GrubMap()
        {
            foreach (MapZone mapZone in Enum.GetValues(typeof(MapZone)))
            {
                Add(mapZone, 0);
                    
            }
        }
    }

    [Serializable]
    public class CorniferMap : SerializableDictionary<Scene, bool>
    {
            
    }
        
}