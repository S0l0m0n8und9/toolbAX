# Native sign-in close and preparation lifetime

**Status:** PR230 native-close review correction implemented and fully locally validated. Hosted rerun/Greptile resolution remains pending.

## Boundary

Closing or cancelling a native dual-write sign-in makes that attempt inactive immediately: it cannot capture a result, navigate late, or publish a late browser-ready event. When WebView2 profile preparation is outstanding, the modal may be hidden but its native host/controller must remain alive until the uninterruptible `ClearBrowsingDataAsync` task settles. Physical window/controller teardown then completes on the original Avalonia UI context. The sequencer retains exclusive gate ownership until preparation and physical close are safe, and every success, fault and cancellation path releases that gate exactly once.

The platform-neutral core is a small close/preparation coordinator with explicit attempt-active, preparation-running, close-requested and physical-close-ready transitions. The actual window supplies thin `Closing`/`Closed`, hide and close integration. It does not release a shared profile early, rotate per-attempt profile directories, kill native processes, or impose an arbitrary timeout on a clear that may still mutate the profile.

Owner shutdown must not be stranded by a hidden modal. Avalonia reports an owned-child close with `OwnerWindowClosing` and documents that a child cancellation cancels the owner's close. When preparation is outstanding, the child therefore cancels the first owner-close attempt, hides, keeps its controller alive, physically closes after preparation settles, then reissues the captured owner close on the UI dispatcher. Application/OS shutdown is allowed through immediately; the sequencer remains conservative about any outstanding preparation rather than releasing a shared-profile gate early.

The native host marks itself destroyed before controller teardown. If asynchronous WebView2 controller creation completes after destruction, that controller is closed immediately and is never assigned to `Browser`, never raises `BrowserReady`, and never navigates.

## Five failure pre-mortems

1. **Teardown aborts the profile clear and DrainPreparation never completes.** Intercept intentional child close before native teardown, make the attempt inactive, hide it, and physically close only after the preparation task settles.
2. **The sequencer releases early while an old clear can still affect the next session.** Keep gate ownership through safe preparation completion and physical-close coordination; do not use timeout-based release.
3. **A cancelled dialog becomes ready late and navigates.** Every controller-ready and post-clear navigation path checks the inactive/destroyed state before publication or navigation.
4. **A cancellation or cleanup exception strands the gate.** Coordinator completion is idempotent; dialog drain observes controlled preparation failure; sequencer retains nested `finally` release and existing `CancelThrows`/`DrainThrows` behavior.
5. **Controller creation completes after the native host was destroyed and leaks.** `DestroyNativeControlCore` marks destroyed first; late-created controllers are closed and discarded without `BrowserReady`.

## Deterministic acceptance

- A fake profile clear that cannot complete after controller destruction proves physical close is deferred until clear completion.
- A queued second sign-in is not created before safe drain, then completes after the first closes.
- Close/cancel publishes no result, navigation, or late-ready event; duplicate close is idempotent.
- Preparation failure/cancellation and existing sequencer cleanup exceptions release once.
- Deferred close continuation runs on the captured UI context, and owner shutdown is not permanently cancelled by the child.
- Delayed controller creation after host destruction closes the created controller and publishes nothing.
- Native focused tests cover both `EnableWebView2=true` and `false` compile paths without launching WebView2 or clearing a real profile.

## Implemented result

`DualWriteClosePreparationCoordinator` is the platform-neutral state boundary. Close/cancel deactivates the attempt immediately. If preparation is running, the coordinator returns `DeferAndHide`, schedules one physical close only after the task settles, and dispatches that close through the supplied UI dispatcher. Duplicate close, fault and cancellation paths are idempotent. `DualWriteSignInSequencer` retains its nested cleanup `finally` structure and does not release until the attempt's safe drain completes.

`DualWriteSignInDialog` wires this boundary through `Closing` and `Closed`. A close completes the pending attempt as null, cancels capture, and suppresses later navigation. Normal and owner-triggered close are deferred while profile clearing is outstanding. The modal is hidden during that interval; physical close re-enables its owner, and a deferred owner close is reissued afterward. Preparation success/fault and programmatic completion close immediately once safe.

`DualWriteNativeHostLifetime` serializes host destruction against asynchronous controller publication. `DestroyNativeControlCore` marks the host destroyed before closing the current controller. A controller created afterward is closed locally and never assigned to `Browser`, never raises `BrowserReady`, and cannot navigate. Initialization failure after destruction is likewise not published.

The reset policy remains unchanged: ordinary sign-in preserves SSO cookies while clearing DOM storage and disk cache; Switch account also clears cookies. Committed-response body limits, cancellation and borrowed-stream ownership are unchanged.

## Framework evidence and inference

Pinned Avalonia 12.0.5 XML confirms `WindowClosingEventArgs.CloseReason` distinguishes `OwnerWindowClosing`, and `OwnerAndChildWindows` lets a child cancellation cancel its owner close. The package nuspec pins Avalonia source commit `fee9c561ce036e8a3e8cee2397c75ca599b4790d`. At that exact commit, `Window.Hide` stops rendering, hides child/platform windows, clears `Owner`, marks visibility/shown state false and disposes the modal subscription; it does not remove content, detach the visual tree, close the window or invoke native-host destruction. `NativeControlHost` performs `DestroyNativeControlCore` from host update after visual-tree detachment/host loss. A headless `NativeControlHost` probe confirms Hide leaves the host attached and invokes neither detachment nor native destruction. Because Hide clears the framework `Owner` property, the production dialog deliberately retains its constructor-captured owner field for the safe close reissue.

Pinned WebView2 1.0.2792.45 XML exposes `ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds)` as a `Task` API without a cancellation token.

The safety design relies on one explicit SDK inference: an outstanding clear is given the best chance to settle by keeping its profile/controller alive until its returned task completes. No live WebView2 launch or real profile clear was used to claim stronger completion semantics. There is no timeout, shared-profile early release, profile rotation, global clear or process killing.

## Runtime evidence

The first coordinator tracer failed at runtime because the unsafe immediate-close implementation returned `AllowPhysicalClose` instead of `DeferAndHide` while preparation was outstanding. After implementation, eight focused lifetime cases pass: deferred teardown, queued sign-in gating, no late navigation/result, fault/cancel/duplicate-close idempotence, UI-context physical close, owner-close reissue, late native-resource rejection, and Hide/native-host lifetime.

The established native compatibility matrix plus lifetime and Hide-lifecycle coverage now totals 43 cases and passes 43/43 under both `EnableWebView2=true` and `EnableWebView2=false`. Both final `CI=true` Release App solution builds completed with zero warnings/errors. Results are recorded under `artifacts/h05/pr230-native/` as `app-build-webview-{true,false}.log`, `native-webview-{true,false}.log`, and matching TRX files. No browser, profile clear, sign-in, Power Platform or other live call occurred.

Parent then ran the combined PR230 offline gate with the Core binding-persistence correction present: the full App suite passed 1,499/1,499 with `EnableWebView2=true`, the full Core suite passed 572/572, and both Release builds completed with zero warnings/errors and no failed/skipped tests. Evidence is `artifacts/h05/pr230-native/parent-full-app-{build,test}.log`, `parent-full-app.trx`, `artifacts/h05/pr230-core/parent-full-core-{build,test}.log` and `parent-full-core.trx`. Hosted rerun/Greptile resolution and live commercial-cloud sign-in acceptance remain pending.
