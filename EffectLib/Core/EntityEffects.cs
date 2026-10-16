using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    // Applies an effect to whatever it lands on - a splash, a hit. Players go through their
    // EffectManager; other creatures get the effect's health and stat changes directly.
    public static class EntityEffects
    {
        // blockReason is checked for players after UtilityEffects' own blocks - a lang key to send
        // instead of applying, or null to allow.
        public static void Apply(
            string effectId,
            Entity entity,
            float multiplier,
            string displayName,
            AssetLocation source = null,
            float durationMul = 1f,
            System.Func<EntityPlayer, EffectContext, string> blockReason = null
        )
        {
            if (entity == null || !entity.Alive)
                return;

            if (entity is EntityPlayer playerEntity)
            {
                EffectManager manager = EntityBehaviorPlayerEffects.ManagerFor(playerEntity);
                EffectContext ctx =
                    manager == null ? null : EffectRegistry.Build(effectId, multiplier, source, durationMul);
                if (ctx == null)
                    return;

                string reason =
                    UtilityEffects.GetBlockReason(playerEntity, ctx)
                    ?? blockReason?.Invoke(playerEntity, ctx);
                if (reason != null)
                {
                    EffectLang.Send(playerEntity.Player as IServerPlayer, reason);
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
