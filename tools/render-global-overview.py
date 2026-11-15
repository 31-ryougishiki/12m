"""Render the reviewed JSON as a self-contained, searchable local reading report."""
import html, json, pathlib, sys
esc=lambda s:html.escape(str(s),quote=True)
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text(encoding='utf-8-sig'))
modules=d['Modules']; ids={m['Id']:m for m in modules}
parts=['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>'+esc(d['Title'])+'</title><style>body{font:16px/1.8 system-ui;margin:auto;max-width:1150px;padding:36px;background:#f4f6fa;color:#20314a}h1{font-size:30px}h2{margin-top:38px}article{background:white;padding:22px;margin:18px 0;border:1px solid #d7dfed;border-radius:12px}a{color:#2058a1}small{color:#58677c}p{white-space:pre-wrap}code{overflow-wrap:anywhere}input{width:95%;padding:12px;font:inherit}nav{display:flex;flex-wrap:wrap;gap:10px}nav a{background:#e4ecfa;padding:6px 10px;border-radius:8px}li{margin:6px 0}</style><h1>'+esc(d['Title'])+'</h1><p>'+esc(d['Scope'])+'</p><p>'+esc(d['Overview'])+'</p><p><b>阅读提示：</b>这是模块学习地图。模块分组与学习顺序不代表运行调用链；文件归属覆盖不等于实现行为已全部核实。每个模块保留后续核验问题。</p><input id="search" placeholder="搜索模块、主体或职责"><h2>模块导航</h2><nav>']
for m in modules: parts.append('<a href="#'+esc(m['Id'])+'">'+esc(m['Title'])+'</a>')
parts.append('</nav><h2>模块层级图</h2><p>连线含义是学习组织归属，不是调用、继承或启动顺序。</p>')
def tree(parent,repo):
    children=[m for m in modules if m.get('ParentId','')==parent and m['Repository']==repo]
    if not children:return ''
    return '<ul>'+''.join('<li><a href="#'+esc(m['Id'])+'">'+esc(m['Title'])+'</a>'+tree(m['Id'],repo)+'</li>' for m in children)+'</ul>'
for repo in sorted(set(m['Repository'] for m in modules)):
    parts.append('<details open><summary><b>'+esc(repo)+'</b></summary>'+tree('',repo)+'</details>')
if d.get('Navigation'):
    parts.append('<h2>按问题查找入口</h2>')
    if isinstance(d['Navigation'],dict):
        for title,route in d['Navigation'].items():
            parts.append('<section><h3>'+esc(title)+'</h3>')
            if isinstance(route,dict):
                parts.append('<p>'+' → '.join('<a href="#'+esc(i)+'">'+esc(ids.get(i,{}).get('Title',i))+'</a>' for i in route.get('Modules',[]))+'</p><p>'+esc(route.get('Note',''))+'</p>')
            else: parts.append('<p>'+esc(route)+'</p>')
            parts.append('</section>')
    else: parts.append('<p>'+esc(d['Navigation'])+'</p>')
parts.append('<h2>模块与展开计划</h2>')
pinfile=p.parent/'repository-pins.json'
pins=json.loads(pinfile.read_text(encoding='utf-8-sig')) if pinfile.exists() else {r['Repository']:r['Commit'] for r in json.loads((p.parent/'manifest.json').read_text(encoding='utf-8-sig'))}
for m in modules:
    parent=ids.get(m.get('ParentId'),{})
    parts.append('<article id="'+esc(m['Id'])+'"><small>'+esc(m['Repository'])+' / '+esc(parent.get('Title','根模块'))+'</small><h3>'+esc(m['Title'])+'</h3><p>'+esc(m['Summary'])+'</p><details><summary>源码入口与归属</summary><ul>')
    for e in m['EntryPoints']:
        url=f"https://github.com/vllm-project/{m['Repository']}/blob/{pins[m['Repository']]}/{e['Path']}#L{e['Line']}"
        parts.append('<li><a href="'+esc(url)+'">'+esc(e['Path'])+':'+str(e['Line'])+' '+esc(e.get('Symbol',''))+'</a></li>')
    parts.append('</ul><p>归属前缀（最长匹配优先）：'+esc(', '.join(x or '[其他未匹配文件]' for x in m['Owns']))+'</p></details><p>边界：'+esc('；'.join(m['Excludes']))+'</p><details><summary>下一步按现有流程展开</summary><ol>')
    parts.extend('<li>'+esc(q)+'</li>' for q in m['NextQuestions'])
    parts.append('</ol><p>固定版本源码核实 → 说明生成 → 独立读者提问与修订两轮 → 事实和结构校验 → 版本检查后保存。</p></details></article>')
parts.append('<h2>跨仓连接</h2><ul>')
for e in d['CrossRepository']:
    parts.append('<li>'+esc(ids[e['From']]['Title'])+' → '+esc(ids[e['To']]['Title'])+' ['+esc(e['Kind'])+']<p>'+esc(e['Explanation'])+'</p><details><summary>固定版本证据</summary><ul>')
    for source in e.get('Evidence',[]):
        url=f"https://github.com/vllm-project/{source['Repository']}/blob/{pins[source['Repository']]}/{source['Path']}#L{source['Start']}-L{source['End']}"
        parts.append('<li><a href="'+esc(url)+'">'+esc(source['Repository']+'/'+source['Path'])+':'+str(source['Start'])+'–'+str(source['End'])+'</a></li>')
    parts.append('</ul></details></li>')
parts.append('</ul><h2>建议学习顺序</h2><p>'+' → '.join(esc(ids[i]['Title']) for i in d['ReadingOrder'])+'</p><h2>尚未核实</h2><ul>')
parts.extend('<li>'+esc(x)+'</li>' for x in d['Unknowns'])
parts.append('</ul><script>document.getElementById("search").addEventListener("input",e=>{let q=e.target.value.toLowerCase();document.querySelectorAll("article").forEach(x=>x.hidden=!x.textContent.toLowerCase().includes(q))})</script></html>')
out=p.parent/'overview.html';out.write_text(''.join(parts),encoding='utf-8'); print(out.resolve())
