/* Plugins home — Fluent landing. Card grid, search box, "operates live" InfoBadge. */

const AvHome = ({ env, onOpen }) => {
  const [q, setQ] = React.useState("");
  const plugins = window.PLUGINS.filter(p => !q || p.name.toLowerCase().includes(q.toLowerCase()) || p.desc.toLowerCase().includes(q.toLowerCase()));
  const iconFor = (id) => ({ query: "database", dwops: "bolt", dualwrite: "map", dwcompare: "branch", metadata: "book", postbuilder: "terminal", profiles: "key" }[id] || "plug");

  return (
    <div style={{ height: "100%", overflow: "auto" }}>
      <div style={{ maxWidth: 1080, margin: "0 auto", padding: "28px 32px 40px" }}>
        <div style={{ display: "flex", alignItems: "flex-end", gap: 16, marginBottom: 6 }}>
          <div style={{ flex: 1 }}>
            <h1 style={{ margin: 0, fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 28, letterSpacing: "-0.01em" }}>Plugins</h1>
            <p className="dim" style={{ margin: "6px 0 0", fontSize: 14 }}>
              Connected to <span style={{ color: "var(--accent)" }}>{env.name}</span>. Open a tool or press <span className="fl-kbd">Ctrl+K</span> to run a command.
            </p>
          </div>
          <div style={{ position: "relative" }}>
            <span style={{ position: "absolute", left: 10, top: 9, color: "var(--txt-3)" }}><Icon name="search" size={14}/></span>
            <input className="fl-input" value={q} onChange={e => setQ(e.target.value)} placeholder="Filter plugins…" style={{ width: 240, paddingLeft: 32 }}/>
          </div>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: 14, marginTop: 24 }}>
          {plugins.map(p => (
            <button key={p.id} onClick={() => onOpen(p.id)} className="fl-card" style={{ textAlign: "left", cursor: "pointer", padding: 0, display: "flex", flexDirection: "column", transition: "border-color .1s ease, background .1s ease" }}
              onMouseEnter={e => { e.currentTarget.style.borderColor = "var(--stroke-2)"; e.currentTarget.style.background = "var(--layer-2)"; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = "var(--stroke)"; e.currentTarget.style.background = "var(--layer-1)"; }}>
              <div style={{ padding: 16, display: "flex", flexDirection: "column", gap: 10, flex: 1 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 11 }}>
                  <span style={{ width: 36, height: 36, borderRadius: 8, background: p.hot ? "var(--accent-tint)" : "var(--layer-3)", color: p.hot ? "var(--accent)" : "var(--txt-1)", display: "grid", placeItems: "center", flexShrink: 0 }}>
                    <Icon name={iconFor(p.id)} size={18}/>
                  </span>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 14.5, fontWeight: 600, color: "var(--txt-0)" }} className="truncate">{p.name}</div>
                    <div className="mono t3" style={{ fontSize: 11 }}>v{p.version} · {p.cat}</div>
                  </div>
                  {p.signed
                    ? <span title="Signed plugin" style={{ color: "var(--ok)" }}><Icon name="check" size={15}/></span>
                    : <span title="Unsigned — sandboxed" style={{ color: "var(--warn)" }}><Icon name="alert" size={15}/></span>}
                </div>
                <p className="dim" style={{ margin: 0, fontSize: 12.5, lineHeight: 1.5, textWrap: "pretty" }}>{p.desc}</p>
              </div>
              <div style={{ padding: "10px 16px", borderTop: "1px solid var(--divider)", display: "flex", alignItems: "center", gap: 8 }}>
                <span className="fl-kbd">Alt+{p.shortcut}</span>
                {p.live && <span className="fl-badge warn"><span className="d"/>operates live</span>}
                <div style={{ flex: 1 }}/>
                {p.builtin && <span className="t3" style={{ fontSize: 11 }}>built-in</span>}
                <Icon name="arrow-r" size={14}/>
              </div>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { AvHome });
