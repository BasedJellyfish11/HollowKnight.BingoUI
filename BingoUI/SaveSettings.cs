using System;
using System.Collections.Generic;
using GlobalEnums;
using Modding;
using UnityEngine;

namespace BingoUI
{
    [Serializable]
    public class SaveSettings : ModSettings
    {
        public int DreamTreesCompleted;

        [SerializeField]
        public Dictionary<MapZone, int> AreaGrubs = new GrubMap();

        [SerializeField]
        public Dictionary<string, bool> Cornifers = new Dictionary<string, bool>();
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