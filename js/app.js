const TOKEN_KEY = 'portalToken';
const statusLabels = { new: 'Новая', in_progress: 'В работе', review: 'На проверке', done: 'Готово' };
const priorityLabels = { low: 'Низкий', medium: 'Средний', high: 'Высокий' };
let profile = null;
let tab = 'profile';
const $ = (s, el = document) => el.querySelector(s);

function apiBase() {
  const base = window.PORTAL_CONFIG?.apiBase;
  if (!base) throw new Error('Не задан API: отредактируйте docs/js/config.js или переменную API_PUBLIC_URL в GitHub Actions.');
  return base.replace(/\/$/, '');
}

async function api(path, opts = {}) {
  const headers = { 'Content-Type': 'application/json', ...(opts.headers || {}) };
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) headers.Authorization = 'Bearer ' + token;
  const res = await fetch(apiBase() + path, { ...opts, headers });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
}

function showScreen(id) {
  document.querySelectorAll('.screen').forEach(s => s.classList.remove('screen--active'));
  document.querySelector(id).classList.add('screen--active');
}

function setTab(name) {
  tab = name;
  document.querySelectorAll('.nav-btn[data-tab]').forEach(b => {
    b.classList.toggle('nav-btn--active', b.dataset.tab === name);
  });
  renderMain();
}

async function login(email, password) {
  const data = await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) });
  if (data.user?.role !== 'client') {
    localStorage.removeItem(TOKEN_KEY);
    throw new Error('Этот вход только для клиентов. Используйте учётку от администратора.');
  }
  localStorage.setItem(TOKEN_KEY, data.token);
  await loadProfile();
  showScreen('#screen-app');
  document.getElementById('header-sub').textContent = profile.company.name;
  setTab('profile');
}

async function loadProfile() { profile = await api('/api/portal/profile'); }

function esc(s) {
  const d = document.createElement('div');
  d.textContent = s ?? '';
  return d.innerHTML;
}

function renderProfile() {
  const c = profile.company;
  const w = profile.assignedWorker;
  const worker = w ? `<div class="worker-card"><h3>Ваш сотрудник</h3><p><strong>${esc(w.name)}</strong><br>${esc(w.email)}</p></div>` : '<p class="hint">Сотрудник ещё не назначен.</p>';
  const rows = [
    ['Название', c.name], ['ИНН', c.inn], ['КПП', c.kpp], ['Email', c.email],
    ['Телефон', c.phone], ['Адрес', c.legalAddress], ['Налогообложение', c.taxSystem]
  ];
  const kv = rows.map(([k,v]) => `<div class="kv-row"><span>${esc(k)}</span><span>${esc(v || '—')}</span></div>`).join('');
  return worker + '<h2 class="panel-title">О предприятии</h2>' + kv;
}

async function renderTasks() {
  const tasks = await api('/api/portal/tasks');
  if (!tasks.length) return '<p class="hint">Пока нет задач.</p>';
  return tasks.map(t => `<article class="task"><h3>${esc(t.title)}</h3><p class="task-meta"><span class="badge">${statusLabels[t.status]||t.status}</span> · ${priorityLabels[t.priority]||t.priority}</p>${t.description?`<p>${esc(t.description)}</p>`:''}</article>`).join('');
}

async function renderChat() {
  const msgs = await api('/api/portal/messages');
  const me = profile.user.id;
  const list = msgs.map(m => {
    const mine = m.senderId === me;
    return `<div class="msg ${mine?'msg--mine':'msg--theirs'}">${esc(m.text)}<div class="msg-meta">${mine?'Вы':esc(m.sender?.name||'Сотрудник')} · ${new Date(m.createdAt).toLocaleString('ru-RU')}</div></div>`;
  }).join('');
  return `<div class="chat" id="chat-list">${list||'<p class="hint">Напишите сотруднику.</p>'}</div><textarea id="chat-input" placeholder="Сообщение..."></textarea><button class="btn btn--primary" id="chat-send">Отправить</button>`;
}

async function renderMain() {
  const main = document.getElementById('main-content');
  main.innerHTML = '<p class="hint">Загрузка...</p>';
  try {
    if (tab === 'profile') main.innerHTML = renderProfile();
    else if (tab === 'tasks') main.innerHTML = await renderTasks();
    else if (tab === 'chat') {
      main.innerHTML = await renderChat();
      const list = document.getElementById('chat-list');
      if (list) list.scrollTop = list.scrollHeight;
      document.getElementById('chat-send')?.addEventListener('click', sendChat);
    }
  } catch (e) {
    let msg = e.message;
    if (e.message === 'Failed to fetch') {
      msg = 'Не удалось связаться с API. Проверьте API_PUBLIC_URL и CORS (должен быть разрешён ваш github.io).';
    }
    main.innerHTML = `<p class="error">${esc(msg)}</p>`;
  }
}

async function sendChat() {
  const text = document.getElementById('chat-input')?.value?.trim();
  if (!text) return;
  await api('/api/portal/messages', { method: 'POST', body: JSON.stringify({ text }) });
  setTab('chat');
}

function buildAppScreen() {
  const el = document.createElement('section');
  el.id = 'screen-app';
  el.className = 'screen';
  el.innerHTML = `<header class="app-header"><h1>Клиенты+</h1><p id="header-sub"></p></header><main class="app-body" id="main-content"></main><nav class="bottom-nav"><button type="button" class="nav-btn nav-btn--active" data-tab="profile">Профиль</button><button type="button" class="nav-btn" data-tab="tasks">Задачи</button><button type="button" class="nav-btn" data-tab="chat">Чат</button><button type="button" class="nav-btn" id="btn-logout">Выход</button></nav>`;
  document.getElementById('app').appendChild(el);
  el.querySelectorAll('.nav-btn[data-tab]').forEach(b => b.addEventListener('click', () => setTab(b.dataset.tab)));
  el.querySelector('#btn-logout').addEventListener('click', () => { localStorage.removeItem(TOKEN_KEY); location.reload(); });
}

function fixLoginCard() {
  document.querySelector('.card--auth').innerHTML = `<div class="brand"><div class="brand__logo">+</div><div><h1 class="brand__title">Клиенты+</h1><p class="brand__sub">Личный кабинет клиента</p></div></div><label for="email">Логин</label><input id="email" type="text" autocomplete="username" value="client" /><label for="password">Пароль</label><input id="password" type="password" autocomplete="current-password" value="client" /><p class="error" id="login-error"></p><button type="button" class="btn btn--primary" id="btn-login">Войти</button><p class="hint">Демо: client / client. Доступ выдаёт администратор.</p>`;
}

async function init() {
  fixLoginCard();
  buildAppScreen();
  document.getElementById('btn-login').addEventListener('click', async () => {
    const err = document.getElementById('login-error');
    err.textContent = '';
    try { await login(document.getElementById('email').value, document.getElementById('password').value); }
    catch (e) {
      let msg = e.message;
      if (e.message === 'Failed to fetch') msg = 'API недоступен. Запустите сервер и проверьте docs/js/config.js (apiBase).';
      err.textContent = msg;
    }
  });
  if (localStorage.getItem(TOKEN_KEY)) {
    try { await loadProfile(); showScreen('#screen-app'); document.getElementById('header-sub').textContent = profile.company.name; setTab('profile'); }
    catch { localStorage.removeItem(TOKEN_KEY); }
  }
}

init();
