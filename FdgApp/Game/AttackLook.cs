using System;
using System.Linq;

namespace FdgRaylib.Game;

/// <summary>How an attack looks and sounds in battle (Kai, 2026-11-26: real shots and swings, with sound).</summary>
public enum EAttackLook
{
    // Shooting
    Bullet, Heavy, Laser, Plasma, Flame, Explosive, Thrown, Spell,
    // Melee
    Blade, Blunt, Claw, Strike,
}

/// <summary>
/// Picks an attack's look and sound from the weapon's name - the same kind of name reading Critical Hits' weapon types
/// use, widened for both games' armouries (bows and javelins, breath weapons, claws and fangs). Looks only: no rule
/// reads it.
/// </summary>
public static class AttackLook
{
    private static readonly string[] ThrownWords = { "bow", "longbow", "crossbow", "shortbow", "warbow", "handbow", "javelin", "sling", "throwing", "dart", "shuriken", "harpoon", "spear", "bolas", "axes", "knives", "stones", "rocks" };
    private static readonly string[] FlameWords = { "flame", "flamer", "fire", "burn", "incinerat", "inferno", "napalm", "pyre", "breath", "magma", "melta", "fusion", "heat", "thermal", "torch" };
    private static readonly string[] ExplosiveWords = { "missile", "rocket", "grenade", "launcher", "mortar", "bomb", "artillery", "cannon", "howitzer", "torpedo", "frag", "shell", "catapult", "trebuchet", "ballista", "demolition", "mine", "explosive" };
    private static readonly string[] LaserWords = { "laser", "las", "beam", "ray", "lance", "sniper" };
    private static readonly string[] PlasmaWords = { "plasma", "ion", "gauss", "pulse", "tesla", "arc", "neutron", "particle", "disintegrat", "photon", "volt", "energy", "lightning", "shock", "storm", "psy", "warp", "blaster", "bolt", "zap" };
    private static readonly string[] HeavyWords = { "heavy", "machine", "minigun", "chaingun", "gatling", "auto", "assault", "shotgun", "scatter", "big gun", "cannon", "rotary", "storm" };
    private static readonly string[] SpellWords = { "spell", "magic", "hex", "curse", "smite", "sorcer", "arcane", "mystic" };

    private static readonly string[] ClawWords = { "claw", "talon", "fang", "bite", "jaw", "maw", "tentacle", "horn", "tusk", "sting", "scything", "rending" };
    private static readonly string[] BladeWords = { "sword", "blade", "axe", "dagger", "knife", "knives", "sabre", "saber", "scythe", "sickle", "cleaver", "razor", "scimitar", "katana", "falchion", "glaive", "rapier", "cutlass", "machete", "spear", "halberd", "pike", "lance", "trident", "polearm", "hand weapon", "bayonet", "chainsaw" };
    private static readonly string[] BluntWords = { "hammer", "club", "flail", "mace", "maul", "fist", "morningstar", "morning star", "crusher", "gauntlet", "cudgel", "staff", "stave", "smasher", "bludgeon", "stomp", "tail", "slam", "crush", "trample" };

    // Words match at the start of a word ("las" in Lasgun, not Glass; "ion" in Ion Rifle, not Legion).
    private static bool Has(string n, string[] words) => words.Any(w =>
        System.Text.RegularExpressions.Regex.IsMatch(n, @"\b" + System.Text.RegularExpressions.Regex.Escape(w), System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public static EAttackLook For(string? weaponName, bool melee)
    {
        string n = weaponName ?? "";
        if (melee)
        {
            if (Has(n, ClawWords)) return EAttackLook.Claw;
            if (Has(n, BluntWords)) return EAttackLook.Blunt;
            if (Has(n, BladeWords)) return EAttackLook.Blade;
            return EAttackLook.Strike;
        }
        if (Has(n, SpellWords)) return EAttackLook.Spell;
        if (Has(n, FlameWords)) return EAttackLook.Flame;
        if (Has(n, ExplosiveWords)) return EAttackLook.Explosive;
        if (Has(n, ThrownWords)) return EAttackLook.Thrown;
        if (Has(n, PlasmaWords)) return EAttackLook.Plasma;
        if (Has(n, LaserWords)) return EAttackLook.Laser;
        if (Has(n, HeavyWords)) return EAttackLook.Heavy;
        return EAttackLook.Bullet;
    }

    public static bool IsMelee(EAttackLook l) => l >= EAttackLook.Blade;

    /// <summary>The sound folder fired with the attack (OprGodot/sounds/&lt;folder&gt;).</summary>
    public static string FireSound(EAttackLook l) => l switch
    {
        EAttackLook.Bullet => "gun",
        EAttackLook.Heavy => "heavy",
        EAttackLook.Laser => "laser",
        EAttackLook.Plasma => "plasma",
        EAttackLook.Flame => "flame",
        EAttackLook.Explosive => "heavy",
        EAttackLook.Thrown => "missile",
        EAttackLook.Spell => "spell",
        _ => "swing",
    };

    /// <summary>The sound folder played where the attack lands (null: none).</summary>
    public static string? HitSound(EAttackLook l) => l switch
    {
        EAttackLook.Explosive => "explosion",
        EAttackLook.Flame or EAttackLook.Spell => null,
        _ when IsMelee(l) => "melee_hit",
        _ => "shot_hit",
    };

    /// <summary>How fast the projectile flies, inches a second (0: no projectile - a flame or a melee blow).</summary>
    public static float Speed(EAttackLook l) => l switch
    {
        EAttackLook.Bullet or EAttackLook.Heavy => 180f,
        EAttackLook.Laser => 400f,
        EAttackLook.Plasma => 120f,
        EAttackLook.Explosive => 60f,
        EAttackLook.Thrown => 45f,
        EAttackLook.Spell => 50f,
        _ => 0f,
    };
}
