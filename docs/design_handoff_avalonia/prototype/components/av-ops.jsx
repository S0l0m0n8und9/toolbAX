/* Dual-Write Operations — Fluent. Same behavior as the terminal version
   (confirm gate + live polling), re-expressed with InfoBar / CommandBar /
   DataGrid / ContentDialog — all native Avalonia Fluent controls. */

const AVO_RESULT = { start: "running", stop: "stopped", pause: "paused", resume: "running", initial: "running" };
const AVO_COLOR  = { running: "ok", paused: "warn", errored: "err", stopped: "neutral", idle: "neutral" };
const AVO_DIR    = { "both": "↔", "fo→dv": "→", "dv→fo": "←" };
const avoTrans = (s) => !AVO_COLOR[s];
const avoNow = () => { const d = new Date(); return [d.getHours(), d.getMinutes(), d.getSeconds()].map(n => String(n).padStart(2, "0")).join(":"); };

const AvOps = ({ env }) => {
  const gw = window.DW_GATEWAY;
  const [maps, setMaps]       = React.useState(() => window.DW_OPS_MAPS.map(m => ({ ...m })));
  const [checked, setChecked] = React.useState(() => new Set());
  const [confirm, setConfirm] = React.useState(null);
  const [flight, setFlight]   = React.useState(null);
  const [log, setLog] = React.useState([
    { ts: "04:21:09", text: "GET Environments?targetType=AX&identifier=…", note: "resolved cid " + gw.cid.slice(0,8) + "… · " + gw.cname, kind: "ok" },
    { ts: "04:21:10", text: "GET Entities?targetType=AX&cid=…", note: window.DW_OPS_MAPS.length + " maps · templates loaded", kind: "ok" },
  ]);
  const allChecked = checked.size > 0 && checked.size === maps.length;
  const busy = !!flight && flight.phase !== "done";
  const toggle = (tid) => setChecked(p => { const n = new Set(p); n.has(tid) ? n.delete(tid) : n.add(tid); return n; });
  const eligible = (a) => maps.filter(m => checked.has(m.tid) && a.needs.includes(m.state));

  const run = (action) => {
    const targets = eligible(action); if (!targets.length) return;
    const ids = targets.map(t => t.tid);
    const requestId = Math.random().toString(16).slice(2, 10);
    setMaps(p => p.map(m => ids.includes(m.tid) ? { ...m, state: action.verb } : m));
    setFlight({ action, ids, requestId, phase: "posting", done: 0, total: ids.length });
    setLog(p => [{ ts: avoNow(), text: `POST Start · action=${action.code} (${action.id}) · ${ids.length} map${ids.length>1?"s":""}`, note: "requestId " + requestId, kind: "info" }, ...p].slice(0,8));
    setTimeout(() => { setFlight(f => f && { ...f, phase: "polling" }); setLog(p => [{ ts: avoNow(), text: `GET Status/${requestId}`, note: "InProgress…", kind: "info" }, ...p].slice(0,8)); }, 520);
    let i = 0;
    const tick = setInterval(() => {
      i += 1;
      setMaps(p => p.map(m => (ids[i-1] === m.tid) ? { ...m, state: AVO_RESULT[action.id] } : m));
      setFlight(f => f && { ...f, done: i });
      if (i >= ids.length) { clearInterval(tick); setFlight(f => f && { ...f, phase: "done" }); setLog(p => [{ ts: avoNow(), text: `Status/${requestId} → Succeeded`, note: `${ids.length}/${ids.length} maps now ${AVO_RESULT[action.id]}`, kind: "ok" }, ...p].slice(0,8)); }
    }, 620);
  };

  return (
    <div style={{ height: "100%", display: "flex", flexDirection: "column", minHeight: 0, position: "relative" }}>
      <div style={{ padding: "16px 24px 0" }}>
        <h1 style={{ margin: 0, fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 24 }}>Dual-Write Operations</h1>
        <div className="fl-infobar warn" style={{ marginTop: 14 }}>
          <span className="ib-icon"><Icon name="alert" size={17}/></span>
          <div><b>Live environment.</b> Mutating actions take effect immediately in <span style={{ color: "var(--accent)" }}>{gw.cname}</span>. Every action is confirmed before it runs.</div>
        </div>
      </div>

      {/* Gateway connection row */}
      <div style={{ display: "flex", alignItems: "center", gap: 18, padding: "14px 24px", flexWrap: "wrap" }}>
        <AvMeta label="GATEWAY" value={gw.host.split(".")[1]}/>
        <div className="vr" style={{ height: 26 }}/>
        <AvMeta label="IDENTIFIER" value={gw.identifier}/>
        <div className="vr" style={{ height: 26 }}/>
        <AvMeta label="CID" value={gw.cid.slice(0,8) + "…"}/>
        <div style={{ flex: 1 }}/>
        <span className="fl-badge ok"><span className="d"/>{gw.auth.mode} · {gw.auth.account}</span>
        <span className="mono t3" style={{ fontSize: 11 }}>token {gw.auth.expires}</span>
        <button className="fl-btn sm subtle"><Icon name="refresh" size={13}/> Discover</button>
      </div>

      {/* CommandBar */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "0 24px 14px", flexWrap: "wrap" }}>
        {window.DW_ACTIONS.map(a => {
          const n = eligible(a).length; const disabled = busy || n === 0; const danger = a.danger;
          return (
            <button key={a.id} className={"fl-btn" + (danger && !disabled ? " danger" : "")} disabled={disabled} onClick={() => setConfirm({ action: a })}>
              <Icon name={a.icon} size={14}/>{a.label}{n > 0 && <span style={{ opacity: 0.7 }}>· {n}</span>}
            </button>
          );
        })}
        <div className="vr" style={{ height: 22 }}/>
        <span className="t3" style={{ fontSize: 12.5 }}>{checked.size} selected</span>
        <div style={{ flex: 1 }}/>
        {busy && <span style={{ color: "var(--accent)", fontSize: 12.5, display: "flex", alignItems: "center", gap: 6 }}><span className="fl-pulse">●</span> polling {flight.requestId} · {flight.done}/{flight.total}</span>}
      </div>

      {/* DataGrid */}
      <div style={{ flex: 1, overflow: "auto", minHeight: 0, borderTop: "1px solid var(--stroke)" }}>
        <table className="fl-grid">
          <thead>
            <tr>
              <th style={{ width: 42 }}><AvCheck on={allChecked} dash={checked.size > 0 && !allChecked} onChange={() => setChecked(allChecked ? new Set() : new Set(maps.map(m => m.tid)))}/></th>
              {["Table map", "Flow", "Template", "Author", "Rows 24h", "Errors", "State"].map((h, i) => <th key={h} style={i >= 4 && i <= 5 ? { textAlign: "right" } : null}>{h}</th>)}
            </tr>
          </thead>
          <tbody>
            {maps.map(m => {
              const on = checked.has(m.tid); const trans = avoTrans(m.state);
              return (
                <tr key={m.tid} className={on ? "sel" : ""} style={{ cursor: "pointer" }} onClick={() => toggle(m.tid)}>
                  <td onClick={e => e.stopPropagation()}><AvCheck on={on} onChange={() => toggle(m.tid)}/></td>
                  <td style={{ color: "var(--txt-0)", whiteSpace: "nowrap" }}><span className="mono">{m.fo}</span> <span style={{ color: "var(--accent)", margin: "0 5px" }}>{AVO_DIR[m.direction]}</span> <span className="mono dim">{m.dv}</span></td>
                  <td className="dim">{m.direction}</td>
                  <td className="mono">v{m.tmplVersion}</td>
                  <td style={{ color: m.author === "Microsoft" ? "var(--txt-2)" : "var(--info)" }}>{m.author}</td>
                  <td className="mono" style={{ textAlign: "right" }}>{m.rows24h.toLocaleString()}</td>
                  <td className="mono" style={{ textAlign: "right", color: m.errors24h > 0 ? "var(--err)" : "var(--txt-3)" }}>{m.errors24h || "—"}</td>
                  <td style={{ whiteSpace: "nowrap" }}>
                    {trans
                      ? <span style={{ color: "var(--accent)", display: "inline-flex", alignItems: "center", gap: 6, fontSize: 12.5 }}><span className="fl-pulse">●</span>{m.state}…</span>
                      : <span className={"fl-badge " + AVO_COLOR[m.state]}><span className="d"/>{m.state}</span>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Request log */}
      <div style={{ flexShrink: 0, borderTop: "1px solid var(--stroke)", background: "var(--mica)", maxHeight: 140, display: "flex", flexDirection: "column" }}>
        <div style={{ padding: "8px 16px", display: "flex", alignItems: "center", gap: 8, fontSize: 12.5, color: "var(--txt-2)", borderBottom: "1px solid var(--divider)" }}>
          <Icon name="logs" size={14}/><span style={{ flex: 1 }}>Gateway requests</span>
          {busy && <span style={{ color: "var(--accent)" }}>{flight.done}/{flight.total}</span>}
        </div>
        <div style={{ overflow: "auto" }}>
          {log.map((l, i) => (
            <div key={i} style={{ display: "flex", alignItems: "center", gap: 10, height: 24, padding: "0 16px" }}>
              <span className="mono t3" style={{ width: 56, fontSize: 11 }}>{l.ts}</span>
              <span style={{ width: 7, height: 7, borderRadius: "50%", background: `var(--${l.kind})`, flexShrink: 0 }}/>
              <span className="mono truncate" style={{ flex: 1, fontSize: 12, color: "var(--txt-1)" }}>{l.text}</span>
              <span className="mono t3 truncate" style={{ fontSize: 11, maxWidth: "44%" }}>{l.note}</span>
            </div>
          ))}
        </div>
      </div>

      {confirm && <AvConfirm action={confirm.action} targets={eligible(confirm.action)} cname={gw.cname} onCancel={() => setConfirm(null)} onConfirm={() => { run(confirm.action); setConfirm(null); }}/>}
    </div>
  );
};

const AvMeta = ({ label, value }) => (
  <div style={{ display: "flex", flexDirection: "column", lineHeight: 1.3 }}>
    <span className="t3" style={{ fontSize: 10, letterSpacing: "0.08em" }}>{label}</span>
    <span className="mono" style={{ fontSize: 12.5, color: "var(--txt-1)" }}>{value}</span>
  </div>
);

const AvCheck = ({ on, dash, onChange }) => (
  <button onClick={onChange} style={{ width: 18, height: 18, padding: 0, display: "grid", placeItems: "center", cursor: "pointer", background: on || dash ? "var(--accent)" : "var(--layer-2)", border: "1px solid " + (on || dash ? "var(--accent)" : "var(--stroke-2)"), borderRadius: 4, color: "var(--on-accent)" }}>
    {on && <Icon name="check" size={13} stroke={2.5}/>}
    {dash && !on && <span style={{ width: 8, height: 2, background: "var(--on-accent)" }}/>}
  </button>
);

const AvConfirm = ({ action, targets, cname, onCancel, onConfirm }) => {
  const danger = action.danger;
  return (
    <div onClick={onCancel} style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.5)", display: "grid", placeItems: "center", zIndex: 100, padding: 24 }}>
      <div onClick={e => e.stopPropagation()} style={{ width: 480, maxWidth: "100%", background: "var(--layer-1)", border: "1px solid var(--stroke-2)", borderRadius: 10, boxShadow: "0 28px 70px rgba(0,0,0,0.6)", overflow: "hidden" }}>
        <div style={{ padding: "18px 22px 6px" }}>
          <div style={{ fontFamily: "var(--font-disp)", fontSize: 19, fontWeight: 600 }}>{action.label} {targets.length} map{targets.length > 1 ? "s" : ""}?</div>
        </div>
        <div style={{ padding: "6px 22px 18px" }}>
          <p className="dim" style={{ margin: "0 0 14px", fontSize: 13.5, lineHeight: 1.6 }}>
            Sends <span className="mono" style={{ color: "var(--accent)" }}>action={action.code}</span> to the Dual-Write gateway for <span style={{ color: "var(--txt-0)" }}>{cname}</span>.
            {action.id === "initial" && <span style={{ color: "var(--err)" }}> Initial sync re-syncs all data and can run for a long time.</span>}
            {action.id === "stop" && <span style={{ color: "var(--err)" }}> Stopping halts replication until restarted.</span>}
          </p>
          <div style={{ border: "1px solid var(--divider)", borderRadius: 6, maxHeight: 156, overflow: "auto" }}>
            {targets.map(t => (
              <div key={t.tid} style={{ display: "flex", alignItems: "center", gap: 8, height: 30, padding: "0 12px", borderBottom: "1px solid var(--divider)", fontSize: 12.5 }}>
                <span className="mono truncate" style={{ flex: 1 }}>{t.fo} <span style={{ color: "var(--accent)" }}>{AVO_DIR[t.direction]}</span> <span className="dim">{t.dv}</span></span>
                <span className={"fl-badge " + (AVO_COLOR[t.state] || "neutral")}><span className="d"/>{t.state}</span>
              </div>
            ))}
          </div>
        </div>
        <div style={{ padding: "14px 22px", background: "var(--mica)", borderTop: "1px solid var(--divider)", display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <button className="fl-btn" onClick={onCancel}>Cancel</button>
          <button className={"fl-btn " + (danger ? "danger-accent" : "accent")} onClick={onConfirm}><Icon name={action.icon} size={14}/> {action.label}</button>
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { AvOps, AVO_DIR, AVO_COLOR });
