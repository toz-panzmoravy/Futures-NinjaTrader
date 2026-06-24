# Futures-NinjaTrader — MES500T Squeeze Momentum

Indikátory pro **NinjaTrader 8** a ruční obchodování **MES** na **500 tick** grafech (squeeze + KC pásma + momentum).

**Repozitář:** [github.com/toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Soubory

| Soubor | Účel |
|--------|------|
| `MES500TSqueezeMomentumV39.cs` | **Aktuální plná verze** — jasné vstupy NÁKUP/PRODEJ (trend leg uprostřed trendu + band reversal u KC pásma), exit assessment, trend runner |
| `MES500TSqueezeMomentumV39Light.cs` | **Aktuální lehká verze** — stejná logika jako V39, méně kreslení (vhodné pro slabší PC) |
| `MES500TDashboard.cs` | **Sub-panel Dashboard** — samostatné okno pod grafem; 6 pruhů: síla nákupního/prodejního trendu, MACD momentum, squeeze komprese, approach skóre BUY/SELL + textový souhrn |
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

## MES500TDashboard — co zobrazuje

Dashboard je **samostatný indikátor** přidaný do nového sub-panelu pod grafem. Používá stejné parametry BB/KC/MACD jako V39.

| Pruh | Rozsah | Popis |
|------|--------|-------|
| **Síla Nákup** (zelená) | 0–100 | Jak silný je nákupní trend: KC slope + cena nad KC mid + MACD kladný + momentum roste |
| **Síla Prodej** (červená) | 0–(−100) | Stejné pro prodejní trend, vykresleno pod nulou |
| **Momentum** (modrá) | −100–+100 | Normalizovaný MACD histogram: roste ↑ zelená, klesá ↓ červená |
| **Squeeze** (žlutá) | 0 / 50 / 100 | 0 = off, 50 = partial, 100 = full squeeze — čekej na výstřel |
| **Approach BUY** (čára) | 0–100 | Skóre pravděpodobnosti blízkého vstupu na nákup (0–7 podmínek) |
| **Approach SELL** (čára) | 0–(−100) | Totéž pro prodej |

Navíc textový popis v levém dolním rohu sub-panelu (lze vypnout `Show Status Text = false`).

### Nastavení Dashboardu — musí odpovídat V39

Zkontroluj, že tyto parametry jsou stejné jako ve V39:

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
