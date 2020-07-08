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
        public Dictionary<MapZone, int> AreaGrubs = new Dictionary<MapZone, int>();

        [SerializeField]
        public Dictionary<string, bool> Cornifers = new Dictionary<string, bool>();
    }
}