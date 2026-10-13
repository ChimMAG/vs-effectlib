using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    /// <summary>
    /// Held in the off hand, coats the main hand's weapon or arrows with this item's effect while
    /// the player holds sneak + right mouse. Driven by <see cref="CoatSourceIdlePatch"/>.
    /// </summary>
    public class CollectibleBehaviorCoatSource(CollectibleObject collObj)
        : CollectibleBehavior(collObj)
    {
        protected string attributeKey;
        protected string idField;
        private float consumeTime;
        private string ownEffectId;

        protected ICoreAPI Api { get; private set; }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            attributeKey = properties["attributeKey"].AsString("effectinfo");
            idField = properties["idField"].AsString("effectId");
            consumeTime = properties["consumeTime"].AsFloat(1.5f);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            Api = api;
            RegisterOwnEffect();
        }

        protected virtual void RegisterOwnEffect()
        {
            if (
                JsonEffectDefinition.TryReadOwn(
                    collObj,
                    attributeKey,
                    idField,
                    "CoatSource",
                    Api.Logger,
                    out JsonObject def,
                    out ownEffectId
                )
            )
                JsonEffectDefinition.RegisterFrom(ownEffectId, collObj.Code.Domain, def, collObj.Code);
        }

        protected virtual float GetConsumeTime() => consumeTime;

        protected virtual ItemStack GetSourceStack(ItemSlot slot) => slot.Itemstack;

        protected virtual bool TryResolveEffect(
            ItemStack sourceStack,
            out string effectId,
            out float potencyMul,
            out float durationMul
        )
        {
            effectId = ownEffectId;
            potencyMul = 1f;
            durationMul = 1f;
            return effectId != null;
        }

        protected virtual bool HasEnoughSource(ItemSlot slot) => (slot.Itemstack?.StackSize ?? 0) > 0;

        protected virtual bool ConsumeSource(ItemSlot slot, EntityAgent byEntity)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
            return true;
        }

        public void CoatingIdle(ItemSlot slot, EntityAgent byEntity)
        {
            ItemStack sourceStack = GetSourceStack(slot);
            TryResolveEffect(sourceStack, out string effectId, out float potencyMul, out float durationMul);
            string itemCode = CoatedEffects.DefaultItemCode(sourceStack?.Collectible);

            CoatingInteraction.HandleIdle(
                Api,
                slot,
                byEntity,
                effectId,
                potencyMul,
                durationMul,
                itemCode,
                s => HasEnoughSource(s) && ConsumeSource(s, byEntity),
                GetConsumeTime()
            );
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot, ref handling);
            ItemStack sourceStack = GetSourceStack(inSlot);
            if (
                !CoatingPolicy.AllowCoating()
                || !TryResolveEffect(sourceStack, out string effectId, out _, out _)
                || !CoatingPolicy.IsEffectCoatable(effectId)
            )
                return interactions;

            return
            [
                .. interactions,
                new WorldInteraction
                {
                    ActionLangCode = EffectLang.Key(sourceStack?.Collectible?.Code?.Domain, "heldhelp-coat"),
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift",
                },
            ];
        }
    }
}
