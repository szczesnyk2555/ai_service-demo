// PRODVISOR desktop application - simulation.
// Data the desktop owns (elements, drawings, technology prompt, dictionaries, saved technologies) comes from /sim/api.
// The AI service is used ONLY through its public API: /api/v1/system-prompts and /api/v1/technology-jobs.
"use strict";

const state = {
  elements: [], lookup: { machines: [], materials: [] }, systemPrompts: [], technologyPrompt: "",
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

const machineByCode = (code) => state.lookup.machines.find((m) => m.code === code);
const machineLabel = (code) => { const m = machineByCode(code); return m ? `${m.code} – ${m.name}` : `${code} (nieznana)`; };

// ---------------------------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------------------------
async function init() {
  const [elements, lookup, prompts, tp] = await Promise.all([
    api("/sim/api/elements"), api("/sim/api/lookup"), api("/api/v1/system-prompts"), api("/sim/api/technology-prompt"),
  ]);
  Object.assign(state, { elements, lookup, systemPrompts: prompts, technologyPrompt: tp.text });
  renderElements();
  if (elements.length) selectElement(elements[0].id);
}

function renderElements() {
  $("#element-list").innerHTML = state.elements.map((e) => `
    <div class="element ${state.element?.id === e.id ? "active" : ""}" data-id="${esc(e.id)}">
      <b>${esc(e.number)}</b>
      <span class="small">${esc(e.name)}</span>
      <div class="small muted">ID ${esc(e.id)} · ${esc(e.drawingFile.split(".").pop().toUpperCase())}
        ${e.savedAt ? '· <span class="badge ok">technologia zapisana</span>' : ""}</div>
    </div>`).join("");
  document.querySelectorAll(".element").forEach((el) => el.addEventListener("click", () => selectElement(el.dataset.id)));
}

async function selectElement(id) {
  if (state.dirty && !confirm("Niezapisane zmiany w technologii zostaną utracone. Kontynuować?")) return;
  state.element = state.elements.find((e) => e.id === id);
  state.card = null; state.job = null; state.saved = null; state.dirty = false;
  try {
    const saved = await api(`/sim/api/elements/${id}/technology`);
    state.saved = { savedAt: saved.savedAt, jobId: saved.jobId };
    state.card = fromSaved(saved.technology);
  } catch { /* no technology yet */ }
  renderElements();
  render();
}

// ---------------------------------------------------------------------------------------------
// Generation: drawing + element_id + technology_prompt (+ system_prompt_id) -> 202 { job_id } -> polling
// ---------------------------------------------------------------------------------------------
async function generate() {
  if (state.card && !confirm("Wygenerować nową technologię? Obecna karta zostanie zastąpiona.")) return;
  const el = state.element;
  state.technologyPrompt = $("#tech-prompt").value;
  const systemPromptId = $("#system-prompt").value;
  state.job = { status: "sending" };
  render();

  try {
    await api("/sim/api/technology-prompt", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ text: state.technologyPrompt }) });
    const drawing = await (await fetch(`/sim/api/elements/${el.id}/drawing`)).blob();

    const form = new FormData();
    form.append("drawing", drawing, el.drawingFile);
    form.append("element_id", el.id);
    form.append("technology_prompt", state.technologyPrompt);
    form.append("system_prompt_id", systemPromptId);
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
  if (!c.operations.length) return toast("Technologia nie zawiera operacji.");
  const ids = (code) => { const m = machineByCode(code); return { machine_code: code, maszyny_id: m?.maszynyId ?? null, stanowisko_id: m?.stanowiskoId ?? null }; };
  const technology = {
    operations: c.operations.map((o, i) => ({
      no: i + 1, name: o.name, ...ids(o.machine_code), alternatives: o.alternatives.map(ids),
      tpz_min: num(o.tpz_min) ?? 0, tj_min: num(o.tj_min) ?? 0, description: o.description, comment: o.comment || null,
    })),
    material: { grade: c.material.grade || null, stock: c.material.stock || null, material_kind_id: c.material.material_kind_id ?? null },
    dimensions: c.dimensions,
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

  $("#workspace").innerHTML = `
    <section class="card">
      <div class="element-head">
        <div>
          <div class="card-head"><h2>${esc(el.number)} – ${esc(el.name)}</h2></div>
          <dl>
            <dt>element_id</dt><dd class="mono">${esc(el.id)}</dd>
            <dt>Materiał</dt><dd>${esc(el.material ?? "—")}</dd>
            <dt>Ilość</dt><dd>${esc(el.quantity ?? "—")} szt.</dd>
            <dt>Rysunek</dt><dd>${esc(el.drawingFile)}</dd>
          </dl>
        </div>
        <img class="drawing" id="drawing" src="/sim/api/elements/${esc(el.id)}/preview" alt="Rysunek">
      </div>

      <details style="margin-top:14px">
        <summary><b>Prompt technologa</b> <span class="small muted">– zasady firmy, zapisywany w aplikacji desktopowej</span></summary>
        <textarea id="tech-prompt" rows="7" style="margin-top:8px">${esc(state.technologyPrompt)}</textarea>
      </details>

      <div class="generate-bar">
        <label class="field">Profil (system prompt serwisu)
          <select id="system-prompt">${state.systemPrompts.map((p) => `<option value="${esc(p.id)}" ${p.is_default ? "selected" : ""}>${esc(p.name)}</option>`).join("")}</select>
        </label>
        <button class="btn primary" id="generate" ${busy ? "disabled" : ""}>Generuj technologię (AI)</button>
        <span id="status"></span>
      </div>
    </section>
    ${state.job?.status === "failed" ? `<div class="notice err"><b>Generowanie nie powiodło się.</b> ${esc(state.job.error?.message)}</div>` : ""}
    ${state.card ? renderCard() : ""}`;

  $("#generate").addEventListener("click", generate);
  $("#tech-prompt").addEventListener("input", (e) => (state.technologyPrompt = e.target.value));
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
    running: `<span class="spinner"></span> AI generuje technologię… ${secs} s`,
    done: `<span class="badge ok">Wygenerowano</span> <span class="small muted">${esc(j.model)}${j.mock ? " (MOCK)" : ""} · ${((j.durationMs ?? 0) / 1000).toFixed(1)} s${j.usage ? ` · ${j.usage.total_tokens} tokenów` : ""}</span>`,
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

function bindCard() {
  const c = state.card;
  // Typing must not re-render (focus would be lost) - only flag the card as changed.
  const changed = () => {
    state.dirty = true;
    $("#save").disabled = false;
    $("#card-state").outerHTML = '<span id="card-state" class="badge accent">projekt – do zatwierdzenia</span>';
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
  $("#add-op").addEventListener("click", () => {
    c.operations.push({ name: "", machine_code: state.lookup.machines[0]?.code ?? "", alternatives: [], tpz_min: 0, tj_min: 0, description: "", comment: "" });
    state.dirty = true; render();
  });
  $("#save").addEventListener("click", () => save().catch((e) => toast(e.message)));
  $("#mat-grade").addEventListener("input", (e) => { c.material.grade = e.target.value; changed(); });
  $("#mat-stock").addEventListener("input", (e) => { c.material.stock = e.target.value; changed(); });
  $("#mat-kind").addEventListener("change", (e) => { c.material.material_kind_id = e.target.value ? +e.target.value : null; changed(); });
  $("#notes").addEventListener("input", (e) => { c.notes = e.target.value; changed(); });
  document.querySelectorAll("[data-dim]").forEach((input) => input.addEventListener(input.tagName === "SELECT" ? "change" : "input", () => {
    const [a, b] = input.dataset.dim.split(".");
    c.dimensions[a][b] = b === "form" ? input.value : num(input.value);
    changed();
  }));
}

window.addEventListener("beforeunload", (e) => { if (state.dirty) e.preventDefault(); });
init().catch((e) => ($("#workspace").innerHTML = `<div class="notice err">${esc(e.message)}</div>`));
