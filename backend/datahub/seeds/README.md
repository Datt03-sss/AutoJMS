# DataHub control-plane seeds

The objects in this directory are the **minimum a fresh VPS must serve before any
station works correctly**. Without them the API answers 404 for every policy path,
and that 404 is not neutral:

```
VpsRuntimePolicyService.FetchPolicyAsync
  → 6 paths tried, all 404
  → LoadCachedPolicy: empty on a new install
  → RuntimePolicyDocument.SafeDefault("BASE")   ← every ULTRA station runs as BASE
```

The station logs `[Policy] source=safe-default tier=BASE` and otherwise behaves
normally, so the symptom reaching the owner is "FullStack disappeared on the new
server", with nothing failing anywhere.

Publish them with [`../scripts/publish-manifests.sh`](../scripts/publish-manifests.sh)
(on the VPS) or [`../scripts/publish-manifests.ps1`](../scripts/publish-manifests.ps1)
(from Windows). The directory layout **is** the object path: `configs/runtime-policy.json`
here is published to `PUT /api/v1/admin/manifests/configs/runtime-policy.json` and
served from `GET /configs/runtime-policy.json`.

## What is here

| Object | Read by | Consequence if missing |
|---|---|---|
| `configs/runtime-policy.ultra.json` | `VpsRuntimePolicyService` (1st path for ULTRA) | every ULTRA station silently runs BASE |
| `configs/runtime-policy.base.json` | `VpsRuntimePolicyService` (1st path for BASE) | falls through to the shared file — no behaviour change |
| `configs/runtime-policy.json` | `VpsRuntimePolicyService` (5th path, any tier) | a tier name with no file of its own (a future `PRO`) downgrades to BASE |
| `manifest/tier-definitions.json` | `VpsManifestService.FetchTierDefinitionsAsync` | a new tier name cannot be granted the FullStack form |

## The one rule for runtime-policy files

`TierRuntimePolicy.Resolve(RuntimePolicyDocument, licenseTier)` ANDs every flag with
the entitlement the license already grants:

```
BASE  + policy true  → false        ULTRA + policy true  → true
BASE  + policy false → false        ULTRA + policy false → false
```

So a published policy **can only take rights away, never grant them**. Two things
follow, and they are the opposite of the intuitive reading:

- Setting a flag `true` here is not a security hole. It cannot turn BASE into ULTRA;
  only the license tier can do that.
- Setting a flag `false` **is** a fleet-wide kill switch. In the shared
  `runtime-policy.json` — the fallback every unmatched tier lands on — a stray `false`
  disables that feature on every ULTRA station whose tier-specific file happens to be
  missing. That is why the shared copy imposes no restriction at all.

A document that declares a `tier` different from the one the station asked for is
rejected by `VpsRuntimePolicyService.TryParsePolicy`, so `runtime-policy.base.json`
can never reach an ULTRA station. `"tier": ""` means "shared" and is stamped with
whatever tier asked for it.

## Which keys actually do something

Only these keys are read by the desktop. Anything else in a policy document is
inert — `System.Text.Json` ignores unknown properties, so a typo fails silently.

| Key | Effect |
|---|---|
| `features["forms.fullStackOperation"]` | FullStack Operation form (AND entitlement) |
| `features["fullStack.backgroundSync"]` | background auto-sync (AND entitlement) |
| `features["fullStack.inventorySync"]` | startup inventory sync (AND entitlement) |
| `features["fullStack.databaseTracking"]` | startup database tracking (AND entitlement) |
| `features["tabs.tracking"]` | manual tracking (AND entitlement) |
| `features["tabs.print"]` | manual print (AND entitlement) |
| `googleSheets.enabled` / `googleSheets.provider` | `AppSettings.GoogleSheetsAccessMode` |
| `print.defaultAutoPrint` / `print.enablePrinterPreflight` | print defaults |
| `debugCapture.enabled` / `debugCapture.slowApiThresholdMs` | HTTP capture |
| `modulePolicy.autoUpdate` / `.silentUpdate` / `.applyOnNextStartup` | `VpsModuleProvider` |

Two traps worth knowing before editing a seed:

- **`googleSheets` must use the typed block, not `features`.**
  `RuntimeGoogleSheetsPolicy.Provider` defaults to the non-blank `"TokenBroker"`, and
  `RuntimePolicyApplier` only consults `features["googleSheets.provider"]` when the
  typed value is blank. A provider set only in `features` is therefore never read.
- **`print.*` and `debugCapture.*` are deliberately absent from these seeds.**
  Both fall back to the station's own `AppSettings` value when the key is missing.
  Publishing them would reset a technician's local choice at every launch. Add them
  only when the intent really is to override every station in the fleet.

`features["tabs.home"]`, `tabs.dkch` and `tabs.about` appear in
`RuntimePolicyDocument.SafeDefault` but have no consumer; they are omitted here so
nobody flips one and waits for something to happen. Tab visibility comes from
`tier-definitions.json`.

## What is deliberately *not* seeded

- `manifest/app-manifest.json`, `manifest/version-latest.json`,
  `manifest/hash-manifest.json`, `selector-updates/…`, `modules/…` — published by
  `release/build-release.ps1` as part of a release, not by hand. Their absence is
  benign: `VpsManifestService` returns an empty manifest and no update is offered.
- `configs/public-config.json` — declared in `VpsConfig.DataHubManifestUrls` but
  fetched by nothing in the current client. Publishing a file for it would be
  publishing a file nobody reads.
- Anything containing a secret. These objects are served **anonymously** to every
  station (`DeviceAuthenticationMiddleware.IsControlPlaneReadPath`); a key placed
  here is a public key in the literal sense.
