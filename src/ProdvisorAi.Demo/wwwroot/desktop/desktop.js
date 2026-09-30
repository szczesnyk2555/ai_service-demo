// PRODVISOR desktop application - simulation.
// Data the desktop owns (elements, drawings, technology prompts, dictionaries, saved technologies) comes from /sim/api.
// The AI service is used ONLY through its public API: /api/v1/technology-jobs.
"use strict";

// Two modes (mode sent to the service):
//   konstruktor - AI reads only material, quantity and dimensions from the drawing -> element card
//   technolog   - full technology: operations, machines, times + material, dimensions, quantity
const state = {
  mode: "technolog",
  elements: [], lookup: { machines: [], materials: [] },
  technologyPrompts: [], promptId: null, promptOpen: false,   // technologist's prompts (company rules), one chosen per generation
  element: null,     // selected element
  card: null,        // technology card being edited (desktop form, machine codes)
  job: null,         // { id, status, error, warnings, model, mock, durationMs, usage }
  saved: null,       // { savedAt, jobId } of the technology stored in PRODVISOR
  dirty: false,
};

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const num = (v) => (v === "" || v == null || isNaN(+v) ? null : +v);
const fmtDate = (d) => (d ? new Date(d).toLocaleString("pl-PL") : "");

async function api(url, opts = {}) {
  const res = await fetch(url, opts);
  const body = res.status === 204 ? null : await res.json().catch(() => null);
  if (!res.ok) throw new Error(body?.error ?? `HTTP ${res.status}`);
  return body;
}

function toast(text) {
  const el = Object.assign(document.createElement("div"), { className: "toast", textContent: text });
  document.body.append(el);
  setTimeout(() => el.remove(), 2600);
}

const fileName = (el) => el.drawingFile.split("/").pop();
const machineByCode = (code) => state.lookup.machines.find((m) => m.code === code);
const machineLabel = (code) => { const m = machineByCode(code); return m ? `${m.code} – ${m.name}` : `${code} (nieznana)`; };

// ---------------------------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------------------------
async function init() {
  const [elements, lookup, prompts] = await Promise.all([
    api("/sim/api/elements"), api("/sim/api/lookup"), api("/sim/api/technology-prompts"),
  ]);
  let last = null, mode = null;
  try { last = localStorage.getItem("technologyPromptId"); mode = localStorage.getItem("mode"); } catch { /* storage unavailable */ }
  const initial = prompts.find((p) => p.id === last) ?? prompts.find((p) => p.id === "cnc") ?? prompts[0];
  Object.assign(state, { elements, lookup, technologyPrompts: prompts, promptId: initial?.id ?? null, mode: mode === "konstruktor" ? "konstruktor" : "technolog" });
  $("#add-element").addEventListener("click", showNewElementForm);
  document.querySelectorAll("#modes a").forEach((a) => a.addEventListener("click", (e) => { e.preventDefault(); setMode(a.dataset.mode); }));
  renderModes();
  renderElements();
  if (elements.length) selectElement(elements[0].id);
  else $("#workspace").innerHTML = '<div class="card muted">Brak elementów – dodaj nowy.</div>';
}

function renderElements() {
  $("#element-list").innerHTML = state.elements.map((e) => `
    <div class="element ${state.element?.id === e.id ? "active" : ""}" data-id="${esc(e.id)}">
      <b>${esc(e.number)}</b>
      <span class="small">${esc(e.name)}</span>
      <div class="small muted">ID ${esc(e.id)} · ${esc(e.drawingFile.split(".").pop().toUpperCase())}
        ${konstruktor()
          ? (e.constructionSavedAt ? '· <span class="badge ok">dane zapisane</span>' : "")
          : (e.savedAt ? '· <span class="badge ok">technologia zapisana</span>' : "")}</div>
    </div>`).join("");
  document.querySelectorAll(".element").forEach((el) => el.addEventListener("click", () => selectElement(el.dataset.id)));
}

// New element: in PRODVISOR the designer creates the element and attaches the drawing
function showNewElementForm() {
  if (state.dirty && !confirm("Niezapisane zmiany w technologii zostaną utracone. Kontynuować?")) return;
  state.element = null; state.card = null; state.job = null; state.dirty = false;
  renderElements();
  $("#workspace").innerHTML = `
    <section class="card" style="max-width:720px">
      <div class="card-head"><h2>Nowy element</h2></div>
      <form id="new-element" style="display:flex;flex-direction:column;gap:12px">
        <div class="grid2">
          <label class="field">Numer rysunku *<input name="number" required placeholder="np. PV-1002"></label>
          <label class="field">Nazwa *<input name="name" required placeholder="np. Tuleja dystansowa"></label>
          <label class="field">Materiał<input name="material" placeholder="np. C45, S355J2, 1.4301"></label>
          <label class="field">Ilość [szt.]<input name="quantity" type="number" min="1" step="1" placeholder="np. 20"></label>
        </div>
        <label class="field">Rysunek * (PNG, JPG, PDF lub DXF)<input name="drawing" type="file" required accept=".png,.jpg,.jpeg,.pdf,.dxf"></label>
        <div id="new-error"></div>
        <div class="row">
          <button class="btn primary" type="submit">Dodaj element</button>
          <button class="btn" type="button" id="new-cancel">Anuluj</button>
        </div>
      </form>
    </section>`;
  $("#new-cancel").addEventListener("click", () => state.elements.length && selectElement(state.elements[0].id));
  $("#new-element").addEventListener("submit", async (e) => {
    e.preventDefault();
    const button = e.target.querySelector("button[type=submit]");
    button.disabled = true;
    try {
      const created = await api("/sim/api/elements", { method: "POST", body: new FormData(e.target) });
      state.elements = await api("/sim/api/elements");
      toast(`Dodano element ${created.number} (element_id ${created.id}).`);
      selectElement(created.id);
    } catch (err) {
      $("#new-error").innerHTML = `<div class="notice err">${esc(err.message)}</div>`;
      button.disabled = false;
    }
  });
}

async function deleteElement() {
  const el = state.element;
  if (!confirm(`Usunąć element ${el.number} – ${el.name}${el.savedAt ? " razem z zapisaną technologią" : ""}?`)) return;
  try {
    await api(`/sim/api/elements/${el.id}`, { method: "DELETE" });
    state.dirty = false;
    state.elements = await api("/sim/api/elements");
    state.element = null;
    if (state.elements.length) selectElement(state.elements[0].id);
    else { renderElements(); $("#workspace").innerHTML = '<div class="card muted">Brak elementów – dodaj nowy.</div>'; }
  } catch (err) { toast(err.message); }
}

const konstruktor = () => state.mode === "konstruktor";

function renderModes() {
  document.querySelectorAll("#modes a").forEach((a) => a.classList.toggle("active", a.dataset.mode === state.mode));
}

function setMode(mode) {
  if (mode === state.mode) return;
  if (state.dirty && !confirm("Niezapisane zmiany zostaną utracone. Kontynuować?")) return;
  state.dirty = false;
  state.mode = mode;
  try { localStorage.setItem("mode", mode); } catch { /* storage unavailable */ }
  renderModes();
  if (state.element) selectElement(state.element.id);
  else renderElements();
}

async function selectElement(id) {
  if (state.dirty && !confirm("Niezapisane zmiany zostaną utracone. Kontynuować?")) return;
  state.element = state.elements.find((e) => e.id === id);
  state.card = null; state.job = null; state.saved = null; state.dirty = false;
  if (konstruktor()) {
    // Data approved earlier by the designer is on the element card
    const e = state.element;
    if (e.constructionSavedAt) {
      state.saved = { savedAt: e.constructionSavedAt };
      state.card = {
        material: { grade: e.material ?? "", material_kind_id: e.materialKindId }, quantity: e.quantity,
        dimensions: normalizeDims({ part: { length_mm: e.lengthMm, diameter_mm: e.diameterMm, width_mm: e.widthMm, height_mm: e.heightMm } }), notes: "",
      };
    }
  } else {
    try {
      const saved = await api(`/sim/api/elements/${id}/technology`);
      state.saved = { savedAt: saved.savedAt, jobId: saved.jobId };
      state.card = fromSaved(saved.technology);
    } catch { /* no technology yet */ }
  }
  renderElements();
  render();
}

// ---------------------------------------------------------------------------------------------
// Technology prompts (company rules): several in the desktop, the technologist picks one per generation
// ---------------------------------------------------------------------------------------------
const selectedPrompt = () => state.technologyPrompts.find((p) => p.id === state.promptId) ?? state.technologyPrompts[0];

async function savePrompt() {
  const p = selectedPrompt();
  if (p?.dirty) {
    await api(`/sim/api/technology-prompts/${p.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: p.name, text: p.text }) });
    p.dirty = false;
  }
  return p;
}

function bindPrompts(prompt) {
  $("#prompt-select").addEventListener("change", async (e) => {
    await savePrompt().catch((err) => toast(err.message));
    state.promptId = e.target.value;
    try { localStorage.setItem("technologyPromptId", state.promptId); } catch { /* storage unavailable */ }
    render();
  });
  $("#prompt-details").addEventListener("toggle", (e) => (state.promptOpen = e.target.open));
  $("#prompt-name")?.addEventListener("input", (e) => { prompt.name = e.target.value; prompt.dirty = true; });
  $("#prompt-text")?.addEventListener("input", (e) => { prompt.text = e.target.value; prompt.dirty = true; });
  $("#prompt-save")?.addEventListener("click", async () => {
    try { await savePrompt(); toast("Prompt zapisany."); render(); } catch (err) { toast(err.message); }
  });
  $("#prompt-new")?.addEventListener("click", async () => {
    const name = prompt === undefined ? "Nowy prompt" : window.prompt("Nazwa nowego promptu technologa:", "Nowy prompt");
    if (!name) return;
    try {
      await savePrompt();
      const created = await api("/sim/api/technology-prompts", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name, text: "Zasady firmowe:\n- " }) });
      state.technologyPrompts.push(created);
      state.promptId = created.id;
      state.promptOpen = true;
      render();
    } catch (err) { toast(err.message); }
  });
  $("#prompt-delete")?.addEventListener("click", async () => {
    if (!confirm(`Usunąć prompt „${prompt.name}”?`)) return;
    await api(`/sim/api/technology-prompts/${prompt.id}`, { method: "DELETE" }).catch((err) => toast(err.message));
    state.technologyPrompts = state.technologyPrompts.filter((p) => p !== prompt);
    state.promptId = state.technologyPrompts[0]?.id ?? null;
    render();
  });
}

// ---------------------------------------------------------------------------------------------
// Generation: drawing + element_id + technology_prompt -> 202 { job_id } -> polling
// ---------------------------------------------------------------------------------------------
async function generate() {
  if (state.card && !confirm(konstruktor() ? "Odczytać dane z rysunku ponownie? Obecne dane zostaną zastąpione."
    : "Wygenerować nową technologię? Obecna karta zostanie zastąpiona.")) return;
  const el = state.element;
  state.job = { status: "sending" };
  render();

  try {
    const drawing = await (await fetch(`/sim/api/elements/${el.id}/drawing`)).blob();
    const form = new FormData();
    form.append("drawing", drawing, fileName(el));
    form.append("element_id", el.id);
    form.append("mode", state.mode);
    if (!konstruktor()) {
      const prompt = await savePrompt();   // unsaved edits of the chosen prompt are saved first
      form.append("technology_prompt", prompt.text);
      form.append("technology_prompt_name", prompt.name);
    }
    const { job_id } = await api("/api/v1/technology-jobs", { method: "POST", body: form });

    state.job = { id: job_id, status: "pending", startedAt: Date.now() };
    render();
    poll(job_id, el.id);
  } catch (err) {
    state.job = { status: "failed", error: { message: err.message } };
    render();
  }
}

async function poll(jobId, elementId) {
  while (state.element?.id === elementId && state.job?.id === jobId) {
    const job = await api(`/api/v1/technology-jobs/${jobId}`).catch(() => null);
    if (job && state.job?.id === jobId) {
      state.job = { ...state.job, status: job.status, error: job.error, warnings: job.warnings ?? [], model: job.model, mock: job.mock, durationMs: job.duration_ms, usage: job.usage };
      if (job.status === "done") {
        state.card = fromApi(job.result);
        state.dirty = true;
        render();
        return;
      }
      if (job.status === "failed") { render(); return; }
      renderStatus();
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
}

// API result (PRODVISOR ids + codes) -> editable card
function fromApi(r) {
  return {
    operations: r.operations.map((o) => ({
      no: o.no, name: o.name, machine_code: o.machine.machine_code, alternatives: o.alternatives.map((a) => a.machine_code),
      tpz_min: o.tpz_min, tj_min: o.tj_min, description: o.description, comment: o.comment ?? "",
    })),
    material: { grade: r.material.grade ?? "", stock: r.material.stock ?? "", material_kind_id: r.material.material_kind_id },
    dimensions: normalizeDims(r.dimensions),
    quantity: r.quantity,
    notes: r.notes ?? "",
  };
}

function fromSaved(t) {
  return { ...t, operations: t.operations.map((o) => ({ ...o, alternatives: o.alternatives.map((a) => a.machine_code) })), dimensions: normalizeDims(t.dimensions) };
}

function normalizeDims(d) {
  return {
    part: { length_mm: null, diameter_mm: null, width_mm: null, height_mm: null, ...(d?.part ?? {}) },
    stock: { form: "other", size_mm: null, length_mm: null, ...(d?.stock ?? {}) },
  };
}

// ---------------------------------------------------------------------------------------------
// Save: the desktop writes the approved technology with its own ids (MASZYNY.ID, STANOWISKO.ID, MaterialsKind.ID)
// ---------------------------------------------------------------------------------------------
async function save() {
  const c = state.card;
  if (konstruktor()) {
    // Designer approves the data read from the drawing -> element card in PRODVISOR
    const p = c.dimensions.part;
    const updated = await api(`/sim/api/elements/${state.element.id}/construction`, {
      method: "PUT", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ material: c.material.grade || null, materialKindId: c.material.material_kind_id ?? null, quantity: num(c.quantity),
        lengthMm: p.length_mm, diameterMm: p.diameter_mm, widthMm: p.width_mm, heightMm: p.height_mm }),
    });
    state.saved = { savedAt: updated.constructionSavedAt };
    state.dirty = false;
    state.elements = await api("/sim/api/elements");
    state.element = state.elements.find((e) => e.id === updated.id);
    renderElements();
    render();
    return toast("Dane elementu zapisane w PRODVISOR.");
  }
  if (!c.operations.length) return toast("Technologia nie zawiera operacji.");
  const ids = (code) => { const m = machineByCode(code); return { machine_code: code, maszyny_id: m?.maszynyId ?? null, stanowisko_id: m?.stanowiskoId ?? null }; };
  const technology = {
    operations: c.operations.map((o, i) => ({
      no: i + 1, name: o.name, ...ids(o.machine_code), alternatives: o.alternatives.map(ids),
      tpz_min: num(o.tpz_min) ?? 0, tj_min: num(o.tj_min) ?? 0, description: o.description, comment: o.comment || null,
    })),
    material: { grade: c.material.grade || null, stock: c.material.stock || null, material_kind_id: c.material.material_kind_id ?? null },
    dimensions: c.dimensions,
    quantity: num(c.quantity),
    notes: c.notes,
  };
  const res = await api(`/sim/api/elements/${state.element.id}/technology`, {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ jobId: state.job?.id ?? state.saved?.jobId, technology }),
  });
  state.saved = { savedAt: res.savedAt, jobId: state.job?.id };
  state.dirty = false;
  state.elements = await api("/sim/api/elements");
  renderElements();
  render();
  toast("Technologia zapisana w PRODVISOR.");
}

// ---------------------------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------------------------
function render() {
  const el = state.element;
  if (!el) return;
  const busy = ["sending", "pending", "running"].includes(state.job?.status);
  const prompt = selectedPrompt();

  $("#workspace").innerHTML = `
    <section class="card">
      <div class="element-head">
        <div>
          <div class="card-head"><h2>${esc(el.number)} – ${esc(el.name)}</h2><span class="spacer"></span><button class="btn ghost small" id="delete-element">Usuń element</button></div>
          <dl>
            <dt>element_id</dt><dd class="mono">${esc(el.id)}</dd>
            <dt>Materiał</dt><dd>${esc(el.material ?? "—")}</dd>
            <dt>Ilość</dt><dd>${esc(el.quantity ?? "—")} szt.</dd>
            ${elementDims(el) ? `<dt>Wymiary</dt><dd>${esc(elementDims(el))}</dd>` : ""}
            ${el.materialKindId ? `<dt>Kartoteka</dt><dd>${esc(state.lookup.materials.find((m) => m.id === el.materialKindId)?.name ?? `ID ${el.materialKindId}`)}</dd>` : ""}
            <dt>Rysunek</dt><dd>${esc(fileName(el))}</dd>
          </dl>
        </div>
        <img class="drawing" id="drawing" src="/sim/api/elements/${esc(el.id)}/preview" alt="Rysunek">
      </div>

      ${konstruktor() ? `
      <div class="generate-bar">
        <button class="btn primary" id="generate" ${busy ? "disabled" : ""}>Odczytaj z rysunku (AI)</button>
        <span class="small muted">materiał, ilość i wymiary</span>
        <span id="status"></span>
      </div>` : `
      <div class="generate-bar">
        <label class="field">Prompt technologa
          <select id="prompt-select">${state.technologyPrompts.map((p) => `<option value="${esc(p.id)}" ${p.id === prompt?.id ? "selected" : ""}>${esc(p.name)}</option>`).join("")}</select>
        </label>
        <button class="btn primary" id="generate" ${busy || !prompt ? "disabled" : ""}>Generuj technologię (AI)</button>
        <span id="status"></span>
      </div>

      <details style="margin-top:12px" ${state.promptOpen ? "open" : ""} id="prompt-details">
        <summary><b>Edytuj prompty technologa</b> <span class="small muted">– zasady firmy, zapisywane w aplikacji desktopowej</span></summary>
        <div style="display:flex;flex-direction:column;gap:8px;margin-top:8px">
          ${prompt ? `
          <label class="field">Nazwa<input id="prompt-name" value="${esc(prompt.name)}"></label>
          <textarea id="prompt-text" rows="8">${esc(prompt.text)}</textarea>` : '<span class="muted">Brak promptów – dodaj nowy.</span>'}
          <div class="row">
            ${prompt ? '<button class="btn" id="prompt-save">Zapisz prompt</button>' : ""}
            <button class="btn" id="prompt-new">+ Nowy prompt</button>
            ${prompt ? '<button class="btn ghost" id="prompt-delete">Usuń</button>' : ""}
          </div>
        </div>
      </details>`}
    </section>
    ${state.job?.status === "failed" ? `<div class="notice err"><b>Generowanie nie powiodło się.</b> ${esc(state.job.error?.message)}</div>` : ""}
    ${state.card ? (konstruktor() ? renderKonstruktorCard() : renderCard()) : ""}`;

  $("#generate").addEventListener("click", generate);
  $("#delete-element").addEventListener("click", deleteElement);
  if (!konstruktor()) bindPrompts(prompt);
  $("#drawing").addEventListener("click", (e) => {
    const box = Object.assign(document.createElement("div"), { className: "lightbox", innerHTML: `<img src="${e.target.src}" alt="">` });
    box.addEventListener("click", () => box.remove());
    document.body.append(box);
  });
  renderStatus();
  if (state.card) bindCard();
}

function renderStatus() {
  const s = $("#status");
  if (!s) return;
  const j = state.job;
  if (!j) { s.innerHTML = state.saved ? `<span class="badge ok">Zapisano ${esc(fmtDate(state.saved.savedAt))}</span>` : ""; return; }
  const secs = j.startedAt ? Math.round((Date.now() - j.startedAt) / 1000) : 0;
  s.innerHTML = {
    sending: `<span class="spinner"></span> Wysyłanie…`,
    pending: `<span class="spinner"></span> W kolejce… <span class="small muted mono">${esc(j.id)}</span>`,
    running: `<span class="spinner"></span> ${konstruktor() ? "AI odczytuje rysunek" : "AI generuje technologię"}… ${secs} s`,
    done: `<span class="badge ok">${konstruktor() ? "Odczytano" : "Wygenerowano"}</span> <span class="small muted">${esc(j.model)}${j.mock ? " (MOCK)" : ""} · ${((j.durationMs ?? 0) / 1000).toFixed(1)} s${j.usage ? ` · ${j.usage.total_tokens} tokenów` : ""}</span>`,
    failed: `<span class="badge err">Błąd</span>`,
  }[j.status] ?? "";
}

function renderCard() {
  const c = state.card;
  const machineOptions = (selected) => state.lookup.machines.map((m) =>
    `<option value="${esc(m.code)}" ${m.code === selected ? "selected" : ""}>${esc(m.code)} – ${esc(m.name)}</option>`).join("");
  const warnings = state.job?.status === "done" ? state.job.warnings ?? [] : [];
  const d = c.dimensions;
  const dimInput = (path, label) => {
    const [a, b] = path.split(".");
    return `<label class="field">${label}<input type="number" step="0.1" data-dim="${path}" value="${esc(d[a][b])}"></label>`;
  };
  const forms = { round_bar: "pręt okrągły", hex_bar: "sześciokąt", square_bar: "pręt kwadratowy", flat_bar: "płaskownik", plate: "blacha", tube: "rura", profile: "profil", other: "inna" };

  return `
    <section class="card">
      <div class="card-head">
        <h2>Technologia</h2>
        ${state.dirty ? '<span id="card-state" class="badge accent">projekt – do zatwierdzenia</span>' : `<span id="card-state">${state.saved ? '<span class="badge ok">zapisana w PRODVISOR</span>' : ""}</span>`}
        <span class="spacer"></span>
        <button class="btn" id="add-op">+ Operacja</button>
        <button class="btn ok" id="save" ${state.dirty ? "" : "disabled"}>Zatwierdź i zapisz</button>
      </div>
      ${warnings.length ? `<div class="notice warn" style="margin-bottom:12px"><b>Uwagi walidacji serwisu</b><ul>${warnings.map((w) => `<li>${esc(w.message)}</li>`).join("")}</ul></div>` : ""}
      <div class="table-wrap">
        <table class="ops">
          <thead><tr><th>Nr</th><th style="width:19%">Operacja</th><th style="width:24%">Maszyna / stanowisko</th><th>TPZ [min]</th><th>TJ [min]</th><th style="width:28%">Opis</th><th style="width:18%">Komentarz</th><th></th></tr></thead>
          <tbody>${c.operations.map((o, i) => {
            const m = machineByCode(o.machine_code);
            return `
            <tr data-i="${i}">
              <td class="no">${i + 1}</td>
              <td><input data-f="name" value="${esc(o.name)}"></td>
              <td>
                <select data-f="machine_code">${machineByCode(o.machine_code) ? "" : `<option selected>${esc(o.machine_code)}</option>`}${machineOptions(o.machine_code)}</select>
                <div class="ids">MASZYNY.ID ${esc(m?.maszynyId ?? "—")} · STANOWISKO.ID ${esc(m?.stanowiskoId ?? "—")}${m?.group ? ` · ${esc(m.group)}` : ""}</div>
                ${o.alternatives.length ? `<div class="alts"><span class="small muted">Alternatywy:</span>${o.alternatives.map((a) =>
                  `<span class="badge info" title="${esc(machineLabel(a))}">${esc(a)} <a href="#" data-remove-alt="${esc(a)}" title="Usuń">×</a></span>`).join("")}</div>` : ""}
              </td>
              <td><input class="num" type="number" step="0.1" min="0" data-f="tpz_min" value="${esc(o.tpz_min)}"></td>
              <td><input class="num" type="number" step="0.1" min="0" data-f="tj_min" value="${esc(o.tj_min)}"></td>
              <td><textarea rows="3" data-f="description">${esc(o.description)}</textarea></td>
              <td><textarea rows="3" data-f="comment">${esc(o.comment)}</textarea></td>
              <td><button class="btn ghost small" data-remove-op title="Usuń operację">✕</button></td>
            </tr>`; }).join("")}
          </tbody>
        </table>
      </div>
    </section>

    <div class="grid2">
      <section class="card">
        <h3 style="margin-bottom:10px">Materiał</h3>
        <div style="display:flex;flex-direction:column;gap:10px">
          <label class="field">Gatunek (z rysunku)<input id="mat-grade" value="${esc(c.material.grade)}"></label>
          <label class="field">Półfabrykat<input id="mat-stock" value="${esc(c.material.stock)}"></label>
          <label class="field">Ilość z rysunku [szt.]<input id="quantity" type="number" min="1" step="1" value="${esc(c.quantity)}" placeholder="brak na rysunku"></label>
          <label class="field">Materiał w kartotece (MaterialsKind)
            <select id="mat-kind"><option value="">— brak / do wyboru —</option>${state.lookup.materials.map((m) =>
              `<option value="${m.id}" ${m.id === c.material.material_kind_id ? "selected" : ""}>${esc(m.name)} (ID ${m.id})</option>`).join("")}</select>
          </label>
        </div>
      </section>
      <section class="card">
        <h3 style="margin-bottom:10px">Wymiary</h3>
        <div class="small muted" style="margin-bottom:4px">Element gotowy [mm]</div>
        <div class="dims">${dimInput("part.length_mm", "Długość")}${dimInput("part.diameter_mm", "Średnica")}${dimInput("part.width_mm", "Szerokość")}${dimInput("part.height_mm", "Wysokość")}</div>
        <div class="small muted" style="margin:10px 0 4px">Półfabrykat</div>
        <div class="dims">
          <label class="field">Forma<select data-dim="stock.form">${Object.entries(forms).map(([k, v]) => `<option value="${k}" ${d.stock.form === k ? "selected" : ""}>${v}</option>`).join("")}</select></label>
          ${dimInput("stock.size_mm", "Wymiar [mm]")}${dimInput("stock.length_mm", "Długość [mm]")}
        </div>
      </section>
    </div>

    <section class="card">
      <h3 style="margin-bottom:10px">Uwagi ogólne</h3>
      <textarea id="notes" rows="4">${esc(c.notes)}</textarea>
    </section>`;
}

function elementDims(el) {
  const parts = [["L", el.lengthMm], ["Ø", el.diameterMm], ["B", el.widthMm], ["H", el.heightMm]].filter(([, v]) => v != null);
  return parts.length ? parts.map(([k, v]) => `${k} ${v}`).join(" · ") + " mm" : "";
}

// "konstruktor" card: only what is read from the drawing, approved into the element card
function renderKonstruktorCard() {
  const c = state.card;
  const warnings = state.job?.status === "done" ? state.job.warnings ?? [] : [];
  const p = c.dimensions.part;
  const dim = (key, label) => `<label class="field">${label}<input type="number" step="0.1" data-dim="part.${key}" value="${esc(p[key])}"></label>`;
  const el = state.element;
  return `
    <section class="card">
      <div class="card-head">
        <h2>Dane z rysunku</h2>
        ${state.dirty ? '<span id="card-state" class="badge accent">do zatwierdzenia</span>' : `<span id="card-state">${state.saved ? '<span class="badge ok">zapisane w PRODVISOR</span>' : ""}</span>`}
        <span class="spacer"></span>
        <button class="btn ok" id="save" ${state.dirty ? "" : "disabled"}>Zatwierdź i zapisz</button>
      </div>
      ${warnings.length ? `<div class="notice warn" style="margin-bottom:12px"><ul style="margin:0">${warnings.map((w) => `<li>${esc(w.message)}</li>`).join("")}</ul></div>` : ""}
      <div class="grid2">
        <div style="display:flex;flex-direction:column;gap:10px">
          <label class="field">Materiał (gatunek z rysunku)<input id="mat-grade" value="${esc(c.material.grade)}"></label>
          <label class="field">Materiał w kartotece (MaterialsKind)
            <select id="mat-kind"><option value="">— brak / do wyboru —</option>${state.lookup.materials.map((m) =>
              `<option value="${m.id}" ${m.id === c.material.material_kind_id ? "selected" : ""}>${esc(m.name)} (ID ${m.id})</option>`).join("")}</select>
          </label>
          <label class="field">Ilość [szt.]<input id="quantity" type="number" min="1" step="1" value="${esc(c.quantity)}" placeholder="brak na rysunku${el.quantity ? ` (w PRODVISOR: ${el.quantity})` : ""}"></label>
        </div>
        <div>
          <div class="field" style="margin-bottom:4px">Wymiary elementu [mm]</div>
          <div class="dims" style="grid-template-columns:repeat(2,minmax(0,1fr))">${dim("length_mm", "Długość")}${dim("diameter_mm", "Średnica")}${dim("width_mm", "Szerokość")}${dim("height_mm", "Wysokość / grubość")}</div>
        </div>
      </div>
      ${c.notes ? `<div class="notice" style="margin-top:12px"><b>Uwagi AI:</b> ${esc(c.notes)}</div>` : ""}
    </section>`;
}

function bindCard() {
  const c = state.card;
  // Typing must not re-render (focus would be lost) - only flag the card as changed.
  const changed = () => {
    state.dirty = true;
    $("#save").disabled = false;
    $("#card-state").outerHTML = `<span id="card-state" class="badge accent">${konstruktor() ? "do zatwierdzenia" : "projekt – do zatwierdzenia"}</span>`;
  };

  document.querySelectorAll(".ops tbody tr").forEach((tr) => {
    const op = c.operations[+tr.dataset.i];
    tr.querySelectorAll("[data-f]").forEach((input) => input.addEventListener(input.tagName === "SELECT" ? "change" : "input", () => {
      op[input.dataset.f] = input.value;
      if (input.dataset.f === "machine_code") { op.alternatives = op.alternatives.filter((a) => a !== input.value); state.dirty = true; render(); }
      else changed();
    }));
    tr.querySelector("[data-remove-op]").addEventListener("click", () => { c.operations.splice(+tr.dataset.i, 1); state.dirty = true; render(); });
    tr.querySelectorAll("[data-remove-alt]").forEach((a) => a.addEventListener("click", (e) => {
      e.preventDefault(); op.alternatives = op.alternatives.filter((x) => x !== a.dataset.removeAlt); state.dirty = true; render();
    }));
  });
  $("#add-op")?.addEventListener("click", () => {
    c.operations.push({ name: "", machine_code: state.lookup.machines[0]?.code ?? "", alternatives: [], tpz_min: 0, tj_min: 0, description: "", comment: "" });
    state.dirty = true; render();
  });
  $("#save").addEventListener("click", () => save().catch((e) => toast(e.message)));
  $("#mat-grade").addEventListener("input", (e) => { c.material.grade = e.target.value; changed(); });
  $("#mat-stock")?.addEventListener("input", (e) => { c.material.stock = e.target.value; changed(); });
  $("#quantity").addEventListener("input", (e) => { c.quantity = num(e.target.value); changed(); });
  $("#mat-kind").addEventListener("change", (e) => { c.material.material_kind_id = e.target.value ? +e.target.value : null; changed(); });
  $("#notes")?.addEventListener("input", (e) => { c.notes = e.target.value; changed(); });
  document.querySelectorAll("[data-dim]").forEach((input) => input.addEventListener(input.tagName === "SELECT" ? "change" : "input", () => {
    const [a, b] = input.dataset.dim.split(".");
    c.dimensions[a][b] = b === "form" ? input.value : num(input.value);
    changed();
  }));
}

window.addEventListener("beforeunload", (e) => { if (state.dirty) e.preventDefault(); });
init().catch((e) => ($("#workspace").innerHTML = `<div class="notice err">${esc(e.message)}</div>`));
