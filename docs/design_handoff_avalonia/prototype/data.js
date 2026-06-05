// Fake F&O world: environments, entities, dual-write maps.
// Shaped to feel authentic to implementers (tableId, legal entity, cross-company, etc.)

window.ENVS = [
  { id: "dev-usmf", name: "USMF Dev",      url: "contoso-dev.operations.dynamics.com",   tenant: "contoso.onmicrosoft.com", legal: "USMF", tier: "Tier 1", status: "connected",   latency: 118 },
  { id: "uat-eur",  name: "EMEA UAT",      url: "contoso-uat.operations.dynamics.com",   tenant: "contoso.onmicrosoft.com", legal: "DEMF", tier: "Tier 2", status: "connected",   latency: 184 },
  { id: "prd-apac", name: "APAC Prod",     url: "contoso.operations.dynamics.com",       tenant: "contoso.onmicrosoft.com", legal: "AUMF", tier: "Prod",   status: "token-expired", latency: null },
  { id: "sbx-fin",  name: "Finance Sbx",   url: "contoso-fin.operations.dynamics.com",   tenant: "contoso.onmicrosoft.com", legal: "USMF", tier: "Sandbox",status: "disconnected", latency: null },
];

window.ENTITIES = [
  { name: "CustomersV3",            module: "AR",  fields: 87,  pk: "dataAreaId,CustomerAccount", company: true,  tag: "common" },
  { name: "VendorsV2",              module: "AP",  fields: 102, pk: "dataAreaId,VendorAccount",   company: true,  tag: "common" },
  { name: "ReleasedProductsV2",     module: "IC",  fields: 143, pk: "dataAreaId,ItemNumber",      company: true,  tag: "common" },
  { name: "SalesOrderHeadersV2",    module: "SO",  fields: 96,  pk: "dataAreaId,SalesOrderNumber",company: true,  tag: "transactional" },
  { name: "SalesOrderLinesV2",      module: "SO",  fields: 124, pk: "dataAreaId,SalesOrderNumber,LineNum", company: true, tag: "transactional" },
  { name: "PurchaseOrderHeadersV2", module: "PO",  fields: 78,  pk: "dataAreaId,PurchaseOrderNumber", company: true, tag: "transactional" },
  { name: "LedgerJournalHeaders",   module: "GL",  fields: 41,  pk: "dataAreaId,JournalNumber",   company: true,  tag: "finance" },
  { name: "ChartOfAccounts",        module: "GL",  fields: 28,  pk: "LedgerChartOfAccounts",      company: false, tag: "finance" },
  { name: "FinancialDimensions",    module: "GL",  fields: 19,  pk: "FinancialDimension",         company: false, tag: "finance" },
  { name: "InventOnHandV2",         module: "IC",  fields: 52,  pk: "dataAreaId,ItemNumber,Site", company: true,  tag: "inventory" },
  { name: "WorkerV2",               module: "HR",  fields: 64,  pk: "PersonnelNumber",            company: false, tag: "hr" },
  { name: "LegalEntities",          module: "SYS", fields: 22,  pk: "LegalEntityId",              company: false, tag: "system" },
  { name: "ExchangeRates",          module: "GL",  fields: 11,  pk: "FromCurrency,ToCurrency,ValidFrom", company: false, tag: "finance" },
  { name: "TaxGroupHeadings",       module: "TAX", fields: 15,  pk: "dataAreaId,TaxGroup",        company: true,  tag: "finance" },
  { name: "Warehouses",             module: "WMS", fields: 32,  pk: "dataAreaId,SiteId,WarehouseId", company: true, tag: "inventory" },
];

window.FIELDS = {
  CustomersV3: [
    { name: "dataAreaId",        type: "String",   len: 4,   nullable: false, pk: true },
    { name: "CustomerAccount",   type: "String",   len: 20,  nullable: false, pk: true },
    { name: "OrganizationName",  type: "String",   len: 100, nullable: true  },
    { name: "CustomerGroupId",   type: "String",   len: 10,  nullable: true  },
    { name: "CurrencyCode",      type: "String",   len: 3,   nullable: false },
    { name: "PaymentTermsName",  type: "String",   len: 10,  nullable: true  },
    { name: "CreditLimit",       type: "Decimal",  prec: 32, nullable: true  },
    { name: "IsOneTime",         type: "Enum",     enumT: "NoYes", nullable: false },
    { name: "CreatedDateTime",   type: "DateTime", nullable: false },
    { name: "ModifiedDateTime",  type: "DateTime", nullable: false },
    { name: "BlockedForInvoice", type: "Enum",     enumT: "CustVendorBlocked" },
    { name: "AddressCountry",    type: "String",   len: 2   },
    { name: "PrimaryContactEmail", type: "String", len: 80  },
    { name: "SalesDistrictId",   type: "String",   len: 10  },
    { name: "SalespersonId",     type: "String",   len: 20  },
  ],
};

window.OPERATORS = [
  { op: "eq", label: "=",          hint: "equals" },
  { op: "ne", label: "≠",          hint: "not equals" },
  { op: "gt", label: ">",          hint: "greater than" },
  { op: "ge", label: "≥",          hint: "greater or equal" },
  { op: "lt", label: "<",          hint: "less than" },
  { op: "le", label: "≤",          hint: "less or equal" },
  { op: "startswith", label: "^=", hint: "starts with" },
  { op: "endswith",   label: "$=", hint: "ends with" },
  { op: "contains",   label: "~",  hint: "contains (wildcard)" },
];

window.ENUMS = {
  NoYes: ["No", "Yes"],
  CustVendorBlocked: ["No", "Invoice", "All"],
};

window.PLUGINS = [
  { id: "query",     name: "Query Builder",          cat: "Data",        version: "1.4.2", signed: true,  desc: "Compose $select / $filter / $expand against OData with live preview and CSV export.",      shortcut: "Q", hot: true },
  { id: "dwops",     name: "Dual-Write Operations",  cat: "Integration", version: "0.3.0", signed: true,  desc: "Drive the Dual-Write Management gateway: start, stop, pause, resume and initial-sync maps with live status.", shortcut: "O", hot: true, live: true },
  { id: "dualwrite", name: "Dual-Write Map Browser", cat: "Integration", version: "0.9.1", signed: true,  desc: "Inspect F&O ↔ Dataverse entity maps, field bindings, value maps, and sync state.",          shortcut: "D", hot: true },
  { id: "dwcompare", name: "Dual-Write Compare",     cat: "Integration", version: "0.2.0", signed: true,  desc: "Diff dual-write maps and row counts across two environments.",                              shortcut: "C" },
  { id: "metadata",  name: "Table/Entity Browser",   cat: "Data",        version: "1.2.0", signed: true,  desc: "Explore $metadata: entity sets, navigation properties, enums, keys.",                      shortcut: "M" },
  { id: "postbuilder", name: "OData POST Builder",   cat: "Data",        version: "0.7.3", signed: true,  desc: "Hand-craft and replay POST/PATCH requests with body validation.",                          shortcut: "P" },
  { id: "profiles",  name: "Profiles",               cat: "System",      version: "1.0.0", signed: true,  desc: "Environments, service principals, interactive sign-in and DPAPI-encrypted secrets.",       shortcut: "E", builtin: true },
  { id: "hello",     name: "Hello Plugin",           cat: "Samples",     version: "0.1.0", signed: false, desc: "SDK sample: minimal plugin showing lifecycle, logging, and capability injection.",         shortcut: "H" },
];

// Dual-Write maps — the core of our audience's day
window.DW_MAPS = [
  {
    id: "cust-account",
    fo: "CustomersV3",
    dv: "account",
    version: "1.0.0.12",
    direction: "both",
    state: "running",
    rows24h: 14218,
    errors24h: 3,
    lastRun: "2m ago",
    bindings: [
      { fo: "CustomerAccount",   dv: "accountnumber",   transform: "none",    required: true,  key: true  },
      { fo: "OrganizationName",  dv: "name",            transform: "none",    required: true               },
      { fo: "CurrencyCode",      dv: "transactioncurrencyid", transform: "lookup → currency", required: true },
      { fo: "CustomerGroupId",   dv: "cdm_customergroup", transform: "value map · CUSTGROUP_MAP" },
      { fo: "PrimaryContactEmail", dv: "emailaddress1", transform: "none"    },
      { fo: "CreditLimit",       dv: "creditlimit",     transform: "none"    },
      { fo: "BlockedForInvoice", dv: "cdm_blocked",     transform: "enum map · BLOCKED_ENUM" },
      { fo: "IsOneTime",         dv: "cdm_isonetime",   transform: "NoYes → bool" },
      { fo: "ModifiedDateTime",  dv: "modifiedon",      transform: "none",    skip: true },
    ],
    valueMaps: [
      { name: "CUSTGROUP_MAP", size: 14 },
      { name: "BLOCKED_ENUM", size: 3 },
    ],
  },
  {
    id: "vend-account",
    fo: "VendorsV2",
    dv: "msdyn_vendor",
    version: "1.0.0.8",
    direction: "fo→dv",
    state: "running",
    rows24h: 4820,
    errors24h: 0,
    lastRun: "4m ago",
  },
  {
    id: "prod-product",
    fo: "ReleasedProductsV2",
    dv: "product",
    version: "1.0.0.21",
    direction: "both",
    state: "paused",
    rows24h: 0,
    errors24h: 0,
    lastRun: "1h ago",
  },
  {
    id: "so-salesorder",
    fo: "SalesOrderHeadersV2",
    dv: "salesorder",
    version: "1.0.0.15",
    direction: "dv→fo",
    state: "errored",
    rows24h: 612,
    errors24h: 41,
    lastRun: "just now",
  },
  {
    id: "soline-salesorderdetail",
    fo: "SalesOrderLinesV2",
    dv: "salesorderdetail",
    version: "1.0.0.15",
    direction: "dv→fo",
    state: "errored",
    rows24h: 2211,
    errors24h: 118,
    lastRun: "just now",
  },
  {
    id: "po-purchaseorder",
    fo: "PurchaseOrderHeadersV2",
    dv: "msdyn_purchaseorder",
    version: "1.0.0.6",
    direction: "both",
    state: "running",
    rows24h: 188,
    errors24h: 0,
    lastRun: "11m ago",
  },
  {
    id: "ledger-chartofaccounts",
    fo: "ChartOfAccounts",
    dv: "msdyn_coa",
    version: "1.0.0.3",
    direction: "fo→dv",
    state: "idle",
    rows24h: 0,
    errors24h: 0,
    lastRun: "3d ago",
  },
];

// Sample rows for Customers preview (realistic-ish)
window.SAMPLE_ROWS = [
  ["USMF","US-001","Contoso Retail",                "10","USD","Net30", 50000,"No","Matthew.Hink@contoso.com"],
  ["USMF","US-002","Forest Wholesales",             "20","USD","Net30",250000,"No","orders@forest.example"],
  ["USMF","US-003","Sparrow & Finch",               "10","USD","Net15",  5000,"No","ap@sparrow.example"],
  ["USMF","US-004","Cedar Mountain Outfitters",     "20","USD","Net30",100000,"No","billing@cedar.example"],
  ["USMF","US-005","Harvest Moon Foods",            "30","USD","Net45", 75000,"No","accounting@harvest.example"],
  ["USMF","US-006","Northwind Traders",             "10","USD","Net30",125000,"No","ap@northwind.example"],
  ["USMF","US-007","Atlas Hardware",                "20","USD","Net15", 25000,"Yes","admin@atlas.example"],
  ["USMF","US-008","Riverbend Manufacturing",       "40","USD","Net60",500000,"No","ap@riverbend.example"],
  ["USMF","US-009","Brightwater Utilities",         "50","USD","Net30",750000,"No","finance@bw.example"],
  ["USMF","US-010","Copperleaf Logistics",          "20","USD","Net30", 60000,"No","ap@copperleaf.example"],
  ["USMF","US-011","Driftwood Surf Co.",            "10","USD","Net30", 12000,"No","paul@driftwood.example"],
  ["USMF","US-012","Elm Street Bakery",             "10","USD","Net15",  8000,"No","hello@elm.example"],
  ["USMF","US-013","Fernwood Paper Goods",          "20","USD","Net30", 40000,"No","ap@fernwood.example"],
  ["USMF","US-014","Granite Peak Consulting",       "60","USD","Net30",200000,"No","billing@granite.example"],
  ["USMF","US-015","Hawthorne & Hollow",            "10","USD","Net30", 15000,"Yes","ap@hawthorne.example"],
  ["USMF","US-016","Ironbark Industrial",           "40","USD","Net60",350000,"No","finance@ironbark.example"],
  ["USMF","US-017","Juniper Systems",               "60","USD","Net30",180000,"No","ap@juniper.example"],
  ["USMF","US-018","Kingfisher Air Services",       "50","USD","Net30",900000,"No","ap@kingfisher.example"],
  ["USMF","US-019","Larkspur Media",                "60","USD","Net15", 22000,"No","accounts@larkspur.example"],
  ["USMF","US-020","Mossrock Granite",              "40","USD","Net60",450000,"No","billing@mossrock.example"],
];

// --- Dual-Write Operations (gateway) ----------------------------------------
// The Operations plugin drives the Dual-Write Management gateway. Distinct data
// source from the read-only Map Browser: resolved environment (cid/cname) +
// gateway-reported maps carrying the active template version & author.

window.DW_GATEWAY = {
  identifier: "contoso.operations.dynamics.com",   // F&O env identifier (from active profile)
  region: "australiaeast",
  globalHost: "projectmanagementservice.us-il101.gateway.prod.island.powerapps.com",
  host: "projectmanagementservice.au-il102.gateway.prod.island.powerapps.com", // resolved regional host
  cid: "0e7b1f44-3c2a-4d9e-9f01-2b6a8c5d7e10",
  cname: "Contoso (AUMF · APAC Prod)",
  client: "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b",   // Data Integrator first-party app
  auth: { mode: "interactive", account: "ops.svc@contoso.com", expires: "47m" },
};

// Action codes per gateway: 1=start, 4=stop, 5=pause, 6=resume, 8=initial-sync
window.DW_ACTIONS = [
  { id: "start",   code: 1, label: "Start",        icon: "play",    needs: ["stopped", "idle"],   mutating: true,  verb: "starting" },
  { id: "stop",    code: 4, label: "Stop",         icon: "stop",    needs: ["running", "paused"], mutating: true,  verb: "stopping", danger: true },
  { id: "pause",   code: 5, label: "Pause",        icon: "pause",   needs: ["running"],           mutating: true,  verb: "pausing" },
  { id: "resume",  code: 6, label: "Resume",       icon: "play",    needs: ["paused"],            mutating: true,  verb: "resuming" },
  { id: "initial", code: 8, label: "Initial sync", icon: "refresh", needs: ["running", "stopped", "idle", "paused"], mutating: true, verb: "initial-syncing", danger: true },
];

window.DW_OPS_MAPS = [
  { tid: "cust-account",   name: "Customers V3",          fo: "CustomersV3",            dv: "account",            direction: "both",  state: "running", tmplVersion: "1.0.0.12", author: "Microsoft",  rows24h: 14218, errors24h: 3 },
  { tid: "vend-account",   name: "Vendors V2",            fo: "VendorsV2",              dv: "msdyn_vendor",       direction: "fo→dv", state: "running", tmplVersion: "1.0.0.8",  author: "Microsoft",  rows24h: 4820,  errors24h: 0 },
  { tid: "prod-product",   name: "Released products V2",  fo: "ReleasedProductsV2",     dv: "product",            direction: "both",  state: "paused",  tmplVersion: "1.0.0.21", author: "contoso.it", rows24h: 0,     errors24h: 0 },
  { tid: "so-header",      name: "Sales order headers",   fo: "SalesOrderHeadersV2",    dv: "salesorder",         direction: "dv→fo", state: "running", tmplVersion: "1.0.0.15", author: "contoso.it", rows24h: 612,   errors24h: 41 },
  { tid: "so-line",        name: "Sales order lines",     fo: "SalesOrderLinesV2",      dv: "salesorderdetail",   direction: "dv→fo", state: "running", tmplVersion: "1.0.0.15", author: "contoso.it", rows24h: 2211,  errors24h: 118 },
  { tid: "po-header",      name: "Purchase order headers",fo: "PurchaseOrderHeadersV2", dv: "msdyn_purchaseorder",direction: "both",  state: "running", tmplVersion: "1.0.0.6",  author: "Microsoft",  rows24h: 188,   errors24h: 0 },
  { tid: "coa",            name: "Chart of accounts",     fo: "ChartOfAccounts",        dv: "msdyn_coa",          direction: "fo→dv", state: "stopped", tmplVersion: "1.0.0.3",  author: "Microsoft",  rows24h: 0,     errors24h: 0 },
  { tid: "exch-rate",      name: "Exchange rates",        fo: "ExchangeRates",          dv: "transactioncurrency",direction: "fo→dv", state: "idle",    tmplVersion: "1.0.0.2",  author: "Microsoft",  rows24h: 0,     errors24h: 0 },
];
