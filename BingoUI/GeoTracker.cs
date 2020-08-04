using System.Reflection;

namespace BingoUI
{
    public static class GeoTracker
    {
        private static readonly FieldInfo geoCounterCurrent = typeof(GeoCounter).GetField("counterCurrent", BindingFlags.NonPublic | BindingFlags.Instance);
        
        internal static void CheckGeoSpent(On.GeoCounter.orig_TakeGeo orig, GeoCounter self, int geo)
        {
            orig(self, geo);

            if (GameManager.instance.GetSceneNameString() == "Fungus3_35" && PlayerData.instance.bankerAccountPurchased)
            {
                return;
            }

            BingoUI._settings.spentGeo += geo;
        }

        public static void UpdateGeoText(On.GeoCounter.orig_Update orig, GeoCounter self)
        {
            orig(self);
            self.geoTextMesh.text = $"{geoCounterCurrent.GetValue(self)} ({BingoUI._settings.spentGeo} spent)";
        }
    }
}
