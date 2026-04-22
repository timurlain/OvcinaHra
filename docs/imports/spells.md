---
type: data-import
entity: Spell
source: rulemaster MAG-005 (svitky) + MAG-006 (úrovně I-V)
last-updated: 2026-04-22
count: 28
notes: >
  Katalog kouzel Ovčina LARP. Importovat do tabulky Spell v hra.ovcina.cz.
  Svitky (IsScroll=true) může sesílat kdokoli, jsou jednorázové.
  Kouzla úrovně I-V (IsScroll=false) může sesílat jen mág s MinMageLevel <= postava.MageLevel.
  Ceny naučení (Price) z MAG-007. Ohnivá/Mentální/Mráz/Jed = SpellSchool; Reakce = IsReaction.
---

# Katalog kouzel — import

## Schéma řádku

| Sloupec | Typ | Poznámka |
|---------|-----|----------|
| Name | string | Kanonický název (česky, diakritika) |
| Level | int 0-5 | 0 = svitek |
| ManaCost | int 0-5 | Svitky = 0 (jednorázové) |
| School | enum | Fire, Frost, Water, Earth, Wind, Poison, Mental, Support, Utility |
| IsScroll | bool | true = svitek MAG-005 |
| IsReaction | bool | true = kouzlo lze seslat mimo pořadí |
| IsLearnable | bool | true = lze se naučit u budovy (obchodník/Mudrc/knihovna). Svitky = false. |
| MinMageLevel | int | 0 u svitků, 1-5 u kouzel MAG-006 |
| Price | int? | Cena naučení u obchodníka v groších (MAG-007); null u svitků |
| Effect | string | Mechanický text karty |
| SourceRule | string | ID pravidla v rulemaster |

**Pravidlo `IsLearnable`**: `IsLearnable = !IsScroll`. Tabulky níže neobsahují samostatný sloupec — loader ho odvodí. Svitky se ve světě nacházejí / kupují jako fyzické předměty, neučí se. Kouzla I-V se učí u budov.

---

## Úroveň 0 — Svitky (MAG-005)

Jednorázové, po seslání se odhodí. Každá postava je může používat.

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Jiskra | 0 | 0 | Fire | true | false | 0 |  | 3 ž ohněm. | MAG-005 |
| Jedový šíp | 0 | 0 | Poison | true | false | 0 |  | 3 ž jedem. | MAG-005 |
| Ledový šíp | 0 | 0 | Frost | true | false | 0 |  | 3 ž mrazem. | MAG-005 |
| Bahno | 0 | 0 | Utility | true | false | 0 |  | Příšera kategorie I neútočí toto kolo. | MAG-005 |
| Léčivé dlaně | 0 | 0 | Support | true | false | 0 |  | Vyleč 5 ž ztracených v tomto kole (žijící cíl). | MAG-005 |
| Klika | 0 | 0 | Utility | true | false | 0 |  | Přehoď 1k6, vyber vyšší výsledek. | MAG-005 |

---

## Úroveň I — 1 mana (od Mág 1) — MAG-006

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Ohnivá střela | 1 | 1 | Fire | false | false | 1 | 10 | Ohnivé. Zraň jeden cíl za 1k6 ž ohněm. | MAG-006 |
| Magický štít | 1 | 1 | Support | false | true | 1 | 10 | *Reakce* po útoku nepřítele. Změň výsledek jednoho hodu 1k6+ na [-1]. | MAG-006 |
| Omámení | 1 | 1 | Mental | false | false | 1 | 10 | Mentální. Cílová bytost s ≤10 ž přijde o akci. | MAG-006 |
| Magická pomoc | 1 | 1 | Support | false | false | 1 | 10 | Spolubojovník má do příštího útoku bonus rovný tvému ÚČ + hází navíc 1k6+. | MAG-006 |

---

## Úroveň II — 2 many (od Mág 2) — MAG-006

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Připrav plamen | 2 | 2 | Fire | false | false | 2 | 15 | Zvyš zranění příštího ohnivého kouzla o 5. | MAG-006 |
| Dodej kuráž | 2 | 2 | Support | false | false | 2 | 15 | Všichni na tvé straně mají toto kolo navíc 1k6+ na blízké a střelecké útoky. | MAG-006 |
| Kamenný pes | 2 | 2 | Utility | false | false | 2 | 15 | Vyvolej bytost 5/2 s 5 ž. Nemůže krýt, může okamžitě zaútočit. Při >4 bytostech zmizí po útoku. | MAG-006 |
| Větrná zeď | 2 | 2 | Utility | false | false | 2 | 15 | Toto kolo všechny výsledky 1k6+ při střeleckých útocích = [1]. | MAG-006 |

---

## Úroveň III — 3 many (od Mág 3) — MAG-006

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Ohnivá koule | 3 | 3 | Fire | false | false | 3 | 22 | Ohnivé. Zraň až dva cíle za 1k6 ž ohněm. | MAG-006 |
| Zrcadlo | 3 | 3 | Mental | false | true | 3 | 22 | Mentální, *reakce* po útoku na tebe. Nepřítel útočí na sebe. | MAG-006 |
| Omdli | 3 | 3 | Mental | false | false | 3 | 22 | Mentální. Nepříteli s ≤7 ž klesnou životy na 0. | MAG-006 |
| Bažina | 3 | 3 | Utility | false | false | 3 | 22 | Toto kolo všechny výsledky 1k6+ při blízkých útocích = [1]. | MAG-006 |

---

## Úroveň IV — 4 many (od Mág 4) — MAG-006

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Připrav výheň | 4 | 4 | Fire | false | false | 4 | 30 | Zvyš zranění příštího ohnivého kouzla o 10. | MAG-006 |
| Rychlost | 4 | 4 | Support | false | false | 4 | 30 | Dva spolubojovníci mohou okamžitě provést útok bez použití akce. | MAG-006 |
| Poslední dech | 4 | 4 | Support | false | true | 4 | 30 | *Reakce* po pádu spolubojovníka na 0 ž. Zachraň ho a vyleč 10 ž. | MAG-006 |
| Staff-fu | 4 | 4 | Utility | false | false | 4 | 30 | V rámci seslání proveď blízký útok s ÚČ 15. Nelze pokud jsi krytý. | MAG-006 |

---

## Úroveň V — 5 man (od Mág 5) — MAG-006

| Name | Level | ManaCost | School | IsScroll | IsReaction | MinMageLevel | Price | Effect | SourceRule |
|------|-------|----------|--------|----------|------------|--------------|------------|--------|------------|
| Ohnivá bouře | 5 | 5 | Fire | false | false | 5 | 40 | Ohnivé. Zraň všechny nepřátele za 1k6 ž ohněm. | MAG-006 |
| Zatemnění mysli | 5 | 5 | Mental | false | false | 5 | 40 | Mentální. Cíl útočí na tvůj cíl (i na sebe, nebo na bytost kterou kryje). | MAG-006 |
| Požehnej zbraně | 5 | 5 | Support | false | false | 5 | 40 | Toto kolo zranění spolubojovníků = dvojnásobek rozdílu (hod vs OČ). | MAG-006 |
| Instinktivní magie | 5 | 5 | Utility | false | false | 5 | 40 | Sešli kouzlo úrovně I nebo II zadarmo. Získej další akci. | MAG-006 |

---

## Poznámky k importu

1. **Typ kouzla (SpellSchool)** — odvozeno z MAG-012 (Typy kouzel). Enum zahrnuje všechny klasické živly (Fire, Frost, Water, Earth, Wind) + Poison, Mental, Support, Utility — i když některé v aktuálním katalogu nemají kouzlo, jsou připravené pro budoucí rozšíření. Kouzla neuvedená v žádném živlu jsou `Support` (buff/heal spojeneckého typu) nebo `Utility` (ovládání bojiště, vyvolávání, přehazování kostky).

2. **Reakce (IsReaction)** — pouze 3 kouzla: Magický štít (I), Zrcadlo (III), Poslední dech (IV). Spotřebují mágovu akci v kole.

3. **Ceny naučení (Price, MAG-007)** — lineární s úrovní: 10 / 15 / 22 / 30 / 40 gr. U svitků null (nekupují se na učení, jsou fyzické jednorázové kartičky). Případná per-game cena svitků patří do `GameSpell.Price`.

4. **Obnovení many** — není per-spell property; je to globální pravidlo MAG-001 (mana se obnoví na konci souboje). Nemodelovat v entitě Spell.

5. **Koncentrace (MAG-003)** — není kouzlo, je to zvláštní akce mága. Nemodelovat v entitě Spell — patří do logiky postavy/souboje.

6. **Arcimágova hůl** — je to artefakt (Item), ne kouzlo. Patří do tabulky `Item` (typ `Staff`), ne sem.

---

## Budovy pro výuku (SpellBuildingRequirement)

Podle MAG-007 se kouzla učí **u obchodníka ve městě, u Mudrců, v knihovně**. Modelováno jako samostatná tabulka `SpellBuildingRequirement(SpellId, BuildingId)` (M:N, bez úrovně) — přesně podle vzoru `SkillBuildingRequirement`.

**`BuildingId` je prostý int FK** do tabulky `Building` (stejně jako ve všech ostatních relacích v projektu). Žádný building-type enum, žádné stringové lookup.

Výchozí pravidlo pro seed: **každé kouzlo s `IsLearnable = true` je dostupné u všech budov odpovídajících Obchodník/Mudrc/Knihovna**. Loader najde ty budovy přes `Building.Name` (nebo jiný existující klíč v seed datech) a vytvoří `SpellBuildingRequirement` řádek pro každou kombinaci. Pokud v budoucnu dojde ke kurikulárnímu rozlišení (např. mentální kouzla jen u Mudrců), lze per-spell ořezat.

Per-game override: `GameSpellBuildingRequirement(GameSpellId, BuildingId)` — mirror `GameSkillBuildingRequirement`.

## Způsob získání kouzla

Kouzlo lze získat třemi způsoby — v doménovém modelu:

| Způsob | Jak je modelováno |
|--------|-------------------|
| **Naučení u budovy** | `Spell.IsLearnable = true` + `SpellBuildingRequirement` vazba. Mág zaplatí `Price` grošů. |
| **Odměna z questu** | Budoucí entita `PersonalQuestSpellReward(PersonalQuestId, SpellId)` — mirror `PersonalQuestSkillReward`. Nemusí mít `IsLearnable = true`. |
| **Odměna ze schopnosti / level-upu** | Budoucí entita `SkillSpellReward(SkillId, SpellId)` nebo jednorázové granty z logiky postavy (MAG-007: "Při postupu na novou úroveň se mág naučí jedno kouzlo dané úrovně zdarma"). |
| **Svitek ve světě** | `Spell.IsScroll = true`. Není to naučení — je to fyzický jednorázový item nalezený v loot / truhle / quest reward. |

**Poznámka**: `IsLearnable = false` znamená „nelze koupit u obchodníka ve výchozím režimu". Takové kouzlo existuje výhradně jako odměna / scroll / quest drop. Pro budoucí kouzla tajná nebo pouze odměnová.
