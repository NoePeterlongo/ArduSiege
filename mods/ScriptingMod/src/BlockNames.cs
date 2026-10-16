// BlockNames — utilitaires du nommage des blocs (experience nommage-blocs).
//
// Le DICTIONNAIRE nom -> position vit dans les donnees du bloc Microcontroller
// (OnSave/OnLoad, cf. MicrocontrollerScript) : namespace par machine, noms
// embarques dans le .bsg (voyagent avec une machine partagee).
//
// CLE = POSITION LOCALE du bloc dans la machine (millimetres entiers, "x,y,z").
// Pourquoi pas le GUID : Machine.AddBlocksFromInfo REGENERE tous les GUID a chaque
// chargement de machine (decomp l.3232, remap interne des refs) — le GUID n'est
// stable qu'au sein d'une session. La position locale vient du .bsg
// (BlockInfo.Position) : stable aux chargements et a travers la sim (la machine de
// sim est une copie Instantiate — decomp l.1911). Limite : DEPLACER le bloc casse
// son nom (acte visible, contrairement a la regen silencieuse des GUID).
//
// NB stockage en millimetres ENTIERS : culture-free (pas de formatage decimal).

using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace ScriptingMod
{
    internal static class BlockNames
    {
        // tolerance de resolution en unites monde (bloc vanilla = 1.0)
        private const float EPS = 0.05f;

        // --- format persiste (millimetres entiers "x,y,z", culture-free) ---

        internal static string ToMm(Vector3 pos)
        {
            int x = (int)Math.Round(pos.x * 1000f);
            int y = (int)Math.Round(pos.y * 1000f);
            int z = (int)Math.Round(pos.z * 1000f);
            return x + "," + y + "," + z;
        }

        internal static bool TryParseMm(string s, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (s == null)
            {
                return false;
            }
            string[] parts = s.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }
            int x, y, z;
            if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)
                || !int.TryParse(parts[2], out z))
            {
                return false;
            }
            pos = new Vector3(x * 0.001f, y * 0.001f, z * 0.001f);
            return true;
        }

        /// <summary>
        /// Resolution d'une position dans la machine : premier bloc dont la position
        /// locale est a moins de EPS (sim ou construction selon l'etat).
        /// </summary>
        internal static Modding.Blocks.Block Resolve(Modding.Blocks.PlayerMachine machine, Vector3 pos)
        {
            if (machine == null)
            {
                return null;
            }
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? machine.SimulationBlocks : machine.BuildingBlocks;
            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb == null)
                {
                    continue;
                }
                Vector3 p = bb.transform.localPosition;
                if (Math.Abs(p.x - pos.x) < EPS && Math.Abs(p.y - pos.y) < EPS
                    && Math.Abs(p.z - pos.z) < EPS)
                {
                    return block;
                }
            }
            return null;
        }
    }
}
