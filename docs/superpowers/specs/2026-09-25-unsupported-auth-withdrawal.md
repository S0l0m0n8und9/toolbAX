# Unsupported App authentication withdrawal

Status: Implemented and focused-validated; parent review, latest-main integration, full gates, PR, and merge pending.

## Objective

Offer only authentication paths the Avalonia application actually supports: Interactive and ClientSecret for F&O and Dataverse, and portal browser sign-in for Data Integrator/dual-write. Legacy Certificate, BearerToken, ROPC, unknown numeric values, and unknown setting text remain loadable and preservable, but cannot be selected or dispatched as supported App authentication.

This withdrawal does not remove Core authentication APIs, change enum ordinals already stored, migrate credentials, or add certificate/ROPC support. It preserves H01's active-profile save transaction and leaves H03 write-outcome uncertainty, H09 aggregate persistence atomicity, and D02 casing-only client edits separately tracked.

## UI contract

`FoAuthMode` retains `Interactive = 0`, `ClientSecret = 1`, and `Certificate = 2`, and adds `Unsupported = -1`. The Profiles screen offers exactly Interactive and ClientSecret. F&O and Dataverse bind named ComboBoxes to nullable selected-mode façades. A legacy or unknown backing draft appears with no supported selection instead of silently becoming ClientSecret or Interactive; null/unsupported setter values are ignored, and backing draft changes notify the façade.

When the selected profile carries an unsupported mode, the tab shows a visible warning: the saved mode is unsupported, unrelated edits preserve its legacy settings, and choosing a supported mode replaces it. The client-ID editor is disabled until a supported replacement is selected. Direct save invocation validates the mode and client change before calling persistence, so bindings and command bypasses cannot create a new unsupported configuration.

Known Certificate values retain `FoAuthMode.Certificate` and their label while remaining outside the supported-mode predicate. Unknown Core service-principal modes and Core `BearerToken` map to `Unsupported`, distinct from Interactive. Only an absent setting with no service-principal row defaults to Interactive.

## Legacy-preserving persistence

`CoreProfileStore` compares a save against its loaded `_cache` snapshot. When an F&O or Dataverse target still has an unsupported mode and its target mode/client are unchanged, the store skips that target's auth setting, client setting, service-principal row, and credential writes. Unrelated profile edits therefore preserve raw auth-mode setting text, service-principal ID/mode, `SecretRef`, `CertThumbprint`, and the referenced blob.

A newly introduced unsupported mode or a client change while the mode remains unsupported is rejected before `UpsertEnvironmentAsync` or any other write. The user must explicitly select Interactive or ClientSecret first.

Replacing Certificate/Unsupported with ClientSecret treats the old credential as incompatible even when the client ID is unchanged. The replacement row is first upserted without `SecretRef` or `CertThumbprint`; only after that succeeds is the old blob deleted. An upsert failure leaves the prior row and blob intact. Replacing with Interactive uses the existing row-first cleanup invariant.

## Data Integrator boundary

`DiAuthMode` retains existing ordinals and adds `Unsupported = -1` for presentation. The Profiles Data Integrator tab contains portal browser sign-in/test only. It removes ROPC selection, manual client/gateway/password entry and storage controls, and retired draft persistence from ordinary saves.

When a loaded profile has legacy DI settings, unknown raw mode text, or a stored DI password reference, the screen states that the retained legacy configuration is unused by portal sign-in. Ordinary saves skip unchanged DI keys, including unknown mode text with a blank client ID. The only credential mutation left on this tab is an explicit **Clear legacy password** action shown when a DI secret exists; it deletes only the DI secret reference/blob through `CoreSecretStore`. Nothing automatically deletes, imports, rewrites, or converts legacy DI data.

`EnvProfile` retains its legacy DI properties for round-trip compatibility. The live connector remains portal/browser based, and Core token APIs are unchanged.

## Runtime boundary

`CoreAuthService` accepts only Interactive and ClientSecret for F&O and Dataverse. Every other `FoAuthMode` is rejected with a clear unsupported-mode error before principal lookup, token broker invocation, or interactive acquisition. Existing Core `AuthBroker` BearerToken behavior and tests remain unchanged.

## Failure pre-mortem

| Failure mechanism | User-visible failure | Mitigation and deterministic proof |
|---|---|---|
| A ComboBox cannot represent an unknown/Certificate value and writes the default back into the draft. | Merely opening Profiles silently converts legacy authentication. | Bind named selectors to nullable supported-mode façades; unsupported drafts return null and ignore null/unsupported setters. A rendered headless regression covers both F&O and Dataverse selectors and proves drafts remain unchanged. |
| An unrelated Save rewrites or deletes legacy auth or raw DI settings. | Renaming an environment destroys a usable credential or historical configuration. | Compare with the loaded cache snapshot and bypass unchanged unsupported target/DI persistence. Real SQLite restart tests preserve unknown text/numeric modes, service-principal identity/mode, refs/thumbprints/blobs, blank-client DI keys, and prove absent configuration still defaults Interactive. |
| Same-client replacement reuses a certificate secret or deletes it before a failed upsert. | ClientSecret dispatch receives incompatible material, or a failed save corrupts the old credential. | Treat mode compatibility as part of credential identity; upsert the unbound replacement before deleting the old blob. Injected failure tests cover F&O and Dataverse ordering and preservation. |
| A hidden or unknown mode falls through runtime dispatch to a broker. | Unsupported auth is attempted and may use the wrong stored credential. | Guard the captured mode before all lookups/acquisition. Tests assert zero principal lookups and zero broker calls for Certificate, Unsupported, and unknown-cast values on both targets. |
| UI withdrawal bypasses H01 active-save confirmation or removes Core BearerToken support. | Declining replacement still changes the active profile, or unrelated Core callers regress. | Integration tests hold/decline the active-profile replacement and assert store/drafts/legacy state remain unchanged. Existing Core AuthBroker BearerToken coverage remains green and is not edited. |

## Acceptance and validation

- F&O and Dataverse offered choices are exactly Interactive and ClientSecret; named selectors render unsupported saved modes with no selected supported value and a visible replacement warning.
- Direct invalid saves perform zero persistence. Confirmed supported replacement uses H01's active-save funnel; decline preserves the original profile and drafts.
- Real SQLite round trips preserve Certificate, BearerToken, unknown setting text/numeric values, service-principal fields, raw DI settings, and vault blobs across unrelated saves and restart.
- Explicit supported replacement clears incompatible target refs only after successful row persistence; injected upsert failures preserve the old row/blob for both F&O and Dataverse.
- Data Integrator offers portal sign-in/test and explicit legacy-password clearing only; legacy values remain unused and preserved until explicitly cleared.
- Runtime tests prove unsupported F&O/Dataverse modes perform no principal lookup or token acquisition.
- README and current model comments describe the supported App surface without rewriting historical design archives.
- Validation is deterministic and offline with headless Avalonia, temporary SQLite/vault data, injected profile/token seams, and `CI=true` Release tests. No live service, authentication, or sign-in is used.

Focused validation passes 193/193 Release tests across Profiles view/model, Shell integration, real `CoreProfileStore` SQLite round trips, `CoreAuthService` snapshot guards, converters, and retained legacy labels. Behavioral RED artifacts and final focused logs/TRX are stored under ignored `artifacts/h07/`. Complete solution gates remain a parent step after integrating the latest main branch.
