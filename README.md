# Futures-NinjaTrader

NinjaTrader 8 automatické obchodní systémy (AOS) pro futures prop-trading.

**GitHub:** [toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Strategie

| Složka | Instrument | Strategie | Verze |
|--------|------------|-----------|-------|
| [`MES/`](MES/) | MES | Microtrend Prop (TICK 200) | **v1.0.4** |

Detailní instalace a parametry: **[MES/README.md](MES/README.md)**

## Instalace

1. Zkopíruj `.cs` soubor strategie do `Documents\NinjaTrader 8\bin\Custom\Strategies\`
2. NinjaScript Editor → **F5** (Compile)
3. Připoj strategii na **MES TICK 200** graf

## Release workflow

Verzování, tagy a push na GitHub: **[RELEASE.md](RELEASE.md)**
