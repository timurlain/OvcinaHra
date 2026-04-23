# Quest Maker Wizard — design (Phase 2)

**Date:** 2026-04-23
**Status:** Approved — ready for implementation planning
**Depends on:** Phase 1 shipped in PR #57 (v0.10.0) — spell rewards + catalog XP.
**Branch:** `feat/personal-quest-wizard`

## Problem

Content managers creating a PersonalQuest currently have to leave the game-quests view whenever a reward's catalog entry is missing: jump to `/items`, create it, maybe also add it to the game, jump back, find their half-filled form, try to remember where they were. Flow breaks every time.

Phase 1 added the missing reward types and XP model but kept the same popup. Creating a quest still means:
- Open popup → fill fields → save
- "Need a new item reward" → close popup → navigate to `/items` → create → navigate to game → link item to game → back to personal quests → reopen popup → try to remember form state → add reward

Phase 2 collapses that into one sitting.

## Scope of this design

A **Quest Maker Wizard** — a guided create-quest flow launched from the game-quests view that handles everything in a single popup, including inline creation of missing rewards. Phase 2 does **not** replace the existing catalog popup; both coexist.

## Decisions made with user

| Q | Answer |
|---|---|
| Wizard mode | Create-only. Existing popup keeps handling edits + quick catalog creates. |
| Shell | Big `DxPopup` (Width=900px, `AllowResize=true`, scrollable body). No new route. |
| Form shape | **Smart form** — all fields visible in sections. No stepper, no draft save. |
| Inline creators | All 3 reward types (Skill, Item, Spell). Each is a small nested `DxPopup`. Reuses existing `Create*Dto` + existing POST endpoints. |
| Inline-creator side-effect | On save: catalog entity created AND auto-linked to the current game (`GameItem` / `GameSkill` / `GameSpell`) with defaults — user doesn't have to add it to the game separately. |
| Launch button | `PersonalQuestList.razor` game-view toolbar, label **"Vytvořit quest pro hru"**, icon `bi-magic`, next to the existing "Nový" button. Only rendered when a game is selected. |
| Save flow | Sequential API calls with one progress indicator. On error: stop, surface message, leave partial state. User can retry or clean up via the existing popup. |
| Post-save | Close wizard, return to game-quests list, new row visible. |
| Field coverage | Full superset of the existing popup: Name, Difficulty, XpCost, 4 class flags, Description, QuestCardText, RewardCardText, RewardNote, Notes + all 3 reward types + per-game config (XpCost override, PerKingdomLimit). |
| Draft save | Not in scope. Closing the wizard mid-flow discards the in-memory state. |
| Version bump | 0.10.0 → 0.10.1 (UI-only additive, no schema change). Final value confirmed at implementation time. |

## Architecture

### Components to create

1. **`QuestWizard.razor`** — the main component. Hosts the outer `DxPopup`, the smart form split into 4 sections, and owns the wizard view-model. Renders the 3 inline-creator sub-popups as children and handles the sequential save flow.
2. **`SkillInlineCreator.razor`** — small nested `DxPopup`; form mirrors `/skills` create popup; POST to `/api/skills` + `/api/skills/game-skill`. Emits `EventCallback<SkillDto>` on save.
3. **`ItemInlineCreator.razor`** — analogous for items. POST to `/api/items` + `/api/items/game-item`.
4. **`SpellInlineCreator.razor`** — analogous for spells. POST to `/api/spells` + `/api/spells/game-spell`. Hides `IsLearnable` from the reward-creator preset (force-sets `false`) because non-learnable is required for quest rewards.

All four live under `src/OvcinaHra.Client/Pages/PersonalQuests/` (co-located with `PersonalQuestList.razor`).

### Form sections (single scrollable body)

Order, top to bottom:

1. **Základ** — Name (required), Difficulty, XpCost spinner.
2. **Pravidla** — 4 class flags (Warrior/Archer/Mage/Thief checkboxes), Description text.
3. **Texty** — QuestCardText (multi-line), RewardCardText (multi-line), RewardNote (short), Notes (multi-line).
4. **Odměny** — three sub-sections: Dovednosti / Předměty / Kouzla. Each has a combobox of existing catalog entries (filtered per reward type; spells filtered to `IsLearnable = false`) + a "Vytvořit novou…" button that opens the appropriate inline creator. Added rewards appear as badges with an `×` remove button and (for items/scrolls) a quantity spinner.
5. **Per-hra** — XpCost override (nullable, `NullText="výchozí: {catalog}"`), PerKingdomLimit spinner.

### View-model shape

One `WizardModel` class holding all the fields above plus three in-memory reward lists (not persisted until save):
```csharp
public sealed class WizardModel
{
    public string Name { get; set; } = "";
    public TreasureQuestDifficulty Difficulty { get; set; } = TreasureQuestDifficulty.Early;
    public int XpCost { get; set; }
    // class flags, texts, per-game fields...
    public List<SkillReward> Skills { get; } = new();
    public List<ItemReward> Items { get; } = new();
    public List<SpellReward> Spells { get; } = new();
    public int? GameXpOverride { get; set; }
    public int? PerKingdomLimit { get; set; }
}
```

The reward sub-types store both the referenced entity ID and a display name for badge rendering before the quest even exists.

### Save flow

Triggered by the single "Uložit a přidat do hry" button. Progress indicator (step label + small spinner) above the button. Sequence:

1. `POST /api/personal-quests` with the catalog fields + XpCost → returns `{ id }`.
2. For each reward in `model.Skills`/`Items`/`Spells`:
   - `POST /api/personal-quests/{questId}/{type}-rewards` with the reward DTO.
3. `POST /api/personal-quests/game-link` with `{ gameId, questId, XpCost: model.GameXpOverride, PerKingdomLimit: model.PerKingdomLimit }`.
4. Close wizard, parent page reloads quest list, new row visible.

Inline creators handle their own linking at the moment the user saves them, NOT at wizard save time — so by the time the wizard saves the quest, every reward entity already exists in the catalog AND is linked to the game. Wizard save only has to wire rewards to the quest and create the game-link.

### Error handling

Each save step has a `try/catch`. On exception:
- Progress indicator shows the failing step.
- Error message (Czech, from the server response body when available) shown in a red alert above the save button.
- Wizard stays open. User can retry (state intact) or close (partial state persists — they can finish via the existing popup).

No atomic "wizard save" endpoint. Each step is idempotent on retry except the initial POST; duplicates are caught by existing uniqueness constraints (e.g., `409 Conflict` on duplicate spell-reward).

### Launch gating

The "Vytvořit quest pro hru" button is rendered only when `GameContext.SelectedGameId is not null` AND the user is on the game view (not catalog). Matches the existing per-game card's visibility logic.

## What's NOT in Phase 2

- **Editing existing quests via wizard** — stays in the popup.
- **Draft save / resume** — close = discard.
- **Batch create** — save closes the wizard.
- **New atomic server endpoint** — the client orchestrates, server endpoints stay as-is.
- **Preview / "Review" step** — smart form is already visible-all-at-once.
- **Templates / duplicate-from-existing** — separate feature.

## Risks & mitigations

- **Partial save on failure:** Not full ACID, but acceptable. Content-manager-only app. If quest is created but reward fails, the user can finish via the existing popup OR cleanup via the existing Delete row. The alternative (new atomic endpoint) would add server complexity disproportionate to the rare failure path.
- **Inline creator desync with main wizard form:** Inline creator closes and returns the new entity; wizard auto-selects it as a reward. If the user cancels inline creator, no change. Simple callback-based contract.
- **Scope creep on validation:** Keep client-side validation minimal — required fields (Name) and numeric bounds (XpCost >= 0). Server is authoritative. Rely on the server's 400s (added in Phase 1's fixup) to catch negative values.

## Version & deployment

- Client version bump **0.10.0 → 0.10.1** in the implementation PR.
- Pure additive UI. No migrations, no server endpoints. Auto-deploys on merge.

## File footprint estimate

- 4 new razor components (~200–400 lines each, varies)
- No new C# files in Shared or Api — reuses everything Phase 1 shipped
- Minor edit to `PersonalQuestList.razor` for the launch button + wizard mounting
- Maybe 1–2 integration tests exercising the full create-quest flow end-to-end (optional — current coverage is probably enough; defer decision to implementation plan)
