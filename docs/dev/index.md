# Working on MyFinance

This half of the documentation is for whoever changes the code — today that is the person
whose money it is, but it is written so it need not stay that way.

Start with {doc}`principles`. Everything else in the repository is downstream of it: the
eleven principles are what every specification, plan and change is checked against, and each
is written with the consequence that makes it non-negotiable.

```{toctree}
:maxdepth: 1

architecture
principles
specifications
building
testing
schema-migrations
releasing
documentation
licensing
```

## Orientation in five facts

1. **Correctness lives outside the WPF layer.** Only `MyFinance.App` targets Windows.
   Everything that has to be provably right is in a platform-neutral project and is tested
   on Linux.
2. **Every write goes through a service** in `MyFinance.Data/Services`. The invariants
   cannot be expressed in the schema, and one enforced in only some write paths is not an
   invariant.
3. **Money is an integer count of cents.** No binary floating point anywhere.
4. **The book is the only copy and has no recovery path.** That fact sets the risk budget
   for everything touching storage.
5. **Nothing leaves the machine.** No telemetry, no network calls, no exceptions.
