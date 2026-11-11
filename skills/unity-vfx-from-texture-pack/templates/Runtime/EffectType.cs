namespace VfxToolkit
{
    /// <summary>One VFX Graph asset per type, configured on the EffectsManager.</summary>
    public enum EffectType
    {
        Hit,
        Death,
        Muzzle,
        Explosion,
        Pickup,
        LevelUp,
        Slash,
        SpawnWarning,
        Nova,
        Crit,
        Lightning,   // size = segment length, direction = segment direction
        Dash,
        Burn,
        Frost,
        Poison,
        PlayerHurt,
        Heal,
        Spawn,
        EnemyShoot,
        // Weapon hit impacts (WeaponDefinition.impact; Hit is the generic fallback)
        ImpactArcane,
        ImpactFire,
        ImpactFrost,
        ImpactPoison,
        ImpactSlash,
        ImpactShock,
        ImpactHoly,
        ImpactBlunt,
    }
}
