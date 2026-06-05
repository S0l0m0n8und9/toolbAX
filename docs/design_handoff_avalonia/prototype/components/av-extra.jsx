/* Remaining tools in Fluent: Dual-Write Compare, Query Builder, Metadata, POST. */

/* ---- Dual-Write Compare -------------------------------------------------- */
const AVC_TARGET = {
  "cust-account": { state: "running", v: "1.0.0.12", rows: 13980 },
  "vend-account": { state: "running", v: "1.0.0.8",  rows: 4760 },
  "prod-product": { state: "running", v: "1.0.0.19", rows: 240 },
  "so-header":    { state: "running", v: "1.0.0.15", rows: 590 },
  "so-line":      { state: "paused",  v: "1.0.0.14", rows: 2050 },
  "po-header":    null,
  "coa":          { state: "running", v: "1.0.0.3",  rows: 12 },
  "exch-rate":    { state: "idle",    v: "1.0.0.2",  rows: 0 },
};
const avcDiff = (s, t) => {
  if (!t) return { kind: "err", label: "only in source" };
  if (s == null) return { kind: "info", label: "only in target" };
  if (s.tmplVersion !== t.v) return { kind: "warn", label: "version drift" };
  if (s.state !== t.state) return { kind: "warn", label: "state differs" };
  if (Math.abs(s.rows24h - t.rows) > 200) return { kind: "info", label: "row delta" };
  return { kind: "ok", label: "in sync" };
};

const AvCompare = ({ env }) => {
  const [src, setSrc] = React.useState("prd-apac");
  const [tgt, setTgt] = React.useState("uat-eur");
  const srcEnv = window.ENVS.find(e => e.id === src);
  const tgtEnv = window.ENVS.find(e => e.id === tgt);
  const rows = React.useMemo(() => window.DW_OPS_MAPS.map(m => {
    const t = AVC_TARGET[m.tid];
    return { tid: m.tid, fo: m.fo, dv: m.dv, direction: m.direction, s: { state: m.state, v: m.tmplVersion, rows: m.rows24h }, t: t ? { state: t.state, v: t.v, rows: t.rows } : null, diff: avcDiff(m, t) };
  }), []);
  const counts = rows.reduce((a, r) => { a[r.diff.label] = (a[r.diff.label] || 0) + 1; return a; }, {});
  const Pick = ({ value, onChange, label }) => (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <span className="t3" style={{ fontSize: 10.5, letterSpacing: "0.06em" }}>{label}</span>
      <select className="fl-combo mono" value={value} onChange={e => onChange(e.target.value)} style={{ width: 220 }}>
        {window.ENVS.map(e => <option key={e.id} value={e.id}>{e.legal} · {e.name}</option>)}
      </select>
    </div>
  );
  return (
    <div style={{ height: "100%", display: "flex", flexDirection: "column", minHeight: 0 }}>
      <div style={{ padding: "16px 24px 14px" }}>
        <h1 style={{ margin: "0 0 14px", fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 24 }}>Dual-Write Compare</h1>
        <div style={{ display: "flex", alignItems: "flex-end", gap: 16 }}>
          <Pick value={src} onChange={setSrc} label="SOURCE"/>
          <span style={{ color: "var(--accent)", fontSize: 22, paddingBottom: 2 }}>→</span>
          <Pick value={tgt} onChange={setTgt} label="TARGET"/>
          <button className="fl-btn accent" disabled={src === tgt}><Icon name="arrow-lr" size={14}/> Compare</button>
          <div style={{ flex: 1 }}/>
          <span className="t3" style={{ fontSize: 12.5 }}>{rows.length} maps · shared Data Integrator credential</span>
        </div>
      </div>
      {src === tgt ? (
        <AvEmpty icon="arrow-lr" msg="Pick two different environments to compare."/>
      ) : (
        <>
          <div style={{ display: "flex", gap: 8, padding: "0 24px 12px", flexWrap: "wrap" }}>
            {[["in sync", "ok"], ["version drift", "warn"], ["state differs", "warn"], ["row delta", "info"], ["only in source", "err"]].map(([k, kind]) =>
              counts[k] ? <span key={k} className={"fl-badge " + kind}><span className="d"/>{counts[k]} {k}</span> : null)}
          </div>
          <div style={{ flex: 1, overflow: "auto", minHeight: 0, borderTop: "1px solid var(--stroke)" }}>
            <table className="fl-grid">
              <thead>
                <tr>
                  <th>Table map</th>
                  <th colSpan="3" style={{ borderLeft: "1px solid var(--stroke)" }}>{srcEnv.legal} <span className="t3">source</span></th>
                  <th colSpan="3" style={{ borderLeft: "1px solid var(--stroke)" }}>{tgtEnv.legal} <span className="t3">target</span></th>
                  <th style={{ borderLeft: "1px solid var(--stroke)" }}>Diff</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.tid}>
                    <td style={{ color: "var(--txt-0)", whiteSpace: "nowrap" }}><span className="mono">{r.fo}</span> <span style={{ color: "var(--accent)" }}>{AVO_DIR[r.direction]}</span> <span className="mono dim">{r.dv}</span></td>
                    <AvCmpCells cell={r.s}/>
                    <AvCmpCells cell={r.t} drift={r.s && r.t && r.s.v !== r.t.v}/>
                    <td style={{ borderLeft: "1px solid var(--divider)" }}><span className={"fl-badge " + r.diff.kind}><span className="d"/>{r.diff.label}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
};
const AvCmpCells = ({ cell, drift }) => cell
  ? <>
      <td style={{ borderLeft: "1px solid var(--divider)" }}><span className={"fl-badge " + (AVO_COLOR[cell.state] || "neutral")}><span className="d"/>{cell.state}</span></td>
      <td className="mono" style={{ color: drift ? "var(--warn)" : "var(--txt-1)" }}>v{cell.v}</td>
      <td className="mono" style={{ textAlign: "right" }}>{cell.rows.toLocaleString()}</td>
    </>
  : <td colSpan="3" className="t3" style={{ borderLeft: "1px solid var(--divider)", fontStyle: "italic" }}>—  absent</td>;

/* ---- Query Builder ------------------------------------------------------- */
const QB_COLS = ["dataAreaId","CustomerAccount","OrganizationName","CustomerGroupId","CurrencyCode","PaymentTermsName","CreditLimit","BlockedForInvoice","PrimaryContactEmail"];
const AvQuery = ({ env }) => {
  const ent = window.ENTITIES || [];
  const [selEnt, setSelEnt] = React.useState("CustomersV3");
  const fields = window.FIELDS[selEnt] || [];
  const meta = ent.find(e => e.name === selEnt);
  const [picked, setPicked] = React.useState(() => new Set(["CustomerAccount", "OrganizationName", "CurrencyCode", "PaymentTermsName"]));
  const rows = window.SAMPLE_ROWS || [];
  const cols = QB_COLS.filter(c => picked.has(c));
  const url = `GET /data/${selEnt}?$select=${cols.join(",") || "*"}&$top=50&cross-company=true`;
  return (
    <div style={{ height: "100%", display: "grid", gridTemplateColumns: "260px 1fr", overflow: "hidden" }}>
      <div style={{ borderRight: "1px solid var(--stroke)", background: "var(--mica)", display: "flex", flexDirection: "column", minHeight: 0 }}>
        <div style={{ padding: "12px 14px 8px", fontWeight: 600, fontSize: 14 }}>Entities <span className="t3" style={{ fontWeight: 400 }}>· {ent.length}</span></div>
        <div style={{ overflow: "auto", flex: 1, padding: "0 6px 8px" }}>
          {ent.map(e => (
            <div key={e.name} className={"fl-nav-item " + (e.name === selEnt ? "sel" : "")} style={{ height: 34 }} onClick={() => setSelEnt(e.name)}>
              <Icon name="database" size={14}/><span className="truncate mono" style={{ flex: 1, fontSize: 12.5 }}>{e.name}</span>
              <span className="t3" style={{ fontSize: 10.5 }}>{e.fields}</span>
            </div>
          ))}
        </div>
      </div>
      <div style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
        <div style={{ padding: "16px 24px 12px" }}>
          <div style={{ display: "flex", alignItems: "baseline", gap: 10, marginBottom: 12 }}>
            <h1 style={{ margin: 0, fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 24 }} className="mono">{selEnt}</h1>
            {meta?.company && <span className="fl-badge neutral">company-aware</span>}
            <span className="t3 mono" style={{ fontSize: 11 }}>pk: {meta?.pk}</span>
          </div>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <div className="fl-input mono" style={{ flex: 1, display: "flex", alignItems: "center", overflow: "hidden", color: "var(--txt-1)" }}>{url}</div>
            <button className="fl-btn accent"><Icon name="play" size={13}/> Run</button>
            <button className="fl-btn"><Icon name="download" size={13}/> CSV</button>
          </div>
          {fields.length ? (
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 12 }}>
              {fields.map(f => {
                const on = picked.has(f.name);
                return <button key={f.name} className="fl-badge" onClick={() => setPicked(p => { const n = new Set(p); n.has(f.name) ? n.delete(f.name) : n.add(f.name); return n; })} style={{ cursor: "pointer", border: on ? "1px solid var(--accent)" : "1px solid var(--stroke)", background: on ? "var(--accent-tint)" : "var(--layer-2)", color: on ? "var(--accent)" : "var(--txt-2)" }}>{on && <Icon name="check" size={11}/>}{f.pk && <span style={{ color: "var(--warn)", fontSize: 9 }}>PK</span>}{f.name}</button>;
              })}
            </div>
          ) : <div className="t3" style={{ marginTop: 12, fontSize: 12.5 }}>$metadata for this entity not cached — run once to populate field list.</div>}
        </div>
        <div style={{ flex: 1, overflow: "auto", borderTop: "1px solid var(--stroke)", minHeight: 0 }}>
          {fields.length ? (
            <table className="fl-grid">
              <thead><tr>{cols.map(c => <th key={c} className="mono" style={{ textAlign: c === "CreditLimit" ? "right" : "left" }}>{c}</th>)}</tr></thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={i}>{cols.map((c, j) => {
                    const idx = QB_COLS.indexOf(c);
                    return <td key={c} className="mono" style={{ fontSize: 12, textAlign: c === "CreditLimit" ? "right" : "left", color: j === 0 ? "var(--txt-0)" : "var(--txt-1)" }}>{r[idx] ?? "—"}</td>;
                  })}</tr>
                ))}
              </tbody>
            </table>
          ) : <AvEmpty icon="database" msg="No cached preview for this entity."/>}
        </div>
        <div style={{ height: 28, flexShrink: 0, borderTop: "1px solid var(--stroke)", background: "var(--mica)", display: "flex", alignItems: "center", padding: "0 16px", gap: 12, fontSize: 11.5 }}>
          <span className="t3">{fields.length ? rows.length : 0} rows · 312 ms</span>
          <span className="fl-badge ok" style={{ height: 18 }}><span className="d"/>200 OK</span>
        </div>
      </div>
    </div>
  );
};

/* ---- Metadata browser ---------------------------------------------------- */
const AvMetadata = ({ env }) => {
  const ent = window.ENTITIES || [];
  const [sel, setSel] = React.useState("CustomersV3");
  const meta = ent.find(e => e.name === sel) || ent[0];
  const fields = window.FIELDS[sel] || [];
  const typeShort = (f) => f.type === "Enum" ? `Enum<${f.enumT}>` : f.type === "String" ? `String(${f.len})` : f.type === "Decimal" ? "Decimal" : f.type;
  return (
    <div style={{ height: "100%", display: "grid", gridTemplateColumns: "300px 1fr", overflow: "hidden" }}>
      <div style={{ borderRight: "1px solid var(--stroke)", background: "var(--mica)", display: "flex", flexDirection: "column", minHeight: 0 }}>
        <div style={{ padding: "12px 14px 8px", fontWeight: 600, fontSize: 14 }}>Entity sets <span className="t3" style={{ fontWeight: 400 }}>· {ent.length}</span></div>
        <div style={{ overflow: "auto", flex: 1, padding: "0 6px 8px" }}>
          {ent.map(e => (
            <div key={e.name} className={"fl-nav-item " + (e.name === sel ? "sel" : "")} style={{ height: 38 }} onClick={() => setSel(e.name)}>
              <Icon name="book" size={14}/>
              <div style={{ flex: 1, minWidth: 0 }}><div className="truncate mono" style={{ fontSize: 12.5 }}>{e.name}</div><div className="t3" style={{ fontSize: 10.5 }}>{e.fields} props · {e.module}</div></div>
            </div>
          ))}
        </div>
      </div>
      <div style={{ overflow: "auto", padding: "18px 28px" }}>
        <div className="t3" style={{ fontSize: 11, letterSpacing: "0.08em" }}>ENTITY · {meta?.module}</div>
        <div style={{ display: "flex", alignItems: "baseline", gap: 12 }}>
          <h1 style={{ margin: "2px 0 4px", fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 24 }} className="mono">{sel}</h1>
          {meta?.company && <span className="fl-badge neutral">company-aware</span>}
        </div>
        <div className="t3 mono" style={{ fontSize: 11.5, marginBottom: 16 }}>pk: {meta?.pk} · {meta?.fields} properties</div>
        {fields.length ? (
          <div className="fl-card">
            <table className="fl-grid">
              <thead><tr><th>Property</th><th>Type</th><th>Key</th><th>Nullable</th></tr></thead>
              <tbody>
                {fields.map(f => (
                  <tr key={f.name}>
                    <td className="mono" style={{ color: "var(--txt-0)" }}>{f.name}</td>
                    <td className="mono dim">{typeShort(f)}</td>
                    <td>{f.pk ? <span className="fl-badge ok" style={{ height: 18 }}>key</span> : ""}</td>
                    <td className="t3">{f.nullable === false ? "no" : "yes"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <div className="fl-infobar info" style={{ maxWidth: 560 }}><span className="ib-icon"><Icon name="alert" size={16}/></span><div>Field metadata for <span className="mono">{sel}</span> isn't cached yet. Open it in Query Builder to fetch <span className="mono">$metadata</span>.</div></div>}
      </div>
    </div>
  );
};

/* ---- POST builder -------------------------------------------------------- */
const AvPost = ({ env }) => {
  const [method, setMethod] = React.useState("POST");
  const body = `{\n  "CustomerAccount": "US-099",\n  "OrganizationName": "Northwind Traders",\n  "CustomerGroupId": "30",\n  "SalesCurrencyCode": "USD",\n  "PaymentTerms": "Net30"\n}`;
  return (
    <div style={{ height: "100%", display: "flex", flexDirection: "column", minHeight: 0 }}>
      <div style={{ padding: "16px 24px 14px" }}>
        <h1 style={{ margin: "0 0 14px", fontFamily: "var(--font-disp)", fontWeight: 600, fontSize: 24 }}>OData POST Builder</h1>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <select className="fl-combo" value={method} onChange={e => setMethod(e.target.value)} style={{ width: 100 }}><option>POST</option><option>PATCH</option><option>DELETE</option></select>
          <input className="fl-input mono" defaultValue="/data/CustomersV3?cross-company=true" style={{ flex: 1 }}/>
          <button className="fl-btn accent"><Icon name="play" size={13}/> Send</button>
        </div>
      </div>
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "1fr 1fr", minHeight: 0, borderTop: "1px solid var(--stroke)" }}>
        <div style={{ borderRight: "1px solid var(--stroke)", display: "flex", flexDirection: "column", minHeight: 0 }}>
          <div style={{ padding: "8px 16px", fontSize: 12.5, color: "var(--txt-2)", borderBottom: "1px solid var(--divider)", background: "var(--mica)" }}>Request body</div>
          <textarea defaultValue={body} spellCheck={false} className="mono" style={{ flex: 1, resize: "none", background: "var(--app-bg)", border: 0, outline: 0, color: "var(--txt-1)", padding: "14px 16px", fontSize: 13, lineHeight: 1.6 }}/>
        </div>
        <div style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
          <div style={{ padding: "8px 16px", fontSize: 12.5, color: "var(--txt-2)", borderBottom: "1px solid var(--divider)", background: "var(--mica)", display: "flex", alignItems: "center", gap: 8 }}>Response <span className="fl-badge ok" style={{ height: 18 }}><span className="d"/>201 Created · 188 ms</span></div>
          <pre className="mono" style={{ flex: 1, margin: 0, overflow: "auto", background: "var(--app-bg)", color: "var(--txt-1)", padding: "14px 16px", fontSize: 12.5, lineHeight: 1.6 }}>{`{\n  "@odata.context": "…/$metadata#CustomersV3/$entity",\n  "CustomerAccount": "US-099",\n  "OrganizationName": "Northwind Traders",\n  "RecId": 5637148921,\n  "dataAreaId": "usmf"\n}`}</pre>
        </div>
      </div>
    </div>
  );
};

const AvEmpty = ({ icon, msg }) => (
  <div style={{ flex: 1, display: "grid", placeItems: "center", color: "var(--txt-3)" }}>
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 12 }}><Icon name={icon} size={32}/><span style={{ fontSize: 13.5 }}>{msg}</span></div>
  </div>
);

Object.assign(window, { AvCompare, AvQuery, AvMetadata, AvPost, AvEmpty });
