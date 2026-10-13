using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EffectLib
{
    /// <summary>
    /// Carry effects are registered under "carry:domain:path" (derived from the item's declared
    /// effectId) so they never share an id with a consumed or coated effect. Any active effect
    /// with this prefix is owned by <see cref="CarryEffectScanner"/>, and is never restored from
    /// a save - the next scan re-derives it from the inventory instead.
    /// </summary>
    public static class CarryEffectIds
    {
        public const string Prefix = "carry:";

        public static string For(string effectId) => Prefix + effectId;

        public static bool Is(string effectId) =>
            effectId?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Re-evaluates every <see cref="CollectibleBehaviorEffectCarry"/>-bearing item a player is
    /// carrying and flips its effect on/off as the item enters/leaves its declared activation
    /// tier (and, for toggle items, its on/off state), plus drains durability from whichever
    /// items are actually granting an effect. Driven by <see cref="EntityBehaviorPlayerEffects"/>.
    /// </summary>
    public static class CarryEffectScanner
    {
        public static bool Scan(EntityPlayer player, EffectManager manager)
        {
            if (!CanScan(player, manager))
                return false;

            List<(CollectibleBehaviorEffectCarry Carry, ItemSlot Slot)> granting = ResolveGranting(player);
            HashSet<string> grantingIds = [.. granting.Select(g => g.Carry.EffectId)];

            // Revoke first, so an item swapped in can take over an exclusivity group the
            // outgoing one held.
            foreach (string id in manager.ActiveIds.Where(CarryEffectIds.Is).ToList())
            {
                if (!grantingIds.Contains(id))
                    manager.RemoveEffect(id);
            }

            foreach ((CollectibleBehaviorEffectCarry carry, _) in granting)
            {
                string id = carry.EffectId;
                bool active = manager.IsActive(id);
                if (active && Equals(manager.SourceOf(id), carry.Source))
                    continue;

                EffectContext ctx = EffectRegistry.Build(id, 1f, carry.Source);
                if (ctx == null || manager.IsGroupBlocked(ctx.ExclusivityGroup, id))
                    continue;

                // A different item (e.g. a higher-order upgrade) now provides this effect - swap
                // its definition in.
                if (active)
                    manager.RemoveEffect(id, notify: false);

                string name = carry.DisplayName;
                if (manager.TryApply(id, ctx, name) && ctx.Notify)
                    EffectLang.SendFor(
                        player.Player as IServerPlayer,
                        EffectRegistry.DomainOf(id),
                        "effect-gain",
                        name
                    );
            }

            return true;
        }

        // True if any item broke or was destroyed, so the caller can rescan straight away.
        public static bool DrainDurability(EntityPlayer player, EffectManager manager, float elapsedSec)
        {
            // Nothing can drain unless a carry effect is active, so skip the inventory walk.
            if (!CanScan(player, manager) || elapsedSec <= 0f || !manager.ActiveIds.Any(CarryEffectIds.Is))
                return false;

            bool anyBroke = false;

            foreach ((CollectibleBehaviorEffectCarry carry, ItemSlot slot) in ResolveGranting(player))
            {
                if (carry.DurabilityDrainIntervalSec <= 0f || carry.DurabilityDrainAmount <= 0)
                    continue;

                // Only the item whose definition is actually applied drains.
                if (!manager.IsActive(carry.EffectId) || !Equals(manager.SourceOf(carry.EffectId), carry.Source))
                    continue;

                float expected =
                    elapsedSec / carry.DurabilityDrainIntervalSec * carry.DurabilityDrainAmount;
                int amount = GameMath.RoundRandom(player.World.Rand, expected);
                if (amount <= 0)
                    continue;

                slot.Itemstack.Collectible.DamageItem(
                    player.World,
                    player,
                    slot,
                    amount,
                    destroyOnZeroDurability: carry.DestroyWhenBroken
                );

                anyBroke |= slot.Empty || IsBroken(slot.Itemstack);
            }

            return anyBroke;
        }

        private static bool CanScan(EntityPlayer player, EffectManager manager) =>
            player?.Player?.InventoryManager != null && manager != null && player.Alive;

        private enum SlotArea
        {
            Hotbar,
            Backpack,
            Worn,
        }

        // One item per effect id: the qualifying copy with the highest order, ties going to the
        // first found - checking the hands, then the rest of the hotbar, the backpack, then worn
        // gear. That copy's definition is applied, and it's the one that drains.
        private static List<(CollectibleBehaviorEffectCarry Carry, ItemSlot Slot)> ResolveGranting(
            EntityPlayer player
        )
        {
            IPlayerInventoryManager inventoryManager = player.Player.InventoryManager;
            IInventory hotbar = inventoryManager.GetHotbarInventory();
            IInventory backpack = inventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
            IInventory worn = inventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);

            ItemSlot rightHand = player.RightHandItemSlot;
            ItemSlot leftHand = player.LeftHandItemSlot;

            IEnumerable<(ItemSlot Slot, SlotArea Area)> slots = new[] { rightHand, leftHand }
                .Concat(hotbar?.Where(slot => slot != rightHand && slot != leftHand) ?? [])
                .Select(slot => (slot, SlotArea.Hotbar))
                .Concat(backpack?.Select(slot => (slot, SlotArea.Backpack)) ?? [])
                .Concat(worn?.Select(slot => (slot, SlotArea.Worn)) ?? []);

            // Ids kept in first-found order, so exclusivity groups still favour the hands.
            List<string> ids = [];
            Dictionary<string, (CollectibleBehaviorEffectCarry Carry, ItemSlot Slot)> best = [];

            foreach ((ItemSlot slot, SlotArea area) in slots)
            {
                CollectibleObject collectible = slot?.Itemstack?.Collectible;
                if (collectible == null)
                    continue;

                foreach (
                    CollectibleBehaviorEffectCarry carry in collectible.CollectibleBehaviors.OfType<CollectibleBehaviorEffectCarry>()
                )
                {
                    if (carry.EffectId == null || !Qualifies(carry, slot, area, rightHand, leftHand))
                        continue;

                    if (!best.TryGetValue(carry.EffectId, out var current))
                        ids.Add(carry.EffectId);
                    else if (carry.Order <= current.Carry.Order)
                        continue;

                    best[carry.EffectId] = (carry, slot);
                }
            }

            return [.. ids.Select(id => best[id])];
        }

        private static bool Qualifies(
            CollectibleBehaviorEffectCarry carry,
            ItemSlot slot,
            SlotArea area,
            ItemSlot rightHand,
            ItemSlot leftHand
        ) =>
            IsUsable(carry, slot)
            && carry.Activation switch
            {
                EnumCarryActivation.Inventory => true,
                EnumCarryActivation.Hotbar => area == SlotArea.Hotbar,
                EnumCarryActivation.Held => IsHeld(carry, slot, rightHand, leftHand),
                EnumCarryActivation.Worn => area == SlotArea.Worn,
                _ => false,
            };

        private static bool IsUsable(CollectibleBehaviorEffectCarry carry, ItemSlot slot) =>
            (carry.AllowWhenBroken || !IsBroken(slot.Itemstack)) && carry.IsToggledOn(slot);

        // Items with no durability at all (maxDurability <= 1) are never "broken".
        private static bool IsBroken(ItemStack stack) =>
            stack.Collectible.GetMaxDurability(stack) > 1
            && stack.Collectible.GetRemainingDurability(stack) <= 0;

        private static bool IsHeld(
            CollectibleBehaviorEffectCarry carry,
            ItemSlot slot,
            ItemSlot rightHand,
            ItemSlot leftHand
        ) =>
            carry.Hand switch
            {
                EnumCarryHand.Main => slot == rightHand,
                EnumCarryHand.Off => slot == leftHand,
                EnumCarryHand.Both => slot == rightHand
                    && leftHand?.Itemstack?.Collectible == slot.Itemstack.Collectible
                    && IsUsable(carry, leftHand),
                _ => slot == rightHand || slot == leftHand, // Either
            };
    }
}
