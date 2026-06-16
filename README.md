# Futures-NinjaTrader

NinjaTrader 8 automatické obchodní systémy (AOS) pro futures prop-trading.

**GitHub:** [toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Strategie

| Složka | Instrument | Strategie | Verze |
|--------|------------|-----------|-------|
| [`MES/`](MES/) | MES | Microtrend Prop (TICK 200) | **v1.0.4** |
| [`MNQ/`](MNQ/) | MNQ | Microtrend Prop (PullbackBreak L+S) | **v1.0.5** |

Detailní instalace: **[MES/README.md](MES/README.md)** · **[MNQ/README.md](MNQ/README.md)**

## Instalace

1. Zkopíruj `.cs` soubor strategie do `Documents\NinjaTrader 8\bin\Custom\Strategies\`
2. NinjaScript Editor → **F5** (Compile)
3. Připoj strategii na **MES** nebo **MNQ TICK 200** graf

## Release workflow

Verzování, tagy a push na GitHub: **[RELEASE.md](RELEASE.md)**
