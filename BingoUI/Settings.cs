using System;
using System.Collections.Generic;
using GlobalEnums;
using Modding;

namespace BingoUI
{
    [Serializable]
    public class SaveSettings : ModSettings
    {
        public int DreamTreesCompleted;

        public Dictionary<MapZone, int> AreaGrubs = new GrubMap();

        public HashSet<string> Cornifers = new HashSet<string>();
        
        public HashSet<(string,string)> Devouts = new HashSet<(string, string)>();
    }

    
    [Serializable]
    public class GlobalSettings : ModSettings
    {
        public bool alwaysDisplay;
    }

    public class GrubMap : Dictionary<MapZone, int>
    {
        public GrubMap()
        {
            foreach (MapZone mz in Enum.GetValues(typeof(MapZone)))
                this[mz] = 0;
        }
    }
}