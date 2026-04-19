# ImagePicker Redesign — Design

**Date:** 2026-04-19
**Status:** Approved — ready for implementation plan
**Trigger:** The existing `ImagePicker` default mode shows a native "Choose File" + "No file chosen" UI that is ugly and inconsistent with the card-style upload the rest of the app now expects. We want a single clean box-with-aspect-ratio component we can drop anywhere the organizer uploads an image.

## Goal

Enhance `src/OvcinaHra.Client/Components/ImagePicker.razor` so it renders only as a click-to-upload box with a caller-specified aspect ratio. No "Choose File" button, no "No file chosen" text anywhere.

When empty, the box shows a centered red X plus the caption "Nahrát obrázek". When set, it shows the image scaled to fit the aspect ratio with a small red `×` delete button in the corner. Click anywhere on the box opens the file picker for both empty and replace flows.

## Non-goals

- Image cropping, rotating, or any editing.
- Drag-and-drop file drop (might come later; not in scope now).
- Multiple-image upload in a single picker.
- Validating image dimensions against the requested aspect ratio (browsers/users pick any image, `object-fit: cover` handles display).

## Component API

Single component, **same file path**: `src/OvcinaHra.Client/Components/ImagePicker.razor` + `.razor.css`.

| Parameter | Type | Default | Purpose |
|---|---|---|---|
| `EntityType` | `string` | *(required)* | URL path segment (`"items"`, `"skills"`) |
| `EntityId` | `int` | *(required)* | ID of the entity the image belongs to |
| `Field` | `string?` | `null` | Optional sub-slot for entities with multiple images (e.g. `"placement"`) |
| `Alt` | `string` | `"Obrázek"` | `alt` attribute on the rendered `<img>` |
| `AspectRatio` | `string` | `"1:1"` | **NEW** — e.g. `"1:1"`, `"3:2"`, `"16:9"`. Internally parsed to CSS `aspect-ratio: W / H`. Invalid input logs a warning and falls back to `1:1`. |
| `Width` | `string` | `"200px"` | **NEW** — CSS width value. `"100%"` for fluid in parent, fixed pixels for a fixed box. Height follows from the aspect ratio. |

**Removed:** `CardMode` parameter. The card/box behavior becomes the only behavior — there is no alternate "default Choose File" mode anymore.

## States

### Empty (no image)

- Wrapper `<div class="image-picker-box">` with subtle dashed gray border, 4px rounded corners.
- Centered red `✕` (≈40% of box height, sized in `em` so it scales with width).
- Caption "Nahrát obrázek" below the X, small muted gray text.
- `cursor: pointer` on hover; subtle background darken on hover.
- Click anywhere on the box → opens the native file picker (Blazor `<InputFile>` hidden via `class="d-none"`, triggered programmatically or via a `<label>` wrapper).

### With image

- Image fills the box with `object-fit: cover`; no dashed border.
- Small red square `×` delete button, top-right corner (keep existing styling from `ImagePicker.razor.css`).
- Click on image → opens file picker (replace flow).
- Click on `×` → API DELETE; on success, reverts to empty state. `@onclick:stopPropagation="true"` so the replace flow isn't triggered.

### Upload in progress

- Box opacity 0.6, cursor `wait`.
- Centered `.spinner-border.spinner-border-sm` overlay on semi-transparent white backdrop.
- Click disabled for the duration.

### Error

- Error text below the box, Czech, small red (same as existing `.text-danger.small.mt-1`).
- Box remains clickable for retry.

## Styling details

- All new styles live in `ImagePicker.razor.css` (Blazor scoped CSS).
- Wrapper uses inline `style="width: @Width; aspect-ratio: @parsedRatio;"` — dynamic per-caller values don't belong in the scoped CSS file.
- Red X color: `#dc3545` (Bootstrap `danger`), same as delete button.
- Empty-state hover: `background-color: #f8f9fa`.
- Font sizing uses `em` so the X and caption scale with box size; for very small boxes, min-sizes keep them legible.

## Migration

All existing callers of `<ImagePicker ...>` in the project will be updated in the implementation plan:

- Any `CardMode="true"` attribute is removed (parameter no longer exists).
- `AspectRatio="..."` added per caller based on domain context (Items likely `1:1`, Locations `3:2`, Characters maybe `2:3`). Each caller is reviewed in the plan.
- `Width="..."` added only where the default 200px doesn't fit the surrounding layout.

The grep sweep happens in the implementation plan — no caller-level decisions are locked in this design.

## Testing

- **Manual browser smoke test per page** — empty → upload → replace → delete. Primary quality gate for this UI-only change.
- **No new unit/component tests** — the component is a thin UI wrapper over `InputFile` + existing HTTP calls. bUnit tests for CSS tweaks are not worth the maintenance cost.
- **Existing API integration tests** continue to pass — no server-side changes in this work.

## Rollout

Single commit sweep on `main` (or on a short-lived feature branch, to be decided in the implementation plan):

1. Component + CSS updates
2. Caller-side migration removing `CardMode` and adding `AspectRatio` / `Width`
3. Manual browser smoke on each touched page

No migration, no data changes, no API changes.

## Open questions

None — all decisions locked during brainstorming.
