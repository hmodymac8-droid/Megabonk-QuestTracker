using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts._Data.Tomes;
using Assets.Scripts.Actors.Player;

namespace QuestTracker;

/// <summary>
/// "Level the Cursed Tome to 10", "Level the Axe to 10", "Reach level 30 as Calcium": the game saves no progress for
/// these while you play (its counter stays 0 until the quest completes), so we read the live level from the run.
/// </summary>
internal static class LevelQuests
{
    private enum Kind { Tome, Weapon, Character }

    private sealed class Need
    {
        public Kind Kind;
        public ETome[] Tomes = Array.Empty<ETome>();
        public EWeapon[] Weapons = Array.Empty<EWeapon>();
        public Need(params ETome[] tomes) { Kind = Kind.Tome; Tomes = tomes; }
        public Need(params EWeapon[] weapons) { Kind = Kind.Weapon; Weapons = weapons; }
        public Need() { Kind = Kind.Character; }
    }

    private static readonly Dictionary<string, Need> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tomes
        ["a_athena"] = new(ETome.Thorns),
        ["a_megachad"] = new(ETome.Damage),
        ["a_blackHole"] = new(ETome.Knockback),
        ["a_bloodMagic"] = new(ETome.Blood),
        ["a_dice"] = new(ETome.Luck),
        ["a_sniperRifle"] = new(ETome.Precision),
        ["a_battery"] = new(ETome.Cooldown),
        ["a_cursedGloves"] = new(ETome.Cursed),
        ["a_corruptedSword"] = new(ETome.Cursed),
        ["a_echoShard"] = new(ETome.Xp),
        ["a_leechingCrystal"] = new(ETome.Regeneration),
        ["a_shatteredKnowledge"] = new(ETome.Attraction),
        ["a_turboSocks"] = new(ETome.Agility),
        ["a_turboSkates"] = new(ETome.Cooldown, ETome.Agility), // both, so the lower one counts
        // Weapons
        ["a_wirelessDaggers"] = new(EWeapon.LightningStaff),
        ["a_durationTome"] = new(EWeapon.Axe),
        ["a_hatMagic"] = new(EWeapon.FireStaff, EWeapon.LightningStaff, EWeapon.BloodMagic),
        // Character level
        ["a_bloodyCleaver"] = new(),
        ["a_demonicSoul"] = new(),
    };

    /// <summary>True for quests whose progress only exists inside a run (a level reached in one run).</summary>
    public static bool IsLevelQuest(string id) => id != null && ById.ContainsKey(id);

    /// <summary>The level reached this run for the quest (the lowest of several when it needs them all), or -1.</summary>
    public static int Live(string id)
    {
        if (id == null || !ById.TryGetValue(id, out var need)) return -1;
        try
        {
            var p = MyPlayer.Instance;
            var inv = p != null ? p.inventory : null;
            if (inv == null) return -1;
            switch (need.Kind)
            {
                case Kind.Tome:
                    var tomes = inv.tomeInventory;
                    if (tomes == null) return -1;
                    return need.Tomes.Min(t => tomes.HasTome(t) ? tomes.GetTomeLevel(t) : 0);
                case Kind.Weapon:
                    var weapons = inv.weaponInventory;
                    if (weapons == null) return -1;
                    return need.Weapons.Min(w => weapons.weapons != null && weapons.weapons.ContainsKey(w) ? weapons.GetWeaponLevel(w) : 0);
                default:
                    return inv.GetCharacterLevel();
            }
        }
        catch { return -1; }
    }
}
