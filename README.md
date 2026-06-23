# Futures-NinjaTrader — MES500T Squeeze Momentum

Indikátory pro **NinjaTrader 8** a ruční obchodování **MES** na **500 tick** grafech (squeeze + KC pásma + momentum).

**Repozitář:** [github.com/toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Soubory

| Soubor | Účel |
|--------|------|
| `MES500TSqueezeMomentumV38.cs` | **Aktuální plná verze** — band reversal u KC pásma, exit assessment (Korekce % / ZAVŘÍT TEĎ), trend runner z V37 |
| `MES500TSqueezeMomentumV38Light.cs` | **Aktuální lehká verze** — stejná logika jako V38, méně kreslení (vhodné pro slabší PC) |
| `MES500TSqueezeMomentumV36.cs` | Plná V36 — panel situace, popup, approach ring, ruční režim |
| `MES500TSqueezeMomentumV36Light.cs` | Lehká V36 — stejná logika signálů, méně kreslení |

Starší verze (V31–V37) nejsou v repozitáři — vývoj pokračuje ve V38 / V38Light; V36/V36Light zůstávají jako reference / fallback.

## Instalace

1. Zkopíruj požadovaný `.cs` soubor do:
   ```
   Documents\NinjaTrader 8\bin\Custom\Indicators\
   ```
2. NinjaScript Editor → **F5** (Compile)
3. Na graf **MES** (500 tick) přidej indikátor:
   - doporučeno: **MES500TSqueezeMomentumV38** nebo **MES500TSqueezeMomentumV38Light**
   - alternativa: V36 nebo V36Light

## Kterou verzi zvolit

- **V38** — signály u horního/dolního KC pásma (reversal), srozumitelné NÁKUP/PRODEJ/ZAVŘÍT, procenta u exitu, plný UI (panel, popup, ring)
- **V38 Light** — jako V38, ale bez popupu/panelu/ringu, starší značky se mažou (max 60 svíček) — pro slabší PC
- **V36** — plný UI (panel, popup, ring), ruční trading
- **V36 Light** — jako V36, ale bez popupu a s omezenou historií značek na grafu

## Požadavky

- NinjaTrader 8
- Instrument: **MES** (Micro E-mini S&P 500)
- Doporučený graf: **500 tick**

## Licence

Soukromý projekt — použití na vlastní odpovědnost.
