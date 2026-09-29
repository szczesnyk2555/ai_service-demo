// PRODVISOR AI – Technology Generator (Demo)
// Plain JavaScript single-page app, no build step. The browser only talks to this service's /api – never to OpenAI.
'use strict';

(() => {
  // =========================================================================================
  // State & helpers
  // =========================================================================================
  const state = {
    lang: loadLang(),
    info: { currency: 'PLN', maxUploadMb: 20 },
    settings: null,
    upload: null,                                   // { file, url } selected on the Generate page
    form: { partNumber: '', material: '', batchSize: '' },
    flush: null,                                    // pending autosave of the job editor (called on navigation)
  };
  let routeToken = 0;                               // increments on every navigation; stale async renders bail out

  const view = document.getElementById('view');
  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  function loadLang() {
    // Polish UI by default; EN only when the user switched to it.
    try { return localStorage.getItem('pv-lang') === 'en' ? 'en' : 'pl'; } catch { return 'pl'; }
  }

  /** Translate a key; {placeholders} are replaced from args. Falls back to English, then to the key. */
  function t(key, args) {
    let s = (I18N[state.lang] && I18N[state.lang][key]) ?? I18N.en[key] ?? key;
    if (args) s = s.replace(/\{(\w+)\}/g, (m, k) => (args[k] ?? m));
    return s;
  }

  const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const locale = () => (state.lang === 'pl' ? 'pl-PL' : 'en-GB');
  const num = (v, d = 1) => new Intl.NumberFormat(locale(), { maximumFractionDigits: d }).format(v || 0);
  const money = (v) => new Intl.NumberFormat(locale(), { style: 'currency', currency: state.info.currency || 'PLN' }).format(v || 0);
  const date = (iso) => (iso ? new Date(iso).toLocaleString(locale(), { dateStyle: 'short', timeStyle: 'short' }) : '–');
  const fileSize = (b) => (b > 1048576 ? `${num(b / 1048576, 1)} MB` : `${num(b / 1024, 0)} kB`);

  /** "45 min" or "12 h 35 min" */
  function duration(min) {
    if (!isFinite(min)) return '–';
    if (min < 60) return `${num(min)} min`;
    const h = Math.floor(min / 60);
    return `${h} h ${Math.round(min - h * 60)} min`;
  }

  /** fetch wrapper: JSON in/out, throws Error(message) for non-2xx responses. */
  async function api(method, url, body) {
    const opts = { method, headers: {} };
    if (body instanceof FormData) opts.body = body;
    else if (body !== undefined) { opts.body = JSON.stringify(body); opts.headers['Content-Type'] = 'application/json'; }
    const res = await fetch(url, opts);
    if (!res.ok) {
      let msg = `${res.status} ${res.statusText}`;
      try { const j = await res.json(); msg = j.error || j.title || msg; } catch { /* not JSON */ }
      throw new Error(msg);
    }
    return res.status === 204 ? null : res.json();
  }

  function toast(message, kind = '') {
    const el = document.createElement('div');
    el.className = `toast ${kind}`;
    el.textContent = message;
    $('#toasts').appendChild(el);
    setTimeout(() => el.remove(), 3800);
  }

  const loading = () => `<div class="empty">${t('common.loading')}</div>`;
  const errorBox = (e) => `<div class="alert err"><b>${t('common.error')}:</b> ${esc(e.message || e)}</div>`;

  // =========================================================================================
  // Shell: navigation, language switch, mode pill
  // =========================================================================================
  const routes = {
    generate: renderGenerate,
    history: renderHistory,
    machines: renderMachines,
    materials: renderMaterials,
    prompts: renderPrompts,
    settings: renderSettings,
    job: renderJob,
  };

  function route() {
    if (state.flush) { state.flush(); state.flush = null; }   // save pending edits before leaving a job
    const [, name = 'generate', arg] = location.hash.replace(/^#/, '').split('/');
    const page = routes[name] ? name : 'generate';
    const token = ++routeToken;
    $$('.nav a').forEach((a) => a.classList.toggle('active', a.dataset.route === (page === 'job' ? 'history' : page)));
    window.scrollTo(0, 0);
    routes[page](arg, token).catch((e) => { if (token === routeToken) view.innerHTML = errorBox(e); });
  }

  function applyStaticTexts() {
    document.documentElement.lang = state.lang;
    document.title = t('app.title');
    $$('[data-i18n]').forEach((el) => { el.textContent = t(el.dataset.i18n); });
    $$('.lang-switch button').forEach((b) => b.classList.toggle('active', b.dataset.lang === state.lang));
    renderModePill();
  }

  function renderModePill() {
    const pill = $('#mode-pill');
    const s = state.settings;
    if (!s) { pill.hidden = true; return; }
    pill.hidden = false;
    pill.className = `pill ${s.effectiveMock ? 'mock' : 'live'}`;
    pill.textContent = s.effectiveMock ? t('mode.mock') : t('mode.live', { model: s.model });
  }

  async function refreshSettings() {
    state.settings = await api('GET', '/api/settings');
    renderModePill();
    return state.settings;
  }

  $$('.lang-switch button').forEach((b) => b.addEventListener('click', () => {
    state.lang = b.dataset.lang;
    try { localStorage.setItem('pv-lang', state.lang); } catch { /* storage unavailable */ }
    applyStaticTexts();
    route();
  }));

  // =========================================================================================
  // Generate
  // =========================================================================================
  async function renderGenerate(_, token) {
    view.innerHTML = loading();
    const [settings, machines] = await Promise.all([refreshSettings(), api('GET', '/api/machines')]);
    if (token !== routeToken) return;

    const active = machines.filter((m) => m.active).length;
    const f = state.form;
    view.innerHTML = `
      <div class="page-head"><div><h1>${t('gen.title')}</h1><p>${t('gen.subtitle')}</p></div></div>
      <div class="grid-2">
        <div class="card">
          <div class="card-title"><span class="step">1</span>${t('gen.drawing')}</div>
          <div id="drop-area"></div>
        </div>
        <div>
          <div class="card">
            <div class="card-title"><span class="step">2</span>${t('gen.partData')}</div>
            <div class="field"><label for="f-part">${t('gen.partNumber')} <span class="muted">(${t('gen.optional')})</span></label>
              <input type="text" id="f-part" value="${esc(f.partNumber)}" placeholder="PV-1001-A"></div>
            <div class="row-fields">
              <div class="field"><label for="f-mat">${t('gen.material')} <span class="muted">(${t('gen.optional')})</span></label>
                <input type="text" id="f-mat" value="${esc(f.material)}" placeholder="C45"></div>
              <div class="field"><label for="f-batch">${t('gen.batchSize')} <span class="muted">(${t('gen.optional')})</span></label>
                <input type="number" id="f-batch" min="1" step="1" value="${esc(f.batchSize)}" placeholder="50"></div>
            </div>
          </div>
          <div class="card">
            <div class="card-title"><span class="step">3</span>${t('gen.context')}</div>
            <ul class="context-list">
              <li><a href="#/machines">${t('gen.ctxMachines', { n: active })}</a></li>
              <li><a href="#/prompts">${t('gen.ctxPrompts', { lang: settings.promptLanguage.toUpperCase() })}</a></li>
              <li><a href="#/settings">${settings.effectiveMock ? t('gen.ctxMock') : t('gen.ctxModel', { model: esc(settings.model) })}</a></li>
            </ul>
            <div id="gen-error" style="margin-bottom:12px"></div>
            <button class="btn primary lg" id="gen-btn" style="width:100%">✦ ${t('gen.button')}</button>
          </div>
        </div>
      </div>`;

    renderDropArea();
    $('#f-part').addEventListener('input', (e) => { f.partNumber = e.target.value; });
    $('#f-mat').addEventListener('input', (e) => { f.material = e.target.value; });
    $('#f-batch').addEventListener('input', (e) => { f.batchSize = e.target.value; });
    $('#gen-btn').addEventListener('click', submitJob);
  }

  function renderDropArea() {
    const el = $('#drop-area');
    if (!el) return;
    const u = state.upload;

    if (!u) {
      el.innerHTML = `
        <div class="dropzone" id="dropzone" tabindex="0">
          <svg viewBox="0 0 24 24"><path d="M12 16V4M7 9l5-5 5 5"/><path d="M4 15v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4"/></svg>
          <div class="big">${t('gen.drop')}</div>
          <div class="muted small">${t('gen.dropHint', { mb: state.info.maxUploadMb })}</div>
          <input type="file" id="file-input" accept=".png,.jpg,.jpeg,.pdf,.dxf,image/png,image/jpeg,application/pdf" hidden>
        </div>
        <div class="btn-row" style="margin-top:10px; gap:18px"><button class="btn link" id="sample-btn">⤓ ${t('gen.useSample')}</button><button class="btn link" id="sample-dxf-btn">⤓ ${t('gen.useSampleDxf')}</button></div>`;

      const dz = $('#dropzone');
      const input = $('#file-input');
      dz.addEventListener('click', () => input.click());
      dz.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') input.click(); });
      dz.addEventListener('dragover', (e) => { e.preventDefault(); dz.classList.add('drag'); });
      dz.addEventListener('dragleave', () => dz.classList.remove('drag'));
      dz.addEventListener('drop', (e) => { e.preventDefault(); dz.classList.remove('drag'); if (e.dataTransfer.files[0]) setFile(e.dataTransfer.files[0]); });
      input.addEventListener('change', () => { if (input.files[0]) setFile(input.files[0]); });
      $('#sample-btn').addEventListener('click', () => loadSample('drawing'));
      $('#sample-dxf-btn').addEventListener('click', () => loadSample('dxf'));
      return;
    }

    const isPdf = u.file.type === 'application/pdf' || /\.pdf$/i.test(u.file.name);
    // DXF cannot be shown by the browser: show the PNG rendered by the server (/api/drawings/preview).
    const media = u.isDxf
      ? (u.dxf ? `<img src="${u.dxf.image}" alt="DXF preview">` : `<div class="empty">${t('gen.dxfRendering')}</div>`)
      : isPdf ? `<iframe src="${u.url}" title="PDF preview"></iframe>` : `<img src="${u.url}" alt="Drawing preview">`;
    el.innerHTML = `
      <div class="preview">
        ${media}
        <div class="preview-bar">
          <span><b>${esc(u.file.name)}</b> <span class="muted">· ${fileSize(u.file.size)}</span></span>
          <button class="btn sm" id="remove-file">✕ ${t('gen.remove')}</button>
        </div>
      </div>
      ${u.dxf ? `<div class="alert info" style="margin-top:10px">ⓘ ${t('gen.dxfInfo', { dims: u.dxf.dimensionCount, texts: u.dxf.textCount })}</div>` : ''}`;
    $('#remove-file').addEventListener('click', () => { URL.revokeObjectURL(u.url); state.upload = null; renderDropArea(); });
  }

  function setFile(file) {
    const errEl = $('#gen-error');
    if (errEl) errEl.innerHTML = '';
    if (!/\.(png|jpe?g|pdf|dxf)$/i.test(file.name) && !/^(image\/(png|jpeg)|application\/pdf)$/.test(file.type)) {
      errEl.innerHTML = `<div class="alert err">${t('gen.badType')}</div>`; return;
    }
    if (file.size > state.info.maxUploadMb * 1048576) {
      errEl.innerHTML = `<div class="alert err">${t('gen.tooLarge', { mb: state.info.maxUploadMb })}</div>`; return;
    }
    if (state.upload) URL.revokeObjectURL(state.upload.url);
    const upload = { file, url: URL.createObjectURL(file), isDxf: /\.dxf$/i.test(file.name), dxf: null };
    state.upload = upload;
    renderDropArea();
    if (upload.isDxf) previewDxf(upload);
  }

  async function previewDxf(upload) {
    const fd = new FormData();
    fd.append('drawing', upload.file, upload.file.name);
    try {
      upload.dxf = await api('POST', '/api/drawings/preview', fd);
      if (state.upload === upload) renderDropArea();
    } catch (e) {
      if (state.upload !== upload) return;
      state.upload = null;
      renderDropArea();
      const errEl = $('#gen-error');
      if (errEl) errEl.innerHTML = errorBox(e);
    }
  }

  async function loadSample(kind = 'drawing') {
    const res = await fetch(`/api/samples/${kind}`);
    if (!res.ok) { toast('Sample drawing not found', 'err'); return; }
    const name = kind === 'dxf' ? 'sample-drawing-shaft.dxf' : 'sample-drawing-shaft.png';
    setFile(new File([await res.blob()], name, { type: kind === 'dxf' ? 'application/dxf' : 'image/png' }));
    // Pre-fill the part data that matches the sample drawing (only empty fields).
    const f = state.form;
    if (!f.partNumber) { f.partNumber = 'PV-1001-A'; $('#f-part').value = f.partNumber; }
    if (!f.material) { f.material = 'C45'; $('#f-mat').value = f.material; }
    if (!f.batchSize) { f.batchSize = '50'; $('#f-batch').value = f.batchSize; }
  }

  async function submitJob() {
    const errEl = $('#gen-error');
    errEl.innerHTML = '';
    if (!state.upload) { errEl.innerHTML = `<div class="alert warn">${t('gen.noFile')}</div>`; return; }

    const fd = new FormData();
    fd.append('drawing', state.upload.file, state.upload.file.name);
    fd.append('partNumber', state.form.partNumber);
    fd.append('material', state.form.material);
    fd.append('batchSize', state.form.batchSize);

    const btn = $('#gen-btn');
    btn.disabled = true;
    try {
      const { jobId } = await api('POST', '/api/jobs', fd);
      URL.revokeObjectURL(state.upload.url);
      state.upload = null;
      state.form = { partNumber: '', material: '', batchSize: '' };
      location.hash = `#/job/${jobId}`;
    } catch (e) {
      errEl.innerHTML = errorBox(e);
      btn.disabled = false;
    }
  }

  // =========================================================================================
  // Job: progress -> result (editable draft)
  // =========================================================================================
  async function renderJob(id, token) {
    view.innerHTML = loading();
    const [job, machines] = await Promise.all([api('GET', `/api/jobs/${id}`), api('GET', '/api/machines')]);
    if (token !== routeToken) return;

    view.innerHTML = `
      <div class="job-layout">
        <aside class="card drawing-panel" id="drawing-panel"></aside>
        <section id="job-main"></section>
      </div>`;
    renderDrawingPanel(job);
    showJobState(job, machines, token);
  }

  function renderDrawingPanel(job) {
    const isPdf = job.drawingContentType === 'application/pdf';
    $('#drawing-panel').innerHTML = `
      <div class="preview">
        ${isPdf ? `<iframe src="${job.drawingUrl}" title="Drawing"></iframe>` : `<img src="${job.drawingUrl}" alt="Drawing">`}
      </div>
      <div style="margin-top:8px"><a href="${job.drawingUrl}" target="_blank" rel="noopener" class="small">↗ ${t('job.openDrawing')}</a></div>
      <dl class="meta">
        <dt>${t('job.meta.part')}</dt><dd>${esc(job.partNumber || '–')}</dd>
        <dt>${t('job.meta.material')}</dt><dd>${esc(job.material || '–')}</dd>
        <dt>${t('job.meta.batch')}</dt><dd>${job.batchSize ? `${job.batchSize} ${t('common.pcs')}` : '–'}</dd>
        <dt>${t('job.meta.file')}</dt><dd>${esc(job.drawingFileName)}</dd>
        <dt>${t('job.meta.created')}</dt><dd>${date(job.createdAt)}</dd>
      </dl>
      ${job.sourceUrl ? `<div class="small" style="margin-top:10px"><span class="badge info">DXF</span> <a href="${job.sourceUrl}">⤓ ${t('job.sourceDxf')}</a></div>` : ''}
      ${job.drawingText ? `<details style="margin-top:8px"><summary class="small" style="cursor:pointer;font-weight:600">${t('job.dxfData')}</summary>
        <pre class="json" style="max-height:320px">${esc(job.drawingText)}</pre></details>` : ''}
      ${job.regeneratedFromId ? `<div class="small muted" style="margin-top:8px">↻ <a href="#/job/${job.regeneratedFromId}">${t('job.regeneratedFrom')}</a></div>` : ''}`;
  }

  function showJobState(job, machines, token) {
    const main = $('#job-main');
    if (!main) return;

    if (job.status === 'pending' || job.status === 'running') {
      renderProgress(main, job);
      // Poll the job status once per second until it finishes.
      setTimeout(async () => {
        if (token !== routeToken) return;
        try {
          const next = await api('GET', `/api/jobs/${job.id}`);
          if (token === routeToken) showJobState(next, machines, token);
        } catch (e) { if (token === routeToken) main.innerHTML = errorBox(e); }
      }, 1000);
      return;
    }

    if (job.status === 'failed') { renderFailed(main, job); return; }
    renderResult(main, job, machines);
  }

  function renderProgress(main, job) {
    const started = new Date(job.startedAt || job.createdAt).getTime();
    const elapsed = Math.max(0, Math.round((Date.now() - started) / 1000));
    const running = job.status === 'running';
    // Cosmetic steps – the real work is one API call; this just shows what is happening.
    const phase = !running ? 0 : elapsed < 2 ? 1 : 2;
    const steps = ['job.step.queued', 'job.step.analyze', 'job.step.routing', 'job.step.validate'];

    if (!$('#progress')) {
      main.innerHTML = `
        <div class="card progress-card" id="progress">
          <div class="spinner"></div>
          <h2 id="progress-title"></h2>
          <ol class="steps" id="progress-steps"></ol>
          <div class="muted small" id="progress-elapsed"></div>
        </div>`;
    }
    $('#progress-title').textContent = t(`job.status.${job.status}`);
    $('#progress-elapsed').textContent = t('job.elapsed', { s: elapsed });
    $('#progress-steps').innerHTML = steps.map((k, i) =>
      `<li class="${i < phase ? 'done' : i === phase ? 'active' : ''}"><span class="dot">${i < phase ? '✓' : ''}</span>${t(k)}</li>`).join('');
  }

  function renderFailed(main, job) {
    const kindText = job.errorKind && I18N.en[`err.${job.errorKind}`] ? t(`err.${job.errorKind}`) : t('err.generic');
    main.innerHTML = `
      <div class="card">
        <div class="card-title">${t('job.failedTitle')}</div>
        <div class="alert err"><div><b>${kindText}</b><div class="small" style="margin-top:4px">${esc(job.error)}</div></div></div>
        <div class="btn-row" style="margin-top:16px">
          <button class="btn primary" id="btn-regen">↻ ${t('job.regenerate')}</button>
          <a class="btn" href="#/generate">${t('job.backToGenerate')}</a>
          <a class="btn" href="#/settings">${t('nav.settings')}</a>
        </div>
      </div>
      ${debugPanel(job)}`;
    $('#btn-regen').addEventListener('click', () => regenerate(job.id));
  }

  async function regenerate(jobId) {
    try {
      const { jobId: newId } = await api('POST', `/api/jobs/${jobId}/regenerate`);
      location.hash = `#/job/${newId}`;
    } catch (e) { toast(e.message, 'err'); }
  }

  // ---------- Result editor ----------------------------------------------------------------
  function renderResult(main, job, machines) {
    const ed = {
      job,
      machines,
      ops: job.result.operations.map((o) => ({ ...o })),
      notes: job.result.general_notes || '',
      material: { grade: null, stock: null, heat_treatment: null, source: 'unknown', stock_form: null, stock_size_mm: null, stock_length_mm: null, system_material_id: null, system_material_reason: null, system_material_by: null, ...(job.result.material || {}) },
      batch: job.batchSize || 1,
      readOnly: !!job.approvedAt,
      dirty: false,
      saving: false,
      version: 0,
      timer: null,
    };
    const dis = ed.readOnly ? 'disabled' : '';

    main.innerHTML = `
      <div class="result-head">
        <h2>${t('job.draft')} <span id="badges"></span></h2>
        <div class="btn-row">
          <span class="save-state" id="save-state"></span>
          <button class="btn" id="btn-regen">↻ ${t('job.regenerate')}</button>
          <button class="btn" id="btn-export">⤓ ${t('job.export')}</button>
          <button class="btn success" id="btn-approve" ${dis}>✓ ${t('job.approve')}</button>
        </div>
      </div>
      <div id="banner"></div>
      <div class="tiles" id="tiles"></div>
      <div class="grid-3" style="margin-bottom:18px">
        <div class="card material-card">
          <div class="card-title">${t('job.material')} <span class="badge ${ed.material.source === 'drawing' ? 'ok' : ed.material.source === 'user' ? 'info' : 'warn'}">${t(`job.matSource.${ed.material.source}`)}</span></div>
          <div class="field"><label for="m-grade">${t('job.matGrade')}</label>
            <input type="text" id="m-grade" data-m="grade" value="${esc(ed.material.grade ?? '')}" ${dis}></div>
          <div class="field"><label for="m-stock">${t('job.matStock')}</label>
            <input type="text" id="m-stock" data-m="stock" value="${esc(ed.material.stock ?? '')}" ${dis}></div>
          <div class="field" style="margin-bottom:0"><label for="m-heat">${t('job.matHeat')}</label>
            <input type="text" id="m-heat" data-m="heat_treatment" value="${esc(ed.material.heat_treatment ?? '')}" placeholder="–" ${dis}></div>
          <div class="row-fields" style="margin-top:10px">
            <div class="field"><label>${t('job.matForm')}</label><select data-m="stock_form" ${dis}>${formOptions(ed.material.stock_form || 'other')}</select></div>
            <div class="field"><label>${t('job.matSize')}</label><input type="number" data-m="stock_size_mm" value="${esc(ed.material.stock_size_mm ?? '')}" ${dis}></div>
            <div class="field"><label>${t('job.matLength')}</label><input type="number" data-m="stock_length_mm" value="${esc(ed.material.stock_length_mm ?? '')}" ${dis}></div>
          </div>
          ${job.material ? `<div class="small muted">${t('job.matUser', { v: esc(job.material) })}</div>` : ''}
          <div id="mm-box"></div>
        </div>
        <div class="card">
          <div class="card-title">${t('job.notes')}</div>
          <textarea id="notes" rows="7" ${dis}>${esc(ed.notes)}</textarea>
        </div>
        <div class="card">
          <div class="card-title">${t('job.warnings')} <span id="warn-count"></span></div>
          <ul class="warn-list" id="warn-list"></ul>
        </div>
      </div>
      <div class="table-wrap">
        <table class="ops">
          <thead><tr>
            <th class="c-no">${t('job.col.no')}</th><th class="c-name">${t('job.col.name')}</th><th class="c-machine">${t('job.col.machine')}</th>
            <th class="c-desc">${t('job.col.description')}</th><th class="c-time">${t('job.col.setup')}</th><th class="c-time">${t('job.col.unit')}</th>
            <th class="c-tools">${t('job.col.tools')}</th><th class="c-conf">${t('job.col.confidence')}</th><th class="c-act"></th>
          </tr></thead>
          <tbody id="ops-body"></tbody>
        </table>
        <div class="table-foot" ${ed.readOnly ? 'hidden' : ''}>
          <button class="btn sm" id="btn-add">＋ ${t('job.addOp')}</button>
          <button class="btn sm" id="btn-renumber">1‥n ${t('job.renumber')}</button>
        </div>
      </div>
      ${debugPanel(job)}`;

    // --- rendering pieces ---
    const renderBadges = () => {
      const j = ed.job;
      $('#badges').innerHTML = [
        j.approvedAt ? `<span class="badge ok">${t('job.badgeApproved')}</span>` : `<span class="badge accent">${t('job.badgeDraft')}</span>`,
        j.edited ? `<span class="badge info">${t('job.badgeEdited')}</span>` : '',
        j.mock ? `<span class="badge warn">${t('job.badgeMock')}</span>` : `<span class="badge">${esc(j.model)}</span>`,
      ].join(' ');
      $('#banner').innerHTML = j.approvedAt
        ? `<div class="alert ok" style="margin-bottom:4px">✓ ${t('job.approvedBanner', { date: date(j.approvedAt) })}</div>`
        : `<div class="alert info" style="margin-bottom:4px">ⓘ ${t('job.aiBanner')}</div>`;
    };

    const renderTiles = () => {
      const rate = (id) => (ed.machines.find((m) => m.id === id) || {}).hourlyRate || 0;
      const setup = ed.ops.reduce((s, o) => s + (+o.setup_time_min || 0), 0);
      const unit = ed.ops.reduce((s, o) => s + (+o.unit_time_min || 0), 0);
      const batchTime = setup + unit * ed.batch;
      const cost = ed.ops.reduce((s, o) => s + ((+o.setup_time_min || 0) + (+o.unit_time_min || 0) * ed.batch) / 60 * rate(o.machine_id), 0);

      if (!$('#tile-batch-input')) {
        $('#tiles').innerHTML = `
          <div class="tile"><div class="label">${t('job.sum.ops')}</div><div class="value" id="tv-ops"></div></div>
          <div class="tile"><div class="label">${t('job.sum.setup')}</div><div class="value" id="tv-setup"></div></div>
          <div class="tile"><div class="label">${t('job.sum.unit')}</div><div class="value" id="tv-unit"></div></div>
          <div class="tile"><div class="label">${t('job.sum.batch')}</div><div class="value" id="tv-batch"></div>
            <div class="sub">${t('job.sum.batchOf', { n: `<input type="number" min="1" step="1" id="tile-batch-input" value="${ed.batch}" ${dis}>` })}</div></div>
          <div class="tile accent"><div class="label">${t('job.sum.cost')}</div><div class="value" id="tv-cost"></div><div class="sub" id="tv-cost-pc"></div></div>`;
        $('#tile-batch-input').addEventListener('input', (e) => {
          const v = parseInt(e.target.value, 10);
          if (v > 0) { ed.batch = v; renderTiles(); markDirty(); }
        });
      }
      $('#tv-ops').textContent = ed.ops.length;
      $('#tv-setup').textContent = duration(setup);
      $('#tv-unit').textContent = duration(unit);
      $('#tv-batch').textContent = duration(batchTime);
      $('#tv-cost').textContent = money(cost);
      $('#tv-cost-pc').textContent = t('job.sum.perPiece', { v: money(cost / ed.batch) });
    };

    const machineOptions = (selected) => {
      const known = ed.machines.some((m) => m.id === selected);
      return (known ? '' : `<option value="${esc(selected)}" selected>${esc(selected)} ${t('job.machineUnknown')}</option>`)
        + ed.machines.map((m) => `<option value="${esc(m.id)}" ${m.id === selected ? 'selected' : ''} ${!m.active && m.id !== selected ? 'disabled' : ''}>`
          + `${esc(m.id)} – ${esc(machineLabel(m))}${m.active ? '' : ` ${t('job.machineInactive')}`}</option>`).join('');
    };

    // Machine group of an operation: chips of interchangeable machines + "add" select (same workplace group first)
    const altsHtml = (o) => {
      const alts = o.alternative_machine_ids || [];
      const primary = ed.machines.find((m) => m.id === o.machine_id);
      const chips = alts.map((a) => `<span class="chip" title="${esc((ed.machines.find((m) => m.id === a) || {}).name || '')}">${esc(a)}${ed.readOnly ? ''
        : `<button type="button" data-act="alt-del" data-alt="${esc(a)}" title="${esc(t('job.altRemove'))}">×</button>`}</span>`).join('');
      // AI's explanation why group members were left out
      const note = o.group_note ? `<div class="group-note" title="${esc(t('job.groupNote'))}">ⓘ ${esc(o.group_note)}</div>` : '';
      if (ed.readOnly) return (alts.length ? `<div class="alts"><span class="alts-label">${t('job.alts')}:</span>${chips}</div>` : '') + note;
      const pool = ed.machines.filter((m) => m.active && m.id !== o.machine_id && !alts.includes(m.id) && !m.isVirtual);
      const same = pool.filter((m) => primary?.groupName && m.groupName === primary.groupName);
      const other = pool.filter((m) => !same.includes(m));
      const opt = (m) => `<option value="${esc(m.id)}">${esc(m.id)} – ${esc(machineLabel(m))}</option>`;
      return `<div class="alts">${alts.length ? `<span class="alts-label">${t('job.alts')}:</span>` : ''}${chips}
        <select class="alt-add"><option value="">＋ ${t('job.addAlt')}</option>
          ${same.length ? `<option value="__group__">＋＋ ${esc(t('job.addWholeGroup', { g: primary.groupName, n: same.length }))}</option>` : ''}
          ${same.length ? `<optgroup label="${esc(t('job.sameGroup', { g: primary.groupName }))}">${same.map(opt).join('')}</optgroup>` : ''}
          <optgroup label="${esc(t('job.otherMachines'))}">${other.map(opt).join('')}</optgroup>
        </select></div>${note}`;
    };

    const renderOps = () => {
      const last = ed.ops.length - 1;
      $('#ops-body').innerHTML = ed.ops.map((o, i) => `
        <tr data-idx="${i}">
          <td data-col="no"><input type="number" class="num" data-f="no" value="${esc(o.no)}" ${dis}></td>
          <td data-col="name"><textarea data-f="name" rows="3" ${dis}>${esc(o.name)}</textarea></td>
          <td data-col="machine_id"><select data-f="machine_id" ${dis}>${machineOptions(o.machine_id)}</select>${altsHtml(o)}</td>
          <td data-col="description"><textarea data-f="description" rows="3" ${dis}>${esc(o.description)}</textarea></td>
          <td data-col="setup_time_min"><input type="number" class="num" step="0.5" min="0" data-f="setup_time_min" value="${esc(o.setup_time_min)}" ${dis}></td>
          <td data-col="unit_time_min"><input type="number" class="num" step="0.1" min="0" data-f="unit_time_min" value="${esc(o.unit_time_min)}" ${dis}></td>
          <td data-col="tools"><textarea data-f="tools" rows="3" ${dis}>${esc(o.tools ?? '')}</textarea></td>
          <td data-col="confidence"><select data-f="confidence" class="conf ${esc(o.confidence)}" ${dis}>
            ${['high', 'medium', 'low'].map((c) => `<option value="${c}" ${o.confidence === c ? 'selected' : ''}>${t(`job.conf.${c}`)}</option>`).join('')}
          </select></td>
          <td class="c-act">${ed.readOnly ? '' : `
            <button class="btn icon" data-act="up" title="${t('job.moveUp')}" ${i === 0 ? 'disabled' : ''}>↑</button>
            <button class="btn icon" data-act="down" title="${t('job.moveDown')}" ${i === last ? 'disabled' : ''}>↓</button>
            <button class="btn icon danger" data-act="del" title="${t('job.deleteOp')}">✕</button>`}
          </td>
        </tr>`).join('');
      applyWarnings();
    };

    // Warnings come from the server (TechnologyValidator) and are refreshed after every save.
    const applyWarnings = () => {
      const warnings = ed.job.warnings || [];
      $$('#ops-body td').forEach((td) => { td.classList.remove('w-warn', 'w-err'); td.removeAttribute('title'); });

      const text = (w) => (I18N.en[`warn.${w.code}`] ? t(`warn.${w.code}`, w.args || {}) : w.message);
      for (const w of warnings) {
        if (w.operationNo == null || !w.field || w.severity === 'info') continue;
        ed.ops.forEach((o, i) => {
          if (o.no !== w.operationNo) return;
          const td = $(`#ops-body tr[data-idx="${i}"] td[data-col="${w.field}"]`);
          if (td) { td.classList.add(w.severity === 'error' ? 'w-err' : 'w-warn'); td.title = text(w); }
        });
      }

      const cls = { error: 'err', warning: 'warn', info: 'info' };
      $('#warn-count').innerHTML = warnings.length
        ? `<span class="badge ${warnings.some((w) => w.severity === 'error') ? 'err' : 'warn'}">${warnings.length}</span>`
        : '<span class="badge ok">✓</span>';
      $('#warn-list').innerHTML = warnings.length
        ? warnings.map((w) => `<li ${w.operationNo != null ? `data-op="${w.operationNo}"` : ''}>
              <span class="badge ${cls[w.severity] || ''}">${w.operationNo != null ? t('warn.op', { no: w.operationNo }) : '•'}</span>
              <span>${esc(text(w))}</span></li>`).join('')
        : `<li class="muted">${t('job.noWarnings')}</li>`;
    };

    const renderSaveState = () => {
      const el = $('#save-state');
      if (!el) return;
      el.className = `save-state ${ed.dirty ? 'dirty' : ''}`;
      el.textContent = ed.readOnly ? '' : ed.saving ? t('job.saving') : ed.dirty ? t('job.unsaved') : (ed.job.edited ? t('job.saved') : '');
    };

    // --- autosave (debounced) ---
    const save = async () => {
      clearTimeout(ed.timer);
      if (!ed.dirty || ed.readOnly) return;
      const version = ++ed.version;
      ed.saving = true; renderSaveState();
      try {
        const job = await api('PUT', `/api/jobs/${ed.job.id}/operations`, {
          technology: { material: ed.material, operations: ed.ops.map(normalizeOp), general_notes: ed.notes },
          batchSize: ed.batch,
        });
        ed.job = job;
        if (version === ed.version) ed.dirty = false;   // no newer edits while saving
        applyWarnings(); renderBadges(); ed.onSaved?.();
      } catch (e) { toast(e.message, 'err'); }
      ed.saving = false; renderSaveState();
    };
    const markDirty = () => {
      ed.dirty = true; ed.version++;
      renderSaveState();
      clearTimeout(ed.timer);
      ed.timer = setTimeout(save, 900);
    };
    state.flush = save;

    // --- events ---
    const body = $('#ops-body');
    body.addEventListener('input', (e) => {
      const f = e.target.dataset.f;
      if (!f) return;
      const op = ed.ops[+e.target.closest('tr').dataset.idx];
      const v = e.target.value;
      op[f] = ['no', 'setup_time_min', 'unit_time_min'].includes(f) ? (v === '' ? 0 : Number(v)) : (f === 'tools' && v.trim() === '' ? null : v);
      if (f === 'confidence') e.target.className = `conf ${v}`;
      if (f === 'machine_id') { op.alternative_machine_ids = (op.alternative_machine_ids || []).filter((a) => a !== v); renderOps(); }
      renderTiles();
      markDirty();
    });
    body.addEventListener('click', (e) => {
      const act = e.target.closest('[data-act]')?.dataset.act;
      if (!act) return;
      const i = +e.target.closest('tr').dataset.idx;
      if (act === 'up' && i > 0) [ed.ops[i - 1], ed.ops[i]] = [ed.ops[i], ed.ops[i - 1]];
      if (act === 'down' && i < ed.ops.length - 1) [ed.ops[i + 1], ed.ops[i]] = [ed.ops[i], ed.ops[i + 1]];
      if (act === 'del') ed.ops.splice(i, 1);
      if (act === 'alt-del') {
        const alt = e.target.closest('[data-alt]').dataset.alt;
        ed.ops[i].alternative_machine_ids = (ed.ops[i].alternative_machine_ids || []).filter((a) => a !== alt);
      }
      renderOps(); renderTiles(); markDirty();
    });
    // Add a machine to the operation's group (interchangeable machines)
    body.addEventListener('change', (e) => {
      if (!e.target.classList.contains('alt-add') || !e.target.value) return;
      const op = ed.ops[+e.target.closest('tr').dataset.idx];
      // "__group__" = all machines of the same PRODVISOR workplace group (STANOWISKO_NAZWA)
      const primary = ed.machines.find((m) => m.id === op.machine_id);
      const add = e.target.value === '__group__'
        ? ed.machines.filter((m) => m.active && !m.isVirtual && m.id !== op.machine_id && primary?.groupName && m.groupName === primary.groupName).map((m) => m.id)
        : [e.target.value];
      op.alternative_machine_ids = [...new Set([...(op.alternative_machine_ids || []), ...add])];
      renderOps(); markDirty();
    });
    $('#notes').addEventListener('input', (e) => { ed.notes = e.target.value; markDirty(); });
    $$('[data-m]', main).forEach((inp) => inp.addEventListener('input', () => {
      const v = inp.value.trim();
      ed.material[inp.dataset.m] = v === '' ? null : inp.type === 'number' ? Number(v) : inp.value;
      // Grade / form / size changed -> the confirmed PRODVISOR material no longer applies; server re-matches on save.
      if (['grade', 'stock_form', 'stock_size_mm'].includes(inp.dataset.m)) { ed.material.system_material_id = null; ed.material.system_material_by = null; ed.material.system_material_reason = null; }
      markDirty();
    }));
    // Material from the PRODVISOR list chosen by the technologist
    const renderMaterialMatch = () => {
      $('#mm-box').innerHTML = materialMatchHtml(ed.job.materialMatch, ed.material.system_material_id, ed.readOnly,
        { by: ed.material.system_material_by, reason: ed.material.system_material_reason });
      const sel = $('#m-system');
      if (!sel) return;
      sel.disabled = ed.readOnly;
      sel.addEventListener('change', () => {
        ed.material.system_material_id = sel.value ? Number(sel.value) : null;
        ed.material.system_material_by = 'user';   // the technologist overrides the AI choice
        renderMaterialMatch();
        markDirty();
      });
    };
    renderMaterialMatch();
    ed.onSaved = renderMaterialMatch;
    $('#warn-list').addEventListener('click', (e) => {
      const no = e.target.closest('li[data-op]')?.dataset.op;
      const idx = ed.ops.findIndex((o) => String(o.no) === no);
      const row = idx >= 0 && $(`#ops-body tr[data-idx="${idx}"]`);
      if (row) { row.scrollIntoView({ behavior: 'smooth', block: 'center' }); row.classList.remove('flash'); void row.offsetWidth; row.classList.add('flash'); }
    });
    if (!ed.readOnly) {
      $('#btn-add').addEventListener('click', () => {
        const qc = ed.machines.find((m) => m.active);
        ed.ops.push({ no: ed.ops.length + 1, name: '', machine_id: qc ? qc.id : '', description: '', setup_time_min: 0, unit_time_min: 0, tools: null, confidence: 'medium' });
        renderOps(); renderTiles(); markDirty();
        $(`#ops-body tr[data-idx="${ed.ops.length - 1}"] textarea`).focus();
      });
      $('#btn-renumber').addEventListener('click', () => {
        ed.ops.forEach((o, i) => { o.no = i + 1; });
        renderOps(); markDirty();
      });
    }
    $('#btn-regen').addEventListener('click', async () => { await save(); regenerate(ed.job.id); });
    $('#btn-export').addEventListener('click', async () => { await save(); location.href = `/api/jobs/${ed.job.id}/export`; });
    $('#btn-approve').addEventListener('click', async () => {
      if (ed.material.system_material_id !== (ed.job.result.material?.system_material_id ?? null)) ed.dirty = true;
      await save();
      try {
        const job = await api('POST', `/api/jobs/${ed.job.id}/approve`);
        toast(`✓ ${t('job.approvedToast')}`, 'ok');
        state.flush = null;
        renderResult(main, job, ed.machines);  // re-render read-only
      } catch (e) { toast(e.message, 'err'); }
    });

    renderBadges(); renderTiles(); renderOps(); renderSaveState();
  }

  /** Machine name for drop-downs; generic names that just repeat the code (e.g. "Stanowiskowy Pakiet Narzędziowy 1501") show the type instead. */
  function machineLabel(m) {
    return m.name.includes(m.id) ? m.type : m.name;
  }

  /** Coerce edited values to the schema types before saving. */
  function normalizeOp(o) {
    return {
      no: Math.round(+o.no || 0),
      name: String(o.name ?? ''),
      machine_id: String(o.machine_id ?? ''),
      description: String(o.description ?? ''),
      setup_time_min: +o.setup_time_min || 0,
      unit_time_min: +o.unit_time_min || 0,
      tools: o.tools == null || String(o.tools).trim() === '' ? null : String(o.tools),
      confidence: ['high', 'medium', 'low'].includes(o.confidence) ? o.confidence : 'medium',
      alternative_machine_ids: [...new Set((o.alternative_machine_ids || []).map(String))].filter((a) => a !== String(o.machine_id ?? '')),
      group_note: o.group_note ? String(o.group_note) : '',
    };
  }

  function debugPanel(job) {
    const json = (v) => esc(v == null ? '–' : JSON.stringify(v, null, 2));
    const u = job.usage || {};
    const tokens = u.totalTokens == null ? (job.mock ? 'n/a (mock)' : '–') : `${num(u.inputTokens, 0)} / ${num(u.outputTokens, 0)} / ${num(u.totalTokens, 0)}`;
    return `
      <details class="debug">
        <summary>${t('job.debug')}</summary>
        <div class="card">
          <div class="kv">
            <span>${t('job.debug.model')}: <b>${esc(job.model || '–')}</b>${job.mock ? ' <span class="badge warn">mock</span>' : ''}</span>
            <span>${t('job.debug.duration')}: <b>${job.durationMs != null ? `${num(job.durationMs / 1000, 1)} s` : '–'}</b></span>
            <span>${t('job.debug.tokens')}: <b>${tokens}</b></span>
            <span>${t('job.debug.lang')}: <b>${esc((job.promptLanguage || '').toUpperCase())}</b></span>
          </div>
          <div class="debug-grid">
            <div><div class="label">${t('job.debug.request')}</div><pre class="json">${json(job.debug?.request)}</pre></div>
            <div><div class="label">${t('job.debug.response')}</div><pre class="json">${json(job.debug?.response)}</pre></div>
          </div>
        </div>
      </details>`;
  }

  // =========================================================================================
  // History
  // =========================================================================================
  async function renderHistory(_, token) {
    view.innerHTML = loading();
    const jobs = await api('GET', '/api/jobs');
    if (token !== routeToken) return;

    const statusBadge = (j) => {
      const cls = { done: 'ok', failed: 'err', running: 'info', pending: '' }[j.status] || '';
      return `<span class="badge ${cls}">${t(`job.status.${j.status}`)}</span>`;
    };

    view.innerHTML = `
      <div class="page-head"><div><h1>${t('hist.title')}</h1><p>${t('hist.subtitle')}</p></div></div>
      ${jobs.length === 0
        ? `<div class="card empty">${t('hist.empty')}<div style="margin-top:14px"><a class="btn primary" href="#/generate">${t('hist.goGenerate')}</a></div></div>`
        : `<div class="table-wrap"><table class="list">
            <thead><tr>
              <th>${t('hist.col.date')}</th><th>${t('hist.col.part')}</th><th>${t('hist.col.drawing')}</th><th>${t('hist.col.model')}</th>
              <th>${t('hist.col.status')}</th><th>${t('hist.col.ops')}</th><th>${t('hist.col.duration')}</th><th>${t('hist.col.tokens')}</th><th>${t('hist.col.approved')}</th>
            </tr></thead>
            <tbody>${jobs.map((j) => `
              <tr class="clickable" data-id="${esc(j.id)}">
                <td class="nowrap">${date(j.createdAt)}</td>
                <td><b>${esc(j.partNumber || '–')}</b>${j.material ? ` <span class="muted">· ${esc(j.material)}</span>` : ''}</td>
                <td>${esc(j.drawingFileName)}</td>
                <td>${j.mock ? '<span class="badge warn">mock</span>' : esc(j.model || '–')}</td>
                <td>${statusBadge(j)}${j.edited ? ` <span class="badge info">${t('job.badgeEdited')}</span>` : ''}</td>
                <td>${j.operationCount ?? '–'}</td>
                <td class="nowrap">${j.durationMs != null ? `${num(j.durationMs / 1000, 1)} s` : '–'}</td>
                <td>${j.totalTokens != null ? num(j.totalTokens, 0) : '–'}</td>
                <td>${j.approvedAt ? `<span class="badge ok">✓ ${date(j.approvedAt)}</span>` : ''}</td>
              </tr>`).join('')}
            </tbody></table></div>`}`;

    $$('tr.clickable').forEach((tr) => tr.addEventListener('click', () => { location.hash = `#/job/${tr.dataset.id}`; }));
  }

  // =========================================================================================
  // Machines
  // =========================================================================================
  async function renderMachines(_, token) {
    view.innerHTML = loading();
    const [machines, pv] = await Promise.all([api('GET', '/api/machines'), api('GET', '/api/prodvisor/info')]);
    if (token !== routeToken) return;

    const types = [...new Set(machines.map((m) => m.type))].sort();
    const ui = { filter: 'own', q: '', onlyActive: false };

    view.innerHTML = `
      <div class="page-head">
        <div><h1>${t('mach.title')}</h1><p>${t('mach.subtitle')}</p></div>
        <div class="btn-row">
          <span class="small muted">${t('mach.source')}: <b>${esc(pv.provider)}</b></span>
          <button class="btn" id="btn-sync">⟳ ${t('mach.sync')}</button>
          <button class="btn primary" id="btn-add-machine">＋ ${t('mach.add')}</button>
        </div>
      </div>
      <div class="toolbar">
        <div class="seg" id="mach-filter">
          <button data-f="own">${t('mach.fOwn')}</button><button data-f="coop">${t('mach.fCoop')}</button><button data-f="all">${t('mach.fAll')}</button>
        </div>
        <input type="text" id="mach-q" placeholder="${esc(t('mach.search'))}" style="max-width:280px">
        <label class="toggle small"><input type="checkbox" id="mach-active"> ${t('mach.onlyActive')}</label>
        <span class="muted small" id="mach-count"></span>
      </div>
      <datalist id="machine-types">${types.map((x) => `<option value="${esc(x)}">`).join('')}</datalist>
      <div class="table-wrap"><table class="machines">
        <thead><tr>
          <th class="c-id">${t('mach.col.id')}</th><th>${t('mach.col.name')}</th><th>${t('mach.col.type')}</th><th>${t('mach.col.description')}</th>
          <th class="c-rate">${t('mach.col.rate')} [${esc(state.info.currency)}]</th><th class="c-active">${t('mach.col.active')}</th><th class="c-act"></th>
        </tr></thead>
        <tbody id="machines-body"></tbody>
      </table></div>`;

    const body = $('#machines-body');
    const byId = new Map(machines.map((m) => [m.id, m]));
    const params = (m) => (m.parameters || []).length
      ? `<div class="params">${m.parameters.map((p) => `<span class="param"><span>${esc(p.name)}</span> ${esc(p.value)}</span>`).join('')}</div>` : '';
    const rowHtml = (m, isNew) => `
      <tr data-id="${esc(m.id)}" ${isNew ? 'data-new="1"' : ''} class="${m.active ? '' : 'inactive'}">
        <td class="c-id">${isNew ? '<input type="text" data-f="id" value="" placeholder="M10">'
          : `<b class="mono">${esc(m.id)}</b>${m.prodvisorId ? `<div class="small muted">ID ${m.prodvisorId}${m.workplaceId ? ` · ${t('mach.ws')} ${m.workplaceId}` : ''}</div>` : ''}${m.groupName ? `<div class="small"><span class="badge info">${esc(m.groupName)}</span>${m.hall != null && m.hall > 0 ? ` <span class="muted">${t('mach.hall')} ${m.hall}</span>` : ''}</div>` : ''}`}</td>
        <td><input type="text" data-f="name" value="${esc(m.name)}">${m.isVirtual ? `<span class="badge accent" style="margin-top:4px">${t('mach.coop')}</span>` : ''}</td>
        <td><input type="text" data-f="type" value="${esc(m.type)}" list="machine-types"></td>
        <td><textarea data-f="description" rows="2" placeholder="${esc(t('mach.descHint'))}">${esc(m.description)}</textarea>${params(m)}</td>
        <td class="c-rate"><input type="number" class="num" min="0" step="5" data-f="hourlyRate" value="${esc(m.hourlyRate)}"></td>
        <td class="c-active"><label class="toggle"><input type="checkbox" data-f="active" ${m.active ? 'checked' : ''}></label></td>
        <td class="c-act">
          <button class="btn sm primary" data-act="save" ${isNew ? '' : 'hidden'}>${t('mach.save')}</button>
          ${isNew ? `<button class="btn sm" data-act="cancel">${t('mach.cancel')}</button>` : `<button class="btn sm danger" data-act="delete">${t('mach.delete')}</button>`}
        </td>
      </tr>`;

    const renderRows = () => {
      const q = ui.q.trim().toLowerCase();
      const list = [...byId.values()].filter((m) =>
        (ui.filter === 'all' || (ui.filter === 'coop') === !!m.isVirtual)
        && (!ui.onlyActive || m.active)
        && (!q || `${m.id} ${m.name} ${m.type}`.toLowerCase().includes(q)));
      body.innerHTML = list.map((m) => rowHtml(m, false)).join('');
      $('#mach-count').textContent = t('mach.count', { n: list.length, active: list.filter((m) => m.active).length });
      $$('#mach-filter button').forEach((b) => b.classList.toggle('active', b.dataset.f === ui.filter));
    };
    renderRows();

    $$('#mach-filter button').forEach((b) => b.addEventListener('click', () => { ui.filter = b.dataset.f; renderRows(); }));
    $('#mach-q').addEventListener('input', (e) => { ui.q = e.target.value; renderRows(); });
    $('#mach-active').addEventListener('change', (e) => { ui.onlyActive = e.target.checked; renderRows(); });

    const readRow = (tr) => ({
      id: tr.dataset.new ? $('[data-f=id]', tr).value.trim() : tr.dataset.id,
      name: $('[data-f=name]', tr).value,
      type: $('[data-f=type]', tr).value,
      description: $('[data-f=description]', tr).value,
      hourlyRate: Number($('[data-f=hourlyRate]', tr).value) || 0,
      active: $('[data-f=active]', tr).checked,
    });

    const saveRow = async (tr) => {
      const dto = readRow(tr);
      try {
        const saved = tr.dataset.new
          ? await api('POST', '/api/machines', dto)
          : await api('PUT', `/api/machines/${encodeURIComponent(dto.id)}`, dto);
        const merged = { ...(byId.get(saved.id) || {}), ...dto, id: saved.id };   // keep PRODVISOR data (parameters, IDs)
        byId.set(saved.id, merged);
        tr.outerHTML = rowHtml(merged, false);
        toast(t('mach.saved', { id: saved.id }), 'ok');
      } catch (e) { toast(e.message, 'err'); }
    };

    body.addEventListener('input', (e) => {
      const tr = e.target.closest('tr');
      if (!tr || e.target.dataset.f === 'active') return;
      tr.classList.add('dirty');
      $('[data-act=save]', tr).hidden = false;
    });
    // Toggling "active" saves immediately (demo: deactivate a machine, regenerate - it disappears from the schema enum).
    body.addEventListener('change', (e) => {
      if (e.target.dataset.f !== 'active') return;
      const tr = e.target.closest('tr');
      tr.classList.toggle('inactive', !e.target.checked);
      if (!tr.dataset.new) saveRow(tr);
    });
    body.addEventListener('click', async (e) => {
      const act = e.target.closest('[data-act]')?.dataset.act;
      if (!act) return;
      const tr = e.target.closest('tr');
      if (act === 'save') saveRow(tr);
      if (act === 'cancel') tr.remove();
      if (act === 'delete') {
        if (!confirm(t('mach.confirmDelete', { id: tr.dataset.id }))) return;
        try {
          await api('DELETE', `/api/machines/${encodeURIComponent(tr.dataset.id)}`);
          byId.delete(tr.dataset.id);
          toast(t('mach.deleted', { id: tr.dataset.id }), 'ok');
          tr.remove();
        } catch (err) { toast(err.message, 'err'); }
      }
    });
    $('#btn-add-machine').addEventListener('click', () => {
      body.insertAdjacentHTML('afterbegin', rowHtml({ id: '', name: '', type: '', description: '', hourlyRate: 100, active: true }, true));
      $('tr[data-new] [data-f=id]', body).focus();
    });
    $('#btn-sync').addEventListener('click', async (e) => {
      e.target.disabled = true;
      try {
        const r = await api('POST', '/api/machines/sync');
        toast(t('mach.synced', { src: r.source, added: r.machinesAdded, updated: r.machinesUpdated, materials: r.materials }), 'ok');
        route();
      } catch (err) { toast(err.message, 'err'); e.target.disabled = false; }
    });
  }

  // =========================================================================================
  // Materials (PRODVISOR dbo.MaterialsKind, read-only)
  // =========================================================================================
  async function renderMaterials(_, token) {
    view.innerHTML = loading();
    const materials = await api('GET', '/api/materials');
    if (token !== routeToken) return;

    view.innerHTML = `
      <div class="page-head"><div><h1>${t('mat.title')}</h1><p>${t('mat.subtitle')}</p></div></div>
      <div class="grid-2">
        <div>
          <div class="toolbar">
            <input type="text" id="mat-q" placeholder="${esc(t('mat.search'))}" style="max-width:320px">
            <span class="muted small" id="mat-count"></span>
          </div>
          <div class="table-wrap" style="max-height:70vh;overflow:auto"><table class="list">
            <thead><tr><th style="width:80px">ID</th><th>${t('mat.name')}</th><th style="width:90px">${t('mach.col.active')}</th></tr></thead>
            <tbody id="mat-body"></tbody>
          </table></div>
        </div>
        <div class="card">
          <div class="card-title">${t('mat.tryTitle')}</div>
          <p class="muted small" style="margin-top:0">${t('mat.tryHint')}</p>
          <div class="row-fields">
            <div class="field"><label>${t('job.matGrade')}</label><input type="text" id="try-grade" value="42CrMo4"></div>
            <div class="field"><label>${t('job.matForm')}</label><select id="try-form">${formOptions('round_bar')}</select></div>
            <div class="field"><label>${t('job.matSize')}</label><input type="number" id="try-size" value="60"></div>
          </div>
          <div id="try-result"></div>
        </div>
      </div>`;

    const renderList = (q) => {
      const n = (s) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/ł/g, 'l');
      const list = q ? materials.filter((m) => n(m.name).includes(n(q))) : materials;
      $('#mat-body').innerHTML = list.slice(0, 500).map((m) =>
        `<tr class="${m.active ? '' : 'inactive'}"><td class="mono">${m.id}</td><td>${esc(m.name)}</td><td>${m.active ? '✓' : `<span class="muted">${t('mat.inactive')}</span>`}</td></tr>`).join('');
      $('#mat-count').textContent = t('mat.count', { n: list.length });
    };
    renderList('');
    $('#mat-q').addEventListener('input', (e) => renderList(e.target.value));

    const tryMatch = async () => {
      const qs = new URLSearchParams({ grade: $('#try-grade').value, form: $('#try-form').value, size: $('#try-size').value || '' });
      try { $('#try-result').innerHTML = materialMatchHtml(await api('GET', `/api/materials/match?${qs}`), null, true); }
      catch (e) { $('#try-result').innerHTML = errorBox(e); }
    };
    ['#try-grade', '#try-form', '#try-size'].forEach((s) => $(s).addEventListener('input', tryMatch));
    tryMatch();
  }

  const STOCK_FORMS = ['round_bar', 'hex_bar', 'square_bar', 'flat_bar', 'plate', 'tube', 'profile', 'other'];
  function formOptions(selected) {
    return STOCK_FORMS.map((f) => `<option value="${f}" ${f === selected ? 'selected' : ''}>${t(`form.${f}`)}</option>`).join('');
  }

  /** Result of matching the drawing material against the PRODVISOR material list. */
  function materialMatchHtml(match, selectedId, readOnly, ai) {
    if (!match) return '';
    const cands = match.candidates || [];
    const selected = selectedId != null
      ? (cands.find((c) => c.id === selectedId) || (match.selected?.id === selectedId ? match.selected : null) || { id: selectedId, name: `ID ${selectedId}` })
      : null;
    const by = ai?.by;
    const status = selected
      ? `<span class="badge ok">✓ ${by === 'user' ? t('mm.byUser') : by === 'ai' ? t('mm.byAi') : t('mm.matched')}</span>`
      : {
        matched: `<span class="badge warn">${t('mm.choose')}</span>`,
        grade_only: `<span class="badge warn">${t('mm.gradeOnly')}</span>`,
        not_found: `<span class="badge err">✕ ${t('mm.notFound')}</span>`,
        no_grade: `<span class="badge">${t('mm.noGrade')}</span>`,
      }[match.status] || '';
    // Options: the chosen item (even if the rules did not propose it) + rule-based candidates
    const options = [...(selected && !cands.some((c) => c.id === selected.id) ? [selected] : []), ...cands];
    return `
      <div class="mm">
        <div class="mm-head"><span class="label">${t('mm.title')}</span> ${status}</div>
        ${selected ? `<div class="mm-selected"><b>${esc(selected.name)}</b> <span class="muted">(ID ${selected.id})</span></div>` : ''}
        ${ai?.reason ? `<div class="small" style="margin-top:4px"><span class="badge info">AI</span> ${esc(ai.reason)}</div>` : ''}
        ${!selected && match.status === 'not_found' ? `<div class="alert err" style="margin-top:8px">${t('mm.notFoundText', { grade: esc(match.grade || '') })}</div>` : ''}
        ${options.length ? `
          <select id="m-system" style="margin-top:8px">
            <option value="">${t('mm.chooseOption')}</option>
            ${options.map((c) => `<option value="${c.id}" ${selected && c.id === selected.id ? 'selected' : ''}>${esc(c.name)} (ID ${c.id})</option>`).join('')}
          </select>` : ''}
        ${cands.length ? `<details style="margin-top:6px"><summary class="small muted" style="cursor:pointer">${t('mm.rules', { n: cands.length })}${match.gradeGroup ? ` · ${esc(match.gradeGroup)}` : ''}</summary>
          <ul class="mm-list">${cands.map((c) => `<li><b>${esc(c.name)}</b> <span class="muted">– ${esc(c.reason)}</span></li>`).join('')}</ul></details>` : ''}
      </div>`;
  }

  // =========================================================================================
  // Prompts
  // =========================================================================================
  async function renderPrompts(_, token) {
    view.innerHTML = loading();
    let data = await api('GET', '/api/prompts');
    if (token !== routeToken) return;
    let editLang = data.language;

    view.innerHTML = `
      <div class="page-head"><div><h1>${t('pr.title')}</h1><p>${t('pr.subtitle')}</p></div></div>
      <div class="card">
        <div class="field" style="margin-bottom:6px">
          <span class="label">${t('pr.genLang')}</span>
          <div class="radio-row">
            <label><input type="radio" name="genlang" value="en"> English</label>
            <label><input type="radio" name="genlang" value="pl"> Polski</label>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="tabs" id="prompt-tabs">
          <button data-lang="en">${t('pr.editing')}: English</button>
          <button data-lang="pl">${t('pr.editing')}: Polski</button>
        </div>
        <div class="field"><label for="p-system">${t('pr.system')}</label><span class="hint">${t('pr.systemHint')}</span>
          <textarea id="p-system" class="prompt"></textarea></div>
        <div class="field"><label for="p-tech">${t('pr.technology')}</label><span class="hint">${t('pr.technologyHint')}</span>
          <textarea id="p-tech" class="prompt" style="min-height:190px"></textarea></div>
        <div class="btn-row">
          <button class="btn primary" id="p-save">${t('pr.save')}</button>
          <button class="btn" id="p-reset"></button>
        </div>
      </div>`;

    // Keep edits of both languages in `data` while switching tabs.
    const collect = () => {
      data.templates[editLang] = { system: $('#p-system').value, technology: $('#p-tech').value };
      data.language = $('input[name=genlang]:checked').value;
    };
    const fill = () => {
      $$('#prompt-tabs button').forEach((b) => b.classList.toggle('active', b.dataset.lang === editLang));
      $('#p-system').value = data.templates[editLang].system;
      $('#p-tech').value = data.templates[editLang].technology;
      $('#p-reset').textContent = `↺ ${t('pr.reset', { lang: editLang.toUpperCase() })}`;
      $(`input[name=genlang][value=${data.language}]`).checked = true;
    };
    fill();

    $$('#prompt-tabs button').forEach((b) => b.addEventListener('click', () => { collect(); editLang = b.dataset.lang; fill(); }));
    $('#p-save').addEventListener('click', async () => {
      collect();
      try { data = await api('PUT', '/api/prompts', data); fill(); toast(t('pr.saved'), 'ok'); refreshSettings(); }
      catch (e) { toast(e.message, 'err'); }
    });
    $('#p-reset').addEventListener('click', async () => {
      if (!confirm(t('pr.confirmReset', { lang: editLang.toUpperCase() }))) return;
      collect();
      try {
        const fresh = await api('POST', `/api/prompts/reset/${editLang}`);
        data.templates[editLang] = fresh.templates[editLang];
        fill(); toast(t('pr.resetDone'), 'ok');
      } catch (e) { toast(e.message, 'err'); }
    });
  }

  // =========================================================================================
  // Settings
  // =========================================================================================
  async function renderSettings(_, token) {
    view.innerHTML = loading();
    const s = await refreshSettings();
    if (token !== routeToken) return;

    view.innerHTML = `
      <div class="page-head"><div><h1>${t('set.title')}</h1><p>${t('set.subtitle')}</p></div></div>
      <div class="grid-2">
        <div>
          <div class="card">
            <div class="card-title">${t('set.connection')}</div>
            <div class="field">
              <label for="s-key">${t('set.apiKey')}</label>
              <input type="password" id="s-key" autocomplete="off" spellcheck="false" placeholder="${esc(t('set.apiKeyPlaceholder'))}">
              <span class="hint" id="s-key-state"></span>
              <span class="hint">🔒 ${t('set.apiKeyHint')}</span>
            </div>
            <div class="field">
              <label for="s-model">${t('set.model')}</label>
              <input type="text" id="s-model" list="model-list" value="${esc(s.model)}">
              <datalist id="model-list">${s.suggestedModels.map((m) => `<option value="${esc(m)}">`).join('')}</datalist>
              <span class="hint">${t('set.modelHint')}</span>
            </div>
            <div class="row-fields">
              <div class="field"><label for="s-org">${t('set.org')} <span class="muted">(${t('gen.optional')})</span></label>
                <input type="text" id="s-org" value="${esc(s.organization || '')}" placeholder="org-…"></div>
              <div class="field"><label for="s-project">${t('set.project')} <span class="muted">(${t('gen.optional')})</span></label>
                <input type="text" id="s-project" value="${esc(s.project || '')}" placeholder="proj_…"></div>
            </div>
            <div class="btn-row">
              <button class="btn primary" id="s-save">${t('set.save')}</button>
              <button class="btn" id="s-test">${t('set.test')}</button>
            </div>
            <div id="s-result" style="margin-top:14px"></div>
          </div>
        </div>
        <div>
          <div class="card">
            <div class="card-title">${t('set.mock')}</div>
            <label class="toggle"><input type="checkbox" id="s-mock" ${s.mockMode ? 'checked' : ''}> ${t('set.mockLabel')}</label>
            <p class="muted small" style="margin:10px 0 0">${t('set.mockHint')}</p>
            <div id="s-mock-forced" style="margin-top:12px"></div>
          </div>
          <div class="card">
            <div class="card-title">${t('set.how')}</div>
            <div class="flow">
              <span class="box">Browser</span><span class="arrow">→</span>
              <span class="box hl">PRODVISOR AI service</span><span class="arrow">→</span>
              <span class="box">OpenAI Responses API</span>
            </div>
            <ul class="how">
              <li>${t('set.how1')}</li><li>${t('set.how2')}</li><li>${t('set.how3')}</li><li>${t('set.how4')}</li><li>${t('set.how5')}</li>
            </ul>
          </div>
        </div>
      </div>`;

    const renderKeyState = (st) => {
      $('#s-key-state').innerHTML = st.hasApiKey
        ? `${t('set.apiKeySaved', { mask: `<b class="mono">${esc(st.apiKeyMasked)}</b>` })} · <button class="btn link" id="s-remove-key">${t('set.removeKey')}</button>`
        : t('set.apiKeyNone');
      $('#s-mock-forced').innerHTML = !st.hasApiKey && !st.mockMode ? `<div class="alert warn">${t('set.mockForced')}</div>` : '';
      $('#s-remove-key')?.addEventListener('click', async () => {
        const next = await api('PUT', '/api/settings', { clearApiKey: true });
        state.settings = next; renderModePill(); renderKeyState(next);
      });
    };
    renderKeyState(s);

    const save = async () => {
      const body = {
        apiKey: $('#s-key').value.trim() || null,   // empty = keep the current key
        model: $('#s-model').value.trim(),
        organization: $('#s-org').value.trim(),
        project: $('#s-project').value.trim(),
      };
      const next = await api('PUT', '/api/settings', body);
      $('#s-key').value = '';                      // the key never stays in the page
      state.settings = next; renderModePill(); renderKeyState(next);
      return next;
    };

    $('#s-save').addEventListener('click', async () => {
      try { await save(); toast(t('set.saved'), 'ok'); } catch (e) { $('#s-result').innerHTML = errorBox(e); }
    });

    $('#s-test').addEventListener('click', async () => {
      const btn = $('#s-test');
      btn.disabled = true; btn.textContent = t('set.testing');
      $('#s-result').innerHTML = '';
      try {
        await save();
        const r = await api('POST', '/api/settings/test');
        const title = r.ok ? t('set.testOk') : (I18N.en[`err.${r.kind}`] ? t(`err.${r.kind}`) : t('err.generic'));
        $('#s-result').innerHTML = `<div class="alert ${r.ok ? 'ok' : 'err'}"><div><b>${r.ok ? '✓' : '✕'} ${esc(title)}</b>
          <div class="small" style="margin-top:3px">${esc(r.ok ? t('set.testOkDetail', { model: state.settings.model }) : r.message)}</div></div></div>`;
      } catch (e) { $('#s-result').innerHTML = errorBox(e); }
      btn.disabled = false; btn.textContent = t('set.test');
    });

    $('#s-mock').addEventListener('change', async (e) => {
      try {
        const next = await api('PUT', '/api/settings', { mockMode: e.target.checked });
        state.settings = next; renderModePill(); renderKeyState(next);
      } catch (err) { toast(err.message, 'err'); }
    });
  }

  // =========================================================================================
  // Start
  // =========================================================================================
  (async () => {
    applyStaticTexts();
    try {
      state.info = await api('GET', '/api/info');
      $('#version').textContent = `v${state.info.version}`;
      await refreshSettings();
    } catch { /* shown by the page render */ }
    window.addEventListener('hashchange', route);
    route();
  })();
})();
