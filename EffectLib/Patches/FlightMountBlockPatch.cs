using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    [HarmonyPatch(typeof(EntityAgent), nameof(EntityAgent.TryMount))]
    internal static class FlightMountBlockPatch
    {
        public static bool Prefix(EntityAgent __instance, ref bool __result)
        {
            if (__instance is not EntityPlayer player || !player.WatchedAttributes.GetBool(EffectAttr.CanFly))
                return true;

            EffectLang.Send(player.Player as IServerPlayer, "effectlib:flight-mount-block");

            __result = false;
            return false;
        }
    }
}
