using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace EffectLib
{
    // Player movement is simulated client-side, so the physics properties EffectManager changes on
    // the server are mirrored onto the client's copy of each player from its watched attributes.
    internal static class PlayerPhysicsSync
    {
        private static readonly string[] Keys =
        [
            EffectAttr.CanClimb,
            EffectAttr.NoClimb,
            EffectAttr.ClimbReach,
            EffectAttr.NoGravity,
        ];

        public static void Attach(EntityPlayer player)
        {
            bool baseCanClimbAnywhere = player.Properties.CanClimbAnywhere;
            bool baseCanClimb = player.Properties.CanClimb;
            float baseClimbTouchDistance = player.Properties.ClimbTouchDistance;
            EnumHabitat baseHabitat = player.Properties.Habitat;

            void Sync()
            {
                ITreeAttribute attrs = player.WatchedAttributes;
                player.Properties.CanClimbAnywhere = baseCanClimbAnywhere || attrs.GetBool(EffectAttr.CanClimb);
                player.Properties.CanClimb = baseCanClimb && !attrs.GetBool(EffectAttr.NoClimb);
                player.Properties.ClimbTouchDistance = baseClimbTouchDistance + attrs.GetFloat(EffectAttr.ClimbReach);
                player.Properties.Habitat = attrs.GetBool(EffectAttr.NoGravity) ? EnumHabitat.Air : baseHabitat;
            }

            Sync();
            foreach (string key in Keys)
                player.WatchedAttributes.RegisterModifiedListener(key, Sync);
        }
    }
}
