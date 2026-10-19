using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class EntityBehaviorHealthOverTime(Entity entity) : EntityBehavior(entity)
    {
        private float dmgPerTick;
        private float tickSec;
        private EnumDamageType damageType;
        private bool done;
        private float tickAccum;
        private float durationAccum;
        private float durationSec;

        private float baseDmgPerTick;
        private bool decayOne;

        public string EffectId { get; private set; }

        public int Stacks { get; private set; } = 1;

        public bool IsEndless => durationSec == EffectContext.EndlessDuration;

        public float RemainingSec => done ? 0f : Math.Max(0f, durationSec - durationAccum);

        public override string PropertyName() => "effectlibHealthOverTime";

        public void Setup(
            float dmgPerTick,
            float tickSec,
            int durationSec,
            EnumDamageType damageType
        )
        {
            Refresh(dmgPerTick, tickSec, durationSec, damageType);
            tickAccum = 0f;
        }

        public void Refresh(
            float dmgPerTick,
            float tickSec,
            int durationSec,
            EnumDamageType damageType
        )
        {
            this.dmgPerTick = dmgPerTick;
            this.tickSec = tickSec;
            this.durationSec = durationSec;
            this.damageType = damageType;
            EffectId = null;
            Stacks = 1;
            baseDmgPerTick = dmgPerTick;
            decayOne = false;
            durationAccum = 0f;
            done = false;
        }

        public void Apply(string effectId, EffectContext ctx)
        {
            bool continuing = !done && EffectId == effectId;
            Refresh(ctx.Health, ctx.TickSec, ctx.Duration, ctx.ResolveDamageType());
            if (!continuing)
                tickAccum = 0f;

            EffectId = effectId;
            Stacks = ctx.Stacks;
            baseDmgPerTick = ctx.Health / ctx.Stacks;
            decayOne =
                ctx.Stacking == EnumEffectStacking.Intensity && ctx.StackDecay == EnumStackDecay.One;
        }

        public override void OnGameTick(float dt)
        {
            if (done || entity.World.Side != EnumAppSide.Server)
                return;

            durationAccum += dt;
            if (durationSec != EffectContext.EndlessDuration && durationAccum >= durationSec)
            {
                if (decayOne && Stacks > 1 && EffectPolicy.IsAllowed(EffectCapability.Stack))
                {
                    Stacks--;
                    dmgPerTick = baseDmgPerTick * Stacks;
                    durationAccum = 0f;
                    return;
                }

                done = true;
                entity.World.RegisterCallback(_ => entity.RemoveBehavior(this), 1);
                return;
            }

            tickAccum += dt;
            if (tickAccum >= tickSec)
            {
                tickAccum -= tickSec;
                ApplyHealthChange();
            }
        }

        private TagSetFast mechanicalEntityTag;
        private bool mechanicalEntityTagCached;

        private TagSetFast GetMechanicalEntityTag()
        {
            if (!mechanicalEntityTagCached)
            {
                mechanicalEntityTagCached = true;
                entity.Api.EntityTagRegistry.TryCreateTagSet(
                    out mechanicalEntityTag,
                    new List<string> { "mechanical" }
                );
            }
            return mechanicalEntityTag;
        }

        private void ApplyHealthChange()
        {
            if (entity.Tags.Overlaps(GetMechanicalEntityTag()))
            {
                done = true;
                return;
            }
            if (!entity.Alive)
            {
                done = true;
                return;
            }

            float healthAmount = Math.Abs(dmgPerTick);
            if (healthAmount > float.Epsilon)
            {
                DamageSource src = new()
                {
                    Source = EnumDamageSource.Internal,
                    Type = damageType,
                    IgnoreInvFrames = true,
                };

                entity.ReceiveDamage(src, healthAmount);
            }
        }
    }
}
