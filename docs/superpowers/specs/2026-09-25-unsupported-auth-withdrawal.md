# Unsupported App authentication withdrawal

Status: Clear-transition review fix source-reviewed and fully validated locally; remote re-review and merge pending.

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
| A user stores or clears a secret before saving a legacy-to-ClientSecret draft. | The still-legacy service-principal blob is overwritten or deleted even if profile replacement is later declined, and an accepted save may discard a prematurely stored secret as incompatible. | Enable secret entry, Store and Clear only when the saved profile is already ClientSecret and draft mode, effective client ID and tenant still match it. Commands repeat the guard before storage mutation; fake and real SQLite/vault sequences cover decline, accepted replacement, subsequent Store/Clear and unrelated save. DI's separately labelled legacy-password clear is unaffected. |
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

Focused validation now passes 211/211 Release tests across Profiles view/model, active-pane headless rendering, Shell integration, real `CoreProfileStore` SQLite/vault round trips, `CoreAuthService` snapshot guards, converters, and retained legacy labels. The Store-transition RED proved both targets could overwrite a still-legacy credential before profile Save; the PR223 Clear-transition RED then proved direct Clear could delete that same credential. Entry, Store and Clear now require the saved and draft ClientSecret context to match, retain rejected input, preserve decline behavior, and block pending client-ID/tenant edits. Accepted saved context permits Store/Clear with target isolation and survives unrelated Save. Evidence is under ignored `artifacts/h07/secret-transition/` and `artifacts/h07/clear-transition/`.

The earlier source `b701702149f9bfac5937eb4a02bb9ec17b7c1379` passed App 1,170/Core 403 before the Store-transition correction. Source `977d2684e6f75c06ff3c60d862c9f1932ea5f309` passed App 1,182/Core 432 before the Clear-transition correction. Both remain historical. At corrected source `dce327cd27829588a44f6a3988ee555572c6c7fc`, both CI-strict Release builds completed with 0 warnings/errors; App passed 1,188/1,188 and Core 432/432, with no failures or skips. Remote re-review and merge remain pending. H09 aggregate persistence and D02 casing-only client-ID changes remain out of scope.
