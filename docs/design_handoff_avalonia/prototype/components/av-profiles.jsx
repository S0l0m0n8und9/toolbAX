/* Profiles — Fluent TabView, ListView master, Fluent inputs/InfoBar/ToggleSwitch. */

const AvProfiles = ({ env }) => {
  const [sel, setSel] = React.useState(env.id);
  const [activeId, setActiveId] = React.useState(env.id);
  const [q, setQ] = React.useState("");
  const list = window.ENVS.filter(x => !q || x.name.toLowerCase().includes(q.toLowerCase()) || x.legal.toLowerCase().includes(q.toLowerCase()));
  const e = window.ENVS.find(x => x.id === sel) || env;
  return (
    <div style={{ height: "100%", display: "grid", gridTemplateColumns: "300px 1fr", overflow: "hidden" }}>
      <div style={{ borderRight: "1px solid var(--stroke)", display: "flex", flexDirection: "column", minHeight: 0, background: "var(--mica)" }}>
        <div style={{ padding: "12px 12px 8px", display: "flex", alignItems: "center", gap: 8 }}>
          <span style={{ fontWeight: 600, fontSize: 14, flex: 1 }}>Profiles</span>
          <button className="fl-btn sm subtle icon"><Icon name="plus" size={15}/></button>
        </div>
        <div style={{ padding: "0 12px 8px", position: "relative" }}>
          <span style={{ position: "absolute", left: 20, top: 9, color: "var(--txt-3)" }}><Icon name="search" size={13}/></span>
          <input className="fl-input" value={q} onChange={ev => setQ(ev.target.value)} placeholder="Search…" style={{ width: "100%", paddingLeft: 30 }}/>
        </div>
        <div style={{ overflow: "auto", flex: 1, padding: "0 6px" }}>
          {list.map(x => {
            const k = x.status === "connected" ? "ok" : x.status === "token-expired" ? "warn" : "err";
            const active = x.id === activeId;
            return (
              <div key={x.id} className={"fl-nav-item " + (x.id === sel ? "sel" : "")} style={{ height: "auto", padding: "9px 12px", alignItems: "flex-start" }} onClick={() => setSel(x.id)}>
                <span style={{ marginTop: 3, width: 8, height: 8, borderRadius: "50%", background: active ? `var(--${k})` : "transparent", border: active ? "0" : "1px solid var(--txt-3)", flexShrink: 0 }}/>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 13, color: "var(--txt-0)", display: "flex", alignItems: "center", gap: 6 }}>
                    <span className="truncate">{x.name}</span>
                    {active && <span className="fl-badge ok" style={{ height: 16, padding: "0 6px", fontSize: 10 }}>ACTIVE</span>}
                  </div>
                  <div className="mono t3 truncate" style={{ fontSize: 10.5 }}>{x.url}</div>
                  <div className="t3" style={{ fontSize: 11 }}>{x.legal} · {x.tier}</div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
      <AvProfileDetail key={sel} e={e} isActive={e.id === activeId} onSetActive={() => setActiveId(e.id)}/>
    </div>
  );
};

const AvProfileDetail = ({ e, isActive, onSetActive }) => {
  const [tab, setTab] = React.useState("fo");
  const [status, setStatus] = React.useState({ text: "No connection tested yet.", kind: "neutral" });
  const [testing, setTesting] = React.useState(false);
  const test = (scope) => {
    setTesting(true); setStatus({ text: `Testing ${scope}…`, kind: "info" });
    setTimeout(() => { setTesting(false); const ok = e.status !== "disconnected"; setStatus(ok ? { text: `${scope} · 200 OK · ${e.latency || 132}ms · just now`, kind: "ok" } : { text: `${scope} · connection refused`, kind: "err" }); }, 900);
  };
  const dvUrl = `org-${e.legal.toLowerCase()}.crm6.dynamics.com`;
  const tabs = [["fo", "FO Environment"], ["ce", "CE · Dataverse"], ["auth", "Auth"], ["di", "Data Integrator"]];
  return (
    <div style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
      <div style={{ padding: "18px 28px 0" }}>
        <div className="t3" style={{ fontSize: 11, letterSpacing: "0.08em" }}>PROFILE</div>
        <div style={{ display: "flex", alignItems: "baseline", gap: 12 }}>
          <h1 style={{ margin: "2px 0 14px", fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 26 }}>{e.name}</h1>
          {isActive ? <span className="fl-badge ok"><span className="d"/>active</span> : <span className="fl-badge neutral">inactive</span>}
          <span className="fl-badge neutral">{e.tier}</span>
        </div>
      </div>
      {/* Fluent pivot tabs */}
      <div style={{ display: "flex", gap: 4, padding: "0 24px", borderBottom: "1px solid var(--stroke)" }}>
        {tabs.map(([id, label]) => (
          <button key={id} onClick={() => setTab(id)} style={{ height: 38, padding: "0 12px", background: "transparent", border: 0, borderBottom: "2px solid " + (tab === id ? "var(--accent)" : "transparent"), color: tab === id ? "var(--txt-0)" : "var(--txt-2)", cursor: "pointer", fontFamily: "var(--font-ui)", fontSize: 13.5, fontWeight: tab === id ? 600 : 400 }}>{label}</button>
        ))}
      </div>
      <div style={{ flex: 1, overflow: "auto", padding: "20px 28px", minHeight: 0 }}>
        {tab === "fo" && (
          <AvPfCard title="FO environment">
            <AvField label="Name"><input className="fl-input" defaultValue={e.name} style={{ flex: 1 }}/></AvField>
            <AvField label="Base URL"><input className="fl-input mono" defaultValue={`https://${e.url}`} style={{ flex: 1 }}/></AvField>
            <AvField label="Tenant ID"><input className="fl-input mono" defaultValue={e.tenant} style={{ flex: 1 }}/></AvField>
            <AvField label="Scope"><input className="fl-input mono" defaultValue={`https://${e.url}/.default`} style={{ flex: 1 }}/></AvField>
            <AvField label="Default company"><select className="fl-combo" defaultValue={e.legal} style={{ width: 150 }}><option>USMF</option><option>DEMF</option><option>AUMF</option></select></AvField>
          </AvPfCard>
        )}
        {tab === "ce" && (
          <AvPfCard title="CE / Dataverse" subtitle="Linked Dataverse environment for dual-write.">
            <AvField label="Base URL"><input className="fl-input mono" defaultValue={`https://${dvUrl}`} style={{ flex: 1 }}/></AvField>
            <AvField label="Tenant ID"><input className="fl-input mono" defaultValue={e.tenant} style={{ flex: 1 }}/></AvField>
            <AvField label="Web API"><input className="fl-input mono" defaultValue={`https://${dvUrl}/api/data/v9.2`} style={{ flex: 1 }}/></AvField>
          </AvPfCard>
        )}
        {tab === "auth" && <AvAuthTab e={e}/>}
        {tab === "di"   && <AvDiTab e={e}/>}
      </div>
      <div style={{ flexShrink: 0, height: 48, borderTop: "1px solid var(--stroke)", background: "var(--mica)", display: "flex", alignItems: "center", gap: 8, padding: "0 20px" }}>
        <button className="fl-btn"><Icon name="refresh" size={13}/> Refresh</button>
        <button className="fl-btn accent"><Icon name="save" size={13}/> Save</button>
        <button className="fl-btn" disabled={isActive} onClick={onSetActive}><Icon name="check" size={13}/> {isActive ? "Active" : "Set active"}</button>
        <div className="vr" style={{ height: 22 }}/>
        <button className="fl-btn" disabled={testing} onClick={() => test("FO")}><Icon name="plug" size={13}/> Test FO</button>
        <button className="fl-btn" disabled={testing} onClick={() => test("CE")}><Icon name="plug" size={13}/> Test CE</button>
        <div style={{ flex: 1 }}/>
        <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <span style={{ width: 8, height: 8, borderRadius: "50%", background: `var(--${status.kind === "neutral" ? "txt-3" : status.kind})` }}/>
          <span style={{ fontSize: 12.5, color: status.kind === "err" ? "var(--err)" : "var(--txt-2)" }}>{status.text}</span>
        </span>
      </div>
    </div>
  );
};

const AvAuthTab = ({ e }) => {
  const [mode, setMode] = React.useState("client");
  const [signed, setSigned] = React.useState(e.status === "connected");
  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", gap: 16 }}>
      <AvPfCard title="App registration">
        <AvField label="Auth mode"><AvSeg value={mode} onChange={setMode} options={[["client", "Client credentials"], ["bearer", "Bearer token"]]}/></AvField>
        <AvField label="Client ID"><input className="fl-input mono" defaultValue="74af7a08-b1dc-44bc-9b37-0b5f4a1e2c8d" style={{ flex: 1 }}/></AvField>
        {mode === "client"
          ? <AvField label="Secret"><div style={{ display: "flex", gap: 6, flex: 1 }}><input className="fl-input mono" type="password" defaultValue="supersecretvalue" style={{ flex: 1 }}/><button className="fl-btn sm">Rotate</button></div></AvField>
          : <AvField label="Redirect"><input className="fl-input mono" defaultValue="http://localhost" readOnly style={{ flex: 1 }}/></AvField>}
      </AvPfCard>
      <AvPfCard title={mode === "bearer" ? "Interactive sign-in" : "Token cache"} subtitle={mode === "bearer" ? "Public-client loopback via MSAL. Renews silently after first sign-in." : "DPAPI (CurrentUser) — per environment."}>
        {mode === "bearer" ? (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              {signed ? <span className="fl-badge ok"><span className="d"/>signed in</span> : <span className="fl-badge neutral">not signed in</span>}
              {signed && <span className="mono t3" style={{ fontSize: 11 }}>matthew.hink@contoso.com · token 53m</span>}
            </div>
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              <button className="fl-btn accent" onClick={() => setSigned(true)}><Icon name="user" size={13}/> Sign in with Microsoft…</button>
              <button className="fl-btn" onClick={() => setSigned(true)}><Icon name="terminal" size={13}/> Azure CLI</button>
              {signed && <button className="fl-btn subtle" onClick={() => setSigned(false)}>Clear</button>}
            </div>
            <div className="mono t3" style={{ fontSize: 11, lineHeight: 1.6 }}>cache · %LocalAppData%\FoToolbox\msal-cache</div>
          </div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            <AvField label="Store"><span className="mono" style={{ fontSize: 12.5, color: "var(--txt-2)" }}>DPAPI · profile.db</span></AvField>
            <AvField label="Expires"><span className="mono" style={{ fontSize: 12.5, color: e.latency ? "var(--txt-2)" : "var(--warn)" }}>{e.latency ? "in 54m" : "expired"}</span></AvField>
            <button className="fl-btn" style={{ alignSelf: "flex-start" }}><Icon name="plug" size={13}/> Acquire token</button>
          </div>
        )}
      </AvPfCard>
    </div>
  );
};

const AvDiTab = ({ e }) => {
  const [mode, setMode] = React.useState("interactive");
  const [signed, setSigned] = React.useState(true);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, maxWidth: 780 }}>
      <div className="fl-infobar warn">
        <span className="ib-icon"><Icon name="alert" size={16}/></span>
        <div>Used by <b>Dual-Write Operations</b> &amp; <b>Compare</b>. The gateway requires a <b>delegated</b> Data Integrator token — app-only is not available.</div>
      </div>
      <AvPfCard title="Acquisition mode">
        <AvField label="Mode"><AvSeg value={mode} onChange={setMode} options={[["ropc", "ROPC (service account)"], ["interactive", "Interactive (MFA)"]]}/></AvField>
        <AvField label="Client ID"><div style={{ display: "flex", gap: 8, flex: 1, alignItems: "center" }}><input className="fl-input mono" defaultValue="2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b" style={{ flex: 1 }}/><span className="t3" style={{ fontSize: 11, whiteSpace: "nowrap" }}>Data Integrator (default)</span></div></AvField>
      </AvPfCard>
      {mode === "ropc" ? (
        <AvPfCard title="Service account" subtitle="Browser-free. DPAPI-encrypted. Use a non-MFA account — ROPC fails under MFA (AADSTS50076).">
          <AvField label="Tenant ID"><input className="fl-input mono" defaultValue={e.tenant} style={{ flex: 1 }}/></AvField>
          <AvField label="Username"><input className="fl-input mono" defaultValue="dualwrite.svc@contoso.com" style={{ flex: 1 }}/></AvField>
          <AvField label="Password"><input className="fl-input mono" type="password" defaultValue="serviceaccountpw" style={{ flex: 1 }}/></AvField>
        </AvPfCard>
      ) : (
        <AvPfCard title="Interactive sign-in" subtitle="Captures a delegated token + refresh token via the Data Integrator portal (WebView2). Renews silently.">
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            {signed ? <span className="fl-badge ok"><span className="d"/>signed in</span> : <span className="fl-badge neutral">not signed in</span>}
            {signed && <span className="mono t3" style={{ fontSize: 11 }}>ops.svc@contoso.com · access 47m · refresh ok</span>}
          </div>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <button className="fl-btn accent" onClick={() => setSigned(true)}><Icon name="user" size={13}/> Sign in with Microsoft…</button>
            {signed && <button className="fl-btn" onClick={() => setSigned(false)}>Switch account</button>}
          </div>
        </AvPfCard>
      )}
    </div>
  );
};

const AvSeg = ({ value, onChange, options }) => (
  <div style={{ display: "inline-flex", background: "var(--layer-2)", border: "1px solid var(--stroke)", borderRadius: 6, padding: 2, gap: 2 }}>
    {options.map(([id, label]) => (
      <button key={id} onClick={() => onChange(id)} style={{ height: 26, padding: "0 12px", borderRadius: 4, border: 0, cursor: "pointer", fontFamily: "var(--font-ui)", fontSize: 12.5, background: value === id ? "var(--accent)" : "transparent", color: value === id ? "var(--on-accent)" : "var(--txt-1)", fontWeight: value === id ? 600 : 400 }}>{label}</button>
    ))}
  </div>
);

const AvPfCard = ({ title, subtitle, children }) => (
  <div className="fl-card">
    <div style={{ padding: "11px 16px", borderBottom: "1px solid var(--divider)", background: "var(--mica)" }}>
      <div style={{ fontSize: 13.5, fontWeight: 600 }}>{title}</div>
      {subtitle && <div className="t3" style={{ fontSize: 11.5, marginTop: 2, lineHeight: 1.5 }}>{subtitle}</div>}
    </div>
    <div style={{ padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>{children}</div>
  </div>
);

const AvField = ({ label, children }) => (
  <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
    <div style={{ width: 130, fontSize: 12.5, color: "var(--txt-2)", flexShrink: 0 }}>{label}</div>
    <div style={{ flex: 1, display: "flex", minWidth: 0 }}>{children}</div>
  </div>
);

Object.assign(window, { AvProfiles });
