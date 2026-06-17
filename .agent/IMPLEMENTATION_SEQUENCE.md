# AutoJMS Implementation Sequence

## Current rule

Do not start feature development until build is stable.

## Safe order

1. Fix build blockers.
2. Stabilize JMS authToken handling.
3. Disable BASE background jobs.
4. Stabilize FullStackOperation lifecycle.
5. Stabilize release pipeline.
6. Mask token logs before production.
7. Normalize manifest schemas.
8. Add minimal tests.
9. Start Operation Center improvements.

## Not now

- Do not migrate to `src/` yet.
- Do not rewrite MainForm.
- Do not rewrite FullStackOperation.
- Do not remove ModuleSystem yet.
- Do not change installer/release scripts unless task requires.

