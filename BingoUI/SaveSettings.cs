using System;
using Modding;

namespace BingoUI
{
    [Serializable]
    public class SaveSettings : ModSettings
    {
        //SaveSettings doesn't save a Dictionary or an array correctly (or I just can't manage it) so I can't do the original idea of Dictionary<MapZone,int>.
        //Instead here's 80 million ints named as the MapZones, because those do save correctlyç
        
        //This is a fucking cry for help btw. Please make the Dictionary work or just tell me how to
        
        public int NONE;
        public int TEST_AREA;
        public int KINGS_PASS;
        public int CLIFFS;
        public int TOWN;
        public int CROSSROADS;
        public int GREEN_PATH;
        public int ROYAL_GARDENS;
        public int FOG_CANYON;
        public int WASTES;
        public int DEEPNEST;
        public int HIVE;
        public int BONE_FOREST;
        public int PALACE_GROUNDS;
        public int MINES;
        public int RESTING_GROUNDS;
        public int CITY;
        public int DREAM_WORLD;
        public int COLOSSEUM;
        public int ABYSS;
        public int ROYAL_QUARTER;
        public int WHITE_PALACE;
        public int SHAMAN_TEMPLE;
        public int WATERWAYS;
        public int QUEENS_STATION;
        public int OUTSKIRTS;
        public int KINGS_STATION;
        public int MAGE_TOWER;
        public int TRAM_UPPER;
        public int TRAM_LOWER;
        public int FINAL_BOSS;
        public int SOUL_SOCIETY;
        public int ACID_LAKE;
        public int NOEYES_TEMPLE;
        public int MONOMON_ARCHIVE;
        public int MANTIS_VILLAGE;
        public int RUINED_TRAMWAY;
        public int DISTANT_VILLAGE;
        public int ABYSS_DEEP;
        public int ISMAS_GROVE;
        public int WYRMSKIN;
        public int LURIENS_TOWER;
        public int LOVE_TOWER;
        public int GLADE;
        public int BLUE_LAKE;
        public int PEAK;
        public int JONI_GRAVE;
        public int OVERGROWN_MOUND;
        public int CRYSTAL_MOUND;
        public int BEASTS_DEN;
        public int GODS_GLORY;
        public int GODSEEKER_WASTE;
        
        
        
        //public GrubMap areaGrubs = new GrubMap();
    }

    /*
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
    */
}