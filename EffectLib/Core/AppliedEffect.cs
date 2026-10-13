using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EffectLib
{
    public sealed class AppliedEffect(string effectId, EffectContext ctx)
    {
        private const string MaxHealthStat = "maxhealthExtraPoints";

        public readonly string EffectId = effectId;
        public readonly EffectContext Context = ctx;

        // The code an effect's modifiers are stored under in entity.Stats.
        public static string StatCode(string effectId) => "effectlib-" + effectId;

        private string OwnStatCode => StatCode(EffectId);

        private bool TicksHealth => Context.TickSec > 0 && Math.Abs(Context.Health) > float.Epsilon;

        // Tags this effect's vanilla damage-over-time so it can be stopped if the effect ends early.
        // Negative, so it never matches vanilla's own EnumDamageOverTimeEffectType values.
        // This is probably over engineered but this allows multiple effects to tick health at once without interfering with each other, and
        // also allows the same effect to be applied multiple times without overwriting the previous one.
        private int DamageOverTimeType
        {
            get
            {
                uint hash = 2166136261;
                foreach (char c in EffectId)
                    hash = (hash ^ c) * 16777619;
                return (int)(hash | 0x80000000);
            }
        }

        public void Apply(EntityPlayer entity, bool resume = false)
        {
            foreach (KeyValuePair<string, float> stat in Context.StatModifiers)
            {
                string category = StatCategory(entity, stat.Key);
                if (category == MaxHealthStat)
                    ApplyMaxHealth(entity, stat.Value);
                else
                    entity.Stats.Set(category, OwnStatCode, stat.Value, false);
            }

            // Vanilla damage-over-time survives a resume by itself, and endless ticking health is
            // driven by EffectManager's tick listener instead.
            if (resume || (TicksHealth && Context.IsEndless))
                return;

            if (TicksHealth)
                ApplyTimedHealth(entity);
            else
                ApplyHealth(entity);
        }

        public void ApplyHealthTick(EntityPlayer entity) => ApplyHealth(entity);

        // stopDamageOverTime is false at natural expiry, so the last vanilla ticks still land.
        public void Remove(EntityPlayer entity, bool stopDamageOverTime = true)
        {
            foreach (string stat in Context.StatModifiers.Keys)
            {
                string category = StatCategory(entity, stat);
                entity.Stats.Remove(category, OwnStatCode);
                if (category == MaxHealthStat)
                    entity.GetBehavior<EntityBehaviorHealth>()?.MarkDirty();
            }

            if (stopDamageOverTime && TicksHealth && !Context.IsEndless)
                entity.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect(DamageOverTimeType);
        }

        // The stat is a fraction of max health.
        private void ApplyMaxHealth(EntityPlayer entity, float fraction)
        {
            EntityBehaviorHealth health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null)
                return;

            entity.Stats.Remove(MaxHealthStat, OwnStatCode);
            health.UpdateMaxHealth();
            entity.Stats.Set(MaxHealthStat, OwnStatCode, health.MaxHealth * fraction, false);
            health.MarkDirty();
        }

        private void ApplyTimedHealth(EntityPlayer entity)
        {
            int ticks = Math.Max(1, (int)(Context.Duration / Context.TickSec));
            entity.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = Context.ResolveDamageType(),
                    Duration = TimeSpan.FromSeconds(Context.Duration),
                    TicksPerDuration = ticks,
                    DamageOverTimeType = DamageOverTimeType,
                    IgnoreInvFrames = true,
                },
                Math.Abs(Context.Health * ticks)
            );
        }

        private void ApplyHealth(EntityPlayer entity)
        {
            if (Math.Abs(Context.Health) <= float.Epsilon)
                return;

            entity.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = Context.ResolveDamageType(),
                    IgnoreInvFrames = true,
                },
                Math.Abs(Context.Health)
            );
        }

        // Stat categories are case-sensitive, so one written in any case (commands lowercase ids)
        // is matched to the entity's own.
        private static string StatCategory(EntityPlayer entity, string stat)
        {
            foreach (KeyValuePair<string, EntityFloatStats> category in entity.Stats)
            {
                if (string.Equals(category.Key, stat, StringComparison.OrdinalIgnoreCase))
                    return category.Key;
            }
            return stat;
        }
    }
}
