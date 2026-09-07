# Investment accounts

A brokerage account holds securities as well as cash. MyFinance models holdings on an
**average-cost** basis and values them from prices you enter by hand.

## What is modelled

| | |
|---|---|
| **Securities** | A name and a symbol |
| **Holdings** | Quantity and cost basis, per security per account |
| **Activity** | Buys, sells and reinvestments |
| **Prices** | Dated, entered by you |

A sale releases a **proportional** share of the cost basis. Selling everything releases the
whole basis exactly, leaving zero rather than a rounding crumb.

:::{note}
Average cost is enough to answer *what do I hold* and *what did I pay*. It is **not** enough
for capital gains, which need individual lots and a matching policy you would have to choose
per sale. MyFinance has no tax report, so lots would be machinery in service of a feature
that does not exist.
:::

## Valuation is careful about its own evidence

**Prices are entered by hand.** MyFinance fetches nothing — nothing leaves the machine, which
rules out a price feed. So a value is only as current as the last time somebody typed a
price, and:

- every valued figure carries **the date of the price behind it**, so a stale number is
  visible rather than silently wrong;
- a holding with **no price at all is valued at cost and says so**, rather than reading as
  though it were worth what was paid;
- a price dated **after** the date you are valuing is not used. A net worth figure for last
  December must not change because you typed a price this morning.

## In net worth over time

Net worth replays what was actually held on each date. A holding bought in 2021 does not
appear in a 2019 figure — and that error would grow the further back the chart goes, which is
exactly where you look to see how you have done.

## Current limitations

:::{caution}
**There is no screen for this yet.** The engine is complete and covered by tests, and
migrated holdings are counted in net worth — but nothing in the interface lets you enter a
buy, a sell, or a price.

In practice that means holdings can only arrive by migrating a Money file, and because Money
records no cost basis this reader can recover, they arrive at a cost of zero and stay valued
at cost until a price can be entered.

Also out of scope for now: corporate actions (splits, mergers, spin-offs), and importing an
OFX investment statement.
:::
