using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace EffectLib
{
    public delegate void EffectBuilder(EffectContext ctx);

    public sealed record EffectRegistration(
        string Id,
        string Domain,
        EffectBuilder Builder,
        AssetLocation IconSource = null,
        IReadOnlyCollection<string> Channels = null,
        string ExclusivityGroup = null,
        AssetLocation IconTexture = null,
        bool HideHud = false
    );

    public static class EffectRegistry
    {
        public const string DefaultDomain = "effectlib";

        internal static ILogger Logger { get; set; }

        private static readonly ConcurrentDictionary<string, byte> warnedInstantStats = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly ConcurrentDictionary<string, EffectRegistration> entries = new(
            StringComparer.OrdinalIgnoreCase
        );

        // Each item keeps its own definition of an effect id, keyed "effectId|itemCode" - the id
        // says which effects are the same effect (they never stack), the item says how strong it
        // is. entries holds the id's default (last registered), for when no item is known.
        private static readonly ConcurrentDictionary<string, EffectRegistration> bySource = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static string SourceKey(string effectId, AssetLocation source) => $"{effectId}|{source}";

        public static IReadOnlyDictionary<string, EffectRegistration> Registrations => entries;

        private static readonly ConcurrentDictionary<string, byte> reserved = new(StringComparer.OrdinalIgnoreCase);

        public static void Reserve(IEnumerable<string> ids)
        {
            foreach (string id in ids)
                if (!string.IsNullOrWhiteSpace(id))
                    reserved.TryAdd(id, 0);
        }

        public static bool IsReserved(string effectId) =>
            effectId != null && reserved.ContainsKey(effectId);

        public static void Register(
            string effectId,
            EffectBuilder builder,
            string domain = DefaultDomain,
            AssetLocation iconSource = null,
            IEnumerable<string> channels = null,
            string exclusivityGroup = null,
            AssetLocation iconTexture = null,
            bool hideHud = false
        )
        {
            if (string.IsNullOrWhiteSpace(effectId) || builder == null)
                return;

            domain = string.IsNullOrWhiteSpace(domain) ? DefaultDomain : domain;
            effectId = EffectIds.Qualify(effectId, domain);

            if (reserved.ContainsKey(effectId))
                return;

            HashSet<string> channelSet =
                channels == null ? null : new HashSet<string>(channels, StringComparer.OrdinalIgnoreCase);

            EffectRegistration registration = new(
                effectId,
                domain,
                builder,
                iconSource,
                channelSet is { Count: > 0 } ? channelSet : null,
                string.IsNullOrWhiteSpace(exclusivityGroup) ? null : exclusivityGroup,
                iconTexture,
                hideHud
            );

            entries[effectId] = registration;
            if (iconSource != null)
                bySource[SourceKey(effectId, iconSource)] = registration;
        }

        private static System.Func<string, EffectRegistration>[] resolvers = [];
        private static readonly object resolversLock = new();

        public static void AddResolver(System.Func<string, EffectRegistration> resolver)
        {
            if (resolver == null)
                return;

            lock (resolversLock)
            {
                if (!resolvers.Contains(resolver))
                    resolvers = [.. resolvers, resolver];
            }
        }

        private static EffectRegistration Resolve(string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId))
                return null;

            if (entries.TryGetValue(effectId, out EffectRegistration entry))
                return entry;

            foreach (System.Func<string, EffectRegistration> resolver in resolvers)
            {
                EffectRegistration resolved = resolver(effectId);
                if (resolved?.Builder != null)
                {
                    entries[effectId] = resolved;
                    return resolved;
                }
            }

            return null;
        }

        private static EffectRegistration Resolve(string effectId, AssetLocation source) =>
            source != null
            && effectId != null
            && bySource.TryGetValue(SourceKey(effectId, source), out EffectRegistration entry)
                ? entry
                : Resolve(effectId);

        public static bool IsRegistered(string effectId) => Resolve(effectId) != null;

        // Whether this particular item has registered its own definition of the effect.
        public static bool IsRegistered(string effectId, AssetLocation source) =>
            source != null && effectId != null && bySource.ContainsKey(SourceKey(effectId, source));

        public static string DomainOf(string effectId) => Resolve(effectId)?.Domain ?? DefaultDomain;

        public static AssetLocation IconSourceOf(string effectId) => Resolve(effectId)?.IconSource;

        public static AssetLocation IconTextureOf(string effectId) => Resolve(effectId)?.IconTexture;

        public static AssetLocation IconTextureOf(string effectId, AssetLocation source) =>
            Resolve(effectId, source)?.IconTexture;

        public static bool AllowsChannel(string effectId, string channel)
        {
            IReadOnlyCollection<string> channels = Resolve(effectId)?.Channels;
            return channels == null || channels.Contains(channel);
        }

        public static bool HasExplicitChannels(string effectId) =>
            Resolve(effectId)?.Channels is { Count: > 0 };

        // Whether this effect's registration asked to stay off the effect HUD.
        public static bool HideHudOf(string effectId, AssetLocation source = null) =>
            Resolve(effectId, source)?.HideHud == true;

        public static string GroupOf(string effectId) => Resolve(effectId)?.ExclusivityGroup;

        public static EffectContext Build(string effectId, float potencyMul) =>
            Build(effectId, potencyMul, null);

        public static EffectContext Build(string effectId, float potencyMul, AssetLocation source) =>
            Build(effectId, potencyMul, source, 1f);

        // Uses source's own definition of the effect if it registered one, else the id's default.
        public static EffectContext Build(
            string effectId,
            float potencyMul,
            AssetLocation source,
            float durationMul
        )
        {
            EffectRegistration entry = Resolve(effectId, source);
            if (entry == null)
                return null;

            EffectContext def = new()
            {
                PotencyMul = potencyMul,
                DurationMul = durationMul,
                ExclusivityGroup = entry.ExclusivityGroup,
                Source = entry.IconSource,
            };

            entry.Builder(def);

            // An instant effect shouldn't be used with stats
            if (def.Duration == 0 && def.StatModifiers.Count > 0)
            {
                def.StatModifiers.Clear();
                if (warnedInstantStats.TryAdd(SourceKey(entry.Id, entry.IconSource), 0))
                    Logger?.Warning(
                        "[EffectLib] Effect {0}{1} sets stats but has no duration, so it is instant and "
                            + "its stats are ignored. Give it a \"duration\" (seconds, or -1 for until death) "
                            + "to use them.",
                        entry.Id,
                        entry.IconSource == null ? "" : $" (from {entry.IconSource})"
                    );
            }

            // Timed only; instant (0) and endless (-1) untouched.
            if (def.Duration > 0 && Math.Abs(durationMul - 1f) > 0.001f)
                def.Duration = Math.Max(1, (int)Math.Round(def.Duration * durationMul));

            return def;
        }
    }
}
