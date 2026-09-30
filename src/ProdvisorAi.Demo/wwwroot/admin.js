// PRODVISOR AI - service administration panel (key + model, system prompt profiles, machines, request log).
"use strict";

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fmtDate = (d) => (d ? new Date(d).toLocaleString("pl-PL") : "");
const view = () => $("#view");

async function api(url, opts = {}) {
  if (opts.json !== undefined) opts = { ...opts, headers: { "Content-Type": "application/json" }, body: JSON.stringify(opts.json) };
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

async function refreshMode() {
  const s = await api("/api/admin/settings");
  const pill = $("#mode");
  pill.className = `badge ${s.effectiveMock ? "warn" : "ok"}`;
  pill.textContent = s.effectiveMock ? "MOCK" : `OpenAI · ${s.model}`;
  return s;
}

const statusBadge = (s) => `<span class="badge ${{ done: "ok", failed: "err", running: "info", pending: "" }[s] ?? ""}">${{ done: "gotowe", failed: "błąd", running: "w toku", pending: "w kolejce" }[s] ?? s}</span>`;

// ---------------------------------------------------------------------------------------------
// Zapytania (request log)
// ---------------------------------------------------------------------------------------------
async function renderJobs() {
  const jobs = await api("/api/admin/jobs");
  view().innerHTML = `
    <section class="card">
      <div class="card-head"><h2>Zapytania z aplikacji desktop</h2><span class="spacer"></span><button class="btn small" id="reload">Odśwież</button></div>
      ${jobs.length ? `<div class="table-wrap"><table>
        <thead><tr><th>Data</th><th>Status</th><th>Element</th><th>Profil</th><th>Model</th><th>Czas</th><th>Tokeny</th></tr></thead>
        <tbody>${jobs.map((j) => `
          <tr class="clickable" data-id="${esc(j.id)}">
            <td>${esc(fmtDate(j.createdAt))}</td><td>${statusBadge(j.status)} ${j.errorKind ? `<span class="small muted">${esc(j.errorKind)}</span>` : ""}</td>
            <td><b>${esc(j.partNumber)}</b> <span class="small muted">(${esc(j.elementId)})</span></td><td>${esc(j.systemPromptId ?? "")}</td>
            <td>${esc(j.model ?? "")}${j.mock ? ' <span class="badge warn">MOCK</span>' : ""}</td>
            <td>${j.durationMs != null ? (j.durationMs / 1000).toFixed(1) + " s" : ""}</td><td>${esc(j.totalTokens ?? "")}</td>
          </tr>`).join("")}</tbody></table></div>`
      : `<p class="muted">Brak zapytań. Wygeneruj technologię w <a href="/desktop/">aplikacji desktop (symulacja)</a>.</p>`}
    </section>`;
  $("#reload").addEventListener("click", renderJobs);
  document.querySelectorAll("tr[data-id]").forEach((tr) => tr.addEventListener("click", () => (location.hash = `#/zapytania/${tr.dataset.id}`)));
}

async function renderJob(id) {
  const j = await api(`/api/admin/jobs/${id}`);
  const a = j.api;
  view().innerHTML = `
    <div><a href="#/zapytania">← Zapytania</a></div>
    <section class="card">
      <div class="card-head"><h2>Zapytanie <span class="mono small">${esc(a.job_id)}</span></h2>${statusBadge(a.status)}${a.mock ? '<span class="badge warn">MOCK</span>' : ""}</div>
      <div class="grid2">
        <div>
          <p>Element <b class="mono">${esc(a.element_id)}</b> · profil <b>${esc(a.system_prompt_id ?? "")}</b> · model ${esc(a.model ?? "")}
            ${a.duration_ms != null ? ` · ${(a.duration_ms / 1000).toFixed(1)} s` : ""}${a.usage ? ` · tokeny ${a.usage.input_tokens} / ${a.usage.output_tokens}` : ""}</p>
          ${a.error ? `<div class="notice err">${esc(a.error.kind)}: ${esc(a.error.message)}</div>` : ""}
          <h3 style="margin:12px 0 6px">Prompt technologa (z desktopu)</h3>
          <pre class="json" style="max-height:200px">${esc(j.technologyPrompt || "(pusty)")}</pre>
          ${j.drawingText ? `<h3 style="margin:12px 0 6px">Dane z DXF</h3><pre class="json" style="max-height:200px">${esc(j.drawingText)}</pre>` : ""}
        </div>
        <div><img class="drawing" style="max-height:340px" src="${esc(j.drawingUrl)}" alt="Rysunek"></div>
      </div>
    </section>
    <section class="card"><h3 style="margin-bottom:8px">Odpowiedź API dla desktopu (GET /api/v1/technology-jobs/{id})</h3><pre class="json">${esc(JSON.stringify(a, null, 2))}</pre></section>
    <section class="card"><h3 style="margin-bottom:8px">Zapytanie do OpenAI (bez base64 rysunku)</h3><pre class="json">${esc(JSON.stringify(j.debug.request, null, 2))}</pre></section>
    <section class="card"><h3 style="margin-bottom:8px">Surowa odpowiedź OpenAI</h3><pre class="json">${esc(JSON.stringify(j.debug.response, null, 2))}</pre></section>`;
  if (["pending", "running"].includes(a.status)) setTimeout(() => location.hash === `#/zapytania/${id}` && renderJob(id), 2000);
}

// ---------------------------------------------------------------------------------------------
// Profile (system prompt)
// ---------------------------------------------------------------------------------------------
async function renderPrompts(selectedId) {
  const prompts = await api("/api/admin/system-prompts");
  const sel = prompts.find((p) => p.id === selectedId) ?? prompts[0];
  view().innerHTML = `
    <div class="notice">System prompt jest wbudowany w serwis. Technolog wybiera profil przy klikaniu „Generuj” w aplikacji desktop
      (<span class="mono">system_prompt_id</span>); bez wyboru używany jest profil domyślny. Zasady firmy (prompt technologa) przychodzą z desktopu.</div>
    <div class="desk">
      <aside class="card elements">
        <h3>Profile</h3>
        ${prompts.map((p) => `<div class="element ${p.id === sel?.id ? "active" : ""}" data-id="${esc(p.id)}"><b>${esc(p.name)}</b>
          <span class="small muted mono">${esc(p.id)}</span> ${p.isDefault ? '<span class="badge ok">domyślny</span>' : ""}</div>`).join("")}
        <div class="element" data-id=""><b>+ Nowy profil</b></div>
      </aside>
      <section class="card">
        <div style="display:flex;flex-direction:column;gap:10px">
          <div class="row">
            <label class="field" style="flex:0 0 180px">ID<input id="p-id" value="${esc(sel?.id ?? "")}" ${sel ? "readonly" : ""} placeholder="np. blachy"></label>
            <label class="field" style="flex:1">Nazwa<input id="p-name" value="${esc(sel?.name ?? "")}"></label>
            <label class="row small" style="padding-top:18px"><input type="checkbox" id="p-default" ${sel?.isDefault ? "checked" : ""}> domyślny</label>
          </div>
          <label class="field">Treść<textarea id="p-text" class="code" rows="20">${esc(sel?.text ?? "")}</textarea></label>
          <div class="row">
            <button class="btn primary" id="p-save">Zapisz</button>
            ${sel && !sel.isDefault ? '<button class="btn" id="p-delete">Usuń</button>' : ""}
            ${sel ? `<span class="small muted">Zmieniono ${esc(fmtDate(sel.updatedAt))}</span>` : ""}
          </div>
        </div>
      </section>
    </div>`;

  document.querySelectorAll(".element[data-id]").forEach((el) => el.addEventListener("click", () => {
    if (el.dataset.id) renderPrompts(el.dataset.id);
    else { renderPrompts("__new__"); }
  }));
  if (selectedId === "__new__") {
    $("#p-id").value = ""; $("#p-id").readOnly = false; $("#p-name").value = ""; $("#p-text").value = ""; $("#p-default").checked = false;
    $("#p-delete")?.remove();
  }
  $("#p-save").addEventListener("click", async () => {
    const id = $("#p-id").value.trim();
    try {
      await api(`/api/admin/system-prompts/${encodeURIComponent(id)}`, { method: "PUT", json: { id, name: $("#p-name").value, text: $("#p-text").value, isDefault: $("#p-default").checked } });
      toast("Zapisano profil.");
      renderPrompts(id);
    } catch (e) { toast(e.message); }
  });
  $("#p-delete")?.addEventListener("click", async () => {
    if (!confirm(`Usunąć profil ${sel.name}?`)) return;
    await api(`/api/admin/system-prompts/${sel.id}`, { method: "DELETE" }).catch((e) => toast(e.message));
    renderPrompts();
  });
}

// ---------------------------------------------------------------------------------------------
// Maszyny
// ---------------------------------------------------------------------------------------------
async function renderMachines() {
  const [machines, info] = await Promise.all([api("/api/admin/machines"), api("/api/admin/info")]);
  view().innerHTML = `
    <section class="card">
      <div class="card-head">
        <h2>Maszyny z PRODVISOR</h2>
        <span class="small muted">źródło: ${esc(info.prodvisorSource)} · ${machines.filter((m) => m.active).length} / ${machines.length} aktywnych (przekazywanych do AI)</span>
        <span class="spacer"></span><button class="btn" id="sync">Synchronizuj z PRODVISOR</button>
      </div>
      <div class="table-wrap"><table>
        <thead><tr><th>Aktywna</th><th>Kod</th><th>Nazwa</th><th>Typ</th><th>Grupa (stanowisko)</th><th>MASZYNY.ID</th><th>STANOWISKO.ID</th><th>Parametry</th></tr></thead>
        <tbody>${machines.map((m) => `
          <tr>
            <td><input type="checkbox" data-id="${esc(m.id)}" ${m.active ? "checked" : ""}></td>
            <td class="mono"><b>${esc(m.id)}</b></td><td>${esc(m.name)}</td>
            <td>${m.isVirtual ? '<span class="badge info">kooperacja</span>' : esc(m.type)}</td>
            <td>${esc(m.groupName ?? "")}</td><td class="mono">${esc(m.prodvisorId ?? "")}</td><td class="mono">${esc(m.workplaceId ?? "")}</td>
            <td class="small muted">${m.parameters.map((p) => `${esc(p.name)}: ${esc(p.value)}`).join("; ")}</td>
          </tr>`).join("")}</tbody>
      </table></div>
    </section>`;
  document.querySelectorAll("input[data-id]").forEach((cb) => cb.addEventListener("change", () =>
    api(`/api/admin/machines/${encodeURIComponent(cb.dataset.id)}/active?active=${cb.checked}`, { method: "PUT" }).catch((e) => toast(e.message))));
  $("#sync").addEventListener("click", async () => {
    try { const r = await api("/api/admin/sync", { method: "POST" }); toast("Zsynchronizowano."); console.log(r); renderMachines(); }
    catch (e) { toast(e.message); }
  });
}

// ---------------------------------------------------------------------------------------------
// Ustawienia
// ---------------------------------------------------------------------------------------------
async function renderSettings() {
  const s = await refreshMode();
  view().innerHTML = `
    <section class="card" style="max-width:720px">
      <div class="card-head"><h2>OpenAI</h2></div>
      <div style="display:flex;flex-direction:column;gap:12px">
        <label class="field">Klucz API (zapisywany zaszyfrowany, nigdy nie wraca do przeglądarki)
          <input id="key" type="password" autocomplete="off" placeholder="${s.hasApiKey ? `zapisany: ${esc(s.apiKeyMasked)} – wpisz, aby zmienić` : "sk-…"}">
        </label>
        <label class="field">Model
          <input id="model" list="models" value="${esc(s.model)}">
          <datalist id="models">${s.suggestedModels.map((m) => `<option value="${esc(m)}">`).join("")}</datalist>
        </label>
        <label class="row"><input type="checkbox" id="mock" ${s.mockMode ? "checked" : ""}> Tryb mock (przykładowa odpowiedź, bez wywołania OpenAI)</label>
        ${s.effectiveMock && !s.mockMode ? '<div class="notice warn">Brak klucza API – serwis działa w trybie mock.</div>' : ""}
        <div class="row">
          <button class="btn primary" id="save">Zapisz</button>
          <button class="btn" id="test">Testuj połączenie</button>
          ${s.hasApiKey ? '<button class="btn" id="clear">Usuń klucz</button>' : ""}
          <span id="test-result"></span>
        </div>
      </div>
    </section>`;
  $("#save").addEventListener("click", async () => {
    await api("/api/admin/settings", { method: "PUT", json: { apiKey: $("#key").value || null, clearApiKey: false, model: $("#model").value, mockMode: $("#mock").checked } })
      .then(() => toast("Zapisano.")).catch((e) => toast(e.message));
    renderSettings();
  });
  $("#clear")?.addEventListener("click", async () => {
    if (!confirm("Usunąć zapisany klucz API?")) return;
    await api("/api/admin/settings", { method: "PUT", json: { clearApiKey: true } });
    renderSettings();
  });
  $("#test").addEventListener("click", async () => {
    $("#test-result").innerHTML = '<span class="spinner"></span>';
    const r = await api("/api/admin/settings/test", { method: "POST" }).catch((e) => ({ ok: false, message: e.message }));
    $("#test-result").innerHTML = `<span class="badge ${r.ok ? "ok" : "err"}">${esc(r.message)}</span>`;
  });
}

// ---------------------------------------------------------------------------------------------
// API (short reference for the desktop team)
// ---------------------------------------------------------------------------------------------
function renderApi() {
  view().innerHTML = `
    <section class="card">
      <h2 style="margin-bottom:12px">API dla aplikacji desktop</h2>
      <table>
        <tbody>
          <tr><td class="mono">GET /api/v1/system-prompts</td><td>Lista profili do wyboru przy generowaniu: <span class="mono">[{ id, name, is_default }]</span></td></tr>
          <tr><td class="mono">POST /api/v1/technology-jobs</td><td><span class="mono">multipart/form-data</span>: <b>drawing</b> (PNG/JPG/PDF/DXF), <b>element_id</b>, <b>technology_prompt</b>, <b>system_prompt_id</b> (opcjonalnie)<br>
            → <span class="mono">202 { job_id, status }</span>. Serwis sam czyta dane elementu, maszyny, grupy i materiały z tabel PRODVISOR.</td></tr>
          <tr><td class="mono">GET /api/v1/technology-jobs/{job_id}</td><td>Odpytywanie co 1–2 s. <span class="mono">status</span>: pending → running → done | failed.<br>
            Po <b>done</b>: <span class="mono">result.operations[]</span> (no, name, machine { machine_code, maszyny_id, stanowisko_id }, alternatives[], tpz_min, tj_min, description, comment),
            <span class="mono">result.material</span> (grade, stock, material_kind_id), <span class="mono">result.dimensions</span>, <span class="mono">result.notes</span>, <span class="mono">warnings[]</span>.</td></tr>
        </tbody>
      </table>
      <h3 style="margin:16px 0 8px">Przykład (curl)</h3>
      <pre class="json">curl -F drawing=@rysunek.dxf -F element_id=100232 -F "technology_prompt=Toczenie na 0502..." -F system_prompt_id=cnc \\
     ${esc(location.origin)}/api/v1/technology-jobs

curl ${esc(location.origin)}/api/v1/technology-jobs/&lt;job_id&gt;</pre>
    </section>`;
}

// ---------------------------------------------------------------------------------------------
// Router
// ---------------------------------------------------------------------------------------------
async function route() {
  const [page, id] = location.hash.replace(/^#\/?/, "").split("/");
  document.querySelectorAll(".topbar nav a").forEach((a) => a.classList.toggle("active", a.getAttribute("href") === `#/${page || "zapytania"}`));
  try {
    if (page === "profile") await renderPrompts();
    else if (page === "maszyny") await renderMachines();
    else if (page === "ustawienia") await renderSettings();
    else if (page === "api") renderApi();
    else if (id) await renderJob(id);
    else await renderJobs();
  } catch (e) {
    view().innerHTML = `<div class="notice err">${esc(e.message)}</div>`;
  }
}

window.addEventListener("hashchange", route);
refreshMode().catch(() => {});
route();
