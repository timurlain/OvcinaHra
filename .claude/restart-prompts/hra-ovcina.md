# OvčinaHra — Restart Handoff (2026-04-24)

## Session outcome
All work from the 2026-04-23 / 24 session has shipped and merged. Repo is clean.

### Shipped PRs this session (on main, latest first)
| PR | Version | What |
|---|---|---|
| #90 | v0.15.5 | `/items/{id}` detail page — 5 tabs (5:7 MTG hero, crafting graph, výskyt, obchod). Second-agent work. |
| #89 | v0.15.3 | **Razítko** (rubber stamp) per catalog Location. `Location.StampImagePath` + migration `AddLocationStampImagePath`. New `ImageEndpoints` entity-type alias `"locationstamps"` keyed by Location.Id, reads/writes StampImagePath (own thumb-cache namespace). `LocationEditPopup` grows third `ImagePicker` (1:1, 180 px). `LocationList` adds hidden "Razítko" column (128×128 thumb). **LayoutKey bumped v3 → v4.** |
| #88 | v0.15.2 | Items grid redesign — photo, type-dot, class pips, flags box. |
| #87 | v0.15.2 (shipped as v0.15.4 after rebase) | `LocationGridStashTile.razor` renders stash thumbnails in `/locations` grid, mirrors `SecretStashGridTile`'s @onload/@onerror state pattern. CSS adds `.oh-lg-stash-tile > img` + veil rules. |
| #86 | — (test-only) | Testcontainers integration for POST `/api/images/backfill-thumbs`. Also fixed Npgsql bug where `ValueTuple.Create` projections in `BackfillAllThumbsAsync` couldn't deserialize — switched to anonymous `new { Id, BlobKey }`. |
| #85 | v0.15.1 | Aerial map on location detail. |
| #84 | v0.15.0 | Pre-gen thumbs on upload + `ThumbnailBackfillHostedService` sweeps at startup. 302 hot-cache redirect. |

### Live state
- `origin/main` HEAD: `e1a3dbc` (v0.15.5) — single branch, zero open PRs.
- Verify deploy: `curl https://api.hra.ovcina.cz/api/version` should report `commit: e1a3dbc` (~8 min after last merge).

## Worktrees right now
Only the root checkout on `main`. No feature worktrees, no stale refs.

## Known unfinished — **image audit (grid=thumb, detail=full SAS)**

User stated the rule explicitly this session: list/grid tiles use the thumbnail endpoint; **detail pages and big peek popups must load the full SAS URL** via `GET /api/images/{entityType}/{id}` → `ImageUrlsDto.ImageUrl`. Saved to memory (`feedback_ovcinahra_image_sourcing_rule.md`).

Violations I found but did NOT fix before the merge sweep:

1. **`src/OvcinaHra.Client/Pages/SecretStashes/SecretStashDetail.razor:43`** — hero `<img src="@stash.ImageUrl">`. `stash` is `SecretStashDetailDto` from `/api/secret-stashes/{Id}` but its `ImageUrl` is populated by the endpoint with a thumbnail URL. Either fix the endpoint projection to emit a full SAS URL on the *detail* DTO, OR add a `GetAsync<ImageUrlsDto>` call and bind a separate `fullImageUrl` — `LocationDetail.razor:466-469` is the reference pattern.
2. **`src/OvcinaHra.Client/Pages/Items/ItemList.razor:258`** (quick-peek popup) — `<img src="@quickPeekItem.ImageUrl">` bound straight from list DTO. Adopt `MonsterList.razor:520-535` pattern: on peek-open, `Api.GetAsync<ImageUrlsDto>($"/api/images/items/{m.Id}")`, assign to `quickPeekImageUrl`.

Already correct (don't touch): `LocationDetail.razor`, `MonsterDetail.razor`, `MonsterList.razor` quick-peek, `LocationEditPopup.razor` card view. All use `ImageUrlsDto`.

## Other TODOs on the horizon
- `drozd.png` missing on `/locations` empty state (`LocationList.razor:74` — currently a 120 px `bi-feather` placeholder).
- Nothing else pending that I know of. Ask user what's next.

## New memories / rules added this session (all indexed in `MEMORY.md`)
- `feedback_ovcinahra_image_sourcing_rule.md` — grid=thumb / detail=full SAS.
- `feedback_ovcinahra_secondary_image_entity_alias.md` — for a second thumbnailed image on an entity, add a new `ValidEntityTypes` key (e.g. `"locationstamps"`); don't thread a `?field=` query param.
- `feedback_parallel_pr_version_collision.md` — when two PRs both bump `<Version>` to the same X.Y.Z, first to merge wins; the others rebase + re-bump.

## Standing conventions (reinforced in prior sessions)
- **Czech spellings are intentional.** `Představěná` and `Standartní` must not be "corrected". Ignore Copilot's rename flags.
- **DxMemo `MaxLength` is not safe** in DevExpress 25.2.5 — enforce length caps server-side (endpoint `BadRequest`) per the Monster Notes pattern.
- **LayoutKey bump** when grid column schema changes (add/remove/rename/reorder). Adding a hidden column still counts; bumped v3→v4 this session in PR #89.
- **Destructive action confirm** — any delete / remove / unlink goes through a `DxPopup` confirmation before the handler runs. Never wire straight.
- **Never commit or push without an explicit user instruction.** Batching multiple tweaks per PR is the norm.
- **Czech UI**, English code and routes. `/locations` not `/lokace`. Diacritics always.
- **Version bump every shippable PR.** `[vX.Y.Z]` in commit message AND PR title.
- **Parallel PRs + version bumps** — check `git log origin/main` for the latest shipped version before opening a new PR. If someone else is mid-flight at the same version, preemptively take the next one.

## Next-session flow
1. New UI/feature change → spawn fresh worktree off `origin/main`: `.worktrees/<slug>` + `feat/<slug>`. Never edit the root checkout directly.
2. Commit → push → `gh pr create` → poll CI → `gh pr merge N --squash --delete-branch`.
   - **Auto-merge is DISABLED on this repo.** Tried `--auto`, got `enablePullRequestAutoMerge` error. Poll CI manually or via `ScheduleWakeup`/`/loop`.
   - After merge: `git worktree remove .worktrees/<slug>` + `git branch -D <branch>`.
3. Verify deploy: `curl https://api.hra.ovcina.cz/api/version` against `git rev-parse origin/main`.

## Tone
User invokes `hra-ovcina-tinkerer` which sets the Tasha voice (Slavic-flavoured English, practical, direct, terse). Keep updates between tool calls short.

## Cheat-sheet (paths relative to repo root)
- Stamp / location images: `src/OvcinaHra.Api/Endpoints/ImageEndpoints.cs` (entity-type dispatch, `GetEntityImagePaths`, `UpdateEntityImagePath`, `BackfillAllThumbsAsync`).
- Location list/detail: `src/OvcinaHra.Client/Pages/Locations/{LocationList,LocationDetail}.razor` + `src/OvcinaHra.Client/Components/LocationEditPopup.razor`.
- Stash grid tile: `src/OvcinaHra.Client/Components/LocationGridStashTile.razor` (new this session, reference pattern for tile thumbnails).
- Items detail: `src/OvcinaHra.Client/Pages/Items/ItemDetail.razor` (new this session, PR #90).
- CSS: `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` (`.oh-lg-*` for /locations grid, `.oh-ssg-*` for /secret-stashes gallery).
- Migrations: `src/OvcinaHra.Api/Migrations/` (latest: `AddLocationStampImagePath`).
