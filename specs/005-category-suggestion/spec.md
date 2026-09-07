# Feature Specification: Recommending a category

**Folder:** `005-category-suggestion`
**Created:** 2026-09-05 (written from shipped code)
**Status:** Implemented
**Input:** "Work out what category a transaction belongs in, on import and in the editor,
without ever quietly filing something wrong."

## User Scenarios & Testing

### Primary user story

Categorizing is the tedious part of keeping books, and it is also what every report depends
on. The user wants the application to fill in what it can justify and to *offer* what it can
only guess, saying why in each case. On a book with decades of history that should cover
most of a statement; on a brand-new book it should say nothing at all rather than invent
answers that then have to be found and undone.

### Acceptance scenarios

1. **Given** a rule "description contains SHELL → Transport : Fuel", **When** a matching row
   arrives, **Then** it is filed under Fuel, and the rule is named as the reason.
2. **Given** two rules that both match, **When** a row arrives, **Then** the one higher in
   the order wins, so a specific rule placed above a general one overrides it.
3. **Given** a QIF file carrying its own categories and a rule that also matches, **When** the
   row is imported, **Then** the **rule** wins — it is a standing instruction in this
   application, while the file's category was a decision made elsewhere in the past.
4. **Given** a payee filed under Groceries every time for ten years, **When** it appears
   again, **Then** Groceries is filled in.
5. **Given** an unknown payee whose words the classifier recognises, **When** it arrives,
   **Then** a category is **offered** with its confidence, not filled in.
6. **Given** `CONTOSO SUPERMARKET #543` on a book that has never seen it, **When** it arrives,
   **Then** a category may be offered on the strength of resembling `NORTHWIND GROCERY STORE`, and
   the offer names that merchant.
7. **Given** a new book with nothing to learn from, **When** a statement is imported,
   **Then** nothing is suggested.
8. **Given** an existing transaction being edited with no category, **When** the user leaves
   the payee field, **Then** the same chain runs — certainties fill, guesses offer.
9. **Given** a transaction that already has a category, **When** it is opened to fix its
   amount, **Then** nothing argues with the category already chosen.

### Edge cases

- A rule that would consume the entire payee name → does not fire.
- A broken regular expression → refused when saved, and if one is already stored it reports
  nothing rather than stopping the import.
- A category with too few examples → not offered.
- A neighbourhood of similar merchants that cannot agree → not reported.
- One near-exact match against a crowd of vague ones → the near-exact match wins.
- A merchant code the book has no mapping for → not invented into a category.
- The embedding model or its native runtime will not load → the feature disappears and the
  other sources carry on unchanged.
- An empty payee → not worth asking about.

## Requirements

### Functional requirements

**The chain**

- **FR-001**: The system MUST consult six sources in this fixed order of authority: a rule
  the user wrote; the category the file carried; what this payee was last filed under; a
  classifier trained on the user's own history; the merchant this one most resembles; the
  merchant's industry code.
- **FR-002**: The system MUST classify the first three as **certain** and the last three as
  **guesses**.
- **FR-003**: The system MUST apply a certainty directly and MUST only ever *offer* a guess.
- **FR-004**: The system MUST explain every suggestion in words a person can read — the rule's
  name, the merchant it resembled, the confidence.
- **FR-005**: The system MUST suggest nothing rather than guess when no source answers.
- **FR-006**: The system MUST run the identical chain during an import and in the transaction
  editor.
- **FR-007**: In the editor, the system MUST only speak when the category is still blank.

**Rules**

- **FR-008**: The system MUST let the user write rules matching on payee, description, amount
  or a combination, with a choice of comparison, optionally restricted to one account.
- **FR-009**: The system MUST evaluate rules in a user-controlled order, first match wins,
  with ties broken deterministically.
- **FR-010**: The system MUST allow a rule to rewrite the payee with or without naming a
  category, and a payee-only rule MUST still win its place in the order.
- **FR-011**: The system MUST allow rules to be disabled without losing them, reordered, and
  deleted.
- **FR-012**: The system MUST validate a pattern when it is saved, and MUST refuse a rule that
  is nameless or does nothing.
- **FR-013**: The system MUST let a rule be tried against the existing register before it is
  saved.
- **FR-014**: The system MUST report which rules fired, so a surprising result can be
  explained, and MUST count usage on the rules screen.

**The classifier**

- **FR-015**: The system MUST learn only from the user's own categorized history, excluding
  transfers and voided rows.
- **FR-016**: The system MUST say nothing below a confidence threshold, below a minimum
  amount of history, or where a candidate category has too few examples.
- **FR-017**: The system MUST say nothing when the text shares no word with anything ever
  categorized. That "prediction" would be the prior alone — "whatever category you use most"
  — which is a guess dressed as evidence.
- **FR-018**: The system MUST drop store and reference numbers rather than learning them as
  noise.
- **FR-019**: The system MUST be retrained from history rather than persisted, so it always
  reflects every correction made since.

**Merchant resemblance**

- **FR-020**: The system MUST be able to suggest a category for a merchant with no shared
  words and no history, by resemblance to past merchants.
- **FR-021**: The system MUST run entirely on the user's machine.
- **FR-022**: The system MUST be entirely optional: if the model or its runtime will not
  load, the feature disappears and the rest of the chain is unaffected.
- **FR-023**: The system MUST report nothing below a similarity floor, and nothing when the
  neighbourhood disagrees.
- **FR-024**: The system MUST weight a near-exact match above a crowd of vague ones.
- **FR-025**: The system MUST name the merchant that convinced it.
- **FR-026**: The system MUST cache a payee's vector, keyed so a change of text or of model
  invalidates it.

**Merchant codes**

- **FR-027**: The system MUST use the merchant's industry code where the bank sends one and
  the book has a mapping, and MUST rank it last — it is a fact about the merchant, but it
  maps to a general category and not necessarily the one this person files that shop under.
- **FR-028**: The system MUST NOT invent a category for an unknown code.

**Freshness**

- **FR-029**: The system MUST NOT retrain per suggestion; the model MUST be built once and
  reused.
- **FR-030**: The system MUST rebuild the model after a transaction is added, after one is
  recategorized, and after a rule is written — without depending on any caller remembering to
  say so.

### Non-functional requirements

- **NFR-001**: A suggestion in the editor MUST NOT block the interface thread.
- **NFR-002**: Embedding MUST be deterministic — the same text MUST embed identically every
  time.
- **NFR-003**: No transaction text may leave the machine.
- **NFR-004**: The bundled model MUST add no more than about 25 MB to the download.

## Key Entities

- **Rule** — name, priority, enabled flag, an optional account restriction, the conditions,
  and what to do: a category, a payee rewrite, or both.
- **Suggestion** — a category, its source, a confidence, whether it is certain, the rule or
  merchant behind it, and a sentence describing it.
- **Suggestion context** — the four things the chain needs (rules, classifier, similarity
  index, merchant-code table), built once and reused.
- **Payee embedding** — a cached vector for a payee, keyed on payee, text hash and model.
- **Merchant code mapping** — an industry code to a category. Brought across from Money.

## Success Criteria

- **SC-001**: The order of authority holds end to end: a rule beats the file's category
  beats payee memory beats the classifier beats resemblance beats the industry code.
  *The chain order is asserted in `CategoryClassifierTests` (`A_rule_outranks_everything_else`,
  `The_files_own_category_beats_payee_memory`, `Payee_memory_beats_the_statistical_guess`,
  `The_statistical_guess_is_the_last_resort`) and in `SimilarityIndexTests`
  (`What_the_payee_was_last_filed_under_beats_a_lookalike`,
  `A_lookalike_from_the_users_own_history_beats_a_generic_industry_code`,
  `A_merchant_code_is_used_when_nothing_else_answers`); and through the service in
  `SuggestionServiceTests.A_rule_beats_what_the_payee_was_last_filed_under`.*
- **SC-002**: Exactly the three authoritative sources are certain; the three inferred ones
  are not, and neither is "nothing".
  *`SimilarityIndexTests.What_the_user_decided_can_be_applied_without_asking`,
  `What_was_inferred_is_only_ever_offered`, `Nothing_suggested_is_not_a_certainty_either`.*
- **SC-003**: A new book invents nothing.
  *`SuggestionServiceTests.A_new_book_with_nothing_to_learn_from_invents_nothing`,
  `ImportServiceTests.A_new_book_gets_no_statistical_guesses`.*
- **SC-004**: The model is built once and reused, and rebuilt after a write — a transaction
  added, a transaction recategorized, or a rule written.
  *`SuggestionServiceTests.The_model_is_built_once_and_reused`,
  `The_model_is_rebuilt_after_a_transaction_is_added`,
  `The_model_is_rebuilt_after_a_transaction_is_recategorized`,
  `The_model_is_rebuilt_after_a_rule_is_written`.*
- **SC-005**: One near-exact match outranks a crowd of vague ones.
  *`SimilarityIndexTests.A_near_exact_match_beats_a_crowd_of_vague_ones`.*
- **SC-006**: The same text embeds identically every time, and a vector is unit length and
  finite. *`OnnxTextEmbedderTests.The_same_text_embeds_the_same_way_every_time`,
  `A_vector_is_the_right_width_finite_and_of_unit_length`,
  `Padding_in_a_batch_does_not_change_a_short_text`.*
- **SC-007**: Merchants of a kind sit closer than merchants of different kinds.
  *`OnnxTextEmbedderTests.Merchants_of_a_kind_sit_closer_than_merchants_of_different_kinds`.*
- **SC-008**: With no embedder available, the chain behaves exactly as it did before the
  feature existed. *`With_no_vector_the_chain_is_unchanged`,
  `The_null_embedder_is_unavailable_and_answers_nothing`.*
- **SC-009**: Measured on a real book of 17,559 categorized transactions, trained to Nov 2019
  and tested on the six following years: payee memory answers 62.6% of rows at 92.7% correct;
  the classifier answers 13.1% at 78.3%; on the 24.3% neither can answer, merchant
  resemblance answers 16.4% at 62.9%. *Measured by
  `tests/MyFinance.Semantics.Tests/CategorySuggestionBenchmark.cs`.*
  **This is below the 85% bar originally proposed for the resemblance source.** It ships
  anyway, at a conservative threshold, because it is the only source that fires where the
  user has no history at all, and because it is never applied without being seen.
- **SC-010**: Every source describes itself in readable words.
  *`Every_source_describes_itself_in_words_a_person_can_read`.*

## Assumptions

- Depends on constitution principles **5** (a guess is never applied unseen — this feature is
  where that principle is defined), **7** (nothing leaves the machine), **4** (the chain is
  pure and lives outside WPF), **9** (suggesting does not block the interface).
- The user's own history is the only authority worth learning from. There is no general model
  of "what category a shop belongs in" beyond the industry-code table, and even that ranks
  last.
- A book carrying decades of payee memory — the migration case — is the one this is tuned
  for.

## Out of Scope

- Learning across users, or any shared model. Permanently.
- Automatically applying a guess, at any confidence.
- Splitting a transaction automatically across categories.
- Suggesting a payee rewrite outside of a rule.
