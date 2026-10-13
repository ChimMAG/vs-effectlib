using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public static class CoatedEffects
    {
        public const string KeyEffectId = "coatedEffectId";
        public const string KeyItemCode = "coatedItemCode";
        public const string KeyMultiplier = "coatMultiplier";
        public const string KeyDurationMul = "coatDurationMul";
        public const string KeyCharges = "coatCharges";

        public static string ResolveDisplayName(string itemCodeOrLangKey, string fallback) =>
            string.IsNullOrEmpty(itemCodeOrLangKey) ? fallback : Lang.Get(itemCodeOrLangKey);

        public static string DefaultItemCode(CollectibleObject col)
        {
            if (col?.Code == null)
                return "";
            string typePrefix = col is Block ? "block" : "item";
            return $"{col.Code.Domain}:{typePrefix}-{col.Code.Path}";
        }

        // Turns a DefaultItemCode lang code ("domain:item-path") back into the item's own code,
        // so a coating builds from its source item's definition of the effect.
        public static AssetLocation SourceFromItemCode(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode))
                return null;

            AssetLocation code = new(itemCode);
            foreach (string prefix in new[] { "item-", "block-" })
                if (code.Path.StartsWith(prefix, StringComparison.Ordinal))
                    return new AssetLocation(code.Domain, code.Path[prefix.Length..]);

            return null;
        }

        public static void ReadWeaponCoat(
            ItemStack stack,
            out string effectId,
            out float multiplier,
            out float durationMul,
            out int charges
        )
        {
            if (
                CombatOverhaulCompat.ShouldUseBuffStorage(stack.Collectible)
                && CombatOverhaulCompat.TryGetCoating(
                    stack,
                    out string coEffectId,
                    out _,
                    out float coMultiplier,
                    out float coDurationMul,
                    out int coCharges
                )
            )
            {
                effectId = coEffectId;
                multiplier = coMultiplier;
                durationMul = coDurationMul;
                charges = coCharges;
                return;
            }

            ITreeAttribute attrs = stack.Attributes;
            effectId = attrs.GetString(KeyEffectId);
            multiplier = attrs.GetFloat(KeyMultiplier);
            durationMul = attrs.GetFloat(KeyDurationMul, 1f);
            charges = attrs.GetInt(KeyCharges);
        }

        public static void WriteWeaponCoat(
            ItemSlot slot,
            string effectId,
            string itemCode,
            float multiplier,
            float durationMul,
            int charges
        )
        {
            if (CombatOverhaulCompat.ShouldUseBuffStorage(slot.Itemstack.Collectible))
            {
                CombatOverhaulCompat.SetCoating(slot, effectId, itemCode, multiplier, durationMul, charges);
                return;
            }

            ITreeAttribute attrs = slot.Itemstack.Attributes;
            attrs.SetString(KeyEffectId, effectId);
            attrs.SetString(KeyItemCode, itemCode ?? "");
            attrs.SetFloat(KeyMultiplier, multiplier);
            attrs.SetFloat(KeyDurationMul, durationMul);
            attrs.SetInt(KeyCharges, charges);
            slot.MarkDirty();
        }

        public static bool HasProjectileCoat(ItemStack stack)
        {
            return CombatOverhaulCompat.ShouldUseProjectileBuffStorage(stack)
                ? CombatOverhaulCompat.TryGetCoating(stack, out _, out _, out _, out _, out _)
                : !string.IsNullOrEmpty(stack.Attributes.GetString(KeyEffectId));
        }

        public static void WriteProjectileCoat(
            ItemStack stack,
            string effectId,
            string itemCode,
            float multiplier,
            float durationMul
        )
        {
            if (CombatOverhaulCompat.ShouldUseProjectileBuffStorage(stack))
            {
                CombatOverhaulCompat.SetProjectileCoating(stack, effectId, itemCode, multiplier, durationMul);
                return;
            }

            ITreeAttribute attrs = stack.Attributes;
            attrs.SetString(KeyEffectId, effectId);
            attrs.SetString(KeyItemCode, itemCode ?? "");
            attrs.SetFloat(KeyMultiplier, multiplier);
            attrs.SetFloat(KeyDurationMul, durationMul);
        }

        internal static void ClearStackCoat(ITreeAttribute attrs)
        {
            attrs.RemoveAttribute(KeyEffectId);
            attrs.RemoveAttribute(KeyItemCode);
            attrs.RemoveAttribute(KeyMultiplier);
            attrs.RemoveAttribute(KeyDurationMul);
            attrs.RemoveAttribute(KeyCharges);
        }

        internal static (string EffectId, float Multiplier, float DurationMul, string ItemCode)? TryConsumeWeaponCharge(
            ItemSlot slot
        )
        {
            ITreeAttribute attrs = slot.Itemstack.Attributes;
            string effectId = attrs.GetString(KeyEffectId);
            if (string.IsNullOrEmpty(effectId))
                return null;

            int charges = attrs.GetInt(KeyCharges);
            if (charges <= 0)
            {
                ClearStackCoat(attrs);
                slot.MarkDirty();
                return null;
            }

            if (!CoatingPolicy.IsEffectCoatable(effectId))
                return null;

            float multiplier = attrs.GetFloat(KeyMultiplier);
            float durationMul = attrs.GetFloat(KeyDurationMul, 1f);
            string itemCode = attrs.GetString(KeyItemCode);

            charges--;
            if (charges <= 0)
                ClearStackCoat(attrs);
            else
                attrs.SetInt(KeyCharges, charges);
            slot.MarkDirty();

            return (effectId, multiplier, durationMul, itemCode);
        }

        internal static (string EffectId, float Multiplier, float DurationMul, string ItemCode)? TryConsumeProjectileCoat(
            ItemStack projectileStack
        )
        {
            ITreeAttribute attrs = projectileStack.Attributes;
            string effectId = attrs.GetString(KeyEffectId);
            if (string.IsNullOrEmpty(effectId))
                return null;

            if (!CoatingPolicy.IsEffectCoatable(effectId))
                return null;

            float multiplier = attrs.GetFloat(KeyMultiplier);
            float durationMul = attrs.GetFloat(KeyDurationMul, 1f);
            string itemCode = attrs.GetString(KeyItemCode);
            ClearStackCoat(attrs);

            return (effectId, multiplier, durationMul, itemCode);
        }

        public static void Apply(string effectId, Entity entity, float multiplier, string displayName) =>
            Apply(effectId, entity, multiplier, displayName, null);

        public static void Apply(
            string effectId,
            Entity entity,
            float multiplier,
            string displayName,
            AssetLocation source
        ) => Apply(effectId, entity, multiplier, displayName, source, 1f);

        public static void Apply(
            string effectId,
            Entity entity,
            float multiplier,
            string displayName,
            AssetLocation source,
            float durationMul
        )
        {
            if (entity == null || !entity.Alive)
                return;

            CoatingPolicy.ApplySideEffects(effectId, entity, multiplier);

            if (entity is EntityPlayer playerEntity)
            {
                EffectManager manager = EntityBehaviorPlayerEffects.ManagerFor(playerEntity);
                EffectContext ctx =
                    manager == null ? null : EffectRegistry.Build(effectId, multiplier, source, durationMul);
                if (ctx == null)
                    return;

                string blockReason =
                    UtilityEffects.GetBlockReason(playerEntity, ctx)
                    ?? CoatingPolicy.GetBlockReason(effectId, playerEntity, ctx);
                if (blockReason != null)
                {
                    EffectLang.Send(playerEntity.Player as IServerPlayer, blockReason);
                    return;
                }

                if (ctx.ResetsEffects)
                    manager.PurgeFor(effectId, ctx);

                if (manager.TryApply(effectId, ctx, displayName) && ctx.Notify)
                    EffectLang.SendFor(
                        playerEntity.Player as IServerPlayer,
                        EffectRegistry.DomainOf(effectId),
                        "effect-gain",
                        displayName
                    );
            }
            else if (entity is EntityAgent agent)
            {
                EffectContext ctx = EffectRegistry.Build(effectId, multiplier, source, durationMul);
                if (ctx == null)
                    return;

                if (ctx.ResetsEffects)
                    ClearAgentEffects(agent);

                if (ctx.TickSec > 0 && Math.Abs(ctx.Health) > float.Epsilon)
                    ApplyTickEffect(agent, ctx);
                else if (Math.Abs(ctx.Health) > float.Epsilon)
                    ApplyInstantHealth(agent, ctx);

                if (ctx.StatModifiers.Count > 0 && ctx.Duration > 0)
                    ApplyStatEffect(agent, effectId, ctx);
            }
        }

        private static void ApplyInstantHealth(EntityAgent agent, EffectContext ctx)
        {
            agent.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = ctx.ResolveDamageType(),
                    IgnoreInvFrames = true,
                },
                Math.Abs(ctx.Health)
            );
        }

        private static void ApplyTickEffect(EntityAgent agent, EffectContext ctx)
        {
            if (Math.Abs(ctx.Health) <= float.Epsilon)
                return;

            if (agent.HasBehavior<EntityBehaviorHealthOverTime>())
                agent.GetBehavior<EntityBehaviorHealthOverTime>()
                    .Refresh(ctx.Health, ctx.TickSec, ctx.Duration, ctx.ResolveDamageType());
            else
            {
                EntityBehaviorHealthOverTime b = new(agent);
                agent.AddBehavior(b);
                b.Setup(ctx.Health, ctx.TickSec, ctx.Duration, ctx.ResolveDamageType());
            }
        }

        // Players have EffectManager; other creatures get one pending stat removal per effect, so a
        // re-hit extends the effect instead of being cut short by the earlier hit's timer.
        private static readonly Dictionary<(long EntityId, string EffectId), (long CallbackId, string[] Stats)> statRemovals = [];

        private static void ApplyStatEffect(EntityAgent agent, string effectId, EffectContext ctx)
        {
            (long, string) key = (agent.EntityId, effectId);
            string statCode = AppliedEffect.StatCode(effectId);
            CancelStatEffect(agent, key);

            foreach (KeyValuePair<string, float> stat in ctx.StatModifiers)
                agent.Stats.Set(stat.Key, statCode, stat.Value, false);

            string[] stats = [.. ctx.StatModifiers.Keys];
            long callbackId = agent.World.RegisterCallback(
                _ =>
                {
                    statRemovals.Remove(key);
                    if (agent.World.GetEntityById(agent.EntityId) is EntityAgent target)
                        RemoveStats(target, stats, statCode);
                },
                (int)Math.Min(int.MaxValue, ctx.Duration * 1000L)
            );
            statRemovals[key] = (callbackId, stats);
        }

        private static void CancelStatEffect(EntityAgent agent, (long EntityId, string EffectId) key)
        {
            if (!statRemovals.Remove(key, out (long CallbackId, string[] Stats) pending))
                return;

            agent.World.UnregisterCallback(pending.CallbackId);
            RemoveStats(agent, pending.Stats, AppliedEffect.StatCode(key.EffectId));
        }

        private static void RemoveStats(EntityAgent agent, string[] stats, string statCode)
        {
            foreach (string stat in stats)
                agent.Stats.Remove(stat, statCode);
        }

        private static void ClearAgentEffects(EntityAgent agent)
        {
            if (agent.GetBehavior<EntityBehaviorHealthOverTime>() is { } healthOverTime)
                agent.RemoveBehavior(healthOverTime);

            foreach ((long, string) key in statRemovals.Keys.Where(k => k.EntityId == agent.EntityId).ToList())
                CancelStatEffect(agent, key);
        }

        // Pending removals hold callback ids from the world that scheduled them.
        internal static void ClearPendingStatRemovals() => statRemovals.Clear();
    }
}
