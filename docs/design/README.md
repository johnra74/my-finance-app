# Design sources

Artwork the application is built from, kept so an asset can be regenerated rather than
recovered from a compiled file.

Distinct from `money-screens/`, which holds screenshots of *Microsoft Money* — someone
else's product, kept as the behavioural reference this application is modelled on. Both are
excluded from the published documentation: the artwork is a source, and the screenshots are
of a real book.

## `app-icon-source.png`

The source for `src/MyFinance.App/Assets/MyFinance.ico`: a 1254×1254 donut chart with a
dollar sign, on white.

Two things to know before regenerating the icon from it.

**The source carries a stock "ICO" file badge** in its bottom-right corner, which is not part
of the design. It sits outside the circle — verified by sampling all 360° of the outer ring —
so masking to the circle removes it cleanly and gives the transparent corners an icon wants.
The circle was measured, not eyeballed: centre row and column scans put it at
(144, 85)–(1105, 1046), a 962px square.

**The small sizes are deliberately not the same picture.** Measured outward from the centre,
the coloured donut ends at 84% of the radius and the outer navy ring plus its gap take the
last 16%. At 16px that ring is half a pixel wide: it cannot render, and contributes nothing
but grey haze over the part of the design that does the work.

So the `.ico` holds two croppings:

| Sizes | Crop | Why |
|---|---|---|
| 16, 32 | 87% of the radius — zoomed past the outer ring | Buys the donut and the dollar sign the 16% the ring wastes. At 32px it is the difference between a legible `$` and a smudge. |
| 48, 64, 128, 256 | Full design | The ring renders crisply from 48px up and is part of the look. |

Both were rendered and compared side by side at true size before choosing; the same
verify-at-the-size-people-see-it discipline that caught a coin clipped by the tile edge in the
previous icon.

Encoding is the conventional split: 32-bit BMP (with the AND mask an `.ico` entry still
requires) for 16–64, PNG for 128 and 256.
