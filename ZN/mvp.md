📋 ZADÁNÍ PRO AI: Vývoj Prop-Trading AOS pro NinjaTrader 8 (ZN - Mean Reversion Strategie)

**Role a Cíl:**
Chovej se jako expert na programování v NinjaScriptu (C#) pro platformu NinjaTrader 8. Tvým úkolem je napsat kompletní a robustní kód pro Automatický Obchodní Systém (AOS). Systém je určen pro splnění prop-trading výzvy.

**1. Základní parametry:**
* Cílový trh: ZN (10-Year T-Note).
* Typ grafu: Minutový (5 Minute) nebo Range graf.

**2. Časové filtry (Time Management):**
* Aktivní hodiny (RTH): Systém smí obchodovat pouze v čase od 15:30 do 21:45 SEČ.
* Flat Before Close: Všechny otevřené pozice musí být nekompromisně uzavřeny ve 21:55 SEČ.

**3. Prop-Trading Ochranný štít (Risk Management):**
* Hard Daily Loss Limit: Jakmile denní P&L dosáhne -500 USD, systém musí zablokovat nové vstupy a vypnout se do konce dne.
* News Filter: Zákaz obchodování (a uzavření otevřených pozic) 15 minut před a po klíčových makro zprávách.

**4. Obchodní logika (Mean Reversion pomocí Bollinger Bands a RSI):**
* Indikátory: Bollinger Bands (perioda 20, odchylka 2) a RSI (perioda 14).
* Setup pro LONG (Přeprodanost): Cena musí klesnout a uzavřít pod spodní hranicí Bollinger Bandu. Zároveň musí být hodnota RSI pod hranicí 30 (extrémní vybočení).
* Trigger pro LONG: Jakmile po splnění setupu uzavře první rostoucí (býčí) svíčka směrem dovnitř pásma, AOS nakupuje (LONG).
* Setup pro SHORT (Překoupenost): Cena uzavře nad horní hranicí Bollinger Bandu. Zároveň RSI je nad hranicí 70.
* Trigger pro SHORT: Jakmile uzavře první klesající (medvědí) svíčka směrem dovnitř pásma, AOS prodává (SHORT).

**5. Trade Management (Výstupy a ochrana zisku):**
* Stop Loss (SL): Fixní uživatelský parametr v ticích (u ZN jsou to frakce, zajisti správný výpočet tick size v C#).
* Profit Target (PT - Dynamický): Systém cílí na návrat k průměru, takže PT bude ležet na střední lince Bollinger Bandu (SMA 20). Jakmile se cena dotkne této linky, pozice se uzavře.
* Ochrana před Trailing Drawdownem: Implementuj Trailing Stop Loss, který se aktivuje po dosažení 50% vzdálenosti k cílové SMA 20, aby se chránil otevřený zisk.

**Požadavky na kód:**
Všechny parametry musí být uživatelsky nastavitelné v rozhraní NT8 (Properties).