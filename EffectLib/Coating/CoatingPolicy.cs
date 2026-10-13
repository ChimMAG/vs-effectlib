using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public sealed class CoatingConfig
    {
        public System.Func<bool> AllowCoating { get; init; }

        public System.Func<int> MaxCharges { get; init; }

        public System.Func<float> EffectMultiplier { get; init; }

        public System.Func<CollectibleObject, bool> IsCoatableWeapon { get; init; }

        public System.Func<CollectibleObject, bool> IsCoatableProjectile { get; init; }

        public System.Func<string, bool> IsEffectCoatable { get; init; }

        public System.Func<ItemStack, (string EffectId, float PotencyMul, float DurationMul)?> ResolveLiquidEffect { get; init; }

        public Action<string, Entity, float> ApplySideEffects { get; init; }

        public System.Func<string, EntityPlayer, EffectContext, string> GetBlockReason { get; init; }

        public System.Func<bool> AllowBarrelCoating { get; init; }

        public System.Func<float> BarrelConsumeLitres { get; init; }

        public System.Func<float> BarrelCheckLitres { get; init; }
    }

    public static class CoatingPolicy
    {
        private static CoatingConfig config = new();
        private static ICoreAPI api;
        private static TagSet coatableWeaponTagSet;
        private static bool coatableWeaponTagSetBuilt;
        private static string[] coatableProjectileCodes;

        public static void Configure(CoatingConfig hooks) => config = hooks ?? new();

        // Also runs per world, so a new world never reuses the last one's tag ids.
        public static void Init(ICoreAPI coreApi)
        {
            api = coreApi;
            InvalidateCaches();
        }

        public static void InvalidateCaches()
        {
            coatableWeaponTagSetBuilt = false;
            coatableProjectileCodes = null;
        }

        public static bool AllowCoating() => config.AllowCoating?.Invoke() ?? true;

        public static int MaxCharges() => config.MaxCharges?.Invoke() ?? 5;

        public static float EffectMultiplier() => config.EffectMultiplier?.Invoke() ?? 1f;

        public static bool IsCoatableWeapon(CollectibleObject col) =>
            config.IsCoatableWeapon?.Invoke(col) ?? DefaultIsCoatableWeapon(col);

        public static bool IsCoatableProjectile(CollectibleObject col) =>
            config.IsCoatableProjectile?.Invoke(col) ?? DefaultIsCoatableProjectile(col);

        public static bool IsEffectCoatable(string effectId) =>
            config.IsEffectCoatable?.Invoke(effectId) ?? true;

        public static bool AllowBarrelCoating() => config.AllowBarrelCoating?.Invoke() ?? true;

        public static float BarrelConsumeLitres() => config.BarrelConsumeLitres?.Invoke() ?? 0.25f;

        public static float BarrelCheckLitres() => config.BarrelCheckLitres?.Invoke() ?? 0.24f;

        public static (string EffectId, float PotencyMul, float DurationMul)? ResolveLiquidEffect(ItemStack stack) =>
            (config.ResolveLiquidEffect ?? DefaultResolveLiquidEffect)(stack);

        public static void ApplySideEffects(string effectId, Entity target, float multiplier) =>
            config.ApplySideEffects?.Invoke(effectId, target, multiplier);

        public static string GetBlockReason(string effectId, EntityPlayer player, EffectContext ctx) =>
            config.GetBlockReason?.Invoke(effectId, player, ctx);

        // Weapons are matched by vanilla item/block tags
        // (default "weapon-melee"), since arrows have no reliable tag as of 
        // VS 1.22.7 they're matched by wildcard item code instead (default "*arrow*").
        private static bool DefaultIsCoatableWeapon(CollectibleObject col) =>
            col?.Tags != null
            && TryGetCoatableWeaponTagSet(out TagSet tagSet)
            && col.Tags.Overlaps(tagSet);

        private static bool TryGetCoatableWeaponTagSet(out TagSet tagSet)
        {
            if (!coatableWeaponTagSetBuilt && api != null)
            {
                api.CollectibleTagRegistry.TryCreateTagSetAndLogIssues(
                    out coatableWeaponTagSet,
                    (ReadOnlySpan<string>)SplitList(EffectLibConfig.Loaded.CoatableWeaponTags)
                );
                coatableWeaponTagSetBuilt = true;
            }
            tagSet = coatableWeaponTagSet;
            return coatableWeaponTagSetBuilt;
        }

        private static bool DefaultIsCoatableProjectile(CollectibleObject col)
        {
            if (col?.Code == null)
                return false;

            string[] codes = coatableProjectileCodes ??= SplitList(EffectLibConfig.Loaded.CoatableProjectilesCodes);
            return codes.Length > 0 && WildcardUtil.Match(codes, col.Code.ToString());
        }

        private static string[] SplitList(string list) =>
            [.. (list ?? "").Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0)];

        private static (string EffectId, float PotencyMul, float DurationMul)? DefaultResolveLiquidEffect(ItemStack stack) =>
            JsonEffectDefinition.TryResolve(stack?.Collectible, "effectinfo", "effectId", out string effectId)
                ? (effectId, 1f, 1f)
                : null;
    }
}
