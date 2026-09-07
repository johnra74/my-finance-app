# Bills and the cash-flow forecast

Under the **Bills** tab.

## The bills summary

Every recurring bill and deposit with its amount, next due date, frequency, payment method
and account — **overdue rows first**. An overdue row says how far behind it is and how many
occurrences have piled up:

> This transaction is 62 days overdue (2 occurrences past due).

## Entering a bill

Writing a bill into the register also settles its due date.

**A bill several occurrences behind is entered in full, each payment recorded on the day it
was actually owed** rather than the whole backlog landing on today — so the running balance
stays truthful and the register reads like what happened.

**Skip** clears one occurrence without paying it.

## Auto-entry

A series can enter itself when the book is opened, up to its own days-ahead setting. Only
series you explicitly set to do so, and it tells you what it did afterwards.

Writing to the register unasked is exactly the sort of thing that should announce itself.

## The calendar

Five months side by side with due dates picked out, and a footer giving the account's
balance now and after everything scheduled has gone out.

## The cash-flow forecast

The balance projected forward day by day, marking the point where it would dip below zero.

**That lowest point matters more than the closing figure.** A large bill early in the month
followed by a salary can end comfortably while going overdrawn in between, and it is the
overdraft that costs you money.

## How due dates are worked out

Every occurrence is computed from the series start date and its position — never from the
occurrence before it.

Stepping forward one at a time looks equivalent and is not. A bill due on the 31st would be
clamped to the 28th by one February and stay there for ever, and a weekend shift would
compound until the series had drifted a week.

Due dates are calendar days, not instants, so no clock change can move one.

A schedule records only what was **done** about each due date, not the dates themselves. The
recurrence rule is the source of truth about when something falls due; the stored rows say
which of those were entered or skipped. That is what stops a bill being paid twice.

:::{note}
Deleting a scheduled bill leaves the transactions it already produced in the register. The
money genuinely left the account, whatever happens to the schedule that predicted it.
:::

:::{admonition} Not supported
:class: caution
A scheduled **transfer** cannot be represented — a bill posts to one account against a
category. A mortgage payment or a monthly sweep into savings has to be entered by hand, or
the receiving account will not move.
:::
