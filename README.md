# Futures-NinjaTrader

NinjaTrader 8 automatické obchodní systémy (AOS) pro futures prop-trading.

**GitHub:** [toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

## Strategie

| Složka | Instrument | Strategie | Verze |
|--------|------------|-----------|-------|
| [`2/`](2/NinjaTrader/) | MES | VWAP Pullback Prop (1min) | v1.0.4 |
| [`2/`](2/NinjaTrader/) | MES | VWAP Pullback Prop (tick) | v1.0.5 |
| [`2/`](2/NinjaTrader/) | MES | VWAP Pullback Prop (prop/tick) | v1.0.6 |
| [`2/`](2/NinjaTrader/) | MES | VWAP Pullback Prop (wide/tick) | v1.0.7 |
| [`2/`](2/NinjaTrader/) | MES | VWAP Pullback Prop **FINAL** (TICK 125) | **v2.0.5** |
| [`MNQ/`](MNQ/) | MNQ | ORB Prop | v1.0.0 |
| [`ZN/`](ZN/) | ZN | Mean Reversion Prop | v1.0.0 |
| [`1/`](1/) | MES | VWAP Pullback (legacy) | v1.0.0 |

Každá složka obsahuje `mvp.md` se specifikací a `NinjaTrader/Strategies/` se zdrojovým kódem.

## Instalace

1. Zkopíruj `.cs` soubor strategie do `Documents\NinjaTrader 8\bin\Custom\Strategies\`
2. NinjaScript Editor → **F5** (Compile)
3. Připoj strategii na graf příslušného instrumentu

Detailní parametry a verze: README ve složce strategie (např. [`2/NinjaTrader/README.md`](2/NinjaTrader/README.md)).

## Release workflow

Verzování, tagy a push na GitHub: **[RELEASE.md](RELEASE.md)**

Před každým commitem / tagem / pushem se řiď tímto dokumentem.
