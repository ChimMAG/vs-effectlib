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
                    EffectLang.SendGain(playerEntity.Player as IServerPlayer, effectId, displayName, ctx);
            }
            else if (entity is EntityAgent agent)
            {
                EffectContext ctx = EffectRegistry.Build(effectId, multiplier, source, durationMul);
                if (ctx == null)
                    return;

                if (ctx.ResetsEffects)
                    ClearAgentEffects(agent, EffectManager.StacksOn(ctx) ? effectId : null);

                StackOnto(agent, effectId, ctx);

                if (ctx.TickSec > 0 && Math.Abs(ctx.Health) > float.Epsilon)
                    ApplyTickEffect(agent, effectId, ctx);
                else if (Math.Abs(ctx.Health) > float.Epsilon)
                    ApplyInstantHealth(agent, ctx);

                if (ctx.StatModifiers.Count > 0 && (ctx.Duration > 0 || ctx.IsEndless))
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

        private static void ApplyTickEffect(EntityAgent agent, string effectId, EffectContext ctx)
        {
            if (Math.Abs(ctx.Health) <= float.Epsilon)
                return;

            if (agent.GetBehavior<EntityBehaviorHealthOverTime>() is not { } b)
            {
                b = new(agent);
                agent.AddBehavior(b);
            }
            b.Apply(effectId, ctx);
        }

        // Carries a running effect's stacks or time left over into a re-hit by the same effect.
        private static void StackOnto(EntityAgent agent, string effectId, EffectContext ctx)
        {
            if (!EffectManager.StacksOn(ctx))
                return;

            int prevStacks = 0;
            float prevRemainingSec = -1f;
            bool prevEndless = false;

            if (statRemovals.TryGetValue((agent.EntityId, effectId), out PendingStats pending))
            {
                prevStacks = pending.Ctx.Stacks;
                prevEndless = pending.Ctx.IsEndless;
                prevRemainingSec = Math.Max(0f, (pending.ExpiryMs - agent.World.ElapsedMilliseconds) / 1000f);
            }

            if (
                agent.GetBehavior<EntityBehaviorHealthOverTime>() is { } hot
                && hot.EffectId == effectId
                && (hot.IsEndless || hot.RemainingSec > 0f)
            )
            {
                prevStacks = Math.Max(prevStacks, hot.Stacks);
                prevEndless |= hot.IsEndless;
                prevRemainingSec = Math.Max(prevRemainingSec, hot.RemainingSec);
            }

            if (prevStacks == 0)
                return;

            if (ctx.Stacking == EnumEffectStacking.Intensity)
                ctx.SetStacks(Math.Min(prevStacks + 1, ctx.MaxStacks));
            else if (ctx.Stacking == EnumEffectStacking.Duration && ctx.Duration > 0 && !prevEndless)
            {
                int total = (int)prevRemainingSec + ctx.Duration;
                ctx.Duration = ctx.MaxDurationSec > 0 ? Math.Min(total, ctx.MaxDurationSec) : total;
            }
        }

        private readonly record struct PendingStats(long CallbackId, string[] Stats, EffectContext Ctx, long ExpiryMs);

        // Players have EffectManager; other creatures get one pending stat removal per effect, so a
        // re-hit extends the effect instead of being cut short by the earlier hit's timer.
        private static readonly Dictionary<(long EntityId, string EffectId), PendingStats> statRemovals = [];

        private static void ApplyStatEffect(EntityAgent agent, string effectId, EffectContext ctx)
        {
            (long, string) key = (agent.EntityId, effectId);
            CancelStatEffect(agent, key);
            SetStats(agent, effectId, ctx);
            ScheduleStatExpiry(agent, key, ctx);
        }

        private static void SetStats(EntityAgent agent, string effectId, EffectContext ctx)
        {
            string statCode = AppliedEffect.StatCode(effectId);
            foreach (KeyValuePair<string, float> stat in ctx.StatModifiers)
                agent.Stats.Set(stat.Key, statCode, stat.Value, false);
        }

        private static void ScheduleStatExpiry(EntityAgent agent, (long EntityId, string EffectId) key, EffectContext ctx)
        {
            string[] stats = [.. ctx.StatModifiers.Keys];
            if (ctx.IsEndless)
            {
                statRemovals[key] = new PendingStats(0L, stats, ctx, long.MaxValue);
                return;
            }

            long delayMs = Math.Min(int.MaxValue, ctx.Duration * 1000L);
            long callbackId = agent.World.RegisterCallback(
                _ =>
                {
                    statRemovals.Remove(key);
                    if (agent.World.GetEntityById(agent.EntityId) is not EntityAgent target)
                        return;

                    RemoveStats(target, stats, AppliedEffect.StatCode(key.EffectId));

                    // Decay "one": drop a single stack and run the timer again.
                    if (
                        ctx is { Stacking: EnumEffectStacking.Intensity, StackDecay: EnumStackDecay.One, Stacks: > 1 }
                        && EffectPolicy.IsAllowed(EffectCapability.Stack)
                    )
                    {
                        ctx.SetStacks(ctx.Stacks - 1);
                        SetStats(target, key.EffectId, ctx);
                        ScheduleStatExpiry(target, key, ctx);
                    }
                },
                (int)delayMs
            );
            statRemovals[key] = new PendingStats(callbackId, stats, ctx, agent.World.ElapsedMilliseconds + delayMs);
        }

        private static void CancelStatEffect(EntityAgent agent, (long EntityId, string EffectId) key)
        {
            if (!statRemovals.Remove(key, out PendingStats pending))
                return;

            if (pending.CallbackId != 0)
                agent.World.UnregisterCallback(pending.CallbackId);
            RemoveStats(agent, pending.Stats, AppliedEffect.StatCode(key.EffectId));
        }

        private static void RemoveStats(EntityAgent agent, string[] stats, string statCode)
        {
            foreach (string stat in stats)
                agent.Stats.Remove(stat, statCode);
        }

        // exceptId (a stacking effect's own reset) survives, so the stack it adds to isn't wiped.
        private static void ClearAgentEffects(EntityAgent agent, string exceptId = null)
        {
            if (agent.GetBehavior<EntityBehaviorHealthOverTime>() is { } healthOverTime && healthOverTime.EffectId != exceptId)
                agent.RemoveBehavior(healthOverTime);

            foreach (
                (long, string) key in statRemovals.Keys
                    .Where(k => k.EntityId == agent.EntityId && k.EffectId != exceptId)
                    .ToList()
            )
                CancelStatEffect(agent, key);
        }

        // Pending removals hold callback ids from the world that scheduled them.
        internal static void ClearPendingStatRemovals() => statRemovals.Clear();
    }
}
