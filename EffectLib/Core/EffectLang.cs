using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EffectLib
{
    public static class EffectLang
    {
        // Which key to show for one of effectlib's lang entries in langCode (default: the current
        // locale): the mod's "<domain>:<key>", then effectlib's, in that language and then in
        // English. Neither having it returns effectlib's key, which shows as-is like any missing entry.
        public static string Key(string domain, string key, string langCode = null)
        {
            string owned = $"{domain}:{key}";
            string shared = $"{EffectRegistry.DefaultDomain}:{key}";

            foreach (string lang in new[] { langCode ?? Lang.CurrentLocale, Lang.DefaultLocale })
            {
                if (!Lang.AvailableLanguages.TryGetValue(lang, out ITranslationService service))
                    continue;
                if (domain != null && service.HasTranslation(owned, false, false))
                    return owned;
                if (service.HasTranslation(shared, false, false))
                    return shared;
            }

            return shared;
        }

        public static string NameFor(string effectId, EffectContext ctx) =>
            Lang.GetIfExists(effectId) ?? EffectDescription.Summary(ctx) ?? effectId;

        public static void Send(IServerPlayer player, string langKey, params object[] args) =>
            player?.SendMessage(
                GlobalConstants.InfoLogChatGroup,
                Lang.GetL(player.LanguageCode ?? Lang.CurrentLocale, langKey, args),
                EnumChatType.Notification
            );

        // Send for one of effectlib's lang entries, preferring the mod's own (see Key) in the
        // player's language.
        public static void SendFor(IServerPlayer player, string domain, string key, params object[] args) =>
            Send(player, Key(domain, key, player?.LanguageCode), args);
    }
}
