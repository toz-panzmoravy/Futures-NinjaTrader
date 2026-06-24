# Futures-NinjaTrader — MES500T Squeeze Momentum

Indikátory pro **NinjaTrader 8** a ruční obchodování **MES** na **500 tick** grafech (squeeze + KC pásma + momentum).

**Repozitář:** [github.com/toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Soubory

| Soubor | Účel |
|--------|------|
| `MES500TSqueezeMomentumV39.cs` | **Aktuální plná verze** — jasné vstupy NÁKUP/PRODEJ (trend leg uprostřed trendu + band reversal u KC pásma), exit assessment, trend runner |
| `MES500TSqueezeMomentumV39Light.cs` | **Aktuální lehká verze** — stejná logika jako V39, méně kreslení (vhodné pro slabší PC) |
| `MES500TDashboard.cs` | **Sub-panel Dashboard v2** — síla trendu, Entry/Exit score, akční panel (Čekej/Vstup/Drž/Zavři), režim trhu, KC zóna |
| `MES500TSqueezeMomentumV38.cs` | Plná V38 — band reversal u KC pásma, exit assessment (Korekce % / ZAVŘÍT TEĎ), trend runner z V37 |
| `MES500TSqueezeMomentumV38Light.cs` | Lehká V38 — stejná logika jako V38, méně kreslení |
| `MES500TSqueezeMomentumV36.cs` | Plná V36 — panel situace, popup, approach ring, ruční režim |
| `MES500TSqueezeMomentumV36Light.cs` | Lehká V36 — stejná logika signálů, méně kreslení |

Starší verze (V31–V37) nejsou v repozitáři — doporučené jsou **V39 / V39Light**; V38/V38Light a V36/V36Light zůstávají jako reference / fallback.

## Instalace

1. Zkopíruj požadovaný `.cs` soubor do:
   ```
   Documents\NinjaTrader 8\bin\Custom\Indicators\
   ```
2. NinjaScript Editor → **F5** (Compile)
3. Na graf **MES** (500 tick) přidej indikátor:
   - doporučeno: **MES500TSqueezeMomentumV39** nebo **MES500TSqueezeMomentumV39Light**
   - alternativa: V38, V38Light, V36 nebo V36Light

## Kterou verzi zvolit

- **V39** — doporučeno pro ruční obchod: signály NÁKUP/PRODEJ i uprostřed trendu (trend leg), u pásma (reversal), blocked entry hints, plný UI (panel, popup, ring)
- **V39 Light** — jako V39, ale bez popupu/panelu/ringu, starší značky se mažou (max 60 svíček) — pro slabší PC
- **V38** — signály hlavně u horního/dolního KC pásma (reversal), srozumitelné NÁKUP/PRODEJ/ZAVŘÍT, procenta u exitu
- **V38 Light** — jako V38, méně kreslení
- **V36** — plný UI (panel, popup, ring), ruční trading
- **V36 Light** — jako V36, ale bez popupu a s omezenou historií značek na grafu

## MES500TDashboard v2 — co zobrazuje

Dashboard je **obchodní pomocník** v sub-panelu pod grafem. Doplňuje V39 — říká **co dělat**, ne jen stav.

### 4 pruhy

| Pruh | Popis |
|------|-------|
| **TrendSila** (sloupce) | Kontinuální síla trendu 0–100, barva = fáze (FORM→ACT→MAT→FADE) |
| **EntryScore** (modrá čára) | + = vhodnost NÁKUPU, − = vhodnost PRODEJE |
| **ExitPressure** (oranžová čára) | Tlak na zavření — u long pod nulou, u short nad nulou |
| **Nula** | referenční linka |

### Akční panel (vlevo dole)

Jedna jasná věta co dělat:

| Akce | Význam |
|------|--------|
| ⏸ ČEKEJ | squeeze nebo bez trendu |
| ⛔ NEVSTUPOVAT | MACD tangle |
| 👁 Sleduj | trend začíná |
| 🟢 VSTUP OK | vhodné vstoupit |
| 🔵 DRŽ | trend běží |
| 🟡 DRŽ — slábne | mature fáze |
| ⚠ ZVAŽ ZAVŘENÍ | fading / exit ≥ 55% |
| 🔴 ZAVŘÍT | exit ≥ 72% |

### Režim trhu + KC zóna

- **Režim:** TREND ↑/↓ · CHOP · SQUEEZE · FLAT
- **Zóna:** horní/dolní pásmo (reversal?) · KC mid (trend leg)

### Značky

- **FORM/ACT/MAT/FADE** — popisek při změně fáze
- **↑/↓ šipka** — start trendu
- **PEAK** — retroaktivní vrchol trendu

### Nastavení — musí odpovídat V39

| Parametr | Výchozí |
|----------|---------|
| BB Period | 20 |
| BB Std Dev | 2.0 |
| KC Period | 20 |
| KC Multiplier | 1.5 |
| MACD Fast / Slow / Signal | 6 / 13 / 9 |
| Entry Buffer Ticks | 1 |
| Approach Near Ticks | 10 |

## V39 — klíčové parametry (skupina 22. V39 Manual Entries)

| Parametr | Výchozí | Účel |
|----------|---------|------|
| Show Trend Leg Entries | ON | PRODEJ/NÁKUP uprostřed trendu (KC mid + momentum), ne jen u pásma |
| Relax Re-Entry In Trend | ON | Re-entry ve směru trendu i po ztrátě |
| Show Blocked Entry Hints | ON | Ukázat NÁKUP/PRODEJ i když simulace neotevře (re-entry filtr) |

## Požadavky

- NinjaTrader 8
- Instrument: **MES** (Micro E-mini S&P 500)
- Doporučený graf: **500 tick**

## Licence

Soukromý projekt — použití na vlastní odpovědnost.
