const $ = (s) => document.querySelector(s);
const byId = (id) => document.getElementById(id);
let activeSession = null;
let pollTimer = null;
let sourceCache = [];
const labels = { '7d':'近 7 天', '30d':'近 30 天', '90d':'近 90 天', all:'不限时间' };
const platformNames = {xiaohongshu:'小红书',youtube:'YouTube',zhihu:'知乎',douyin:'抖音',github:'GitHub / 开源',academic:'学术论文',x:'X / Twitter',web:'全网网页'};

function esc(v='') { return String(v).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
async function api(url, options={}) {
  const response = await fetch(url, {headers:{'Content-Type':'application/json', ...(options.headers||{})}, ...options});
  const body = await response.json().catch(()=>({}));
  if (!response.ok) throw new Error(body.error || `请求失败 (${response.status})`);
  return body;
}

function setChip(id, text, state) { const el=byId(id); el.classList.remove('ready','missing'); if(state)el.classList.add(state); el.querySelector('b').textContent=text; }
async function loadStatus() {
  const dot=byId('overallDot'), status=byId('overallStatus');
  try {
    const data=await api('/api/status');
    const modelName=data.ollama.models?.[0];
    setChip('ollamaChip', modelName || '未连接', modelName?'ready':'missing');
    const publicSearch=data.publicSearch?.available;
    const configured=[];
    if(data.freeSearch?.enabled)configured.push(data.freeSearch.verified?'Truegle 免费 · 已验证':'Truegle 免费 · 待查询');
    if(data.searxng.available)configured.push('SearXNG');
    if(data.searchProviders?.braveConfigured)configured.push('Brave 已配置');
    if(data.searchProviders?.tavilyConfigured)configured.push('Tavily 已配置');
    if(publicSearch)configured.push('DuckDuckGo');
    setChip('searxChip', configured.join(' / ')||'需配置搜索通道', configured.length?'ready':'missing');
    setChip('youtubeChip', data.youtubeApi.configured?'官方 API 已配置':'公开搜索页回退', data.youtubeApi.configured?'ready':'');
    dot.className='status-dot '+(modelName?'ok':'warn');
    status.textContent=modelName?`本机模型已连接 · ${modelName}`:'本机模型未连接 · 可先检索，报告将以来源清单呈现';
  } catch { dot.className='status-dot warn'; status.textContent='服务状态暂不可用'; }
}

async function loadSessions() {
  try {
    const sessions=await api('/api/sessions');
    byId('sessionList').innerHTML=sessions.length?sessions.slice(0,30).map(s=>`<button class="history-item ${s.id===activeSession?'selected':''}" data-id="${esc(s.id)}" title="${esc(s.question)}">${esc(s.question)}</button>`).join(''):'<div class="side-empty">这里会保存你的研究记录</div>';
    document.querySelectorAll('.history-item').forEach(b=>b.addEventListener('click',()=>openSession(b.dataset.id)));
  } catch {}
}
function prettyReport(text='') {
  const lines=esc(text).split(/\r?\n/); let html='';
  for(const line of lines){
    if(line.startsWith('## ')) html+=`<h2>${line.slice(3)}</h2>`;
    else if(line.trim()) {
      const body=line.replace(/\[(S\d{2,})[–-](S\d{2,})\]/g,'<a class="citation" href="#evidence-$1">[$1]</a> … <a class="citation" href="#evidence-$2">[$2]</a>').replace(/\[(S\d{2,})\]/g,'<a class="citation" href="#evidence-$1">[$1]</a>');
      html+=`<p>${body}</p>`;
    }
  }
  return html || '<p class="muted">暂无简报。</p>';
}
function showResults(session, loading=false) {
  activeSession=session.id; sourceCache=session.sources||[];
  byId('results').classList.remove('hidden');
  byId('resultSessionId').textContent=`· ${session.id.slice(0,8).toUpperCase()}`;
  byId('resultCreatedAt').textContent=session.createdAt?`· ${new Date(session.createdAt).toLocaleString('zh-CN',{hour12:false})}`:'';
  byId('resultQuestion').textContent=session.question;
  byId('angleStrip').innerHTML=(session.angles||[]).map((angle,i)=>`<span class="angle-chip"><b>0${i+1}</b>${esc(angle)}</span>`).join('');
  byId('rangeLabel').textContent=labels[session.timeRange]||'近 30 天';
  byId('report').innerHTML=loading?'<div class="report-wait"><span class="spinner"></span> 正在检索并交叉整理证据…</div>':session.error?`<p class="error-note">${esc(session.error)}</p>`:prettyReport(session.report||'');
  byId('progressLine').classList.toggle('working',loading);
  byId('progressLine').querySelector('span').style.width=loading?'65%':'100%';
  byId('showImport').disabled=loading;
  byId('sourceCount').textContent=sourceCache.length;
  const errorCount=Object.keys(session.sourceErrors||{}).length;
  byId('resultAudit').textContent=`检索返回 ${session.queryResultCount||0} 条候选 · 日期过滤 ${session.dateFilteredCount||0} 条 · URL 去重 ${session.duplicateCount||0} 条 · 低相关过滤 ${session.relevanceFilteredCount||0} 条${errorCount?` · ${errorCount} 个来源端点异常（悬停查看）`:''}`;
  byId('evidenceList').innerHTML=sourceCache.map(x=>`<article class="evidence-card" id="evidence-${esc(x.id)}"><div class="evidence-meta"><b>${esc(x.id)}</b><span>${esc(x.platform)}</span><span>${esc(x.type)}</span>${x.publishedAt?`<span>${esc(x.publishedAt.slice(0,10))}</span>`:''}${x.metric?`<span>${esc(x.metric)}</span>`:''}</div><a class="evidence-title" href="${esc(x.url)}" target="_blank" rel="noopener noreferrer">${esc(x.title||x.url)}</a><p class="evidence-snippet">${esc(x.snippet||'（来源未提供摘要）')}</p><span class="evidence-url">${esc(x.url)}</span>${x.scoreNote?`<div class="evidence-score">${esc(x.scoreNote)}</div>`:''}</article>`).join('');
  byId('emptyEvidence').classList.toggle('hidden',sourceCache.length>0);
  let filtered=byId('filteredCandidates');
  if(!filtered){filtered=document.createElement('details');filtered.id='filteredCandidates';filtered.className='result-audit';byId('evidenceList').after(filtered);}
  const candidates=session.filteredCandidates||[];
  filtered.hidden=!candidates.length;
  filtered.innerHTML=`<summary>查看 ${candidates.length} 条未纳入报告的候选</summary>`+candidates.map(x=>`<p><b>${esc(x.platform)}</b> · <a href="${esc(x.url)}" target="_blank" rel="noopener noreferrer">${esc(x.title)}</a><br><small>${esc(x.scoreNote)}</small></p>`).join('');
  const coverage=session.coverage||{};
  byId('coverageList').innerHTML=(session.platforms||[]).map(p=>{
    const name=platformNames[p]||p, text=coverage[name]||'等待检索';
    const cls=text.startsWith('有结果')||text==='已检索'||text==='已检索公开索引'?'good':(text.includes('未配置')||text.includes('无匹配')||text.includes('暂无')?'warn':'');
    const errorKeys=p==='academic'?['arXiv','Crossref']:p==='youtube'?['YouTube','YouTube API','Public search']:p==='github'?['GitHub','Public search']:p==='web'?['全网网页','Public search']:[name,'Public search'];
    const scoped=Object.keys(session.sourceErrors||{}).filter(k=>k.startsWith(name+' / '));
    const detail=[...new Set([...errorKeys,...scoped])].filter(k=>session.sourceErrors?.[k]).map(k=>`${k}: ${session.sourceErrors[k]}`).join('；');
    return `<div class="coverage-row"><span class="coverage-name">${esc(name)}</span><span class="coverage-state ${cls}" title="${esc(detail)}">${esc(text)}</span></div>`;
  }).join('');
  byId('searchAttempts').innerHTML=(session.searchAttempts||[]).map(a=>`<p><b>${esc(a.platform)} · ${esc(a.provider)}</b><br>${esc(({results:'有结果',empty:'查询成功，无匹配',blocked:'访问受限',failed:'请求失败',invalid:'返回内容不可用'})[a.state]||a.state)} · ${a.resultCount} 条<br><small>${esc(a.detail)}</small></p>`).join('')||'<p>旧记录没有逐通道日志；新调研会自动记录。</p>';
  loadSessions();
}
async function openSession(id) { try { const s=await api(`/api/sessions/${encodeURIComponent(id)}`); showResults(s,s.status==='running'||s.status==='writing'); byId('results').scrollIntoView({behavior:'smooth',block:'start'}); } catch(e){alert(e.message)} }
function startNew() { activeSession=null; byId('question').value=''; byId('questionCount').textContent='0 / 1000'; byId('results').classList.add('hidden'); byId('question').focus(); loadSessions(); window.scrollTo({top:0,behavior:'smooth'}); }

byId('question').addEventListener('input',e=>byId('questionCount').textContent=`${e.target.value.length} / 1000`);
byId('selectAll').addEventListener('click',()=>document.querySelectorAll('input[name=platform]').forEach(x=>x.checked=true));
byId('refreshStatus').addEventListener('click',loadStatus);
byId('newResearch').addEventListener('click',startNew);
byId('newFromResults').addEventListener('click',startNew);
byId('researchForm').addEventListener('submit',async e=>{
  e.preventDefault(); const question=byId('question').value.trim();
  const platforms=[...document.querySelectorAll('input[name=platform]:checked')].map(x=>x.value);
  const button=byId('runButton'); button.disabled=true;
  try {
    const s=await api('/api/research',{method:'POST',body:JSON.stringify({question,platforms,timeRange:byId('timeRange').value,maxPerSource:Number(byId('maxPerSource').value)})});
    activeSession=s.id; const session=await api(`/api/sessions/${encodeURIComponent(s.id)}`); showResults(session,true); byId('results').scrollIntoView({behavior:'smooth'});
    if(pollTimer)clearInterval(pollTimer);
    pollTimer=setInterval(async()=>{try{const latest=await api(`/api/sessions/${encodeURIComponent(s.id)}`);showResults(latest,latest.status==='running'||latest.status==='writing');if(!['running','writing'].includes(latest.status)){clearInterval(pollTimer);pollTimer=null;button.disabled=false;}}catch{clearInterval(pollTimer);button.disabled=false;}},1300);
  } catch(err) { alert(err.message); button.disabled=false; }
});

const dialog=byId('importDialog');
byId('showImport').addEventListener('click',()=>{ if(!activeSession){alert('请先开始或打开一项调研。');return;} dialog.showModal(); });
byId('closeImport').addEventListener('click',()=>dialog.close()); byId('cancelImport').addEventListener('click',()=>dialog.close());
byId('importForm').addEventListener('submit',async e=>{
  e.preventDefault(); if(!activeSession)return;
  const submit=e.submitter;submit.disabled=true;
  try {
    await api(`/api/sessions/${encodeURIComponent(activeSession)}/sources`,{method:'POST',body:JSON.stringify({title:byId('importTitle').value,url:byId('importUrl').value,snippet:byId('importSnippet').value,platform:byId('importPlatform').value})});
    dialog.close(); byId('importForm').reset();
    byId('report').innerHTML='<div class="report-wait"><span class="spinner"></span> 正在把补充来源纳入简报…</div>';
    const s=await api(`/api/sessions/${encodeURIComponent(activeSession)}/report`,{method:'POST',body:'{}'});showResults(s,false);
  } catch(err){alert(err.message)} finally{submit.disabled=false;}
});

loadStatus();loadSessions();
const linkedSession = new URLSearchParams(location.search).get('session');
if(linkedSession && /^[a-f0-9]{32}$/.test(linkedSession)) openSession(linkedSession);

const settingsDialog=byId('searchSettingsDialog');
byId('openSearchSettings').addEventListener('click',async()=>{
  try {
    const c=await api('/api/search-settings');
    byId('searchSettingsForm').reset();byId('settingSearx').value=c.searxUrl||'';
    for(const [id,ready] of [['braveConfigured',c.braveConfigured],['tavilyConfigured',c.tavilyConfigured],['youtubeConfigured',c.youtubeConfigured]])byId(id).textContent=ready?'（已配置）':'（未配置）';
    byId('settingsFeedback').textContent='';settingsDialog.showModal();
  }catch(e){alert(e.message)}
});
for(const id of ['closeSearchSettings','cancelSearchSettings'])byId(id).addEventListener('click',()=>settingsDialog.close());
byId('searchSettingsForm').addEventListener('submit',async e=>{
  e.preventDefault();const button=e.target.querySelector('button[type=submit]');button.disabled=true;
  try {
    await api('/api/search-settings',{method:'POST',body:JSON.stringify({searxUrl:byId('settingSearx').value,braveKey:byId('settingBrave').value,tavilyKey:byId('settingTavily').value,youTubeKey:byId('settingYouTube').value,clearBrave:byId('clearBrave').checked,clearTavily:byId('clearTavily').checked,clearYouTube:byId('clearYouTube').checked})});
    e.target.reset();settingsDialog.close();await loadStatus();
  }catch(e){byId('settingsFeedback').textContent=e.message}finally{button.disabled=false}
});

/* ==================== 模型一键配置（自动注入） ==================== */
(function () {
  if (window.__modelSetupMounted) return;
  window.__modelSetupMounted = true;

  var css = document.createElement('style');
  css.textContent = [
    '#msetup{position:fixed;right:16px;bottom:16px;z-index:9999;background:#153a35;color:#eaf4f1;',
    'border-radius:12px;padding:12px 14px;min-width:280px;max-width:340px;',
    'box-shadow:0 8px 28px rgba(0,0,0,.28);font:13px/1.55 system-ui,"Segoe UI",sans-serif}',
    '#msetup .ms-title{font-weight:600;margin-bottom:6px}',
    '#msetup .ms-row{display:flex;align-items:center;gap:8px;margin-bottom:8px}',
    '#msetup .ms-state{opacity:.88}',
    '#msetup button{background:#2ea88a;color:#fff;border:0;border-radius:8px;padding:7px 14px;font-size:13px;cursor:pointer}',
    '#msetup button:disabled{opacity:.55;cursor:default}',
    '#msetup .ms-bar{height:8px;border-radius:6px;background:rgba(255,255,255,.18);overflow:hidden}',
    '#msetup .ms-bar > i{display:block;height:100%;width:0;background:#4dd0aa;transition:width .25s}',
    '#msetup .ms-text{font-size:12px;opacity:.82;margin-top:6px;word-break:break-all}',
    '#msetup .ms-err{color:#ffb4a8}'
  ].join('');

  function mount() {
    if (document.getElementById('msetup')) return;
    document.head.appendChild(css);

    var box = document.createElement('div');
    box.id = 'msetup';
    box.innerHTML =
      '<div class="ms-title">本地模型</div>' +
      '<div class="ms-row"><span class="ms-state">检测中…</span></div>' +
      '<div class="ms-row"><button type="button">一键配置</button></div>' +
      '<div class="ms-prog" hidden><div class="ms-bar"><i></i></div><div class="ms-text"></div></div>';
    document.body.appendChild(box);

    var stateEl = box.querySelector('.ms-state');
    var btn = box.querySelector('button');
    var prog = box.querySelector('.ms-prog');
    var bar = box.querySelector('.ms-bar > i');
    var text = box.querySelector('.ms-text');
    var es = null;

    function pct(s) {
      if (!s.total || s.total <= 0) return null;
      return Math.max(0, Math.min(100, Math.round(s.completed * 100 / s.total)));
    }

    function mb(v) { return (v / 1048576).toFixed(0); }

    function render(s) {
      if (s.ready) {
        stateEl.textContent = '● 已就绪（' + s.model + '）';
        btn.hidden = true;
        prog.hidden = true;
        if (es) { es.close(); es = null; }
        return;
      }
      btn.hidden = false;

      if (s.running) {
        btn.disabled = true;
        btn.textContent = '配置中…';
        prog.hidden = false;
        var p = pct(s);
        bar.style.width = (p === null ? 100 : p) + '%';
        bar.style.opacity = (p === null ? 0.35 : 1);
        var label = s.status || '处理中';
        if (p !== null) label += '　' + p + '%　' + mb(s.completed) + ' / ' + mb(s.total) + ' MB';
        text.textContent = label;
        text.className = 'ms-text';
        return;
      }

      if (s.phase === 'error') {
        btn.disabled = false;
        btn.textContent = '重试';
        prog.hidden = false;
        bar.style.width = '100%';
        bar.style.opacity = 0.25;
        text.textContent = s.error || '配置失败';
        text.className = 'ms-text ms-err';
        if (es) { es.close(); es = null; }
        return;
      }

      btn.disabled = false;
      btn.textContent = '一键配置';
      prog.hidden = true;
      stateEl.textContent = s.ollamaRunning ? '● 未安装模型' : '● Ollama 未运行（将自动启动）';
    }

    function poll() {
      fetch('/api/setup/state').then(function (r) { return r.json(); }).then(render).catch(function () { });
    }

    function listen() {
      if (es) { es.close(); es = null; }
      es = new EventSource('/api/setup/progress');
      es.onmessage = function (ev) {
        try {
          var s = JSON.parse(ev.data);
          render(s);
          if (!s.running) { es.close(); es = null; }
        } catch (e) { }
      };
      es.onerror = function () { if (es) { es.close(); es = null; } };
    }

    btn.addEventListener('click', function () {
      btn.disabled = true;
      btn.textContent = '配置中…';
      prog.hidden = false;
      bar.style.width = '0%';
      bar.style.opacity = 1;
      text.textContent = '正在启动…';
      text.className = 'ms-text';
      stateEl.textContent = '● 配置中';
      fetch('/api/setup/ensure-model', { method: 'POST' })
        .then(function () { listen(); })
        .catch(function (e) {
          btn.disabled = false;
          btn.textContent = '重试';
          text.textContent = '请求失败：' + e.message;
          text.className = 'ms-text ms-err';
        });
    });

    poll();
    setInterval(function () { if (!es && !btn.disabled) poll(); }, 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount);
  else mount();
})();
