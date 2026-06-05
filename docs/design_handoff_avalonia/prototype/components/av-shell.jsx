/* Avalonia Fluent shell: caption bar (Mica + window controls), NavigationView,
   status strip, command palette. Fluent idioms throughout. */

const AV_NAV = [
  { id: "home",       icon: "plug",     label: "Plugins" },
  { sep: "TOOLS" },
  { id: "query",      icon: "database", label: "Query Builder" },
  { id: "dwops",      icon: "bolt",     label: "Dual-Write Operations" },
  { id: "dualwrite",  icon: "map",      label: "Dual-Write Map Browser" },
  { id: "dwcompare",  icon: "branch",   label: "Dual-Write Compare" },
  { id: "metadata",   icon: "book",     label: "Metadata Browser" },
  { id: "postbuilder",icon: "terminal", label: "POST Builder" },
  { sep: "SYSTEM" },
  { id: "profiles",   icon: "key",      label: "Profiles" },
];

const AvCaption = ({ env, onCmd }) => (
  <div style={{ height: 40, flexShrink: 0, display: "flex", alignItems: "center", background: "var(--mica)", borderBottom: "1px solid var(--stroke)", WebkitAppRegion: "drag", userSelect: "none" }}>
    <div style={{ display: "flex", alignItems: "center", gap: 9, padding: "0 14px" }}>
      <div style={{ width: 20, height: 20, borderRadius: 4, background: "var(--accent)", color: "var(--on-accent)", display: "grid", placeItems: "center", fontWeight: 700, fontSize: 12 }}>t</div>
      <span style={{ fontSize: 13, color: "var(--txt-1)" }}>tool<span style={{ color: "var(--accent)" }}>B</span>ax</span>
      <span className="t3" style={{ fontSize: 12 }}>— {env.name}</span>
    </div>

    {/* Fluent search/command entry — centered, like a Win11 app */}
    <div style={{ flex: 1, display: "flex", justifyContent: "center", WebkitAppRegion: "no-drag" }}>
      <button onClick={onCmd} style={{
        width: 380, maxWidth: "46vw", height: 28, display: "flex", alignItems: "center", gap: 8,
        background: "var(--layer-2)", border: "1px solid var(--stroke)", borderRadius: 6,
        color: "var(--txt-2)", cursor: "pointer", padding: "0 10px", fontFamily: "var(--font-ui)", fontSize: 12.5,
      }}>
        <Icon name="search" size={13}/>
        <span style={{ flex: 1, textAlign: "left" }}>Search tools &amp; run commands</span>
        <span className="fl-kbd">Ctrl+K</span>
      </button>
    </div>

    {/* Window caption buttons */}
    <div style={{ display: "flex", height: "100%", WebkitAppRegion: "no-drag" }}>
      <button className="fl-caption-btn" title="Minimize"><svg width="11" height="11" viewBox="0 0 12 12"><rect x="1" y="5.5" width="10" height="1" fill="currentColor"/></svg></button>
      <button className="fl-caption-btn" title="Maximize"><svg width="11" height="11" viewBox="0 0 12 12" fill="none" stroke="currentColor"><rect x="1.5" y="1.5" width="9" height="9"/></svg></button>
      <button className="fl-caption-btn close" title="Close"><svg width="11" height="11" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="1.1"><path d="M1 1l10 10M11 1L1 11"/></svg></button>
    </div>
  </div>
);

const AvNavView = ({ view, onView, env, onEnvChange }) => {
  const [expanded, setExpanded] = React.useState(true);
  const w = expanded ? 248 : 52;
  return (
    <div style={{ width: w, flexShrink: 0, background: "var(--mica)", borderRight: "1px solid var(--stroke)", display: "flex", flexDirection: "column", transition: "width .14s ease", overflow: "hidden" }}>
      <div style={{ padding: "8px 6px 4px" }}>
        <button className="fl-btn subtle icon" onClick={() => setExpanded(e => !e)} style={{ marginLeft: 2 }} title="Toggle pane"><Icon name="menu" size={16}/></button>
      </div>
      <div style={{ flex: 1, overflow: "auto", paddingBottom: 8 }}>
        {AV_NAV.map((it, i) => it.sep
          ? (expanded
              ? <div key={i} className="t3" style={{ fontSize: 10.5, letterSpacing: "0.08em", padding: "12px 18px 4px" }}>{it.sep}</div>
              : <div key={i} style={{ height: 1, background: "var(--divider)", margin: "8px 12px" }}/>)
          : (
            <div key={it.id} className={"fl-nav-item " + (view === it.id ? "sel" : "")} onClick={() => onView(it.id)}
              title={expanded ? "" : it.label} style={!expanded ? { justifyContent: "center", padding: 0, gap: 0 } : null}>
              <span style={{ flexShrink: 0, display: "grid", placeItems: "center", width: 18 }}><Icon name={it.icon} size={17}/></span>
              {expanded && <span className="truncate" style={{ flex: 1 }}>{it.label}</span>}
              {expanded && it.id === "dwops" && <span className="fl-badge warn" style={{ height: 18, padding: "0 7px" }}>live</span>}
            </div>
          ))}
      </div>

      {/* Profile selector docked bottom — Fluent NavView footer pattern */}
      <div style={{ borderTop: "1px solid var(--divider)", padding: 8 }}>
        <AvEnvSwitcher env={env} onChange={onEnvChange} compact={!expanded}/>
      </div>
    </div>
  );
};

const AvEnvSwitcher = ({ env, onChange, compact }) => {
  const [open, setOpen] = React.useState(false);
  const k = env.status === "connected" ? "ok" : env.status === "token-expired" ? "warn" : "err";
  return (
    <div style={{ position: "relative" }}>
      <button onClick={() => setOpen(o => !o)} className="fl-nav-item" style={{ margin: 0, width: "100%", height: 44, background: open ? "var(--layer-3)" : "transparent" }}>
        <span className="fl-badge" style={{ width: 26, height: 26, padding: 0, justifyContent: "center", borderRadius: 6, background: "var(--layer-3)", color: "var(--txt-0)", fontWeight: 600 }}>{env.legal.slice(0,2)}</span>
        {!compact && (
          <span style={{ flex: 1, minWidth: 0, textAlign: "left" }}>
            <span style={{ display: "block", fontSize: 12.5, color: "var(--txt-0)" }} className="truncate">{env.name}</span>
            <span className="t3" style={{ fontSize: 11, display: "flex", alignItems: "center", gap: 5 }}><span className="d" style={{ width: 6, height: 6, borderRadius: "50%", background: `var(--${k})` }}/>{env.legal}</span>
          </span>
        )}
        {!compact && <Icon name="chev-d" size={13}/>}
      </button>
      {open && (
        <>
          <div onClick={() => setOpen(false)} style={{ position: "fixed", inset: 0, zIndex: 30 }}/>
          <div style={{ position: "absolute", bottom: 50, left: 0, width: 252, background: "var(--layer-1)", border: "1px solid var(--stroke-2)", borderRadius: 8, zIndex: 31, padding: 4, boxShadow: "0 16px 40px rgba(0,0,0,0.5)" }}>
            {window.ENVS.map(e => {
              const kk = e.status === "connected" ? "ok" : e.status === "token-expired" ? "warn" : "err";
              return (
                <div key={e.id} className="fl-nav-item" style={{ margin: 0, height: 40 }} onClick={() => { onChange(e); setOpen(false); }}>
                  <span className="d" style={{ width: 7, height: 7, borderRadius: "50%", background: `var(--${kk})` }}/>
                  <span style={{ flex: 1, minWidth: 0 }}><span className="truncate" style={{ display: "block", fontSize: 12.5 }}>{e.name}</span><span className="t3 mono" style={{ fontSize: 10.5 }}>{e.legal} · {e.tier}</span></span>
                  {e.id === env.id && <Icon name="check" size={14}/>}
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
};

const AvStatusStrip = ({ env, viewLabel, busy }) => {
  const k = env.status === "connected" ? "ok" : env.status === "token-expired" ? "warn" : "err";
  const conn = env.status === "connected" ? "2m ago" : env.status === "token-expired" ? "1h ago" : "—";
  const Seg = ({ children }) => <div style={{ display: "flex", alignItems: "center", gap: 6, padding: "0 12px", borderRight: "1px solid var(--divider)", height: "100%" }}>{children}</div>;
  return (
    <div style={{ height: 26, flexShrink: 0, display: "flex", alignItems: "center", background: "var(--mica)", borderTop: "1px solid var(--stroke)", fontSize: 11.5, color: "var(--txt-2)" }}>
      <Seg><Icon name="plug" size={12}/> {viewLabel}</Seg>
      <Seg><span style={{ width: 7, height: 7, borderRadius: "50%", background: `var(--${k})` }}/> {env.legal} · {env.name}</Seg>
      <Seg>{busy ? <><span className="fl-pulse" style={{ color: "var(--accent)" }}>●</span> <span style={{ color: "var(--accent)" }}>working…</span></> : <>idle</>}</Seg>
      {conn !== "—" && <Seg><span className="t3">conn</span> {conn}</Seg>}
      <div style={{ flex: 1 }}/>
      <Seg><Icon name="branch" size={12}/> main</Seg>
      <Seg>SDK 1.2.0 · .NET 10</Seg>
      <div style={{ display: "flex", alignItems: "center", gap: 6, padding: "0 12px", color: "var(--accent)" }}><Icon name="sparkles" size={12}/> update ready</div>
    </div>
  );
};

const AvCommandPalette = ({ onClose, onView }) => {
  const [q, setQ] = React.useState("");
  const cmds = [
    { label: "Open Query Builder", id: "query", hint: "Alt+Q" },
    { label: "Open Dual-Write Operations", id: "dwops", hint: "Alt+O" },
    { label: "Open Dual-Write Map Browser", id: "dualwrite", hint: "Alt+D" },
    { label: "Open Dual-Write Compare", id: "dwcompare", hint: "Alt+C" },
    { label: "Open Metadata Browser", id: "metadata", hint: "Alt+M" },
    { label: "Open POST Builder", id: "postbuilder", hint: "Alt+P" },
    { label: "Manage Profiles", id: "profiles", hint: "Alt+E" },
    { label: "Plugins home", id: "home", hint: "Alt+H" },
  ];
  const f = cmds.filter(c => !q || c.label.toLowerCase().includes(q.toLowerCase()));
  return (
    <div onClick={onClose} style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.4)", display: "flex", justifyContent: "center", paddingTop: "12vh", zIndex: 200 }}>
      <div onClick={e => e.stopPropagation()} style={{ width: 540, maxWidth: "90vw", height: "fit-content", background: "var(--layer-1)", border: "1px solid var(--stroke-2)", borderRadius: 10, boxShadow: "0 24px 70px rgba(0,0,0,0.6)", overflow: "hidden" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "12px 16px", borderBottom: "1px solid var(--divider)" }}>
          <Icon name="search" size={16}/>
          <input autoFocus value={q} onChange={e => setQ(e.target.value)} placeholder="Search tools & commands…" style={{ flex: 1, background: "transparent", border: 0, outline: 0, color: "var(--txt-0)", fontFamily: "var(--font-ui)", fontSize: 15 }}/>
          <span className="fl-kbd">Esc</span>
        </div>
        <div style={{ maxHeight: 320, overflow: "auto", padding: 6 }}>
          {f.map(c => (
            <div key={c.id} className="fl-nav-item" style={{ margin: "2px 0", height: 40 }} onClick={() => { onView(c.id); onClose(); }}>
              <Icon name="arrow-r" size={14}/>
              <span style={{ flex: 1 }}>{c.label}</span>
              <span className="fl-kbd">{c.hint}</span>
            </div>
          ))}
          {!f.length && <div className="t3" style={{ padding: 20, textAlign: "center" }}>No matches</div>}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { AvCaption, AvNavView, AvEnvSwitcher, AvStatusStrip, AvCommandPalette, AV_NAV });
