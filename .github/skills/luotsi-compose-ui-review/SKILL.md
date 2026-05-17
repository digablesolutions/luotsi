---
name: luotsi-compose-ui-review
description: 'Compose UI review guidance for Luotsi and Android helper work. Use when adding or reviewing @Composable functions, state hoisting, ViewModel state collection, Material 3, Navigation Compose, side effects, recomposition, accessibility, previews, or Compose testing in Luotsi.ViewServer.Android or future Android UI modules.'
user-invocable: true
---

# Luotsi Compose UI Review

## When to Use

- Reviewing or adding Compose UI in Luotsi Android code
- Designing consent, diagnostics, setup, or future operator-facing Android screens
- Evaluating state ownership, recomposition, side effects, or accessibility

## Standards

- Prefer Material 3 and Compose-first patterns for new Android UI.
- Hoist state by default. Keep composables stateless unless local UI-only state is clearly warranted.
- Keep state production outside composables when business logic is involved. Use a state holder or `ViewModel` only when complexity warrants it.
- Collect long-lived streams with lifecycle-aware APIs such as `collectAsStateWithLifecycle`.
- Use `LaunchedEffect`, `DisposableEffect`, and `rememberUpdatedState` deliberately. Avoid effect-driven logic that could be expressed as state.
- Optimize recomposition only with evidence. Prefer stable parameters, deferred reads, and simple state flow before adding complexity.
- Require accessibility basics: touch targets, content descriptions where meaningful, contrast, and keyboard/focus behavior if the UI is interactive.

## Review Questions

1. Is state owned at the right level?
2. Could this composable be made more stateless and reusable?
3. Are side effects tied to stable keys and lifecycle?
4. Is `Flow` or `StateFlow` collected in a lifecycle-aware way?
5. Are recomposition-sensitive reads isolated from expensive work?
6. Does the UI use Material 3 and Android-native interaction patterns?

## Anti-Patterns

- Business logic hidden inside composables
- `remember { mutableStateOf(...) }` used where a higher-level state holder should own the data
- Navigation or snackbar side effects firing from unstable keys
- Premature recomposition micro-optimizations without measurement
- Accessibility treated as optional

## Luotsi-Specific Notes

- The current Android helper is not a UI-heavy app. Do not introduce Compose unless the helper actually gains real product UI beyond consent/setup surfaces.
- If Compose is introduced, keep the helper thin and avoid importing host-side workflow concerns into the Android UI layer.
