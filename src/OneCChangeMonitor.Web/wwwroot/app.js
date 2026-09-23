const state = { projects: [], project: null, branch: null, commits: [], selectedSha: null };
const projectSelect = document.querySelector('#projectSelect');
const branchSelect = document.querySelector('#branchSelect');
const commitList = document.querySelector('#commitList');
const commitDetail = document.querySelector('#commitDetail');
const searchInput = document.querySelector('#searchInput');
const syncButton = document.querySelector('#syncButton');
const syncStatus = document.querySelector('#syncStatus');
const workspace = document.querySelector('#workspace');
const emptyState = document.querySelector('#emptyState');

const escapeHtml = value => String(value ?? '').replace(/[&<>"]/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;' })[char]);

async function api(path, options) {
  const response = await fetch(path, options);
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || `Ошибка HTTP ${response.status}`);
  }
  return response.json();
}

async function initialize() {
  try {
    state.projects = await api('/api/projects');
    if (!state.projects.length) return showEmpty('Нет подключённых проектов. Добавьте проект в appsettings.Local.json.');
    projectSelect.innerHTML = state.projects.map(project => `<option value="${escapeHtml(project.id)}">${escapeHtml(project.name)}</option>`).join('');
    await selectProject(state.projects[0].id);
  } catch (error) { showError(error); }
}

async function selectProject(id) {
  state.project = state.projects.find(project => project.id === id);
  const branches = await api(`/api/projects/${encodeURIComponent(id)}/branches`);
  branchSelect.innerHTML = branches.map(branch => `<option value="${escapeHtml(branch)}">${escapeHtml(branch)}</option>`).join('');
  state.branch = branches.includes(state.project.defaultBranch) ? state.project.defaultBranch : branches[0];
  branchSelect.value = state.branch;
  await loadCommits();
}

async function loadCommits() {
  commitList.innerHTML = '<div class="detail-placeholder">Загрузка истории…</div>';
  state.commits = await api(`/api/projects/${encodeURIComponent(state.project.id)}/commits?branch=${encodeURIComponent(state.branch)}&limit=60`);
  renderCommits();
  if (state.commits.length) await selectCommit(state.commits[0].sha);
}

function renderCommits() {
  const query = searchInput.value.trim().toLocaleLowerCase('ru');
  const filtered = state.commits.filter(commit => !query || `${commit.subject} ${commit.author} ${commit.shortSha}`.toLocaleLowerCase('ru').includes(query));
  commitList.innerHTML = filtered.map(commit => `<button class="commit ${commit.sha === state.selectedSha ? 'selected' : ''}" data-sha="${commit.sha}">
    <span class="commit-header"><span class="commit-title">${escapeHtml(commit.subject)}</span><span class="hash">${escapeHtml(commit.shortSha)}</span></span>
    <span class="meta">${escapeHtml(commit.author)} · ${new Date(commit.authoredAt).toLocaleString('ru-RU')}</span>
  </button>`).join('') || '<div class="detail-placeholder">Ничего не найдено</div>';
  commitList.querySelectorAll('[data-sha]').forEach(button => button.addEventListener('click', () => selectCommit(button.dataset.sha)));
}

async function selectCommit(sha) {
  state.selectedSha = sha;
  renderCommits();
  commitDetail.innerHTML = '<div class="detail-placeholder">Анализ коммита…</div>';
  try {
    const details = await api(`/api/projects/${encodeURIComponent(state.project.id)}/commits/${sha}`);
    renderDetails(details);
    const codeFile = details.files.find(file => file.oneCObject?.isCode) || details.files.find(file => !file.path.endsWith('.bin'));
    if (codeFile) await loadDiff(sha, codeFile.path);
  } catch (error) { commitDetail.innerHTML = `<div class="detail-placeholder error">${escapeHtml(error.message)}</div>`; }
}

function renderDetails(details) {
  const suspicious = details.files.some(file => file.oneCObject?.isSuspicious);
  commitDetail.innerHTML = `<div class="detail">
    <div class="detail-top"><div><h2>${escapeHtml(details.commit.subject)}</h2><div class="meta">${escapeHtml(details.commit.shortSha)} · ${escapeHtml(details.commit.author)} · ${new Date(details.commit.authoredAt).toLocaleString('ru-RU')}</div></div></div>
    ${suspicious ? '<div class="warning">Обнаружены резервные или бинарные файлы (.orig/.bin). Их изменения требуют проверки.</div>' : ''}
    <div class="section-title">Изменённые файлы и объекты</div>
    <div id="fileList">${details.files.map(file => `<button class="file" data-path="${escapeHtml(file.path)}">
      <span class="status">${escapeHtml(file.status)}</span><span><span class="file-name">${escapeHtml(file.oneCObject ? `${file.oneCObject.objectType}.${file.oneCObject.objectName}` : file.path)}</span>
      <span class="file-object">${escapeHtml(file.oneCObject?.component || file.path)}</span></span></button>`).join('')}</div>
    <div id="diffArea"></div>
  </div>`;
  commitDetail.querySelectorAll('[data-path]').forEach(button => button.addEventListener('click', () => loadDiff(details.commit.sha, button.dataset.path)));
}

async function loadDiff(sha, path) {
  const area = document.querySelector('#diffArea');
  if (!area) return;
  area.innerHTML = '<div class="detail-placeholder">Загрузка diff…</div>';
  try {
    const diff = await api(`/api/projects/${encodeURIComponent(state.project.id)}/commits/${sha}/diff?path=${encodeURIComponent(path)}`);
    const lines = diff.content.split('\n').map(line => {
      const type = line.startsWith('+') && !line.startsWith('+++') ? 'diff-add' : line.startsWith('-') && !line.startsWith('---') ? 'diff-del' : line.startsWith('@@') ? 'diff-info' : '';
      return `<span class="diff-line ${type}">${escapeHtml(line) || ' '}</span>`;
    }).join('');
    area.innerHTML = `<div class="diff-head">${escapeHtml(path)}</div><pre class="diff">${lines}</pre>`;
  } catch (error) { area.innerHTML = `<div class="detail-placeholder error">${escapeHtml(error.message)}</div>`; }
}

function showEmpty(message) { workspace.classList.add('hidden'); emptyState.classList.remove('hidden'); emptyState.textContent = message; }
function showError(error) { showEmpty(error.message || String(error)); emptyState.classList.add('error'); }

projectSelect.addEventListener('change', () => selectProject(projectSelect.value).catch(showError));
branchSelect.addEventListener('change', () => { state.branch = branchSelect.value; loadCommits().catch(showError); });
searchInput.addEventListener('input', renderCommits);
syncButton.addEventListener('click', async () => {
  syncButton.disabled = true; syncStatus.textContent = 'Синхронизация…';
  try { await api(`/api/projects/${encodeURIComponent(state.project.id)}/sync`, { method: 'POST' }); syncStatus.textContent = 'Синхронизировано'; await loadCommits(); }
  catch (error) { syncStatus.textContent = `Ошибка: ${error.message}`; }
  finally { syncButton.disabled = false; }
});

initialize();
