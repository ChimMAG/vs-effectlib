using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    /// <summary>
    /// Shows a weapon or arrow's current coating in its tooltip. Attached automatically to
    /// everything <see cref="CoatingPolicy"/> treats as coatable.
    /// </summary>
    public class CollectibleBehaviorCoatable(CollectibleObject collObj) : CollectibleBehavior(collObj)
    {
        public override void GetHeldItemInfo(
            ItemSlot inSlot,
            StringBuilder dsc,
            IWorldAccessor world,
            bool withDebugInfo
        )
        {
            if (inSlot?.Itemstack == null)
                return;

            ITreeAttribute attrs = inSlot.Itemstack.Attributes;
            string effectId = attrs.GetString(CoatedEffects.KeyEffectId);
            if (string.IsNullOrEmpty(effectId))
                return;

            string itemCode = attrs.GetString(CoatedEffects.KeyItemCode);
            string displayName = CoatedEffects.ResolveDisplayName(itemCode, effectId);
            string domain = CoatedEffects.SourceFromItemCode(itemCode)?.Domain;
            bool isProjectile = CoatingPolicy.IsCoatableProjectile(inSlot.Itemstack.Collectible);

            dsc.Append("<font color=\"#b8bb00\">");
            dsc.Append(
                isProjectile
                    ? Lang.Get(EffectLang.Key(domain, "arrow-coated"), displayName)
                    : Lang.Get(EffectLang.Key(domain, "weapon-coated"), displayName, attrs.GetInt(CoatedEffects.KeyCharges))
            );
            if (!CoatingPolicy.IsEffectCoatable(effectId))
                dsc.Append(" (" + Lang.Get(EffectLang.Key(domain, "coating-disabled-suffix")) + ")");
            dsc.AppendLine("</font>");
        }
    }
}
