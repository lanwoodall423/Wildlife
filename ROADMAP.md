# Wildlife Roadmap

This roadmap tracks planned Wildlife features and their intended implementation sequence. Entries describe design goals, not release commitments. Features remain unimplemented until explicitly marked otherwise.

## Planned: Animal Exertion & Endurance

**Status:** Proposed / not implemented  
**Release target:** Unscheduled

### Goal

Make pursuit and escape depend on both an animal's immediate speed and its ability to sustain exertion. Short-burst sprinters should excel at closing a short distance; endurance runners should be able to maintain pressure; prey should be able to sprint, pace itself, break contact, or seek cover. A faster animal should not automatically win every extended chase.

### Initial scope

1. **Species physiology profiles.** Define independently configurable burst-speed multiplier, burst capacity, sustained-running efficiency/endurance, recovery rate, and exhaustion resistance. Provide sensible default archetypes (sprinter, endurance runner, balanced runner, short-dash prey) and safe fallbacks for animals added by other mods. Treat numeric species profiles as balance data, not claims of biological precision.
2. **Per-animal exertion state.** Track a bounded immediate stamina reserve and slower accumulated fatigue for participating animals. Drain exertion during actual high-urgency fleeing and chasing, allow recovery when resting or moving calmly, and avoid drain from normal wandering. Persist state where individual animals remain authoritative, with backward-compatible defaults on existing saves. Respect Wildlife's active/latent population and identity-anchor boundaries; do not persist or simulate full transient Pawn state in regional aggregates.
3. **Speed and movement.** Let burst movement temporarily exceed each animal's ordinary movement speed, then taper toward a sustainable pace and, if necessary, a tired pace. Preserve vanilla speed effects from injuries, health, terrain, and other mods. Cap modifiers and reconcile existing `Herds_HuntedAdrenaline`, `Herds_FlightBurst`, and `Herds_HuntFatigue` effects so bonuses and penalties do not stack without control.
4. **Predator pursuit decisions.** Feed available stamina, fatigue, and speed sustainability into the existing Packs `HuntPhase.Chase`, `HuntJobFor`, `ScanHunts`, and `ShouldAbandonHunt` flows. Permit sprinters to commit to short attempts and disengage after failed bursts; allow endurance predators to maintain paced pursuits. Preserve hunger, pack roles, deterrents, health, distance, and current hunt-cooldown decisions.
5. **Prey escape decisions.** Integrate with Herds `HerdDefenseJobs`, flight/scatter/hide orders, and the existing predator-escape calculation. Prey can spend a burst to open distance, then slow, seek refuge, or change escape tactics when winded. Avoid repeated panic/restart loops and preserve the existing threat-end notification behavior.

### Implementation stages

- **Stage 1 — Model and instrumentation:** Implement configurable physiology and per-animal exertion, safe serialization/recovery, a bounded update cadence, and developer-only telemetry. Verify stable speed curves and stamina state before changing hunt outcomes.
- **Stage 2 — Encounter integration:** Wire exertion into existing predator and prey job/decision paths, tune chase abandonment and escape behavior, and reconcile the current movement-related Hediffs. Keep Herds and Packs optional; expose state via compatibility-safe interfaces without adding hard compile-time dependencies between companion assemblies.
- **Stage 3 — Wildlife knowledge and polish:** Reveal learned species traits (for example, *explosive sprinter*, *exceptional endurance*, *tires quickly*) through the existing Knowledge Framework / `WildlifeInsightDisclosure` presentation boundary. Prefer qualitative, evidence-gated information to exact hidden stamina values; reserve precise values for developer diagnostics. Consider individual variation, age, weather, terrain, pack-relay tactics, and training only after the core system is balanced.

### Acceptance criteria

- A short-burst sprinter can briefly outrun a faster-at-distance opponent, but loses its advantage if the encounter is prolonged.
- An endurance runner can maintain a sustainable chase longer without an indefinite speed buff.
- A prey animal can recover between threats, but repeated escapes can accumulate meaningful fatigue.
- Hunters abandon impossible or exhausting pursuits rather than continuously re-entering them; no job/threat oscillation is introduced.
- Existing hunting, defensive behavior, refuge use, and Hediff effects still work when the feature is disabled or Packs is absent.
- Existing saves load safely; state remains correct across save/reload, despawn, hiding, and active/latent wildlife transitions.
- No per-tick whole-map animal scans or unbounded per-Pawn allocations are introduced; updates focus on active encounters and bounded recovery.
- Add deterministic chase, recovery, abandonment, serialization, compatibility, and performance checks to the existing in-game test suite. Follow `AGENTS.md`: run `rimtest doctor --json` and `rimtest affected --run --json` for implementation validation.

**Out of scope for the first release:** A global stamina bar for all pawn activity, detailed cardiovascular simulation, complex relay hunting, long-term athletic training, and regional aggregate stamina.
