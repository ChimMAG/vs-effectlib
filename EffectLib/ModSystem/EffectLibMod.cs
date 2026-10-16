using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class EffectLibMod : ModSystem
    {
        private const string HarmonyId = "llama3013.EffectLib";
        private const string ConfigSyncChannelName = "effectlibconfigsync";

        private ICoreServerAPI sapi;
        private Harmony harmony;

        public override double ExecuteOrder() => 0.2;

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            const string cfgFileName = "effectlib.json";
            try
            {
                EffectLibConfig fromDisk = api.LoadModConfig<EffectLibConfig>(cfgFileName);
                if (fromDisk == null)
                    api.StoreModConfig(EffectLibConfig.Loaded = new EffectLibConfig(), cfgFileName);
                else
                    EffectLibConfig.Loaded = fromDisk;
            }
            catch (Exception err)
            {
                // Left untouched on disk, so a typo can be fixed instead of being wiped.
                api.Logger.Error("[EffectLib] Failed to load {0}, using default settings: {1}", cfgFileName, err);
                EffectLibConfig.Loaded = new EffectLibConfig();
            }

            EffectLibConfig.Loaded.Clamp();
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            EffectRegistry.Logger = api.Logger;
            EffectPrimitives.RegisterAll();

            UtilityEffects.PlayerModelLibPresent = api.ModLoader.IsModEnabled("playermodellib");

            api.RegisterCollectibleBehaviorClass(
                "EffectItem",
                typeof(CollectibleBehaviorEffectItem)
            );

            api.RegisterCollectibleBehaviorClass(
                "EffectLiquid",
                typeof(CollectibleBehaviorEffectLiquid)
            );

            api.RegisterCollectibleBehaviorClass(
                "EffectCarry",
                typeof(CollectibleBehaviorEffectCarry)
            );

            api.RegisterEntity("EntityThrownEffectItem", typeof(EntityThrownEffectItem));

            if (!Harmony.HasAnyPatches(HarmonyId))
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }

            api.Network
                .RegisterChannel(ConfigSyncChannelName)
                .RegisterMessageType<EffectLibConfigSyncPacket>();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            EffectHandlers.Register(UtilityEffectHandler.Instance);
            EffectHandlers.Register(CarryRescanHandler.Instance);

            api.Event.PlayerNowPlaying += OnPlayerReady;
            api.Event.PlayerDisconnect += OnPlayerDisconnect;
            api.Event.PlayerDeath += OnPlayerDeath;
            api.Event.PlayerJoin += SendConfigSync;
            api.Event.AfterActiveSlotChanged += OnActiveSlotChanged;

            base.StartServerSide(api);
        }

        private void SendConfigSync(IServerPlayer player) =>
            sapi.Network
                .GetChannel(ConfigSyncChannelName)
                .SendPacket(EffectLibConfig.Loaded.ToSyncPacket(), player);

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            api.Network
                .GetChannel(ConfigSyncChannelName)
                .SetMessageHandler<EffectLibConfigSyncPacket>(packet =>
                    EffectLibConfig.Loaded.ApplySyncPacket(packet)
                );

            api.Event.LevelFinalize += () =>
            {
                if (api.World.Player?.Entity is EntityPlayer sizedPlayer)
                    UtilityEffects.ApplySizeToEntity(sizedPlayer);
            };
        }

        private static void OnPlayerReady(IServerPlayer player)
        {
            EffectManager manager = EntityBehaviorPlayerEffects.ManagerFor(player?.Entity);
            manager?.RestoreEffects();

            player?.Entity?.GetBehavior<EntityBehaviorPlayerEffects>()?.StartCarryTracking();
        }

        private static void OnPlayerDisconnect(IServerPlayer player)
        {
            EntityPlayer entity = player?.Entity;
            if (entity?.Properties == null || !entity.HasBehavior<EntityBehaviorPlayerEffects>())
                return;

            EntityBehaviorPlayerEffects behavior = entity.GetBehavior<EntityBehaviorPlayerEffects>();
            behavior?.SuspendCarryEffects();
            behavior?.Manager?.Suspend();
        }

        private static void OnActiveSlotChanged(IServerPlayer player, ActiveSlotChangeEventArgs args) =>
            player?.Entity?.GetBehavior<EntityBehaviorPlayerEffects>()?.MarkCarryDirty();

        private static void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
        {
            EntityPlayer entity = player?.Entity;
            if (entity?.Properties == null || !entity.HasBehavior<EntityBehaviorPlayerEffects>())
                return;

            entity.GetBehavior<EntityBehaviorPlayerEffects>()?.Manager?.ResetAll();
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.PlayerNowPlaying -= OnPlayerReady;
                sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;
                sapi.Event.PlayerDeath -= OnPlayerDeath;
                sapi.Event.PlayerJoin -= SendConfigSync;
                sapi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
                sapi = null;

                EntityEffects.ClearPendingStatRemovals();
            }

            harmony?.UnpatchAll(HarmonyId);
            harmony = null;

            base.Dispose();
        }
    }

    public class EffectLibHudMod : ModSystem
    {
        private ICoreClientAPI capi;
        private GuiHudEffects hud;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            capi = api;
            hud = new GuiHudEffects(api);

            api.Input.RegisterHotKey(
                GuiHudEffects.HotkeyToggle,
                Lang.Get("effectlib:hotkey-toggle-hud"),
                GlKeys.LBracket,
                HotkeyType.GUIOrOtherControls
            );
            api.Input.SetHotKeyHandler(GuiHudEffects.HotkeyToggle, ToggleGui);

            api.Input.RegisterHotKey(
                GuiHudEffects.HotkeyMove,
                Lang.Get("effectlib:hotkey-move-hud"),
                GlKeys.RBracket,
                HotkeyType.GUIOrOtherControls
            );
            api.Input.SetHotKeyHandler(GuiHudEffects.HotkeyMove, MoveGui);

            api.Input.RegisterHotKey(
                GuiHudEffects.HotkeyStyle,
                Lang.Get("effectlib:hotkey-style-hud"),
                GlKeys.BackSlash,
                HotkeyType.GUIOrOtherControls
            );
            api.Input.SetHotKeyHandler(GuiHudEffects.HotkeyStyle, StyleGui);

            api.Event.LevelFinalize += () =>
            {
                hud.HookPlayer();

                if (!capi.Settings.Bool.Exists(GuiHudEffects.SettingAutoEnabled))
                {
                    if (!capi.Settings.Bool[GuiHudEffects.SettingEnabled])
                    {
                        capi.Settings.Bool[GuiHudEffects.SettingEnabled] = true;
                    }
                    capi.Settings.Bool[GuiHudEffects.SettingAutoEnabled] = true;
                }

                if (capi.Settings.Bool[GuiHudEffects.SettingEnabled])
                {
                    hud.TryOpen();
                }
            };
        }

        private bool ToggleGui(KeyCombination comb)
        {
            if (hud.IsOpened())
            {
                hud.TryClose();
                capi.Settings.Bool[GuiHudEffects.SettingEnabled] = false;
            }
            else
            {
                hud.TryOpen();
                capi.Settings.Bool[GuiHudEffects.SettingEnabled] = true;
            }

            return true;
        }

        private bool MoveGui(KeyCombination comb)
        {
            if (hud.IsOpened() && hud.SingleComposer.Composed)
            {
                hud.CyclePosition();
            }
            return true;
        }

        private bool StyleGui(KeyCombination comb)
        {
            hud.ToggleStyle();
            return true;
        }
    }
}
