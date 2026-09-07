# Tasks: The encrypted book

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** complete

## Phase 1 — Key derivation

- [X] **T001** Argon2id derivation with per-book random salt and recorded cost parameters
  - Implements: `src/MyFinance.Data/Security/BookKeyDerivation.cs`
  - Proven by: `BookKeyDerivationTests.The_same_password_and_salt_always_derive_the_same_key`,
    `Different_passwords_derive_different_keys`,
    `The_same_password_under_a_different_salt_derives_a_different_key`,
    `Each_new_parameter_set_gets_a_fresh_random_salt`, `A_derived_key_is_256_bits`
  - Requirements: FR-002, FR-005
- [X] **T002** Memory-hard production defaults, asserted rather than assumed
  - Implements: `BookKeyDerivation.DefaultIterations/DefaultMemoryKib/DefaultParallelism`
  - Proven by: `BookKeyDerivationTests.Production_defaults_are_memory_hard`
  - Requirements: NFR-001, NFR-002
- [X] **T003** [P] A key that zeroes itself, and the password's byte copy with it
  - Implements: `src/MyFinance.Data/Security/BookKey.cs`
  - Proven by: `BookKeyDerivationTests.A_disposed_key_can_no_longer_be_read`,
    `Disposing_a_key_twice_is_harmless`
  - Requirement: NFR-003

## Phase 2 — The sidecar

- [X] **T004** Sidecar serialization with a version gate and a KDF name
  - Implements: `src/MyFinance.Data/Security/BookKeyParameters.cs`
  - Proven by: `BookKeyDerivationTests.Parameters_round_trip_through_json`,
    `Restored_parameters_derive_the_identical_key`,
    `A_sidecar_from_a_newer_version_is_refused_rather_than_misread`,
    `A_sidecar_naming_an_unknown_kdf_is_refused`, `A_malformed_sidecar_is_refused`
  - Requirements: FR-004, FR-015
- [X] **T005** Refuse nonsensical cost parameters at construction
  - Implements: `BookKeyDerivation.CreateParameters` guards
  - Proven by: `BookKeyDerivationTests.Nonsensical_cost_parameters_are_refused`
  - Requirement: FR-005

## Phase 3 — Creating and opening

- [X] **T006** Create a book: schema, seeded categories, sidecar written last
  - Implements: `src/MyFinance.Data/Security/BookFileService.cs` (`Create`),
    `src/MyFinance.Data/Services/DefaultCategories.cs`
  - Proven by: `BookFileServiceTests.Creating_a_book_writes_both_the_database_and_its_sidecar`,
    `A_new_book_has_the_schema_applied`
  - Requirements: FR-001, FR-008
- [X] **T007** Whole-database encryption verified against the bytes on disk
  - Implements: `src/MyFinance.Data/Security/EncryptedConnectionFactory.cs`
  - Proven by: `BookFileServiceTests.The_database_file_is_encrypted_on_disk`,
    `Account_names_do_not_appear_in_plaintext_on_disk`
  - Requirement: FR-001
- [X] **T008** Open with a probe read, so a wrong key fails at unlock and not later
  - Implements: `BookFileService.Open`, `EncryptedConnectionFactory.CanRead`
  - Proven by: `BookFileServiceTests.A_book_reopens_with_the_correct_password`,
    `The_wrong_password_is_rejected`, `A_password_differing_by_one_character_is_rejected`
  - Requirement: FR-006
- [X] **T009** Distinguish a missing book from a wrong password, including a missing sidecar
  - Implements: `BookFileService.Exists`, `src/MyFinance.Data/Security/BookExceptions.cs`
  - Proven by: `BookFileServiceTests.Opening_a_missing_book_reports_it_as_missing_not_as_a_bad_password`,
    `Opening_a_book_whose_sidecar_is_missing_is_reported_as_missing`
  - Requirement: FR-007
- [X] **T010** Refuse to create over an existing book; leave nothing behind on failure
  - Implements: `BookFileService.Create`, `TryCleanUpPartialBook`
  - Proven by: `BookFileServiceTests.Creating_over_an_existing_book_is_refused`,
    `A_failed_creation_leaves_no_half_written_book_behind`
  - Requirements: FR-013, FR-014
- [X] **T011** Refuse an empty password at creation
  - Implements: `BookFileService.Create` guard
  - Proven by: `BookFileServiceTests.An_empty_password_is_rejected_at_creation`
  - Requirement: FR-003

## Phase 4 — Changing the password

- [X] **T012** Re-key under a fresh salt, advancing the sidecar only after the rekey succeeds
  - Implements: `BookFileService.ChangePassword`
  - Proven by: `BookFileServiceTests.Changing_the_password_switches_which_password_opens_the_book`,
    `Changing_the_password_generates_a_fresh_salt`,
    `Changing_the_password_with_the_wrong_current_password_is_refused`
  - Requirements: FR-009, FR-010

## Phase 5 — The user-facing edges

- [X] **T013** [P] Password strength bands for the create screen
  - Implements: `src/MyFinance.Core/Security/PasswordStrength.cs`
  - Proven by: `tests/MyFinance.Core.Tests/Security/PasswordStrengthTests.cs`
  - Requirement: FR-011
- [X] **T014** [P] Amounts round-trip through the encrypted store exactly, sign included
  - Implements: schema mapping of `Money` to `INTEGER`
  - Proven by: `BookFileServiceTests.Money_survives_a_save_and_reload_exactly`,
    `A_negative_balance_round_trips_with_its_sign`
  - Requirement: constitution principle 1
- [X] **T015** [P] A disposed book refuses further access
  - Implements: `src/MyFinance.Data/Security/Book.cs`
  - Proven by: `BookFileServiceTests.A_disposed_book_will_not_hand_out_further_contexts`
  - Requirement: NFR-003

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T006, T007 | ✅ |
| FR-002 | T001 | ✅ |
| FR-003 | T011 (and by construction — nothing writes it) | ✅ |
| FR-004 | T004 | ✅ |
| FR-005 | T001, T005 | ✅ |
| FR-006 | T008 | ✅ |
| FR-007 | T009 | ✅ |
| FR-008 | T006 | ✅ |
| FR-009 | T012 | ✅ |
| FR-010 | T012 | ✅ |
| FR-011 | T013 | ✅ |
| FR-012 | — (a prohibition; kept by there being no such code) | ✅ |
| FR-013 | T010 | ✅ |
| FR-014 | T010 | ✅ |
| FR-015 | T004 | ✅ |
| NFR-001/002 | T002 | ✅ |
| NFR-003 | T003, T015 | ✅ |
