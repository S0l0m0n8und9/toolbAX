/* Dual-Write Map Browser (Fluent) — READ-ONLY inspector. Master/detail:
   map list → header (state, direction, KPIs, sparkline) → tabs
   (Bindings · Value maps · Runs · Errors). No mutations, no CommandBar —
   that's the Operations tool. This is the §4 detail design referenced in the
   handoff control-map. */

const AVM_COLOR = { running: "ok", paused: "warn", errored: "err", idle: "neutral", stopped: "neutral" };
const AVM_DIR   = { "both": "↔", "fo→dv": "→", "dv→fo": "←" };

const AvMapBrowser = ({ env }) => {
  const maps = window.DW_MAPS;
  const [mapId, setMapId] = React.useState("cust-account");
  const [tab, setTab] = React.useState("bindings");
  const [q, setQ] = React.useState("");
  const map = maps.find(m => m.id === mapId) || maps[0];
  const list = maps.filter(m => !q || m.fo.toLowerCase().includes(q.toLowerCase()) || m.dv.toLowerCase().includes(q.toLowerCase()));

  const spark = React.useMemo(() => {
    const seed = mapId.charCodeAt(0) + mapId.charCodeAt(mapId.length - 1);
    return Array.from({ length: 28 }, (_, i) => 18 + Math.abs(Math.sin(i * 0.6 + seed)) * 80);
  }, [mapId]);
  const tabs = [["bindings", "Bindings"], ["values", "Value maps"], ["runs", "Runs"], ["errors", "Errors"]];

  return (
    <div style={{ height: "100%", display: "grid", gridTemplateColumns: "320px 1fr", overflow: "hidden" }}>
      {/* Master */}
      <div style={{ borderRight: "1px solid var(--stroke)", background: "var(--mica)", display: "flex", flexDirection: "column", minHeight: 0 }}>
        <div style={{ padding: "12px 14px 8px", display: "flex", alignItems: "center", gap: 8 }}>
          <span style={{ fontWeight: 600, fontSize: 14, flex: 1 }}>Table maps <span className="t3" style={{ fontWeight: 400 }}>· {maps.length}</span></span>
        </div>
        <div style={{ padding: "0 12px 8px", position: "relative" }}>
          <span style={{ position: "absolute", left: 20, top: 9, color: "var(--txt-3)" }}><Icon name="search" size={13}/></span>
          <input className="fl-input" value={q} onChange={e => setQ(e.target.value)} placeholder="Filter maps…" style={{ width: "100%", paddingLeft: 30 }}/>
        </div>
        <div style={{ overflow: "auto", flex: 1, padding: "0 6px 8px" }}>
          {list.map(m => (
            <div key={m.id} className={"fl-nav-item " + (m.id === mapId ? "sel" : "")} style={{ height: "auto", padding: "9px 12px", alignItems: "flex-start" }} onClick={() => setMapId(m.id)}>
              <span style={{ marginTop: 4, width: 8, height: 8, borderRadius: "50%", background: `var(--${AVM_COLOR[m.state]})`, flexShrink: 0 }}/>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="mono" style={{ fontSize: 12, color: "var(--txt-0)", display: "flex", alignItems: "center", gap: 5 }}>
                  <span className="truncate" style={{ flex: 1 }}>{m.fo}</span>
                  <span style={{ color: "var(--accent)" }}>{AVM_DIR[m.direction]}</span>
                  <span className="truncate dim" style={{ flex: 1, textAlign: "right" }}>{m.dv}</span>
                </div>
                <div style={{ display: "flex", gap: 7, fontSize: 10.5, marginTop: 3 }} className="mono t3">
                  <span>v{m.version}</span><span>·</span><span className="truncate" style={{ flex: 1 }}>{m.lastRun}</span>
                  {m.errors24h > 0 && <span style={{ color: "var(--err)" }}>{m.errors24h} err</span>}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Detail */}
      <div style={{ display: "flex", flexDirection: "column", minHeight: 0, overflow: "hidden" }}>
        <div style={{ padding: "18px 24px 0" }}>
          <div className="fl-infobar info" style={{ marginBottom: 16 }}>
            <span className="ib-icon"><Icon name="book" size={15}/></span>
            <div>Read-only inspector. To start, stop, pause or sync a map, open <b>Dual-Write Operations</b>.</div>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
            <span className="t3" style={{ fontSize: 11, letterSpacing: "0.08em" }}>DUAL-WRITE MAP</span>
            <span className={"fl-badge " + AVM_COLOR[map.state]}><span className="d"/>{map.state}</span>
            <span className="fl-badge neutral">{map.direction}</span>
            <span className="fl-badge neutral">v{map.version}</span>
          </div>
          <div style={{ display: "flex", alignItems: "flex-end", gap: 20, flexWrap: "wrap" }}>
            <div className="mono" style={{ fontSize: 22, color: "var(--txt-0)", display: "flex", alignItems: "center", gap: 12 }}>
              <span>{map.fo}</span><span style={{ color: "var(--accent)", fontSize: 26 }}>{AVM_DIR[map.direction]}</span><span className="dim">{map.dv}</span>
            </div>
            <div style={{ flex: 1 }}/>
            <AvKpi label="rows 24h" value={map.rows24h.toLocaleString()}/>
            <AvKpi label="errors 24h" value={map.errors24h.toLocaleString()} color={map.errors24h > 0 ? "var(--err)" : "var(--ok)"}/>
            <AvKpi label="latency p95" value="412 ms"/>
            <div style={{ borderLeft: "1px solid var(--divider)", paddingLeft: 18 }}>
              <div className="t3" style={{ fontSize: 10, letterSpacing: "0.06em", marginBottom: 4 }}>ACTIVITY · 24H</div>
              <AvSpark data={spark} w={150} h={30} color={map.errors24h > 0 ? "var(--err)" : "var(--accent)"}/>
            </div>
          </div>
        </div>

        {/* Pivot tabs */}
        <div style={{ display: "flex", gap: 4, padding: "16px 24px 0", borderBottom: "1px solid var(--stroke)" }}>
          {tabs.map(([id, label]) => (
            <button key={id} onClick={() => setTab(id)} style={{ height: 38, padding: "0 12px", background: "transparent", border: 0, borderBottom: "2px solid " + (tab === id ? "var(--accent)" : "transparent"), color: tab === id ? "var(--txt-0)" : "var(--txt-2)", cursor: "pointer", fontFamily: "var(--font-ui)", fontSize: 13.5, fontWeight: tab === id ? 600 : 400, display: "flex", alignItems: "center", gap: 7 }}>
              {label}{id === "errors" && map.errors24h > 0 && <span style={{ color: "var(--err)" }}>●</span>}
            </button>
          ))}
        </div>

        <div style={{ flex: 1, overflow: "auto", minHeight: 0 }}>
          {tab === "bindings" && <AvBindings map={map}/>}
          {tab === "values"   && <AvValueMaps map={map}/>}
          {tab === "runs"     && <AvRuns map={map}/>}
          {tab === "errors"   && <AvErrors map={map}/>}
        </div>
      </div>
    </div>
  );
};

const AvKpi = ({ label, value, color }) => (
  <div style={{ minWidth: 86 }}>
    <div className="t3" style={{ fontSize: 10, letterSpacing: "0.06em", marginBottom: 3 }}>{label.toUpperCase()}</div>
    <div className="mono" style={{ fontSize: 19, color: color || "var(--txt-0)" }}>{value}</div>
  </div>
);

const AvSpark = ({ data, w, h, color }) => {
  const max = Math.max(...data, 1);
  const step = w / (data.length - 1);
  const pts = data.map((v, i) => `${i * step},${h - (v / max) * h}`).join(" ");
  return (
    <svg width={w} height={h} style={{ display: "block" }}>
      <polyline points={pts} fill="none" stroke={color} strokeWidth="1.5" strokeLinejoin="round"/>
      <polyline points={`0,${h} ${pts} ${w},${h}`} fill={color} opacity="0.12"/>
    </svg>
  );
};

const AvBindings = ({ map }) => {
  const b = map.bindings;
  if (!b) return <AvEmpty icon="book" msg="Field bindings for this map aren't cached — open it once to fetch the template."/>;
  return (
    <table className="fl-grid">
      <thead><tr>
        <th style={{ width: 44 }}>#</th>
        <th>{map.fo} <span className="t3">(F&amp;O)</span></th>
        <th style={{ width: 50, textAlign: "center" }}>Flow</th>
        <th>{map.dv} <span className="t3">(Dataverse)</span></th>
        <th>Transform</th>
        <th style={{ width: 150 }}>Flags</th>
      </tr></thead>
      <tbody>
        {b.map((r, i) => (
          <tr key={i} style={{ opacity: r.skip ? 0.5 : 1 }}>
            <td className="mono t3">{String(i + 1).padStart(2, "0")}</td>
            <td className="mono" style={{ color: "var(--txt-0)" }}>{r.key && <span className="fl-badge warn" style={{ height: 17, padding: "0 6px", marginRight: 6 }}>PK</span>}{r.fo}</td>
            <td style={{ textAlign: "center", color: r.skip ? "var(--txt-3)" : "var(--accent)", fontSize: r.skip ? 11 : 15 }}>{r.skip ? "skip" : AVM_DIR[map.direction]}</td>
            <td className="mono">{r.dv}</td>
            <td className="mono" style={{ color: r.transform === "none" ? "var(--txt-3)" : "var(--txt-1)" }}>{r.transform}</td>
            <td>{r.required && <span className="fl-badge neutral" style={{ marginRight: 4 }}>required</span>}{r.key && <span className="fl-badge info">key</span>}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
};

const AvValueMaps = ({ map }) => {
  const vms = map.valueMaps || [];
  if (!vms.length) return <AvEmpty icon="arrow-lr" msg="No value maps defined for this table map."/>;
  const sampleFor = (name) => name === "CUSTGROUP_MAP"
    ? [["10", "Wholesale"], ["20", "Retail"], ["30", "Distribution"], ["40", "Internal"]]
    : [["No", "false"], ["Yes", "true"], ["", "(null)"]];
  return (
    <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
      {vms.map(v => (
        <div key={v.name} className="fl-card">
          <div className="fl-card-h" style={{ background: "var(--mica)" }}>
            <Icon name="arrow-lr" size={14}/><span className="mono" style={{ fontWeight: 600 }}>{v.name}</span>
            <span className="t3 mono" style={{ fontWeight: 400, fontSize: 11 }}>· {v.size} entries</span>
          </div>
          <table className="fl-grid">
            <thead><tr><th>F&amp;O value</th><th style={{ width: 50, textAlign: "center" }}>→</th><th>Dataverse value</th></tr></thead>
            <tbody>
              {sampleFor(v.name).map((row, i) => (
                <tr key={i}><td className="mono">{row[0] || <span className="t3">(empty)</span>}</td><td style={{ textAlign: "center", color: "var(--accent)" }}>→</td><td className="mono">{row[1]}</td></tr>
              ))}
            </tbody>
          </table>
          {v.size > sampleFor(v.name).length && <div className="t3" style={{ padding: "8px 16px", fontSize: 11.5 }}>+ {v.size - sampleFor(v.name).length} more entries</div>}
        </div>
      ))}
    </div>
  );
};

const AVM_RUNS = [
  { ts: "04:21:09", dur: "1.2s", rows: 84, ok: 84, fail: 0, trigger: "scheduled" },
  { ts: "04:16:08", dur: "0.9s", rows: 41, ok: 41, fail: 0, trigger: "scheduled" },
  { ts: "04:11:08", dur: "2.4s", rows: 213, ok: 210, fail: 3, trigger: "scheduled" },
  { ts: "04:06:07", dur: "0.7s", rows: 12, ok: 12, fail: 0, trigger: "scheduled" },
  { ts: "03:58:55", dur: "6.1s", rows: 1402, ok: 1402, fail: 0, trigger: "initial-sync" },
];
const AvRuns = ({ map }) => (
  <table className="fl-grid">
    <thead><tr><th>Time</th><th>Trigger</th><th style={{ textAlign: "right" }}>Rows</th><th style={{ textAlign: "right" }}>OK</th><th style={{ textAlign: "right" }}>Failed</th><th style={{ textAlign: "right" }}>Duration</th><th>Result</th></tr></thead>
    <tbody>
      {AVM_RUNS.map((r, i) => (
        <tr key={i}>
          <td className="mono">{r.ts}</td>
          <td>{r.trigger === "initial-sync" ? <span className="fl-badge info" style={{ height: 18 }}>initial-sync</span> : <span className="t3">scheduled</span>}</td>
          <td className="mono" style={{ textAlign: "right" }}>{r.rows.toLocaleString()}</td>
          <td className="mono" style={{ textAlign: "right", color: "var(--ok)" }}>{r.ok.toLocaleString()}</td>
          <td className="mono" style={{ textAlign: "right", color: r.fail > 0 ? "var(--err)" : "var(--txt-3)" }}>{r.fail || "—"}</td>
          <td className="mono t3" style={{ textAlign: "right" }}>{r.dur}</td>
          <td>{r.fail > 0 ? <span className="fl-badge warn" style={{ height: 18 }}>partial</span> : <span className="fl-badge ok" style={{ height: 18 }}><span className="d"/>ok</span>}</td>
        </tr>
      ))}
    </tbody>
  </table>
);

const AVM_ERRORS = [
  { ts: "04:11:09", code: "0x80040237", key: "US-014", field: "CustomerGroupId", msg: "Value 'EXPORT' not found in value map CUSTGROUP_MAP." },
  { ts: "04:11:09", code: "0x80040237", key: "US-031", field: "CustomerGroupId", msg: "Value 'EXPORT' not found in value map CUSTGROUP_MAP." },
  { ts: "04:11:08", code: "0x80048408", key: "US-009", field: "PrimaryContactEmail", msg: "Duplicate detected: emailaddress1 already exists on account." },
];
const AvErrors = ({ map }) => {
  if (!map.errors24h) return <AvEmpty icon="check" msg="No errors in the last 24 hours."/>;
  return (
    <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 8 }}>
      <div className="fl-infobar err" style={{ marginBottom: 6 }}>
        <span className="ib-icon"><Icon name="alert" size={15}/></span>
        <div><b>{map.errors24h} errors</b> in the last 24h. Most are value-map gaps on <span className="mono">CustomerGroupId</span>.</div>
      </div>
      {AVM_ERRORS.map((e, i) => (
        <div key={i} className="fl-card" style={{ padding: "10px 14px", display: "flex", gap: 12, alignItems: "flex-start" }}>
          <span style={{ color: "var(--err)", marginTop: 1 }}><Icon name="alert" size={15}/></span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13, color: "var(--txt-0)" }}>{e.msg}</div>
            <div className="mono t3" style={{ fontSize: 11, marginTop: 3, display: "flex", gap: 12, flexWrap: "wrap" }}>
              <span>{e.ts}</span><span style={{ color: "var(--err)" }}>{e.code}</span><span>key {e.key}</span><span>field {e.field}</span>
            </div>
          </div>
          <button className="fl-btn sm subtle">Retry</button>
        </div>
      ))}
    </div>
  );
};

Object.assign(window, { AvMapBrowser });
