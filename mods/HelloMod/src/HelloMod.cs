using Modding;
using UnityEngine;

namespace HelloMod
{
    // Point d'entree du mod : instancie par le mod loader.
    // Charge quand le joueur entre dans le multiverse ou dans un niveau
    // (sauf si <LoadInTitleScreen /> est present dans Mod.xml).
    public class HelloModEntryPoint : ModEntryPoint
    {
        public override void OnLoad()
        {
            ModConsole.Log("[HelloMod] Bonjour ! Le mod est charge.");
            Debug.Log("[HelloMod] OnLoad execute.");
        }
    }
}
