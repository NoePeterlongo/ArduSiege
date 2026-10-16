// BlockNames — nommage des blocs (experience branche nommage-blocs).
//
// Objectif : identifier les blocs depuis Lua sans dependre du systeme vanilla de
// variables (« name <TAB> guid ») :
//   - le joueur nomme un bloc (console : `script name <nom>`, bloc selectionne via
//     Advanced Building ou simplement vise par la souris) ;
//   - le nom est persiste dans BlockNames.tsv (dossier data du mod) et BINDE au GUID
//     du bloc — le GUID survit aux sauvegardes/rechargements (c'est leur role) ;
//   - cote Lua : machine:block("nom") -> bloc ou nil ; machine:named() -> table.
//
// Modele v0 (experiment) : UN namespace global (pas par machine) — un nom designe
// un GUID ; la resolution se fait dans la machine courante (un GUID etrang a la
// machine ne resolve pas). Rebond d'un nom = remplacement silencieux + log.
//
// NB CLR 2.0 : pas de Guid.TryParse (net40) -> new Guid(string) + catch.

using System;
using System.Collections.Generic;
using Modding;

namespace ScriptingMod
{
    internal static class BlockNames
    {
        private const string FILE = "BlockNames.tsv";

        private static Dictionary<string, Guid> s_Names;
        private static bool s_Loaded;

        private static void EnsureLoaded()
        {
            if (s_Loaded)
            {
                return;
            }
            s_Names = new Dictionary<string, Guid>();
            s_Loaded = true;
            try
            {
                if (!Modding.ModIO.ExistsFile(FILE, data: true))
                {
                    return;
                }
                string content = Modding.ModIO.ReadAllText(FILE, data: true);
                if (content == null)
                {
                    return;
                }
                foreach (string rawLine in content.Split('\n'))
                {
                    string line = rawLine.Trim().TrimStart('#');
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    int tab = line.IndexOf('\t');
                    if (tab <= 0)
                    {
                        continue;
                    }
                    Guid guid;
                    try
                    {
                        guid = new Guid(line.Substring(tab + 1).Trim());
                    }
                    catch (FormatException)
                    {
                        continue;
                    }
                    s_Names[line.Substring(0, tab).Trim()] = guid;
                }
            }
            catch (Exception)
            {
                // fichier illisible : on repart d'une table vide (sauvegarde au prochain Set)
            }
        }

        private static void Save()
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("# noms de blocs : nom <TAB> GUID (un nom = un bloc)\n");
                foreach (KeyValuePair<string, Guid> kv in s_Names)
                {
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString()).Append('\n');
                }
                Modding.ModIO.WriteAllText(FILE, sb.ToString(), data: true);
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] ERREUR sauvegarde BlockNames.tsv : " + ex.Message);
            }
        }

        /// <summary>Recupere le GUID binde a un nom.</summary>
        internal static bool TryGet(string name, out Guid guid)
        {
            EnsureLoaded();
            return s_Names.TryGetValue(name, out guid);
        }

        /// <summary>Binde un nom a un GUID (remplace l'ancien porteur du nom).</summary>
        internal static void Set(string name, Guid guid)
        {
            EnsureLoaded();
            bool remplace = s_Names.ContainsKey(name);
            s_Names[name] = guid;
            Save();
            if (remplace)
            {
                ModConsole.Log("[ScriptingMod] nom \"" + name + "\" rebine (ancien bloc remplace)");
            }
        }

        /// <summary>Supprime un nom.</summary>
        internal static bool Remove(string name)
        {
            EnsureLoaded();
            bool ok = s_Names.Remove(name);
            if (ok)
            {
                Save();
            }
            return ok;
        }

        /// <summary>Tous les liens nom -> GUID.</summary>
        internal static IDictionary<string, Guid> All
        {
            get
            {
                EnsureLoaded();
                return s_Names;
            }
        }
    }
}
