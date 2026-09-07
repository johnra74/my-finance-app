# Implementation Plan: Recommending a category

**Spec:** `./spec.md` · **Status:** As-built

## Summary

A six-source chain, evaluated as one pure function (`CategorySuggester.Suggest`) over inputs
that a single cached service assembles. The two decisions this turns on: **certainty is a
property of the suggestion, not of the caller**, so "fill versus offer" is testable without a
window; and the context is kept fresh by a **cheap staleness probe rather than invalidation
calls**, because an invalidation somebody forgets to make is a wrong suggestion nobody
notices.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 5 — a guess is never applied unseen | `CategorySuggestion.IsCertain`. This feature is where the principle is defined. |
| 7 — nothing leaves the machine | The embedding model runs locally, bundled into the executable. |
| 4 — correctness outside WPF | The whole chain is in `MyFinance.Import`, with no database and no UI dependency. |
| 9 — off the interface thread | The editor's lookup runs on a pool thread, with a guard for the payee changing mid-lookup. |

## Technical context

- **Projects:** `MyFinance.Import/Categorization` (pure), `MyFinance.Semantics` (the model,
  isolated so the other projects stay quick to test), `MyFinance.Data/Services/SuggestionService.cs`.
- **Dependencies:** `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers`. Both confined to
  `MyFinance.Semantics`; `MyFinance.Import` depends only on the `ITextEmbedder` interface, so
  the whole model is optional at both compile time and run time.
- **Model:** all-MiniLM-L6-v2, int8-quantised, ~22 MB, fetched by `tools/fetch-model.sh`
  or `tools/fetch-model.cmd`
  against pinned hashes. The weights are **not** in the repository.
- **Testing:** `MyFinance.Import.Tests/Categorization` (pure), `MyFinance.Semantics.Tests`
  (the model, plus the accuracy benchmark), `MyFinance.Data.Tests` (the cached service).

## Design

### The order of authority

Each step down is one step further from something the user actually decided:

1. **A rule** — a standing instruction, written deliberately.
2. **The file's category** — a decision, but made in another program at some point in the past.
   A rule beats it: when somebody writes "Shell goes to Fuel" they mean it to win.
3. **Payee memory** — what this person did last time.
4. **The classifier** — inference from their own history.
5. **Merchant resemblance** — inference from a merchant that is not this one.
6. **The industry code** — a fact about the merchant, but a general one. Somebody may well
   keep warehouse clubs under Household rather than Groceries.

### Certainty on the suggestion

`IsCertain => Source is Rule or FileCategory or PayeeMemory`. Putting the fill-or-offer line
on the suggestion rather than in the dialog is what makes it testable with no window, and
means the import preview and the editor cannot disagree about it.

### The classifier: naive Bayes over tokens

Multinomial naive Bayes over the words in payee and memo. Scored in **logarithms** —
multiplying a dozen small probabilities in floating point underflows to zero and every
category then looks equally likely. Laplace smoothing so an unseen word does not drive the
product to zero. It declines to answer with fewer than two candidate categories (the
posterior would be 1 by construction), below 20 training documents, for a category with fewer
than 3 examples, and when the text shares no word with the vocabulary.

Retrained from history rather than persisted: a personal book is small enough that it costs
one query, and it means the model always reflects every correction made since — which a
persisted model would not without invalidation logic nobody would remember to write.

### Merchant resemblance

The gap the other five cannot fill: a merchant with no rule, no file category, no history and
no shared words. On a freshly migrated book, that is every new merchant.

Mean pooling over the attention mask (not the `[CLS]` token), L2-normalised. **Batch size 1**:
the int8 export is dynamically quantised, so activation scales are computed at run time from
the batch in hand and the same text embeds slightly differently depending on what it was
batched with — measured at cosine 0.989–0.992 between batched and unbatched. Determinism is
worth more than the throughput.

Neighbour weight is measured **from the similarity floor**, not from zero. With linear
weighting from zero at a 0.80 floor, four vague matches at 0.81 outvoted one near-exact match
at 0.98 — a real defect caught by `A_near_exact_match_beats_a_crowd_of_vague_ones`. Measuring
from the floor lifted mid-range precision materially (at a 0.55 floor: 47.5% → 56.1%).

Vectors are cached per payee in `PayeeEmbeddings`, keyed on payee id, a hash of the text, and
the model name, and quantised to int8 — one byte a dimension plus a small header.

### Freshness by probe, not by invalidation

`SuggestionService` caches the context and checks it against a cheap fingerprint: transaction
count, rule count, payee count, newest transaction id, newest split id. The split id is what
catches a **recategorization**, which changes no count. Rebuilt only when the fingerprint
differs; the rebuild is guarded by a semaphore with a double check.

Threading an `Invalidate()` through `RegisterService`, `ImportService`, `MigrationService`
and the rule editor would be four chances to forget one, and a forgotten one leaves the editor
recommending from a model that predates the last hundred transactions — wrong in a way nobody
would notice. The probe cannot be forgotten.

`MAX(ModifiedUtc)` was the first attempt and SQLite would not translate it; max ids do the
same job and cost less.

### Alternatives rejected

- **TF-IDF character n-grams instead of an embedding model.** Recommended, and measured
  against: the model beat n-grams clearly at matched coverage (43% vs 28%). The
  recommendation was wrong.
- **A persisted classifier.** Needs invalidation logic; see above.
- **Applying a guess above some confidence.** Constitution 5. The measured 62.9% for
  resemblance is the argument.
- **Batching embeddings for speed.** Non-deterministic under dynamic int8 quantization.
- **Making `MyFinance.Import` depend on ONNX Runtime.** Would put a 20 MB native dependency
  in the path of every fast unit test, and make the feature non-optional.

## Project structure

```
src/MyFinance.Import/Categorization/CategorySuggester.cs    the chain, and IsCertain
src/MyFinance.Import/Categorization/RuleEvaluator.cs        RuleSpec, RuleInput, RuleMatch
src/MyFinance.Import/Categorization/CategoryClassifier.cs   naive Bayes + tokenizer
src/MyFinance.Import/Categorization/SimilarityIndex.cs      neighbours, floor, weighting
src/MyFinance.Import/Categorization/VectorQuantizer.cs      int8 pack/unpack
src/MyFinance.Import/Categorization/ITextEmbedder.cs        the seam; NullTextEmbedder
src/MyFinance.Semantics/OnnxTextEmbedder.cs                 the model; never throws on load
src/MyFinance.Data/Services/SuggestionService.cs            context, cache, fingerprint
src/MyFinance.Data/Services/PayeeEmbeddingService.cs        the vector cache
src/MyFinance.Data/Services/CategorizationRuleService.cs    rules, order, try-before-save
src/MyFinance.App/ViewModels/Dialogs/TransactionEditorViewModel.cs
src/MyFinance.App/ViewModels/Pages/RulesPageViewModel.cs
```

## Risks

- **A stale context is the quiet failure.** It would neither throw nor look wrong. Hence the
  probe, and hence four tests specifically for rebuild-after-write.
- **The probe runs on every dialog open.** It must stay a few counts and two maxima. If it
  ever scans, fall back to counting rows and note the weaker guarantee rather than adding an
  index for a cache check.
- **Resemblance is the least reliable source in the chain** — 62.9%. Contained by ranking it
  fifth, by a conservative floor, by naming the merchant that convinced it, and by never
  applying it.
- **The model is an optional native dependency.** `OnnxTextEmbedder.Create` never throws; it
  returns an unavailable embedder, and the chain then behaves exactly as it did before the
  feature existed.
