using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    // Runs once per player entity instance, so listeners here never pile up across respawns.
    [HarmonyPatch(typeof(Entity), "Initialize")]
    internal static class PlayerInitPatch
    {
        public static void Postfix(Entity __instance)
        {
            if (__instance is not EntityPlayer player)
                return;

            UtilityEffects.ApplySizeToEntity(player);
            player.WatchedAttributes.RegisterModifiedListener(
                UtilityEffects.SizeDeltaAttr,
                () => UtilityEffects.ApplySizeToEntity(player)
            );
            player.WatchedAttributes.RegisterModifiedListener(
                UtilityEffects.SizeOffsetAttr,
                () => UtilityEffects.ApplySizeToEntity(player)
            );

            // The server's EffectManager sets these directly.
            if (player.World.Side == EnumAppSide.Client)
                PlayerPhysicsSync.Attach(player);
        }
    }

    // PlayerModelLib resets size here, after Initialize.
    [HarmonyPatch(typeof(Entity), nameof(Entity.OnEntitySpawn))]
    internal static class PlayerSpawnSizePatch
    {
        public static void Postfix(Entity __instance)
        {
            if (__instance is EntityPlayer player)
                UtilityEffects.ApplySizeToEntity(player);
        }
    }

    // PlayerModelLib resets size here, after Initialize.
    [HarmonyPatch(typeof(Entity), nameof(Entity.OnEntityLoaded))]
    internal static class PlayerLoadedSizePatch
    {
        public static void Postfix(Entity __instance)
        {
            if (__instance is EntityPlayer player)
                UtilityEffects.ApplySizeToEntity(player);
        }
    }
}
