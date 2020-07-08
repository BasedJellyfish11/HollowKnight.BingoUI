using System;
using Modding;
using UnityEngine;

namespace BingoUI
{
    [Serializable]
    public class SaveSettings : ModSettings
    {
        public int DreamTreesCompleted;
            
        [SerializeField]
        public SerializableIntDictionary AreaGrubs = new SerializableIntDictionary();
        
        [SerializeField]
        public SerializableBoolDictionary Cornifers = new SerializableBoolDictionary();
    }
}
