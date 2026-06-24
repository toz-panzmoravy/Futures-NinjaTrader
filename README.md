# Futures-NinjaTrader — MES500T Squeeze Momentum

Indikátory pro **NinjaTrader 8** a ruční obchodování **MES** na **500 tick** grafech (squeeze + KC pásma + momentum).

**Repozitář:** [github.com/toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Soubory

| Soubor | Účel |
|--------|------|
| `MES500TSwingMap.cs` | Swing mapa V1 — barvy BUY/SELL/korekce, **VSTUP** a **◆ PEAK** |
| `MES500TSwingMapV2.cs` | **Doporučeno** — dvoustupňový exit: **PEAK?** (varování) + **◆ PEAK** (potvrzený po pullbacku) |
| `MES500TSwingMapV3.cs` | jako V2 + **% pravděpodobnost** u PEAK? |
| `MES500TSwingMapV4.cs` | V3 + statistiky (win rate, ticky, USD) |
| `MES500TSwingMapV4Light.cs` | V4 Light — statistiky za 1h |
| `MES500TSwingMapV5.cs` | **Aktuální SwingMap** — V4 + **VSTUP až po korekci** (ne uprostřed růžové fáze) |
| `MES500TSwingMapV5Light.cs` | V5 Light — statistiky za 1h, méně kreslení |
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
   - swing mapa: **MES500TSwingMapV5** (doporučeno) nebo V5Light / V4 / V3 / V2 / V1
   - doporučeno: **MES500TSqueezeMomentumV39** nebo **MES500TSqueezeMomentumV39Light**
   - alternativa: V38, V38Light, V36 nebo V36Light

## MES500TSwingMap — co zobrazuje

Overlay indikátor přímo na cenovém grafu — čte swing strukturu (ATR práh, korekce vs. impuls).

| Prvek | Význam |
|-------|--------|
| **Zelená svíčka** | aktivní BUY leg |
| **Červená svíčka** | aktivní SELL leg |
| **Růžová svíčka** | korekce — neobchodovat |
| **Šedá svíčka** | bez signálu (příliš malý pohyb) |
| **VSTUP ↑/↓** | potvrzený vstup po uzavření svíčky (plná čára ke knotu) |
| **PEAK?** (V2/V3) | varování u extrému — může být falešný, drž pokud trend pokračuje |
| **◆ PEAK** | potvrzený exit po pullbacku (čárkovaná tenčí čára) |
| **42%** (V3) | síla signálu vrcholu u PEAK? — &lt;40 % drž, ≥60 % zvaž exit |

Signály se mění **až po uzavření svíčky** (`Calculate.OnBarClose`) — žádné přebarvování historie.

### SwingMap — kterou verzi zvolit

| Verze | Kdy použít |
|-------|------------|
| **V5** | V4 + VSTUP až po skončení korekce (růžová fáze = bez VSTUP) |
| **V5 Light** | V5 pro slabší PC (statistiky za 1h) |
| **V4** | V3 + panel statistik (win rate, ticky, USD) |
| **V3** | PEAK? s % + ◆ PEAK po pullbacku |
| **V2** | Stejné jako V3, ale bez % u PEAK? |
| **V1** | Základ — jeden PEAK signál bez dvoustupňového potvrzení |

### Statistiky (V4/V5, skupina 5. Statistiky)

Panel vlevo nahoře počítá dokončené obchody **VSTUP → ◆ PEAK** (PEAK? se nepočítá).

| Metrika | Význam |
|---------|--------|
| Win rate | % obchodů se ziskem v ticích (exit close vs entry close) |
| Ticků celkem | součet P/L všech dokončených obchodů |
| USD | ticky × $1.25 × Contract Count (MES, výchozí 10 kon) |

| Parametr | V4 / V5 | V4/V5 Light |
|----------|---------|-------------|
| Stats Lookback Hours | 0 (celá historie) | 1 |
| Max Stats Trades | 500 | 80 |
| Show Swing Lines | ON (V5), ON (V4) | OFF |
| Show Strength Panel | ON (V5), ON (V4) | OFF |

### VSTUP a korekce (V5)

- **Růžová fáze (KOREKCE)** → žádný VSTUP, panel „NEVSTUPOVAT“
- **VSTUP** až po potvrzeném pivotu nového BUY/SELL impulsu
- Po korekci label: „Po korekci ↑ BUY — VSTUP“ / „Po korekci ↓ SELL — VSTUP“

### Klíčové parametry (V2/V3/V4/V5, skupina 4. PEAK)

| Parametr | Výchozí | Účel |
|----------|---------|------|
| Peak Min Ticks | 30 | minimální pohyb trendu před PEAK signálem |
| Peak Pullback Ticks | 12 | ◆ PEAK až po tomto couvnutí od vrcholu/dna |
| Peak Require Break | ON | ◆ PEAK vyžaduje průraz low/high předchozí svíčky |
| Show Peak Soft Markers | ON | oranžové PEAK? (varování) |

### Klíčové parametry (V1)

| Parametr | Výchozí | Účel |
|----------|---------|------|
| Min Swing Ticks | 20 | filtr šumu |
| Correction Ratio | 0.62 | korekce = pohyb menší než podíl předchozího impulsu |
| Confirm Bars | 1 | potvrzení pivotu |
| Peak Min Ticks | 30 | minimální pohyb před zobrazením PEAK |
| Show Entry Markers | ON | značky VSTUP |
| Show Peak Markers | ON | značky PEAK |

## Kterou verzi zvolit

- **MES500TSwingMapV5** — doporučeno: VSTUP po korekci + statistiky
- **MES500TSwingMapV5Light** — V5 pro slabší PC
- **MES500TSwingMapV4** — swing mapa + statistiky (VSTUP může přijít uprostřed korekce)
- **MES500TSwingMapV3** — swing mapa s VSTUP/PEAK?/◆ PEAK a % u varování
- **MES500TSwingMapV2** — swing mapa s dvoustupňovým PEAK bez %
- **MES500TSwingMap** — základní swing mapa (V1)
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
