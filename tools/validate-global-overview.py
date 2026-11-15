"""Validate model-authored overview against pinned Git sources; export blind-reader projection."""
import ast, collections, html, json, pathlib, re, subprocess, sys
PINS={}

def validate(path):
    global PINS
    pins_path=path.parent/'repository-pins.json'
    if pins_path.exists(): PINS=json.loads(pins_path.read_text(encoding='utf-8-sig'))
    else: PINS={r['Repository']:r['Commit'] for r in json.loads((path.parent/'manifest.json').read_text(encoding='utf-8-sig'))}
    data=json.loads(path.read_text(encoding='utf-8-sig'))
    inv=json.loads((path.parent/'inventory.json').read_text(encoding='utf-8'))
    errors=[]; modules=data['Modules']; ids={m['Id']:m for m in modules}; prefixes={}; cache={}; coverage={}
    if len(ids)!=len(modules) or not 1<=len(modules)<=60: errors.append('Invalid/duplicate module IDs or count')
    def read(repo,p):
        if (repo,p) not in cache:
            cache[repo,p]=subprocess.check_output(['git','-C',f'D:/code/elyms-learning/{repo}','show',PINS[repo]+':'+p]).decode('utf-8-sig',errors='replace').splitlines()
        return cache[repo,p]
    for m in modules:
        repo=m['Repository']; mid=m['Id']
        if repo not in inv: errors.append(mid+': unknown repo'); continue
        ancestors={mid}; parent=m.get('ParentId')
        while parent:
            if parent not in ids or parent in ancestors: errors.append(mid+': invalid/cyclic parent'); break
            ancestors.add(parent)
            if ids[parent]['Repository']!=repo: errors.append(mid+': cross-repo parent')
            parent=ids[parent].get('ParentId')
        if len(ancestors)>12: errors.append(mid+': excessive depth')
        for prefix in m['Owns']:
            key=(repo,prefix)
            if key in prefixes: errors.append(mid+': duplicate prefix '+prefix)
            prefixes[key]=mid
            if prefix and not any(p==prefix or (prefix.endswith('/') and p.startswith(prefix)) for p in inv[repo]): errors.append(mid+': nonexistent path '+prefix)
        if not m.get('EntryPoints'): errors.append(mid+': missing entry')
        for ep in m.get('EntryPoints',[]):
            p=ep['Path']; line=ep['Line']; symbol=ep.get('Symbol','')
            if p not in inv[repo]: errors.append(mid+': nonexistent entry '+p); continue
            lines=read(repo,p)
            if not isinstance(line,int) or not 1<=line<=len(lines): errors.append(mid+': invalid line '+p); continue
            if symbol:
                if not p.endswith('.py'):
                    if not re.search(r'\b'+re.escape(symbol.split('::')[-1])+r'\s*\(',lines[line-1]):
                        errors.append(f'{mid}: native symbol not present at {p}:{line} {symbol}')
                    continue
                # Exact declaration line, allowing decorators' first line as AST does.
                try:
                    tree=ast.parse('\n'.join(lines))
                    matches=[n for n in ast.walk(tree) if isinstance(n,(ast.ClassDef,ast.FunctionDef,ast.AsyncFunctionDef)) and n.name==symbol.split('.')[-1] and n.lineno==line]
                    if not matches: errors.append(f'{mid}: declaration mismatch {p}:{line} {symbol}')
                except SyntaxError: errors.append(mid+': cannot verify declaration '+p)
    for repo,paths in inv.items():
        counts=collections.Counter(); missing=[]; fallback_groups=collections.Counter()
        for p in paths:
            matches=[(len(prefix),mid) for (r,prefix),mid in prefixes.items() if r==repo and (prefix=='' or p==prefix or (prefix.endswith('/') and p.startswith(prefix)))]
            if not matches: missing.append(p)
            else:
                size,owner=max(matches); counts[owner]+=1
                if size==0: fallback_groups['/'.join(p.split('/')[:2])]+=1
        coverage[repo]={'Total':len(paths),'Assigned':sum(counts.values()),'PerModule':dict(counts),'FallbackGroups':dict(fallback_groups),'Unassigned':missing}
        if missing: errors.append(repo+': unassigned files '+str(len(missing)))
    for edge in data.get('CrossRepository',[]):
        if edge['From'] not in ids or edge['To'] not in ids: errors.append('Unknown relation endpoint')
        for e in edge.get('Evidence',[]):
            if e['Repository'] not in inv or e['Path'] not in inv[e['Repository']]: errors.append('Missing relation evidence'); continue
            lines=read(e['Repository'],e['Path'])
            if not 1<=e['Start']<=e['End']<=len(lines): errors.append('Invalid evidence range')
    if any(i not in ids for i in data['ReadingOrder']): errors.append('Unknown reading-order ID')
    if 'FocusGroups' in data:
        groups=data['FocusGroups']
        if not 6<=len(groups)<=8: errors.append('Expected 6-8 code-learning focus groups')
        if sum(g.get('Priority')=='P0' for g in groups)>3:errors.append('Too many first-reading P0 focus groups')
        if len({g['Id'] for g in groups})!=len(groups):errors.append('Duplicate focus group ID')
        for g in groups:
            if not g.get('CodeFlow'):errors.append(g['Id']+': missing code flow')
            if any(i not in ids for i in g.get('ModuleIds',[])):errors.append(g['Id']+': unknown module reference')
        for m in modules:
            if m.get('Priority') not in ('P0','P1','P2') or not m.get('PriorityReason'):errors.append(m['Id']+': invalid priority/reason')
    report={'Errors':errors,'Coverage':coverage,'Checks':'IDs, parents, depth, path existence, longest-prefix assignment, declaration lines, relation endpoints and evidence range. Semantic correctness is separately reviewed.'}
    path.with_name(path.stem+'-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    # Blind projection: no source paths, snippets, evidence, old graph, or author history.
    blind={k:data[k] for k in ('Title','Scope','Overview','Unknowns')}
    blind['Modules']=[{k:m[k] for k in ('Id','Title','Repository','ParentId','Summary','Excludes','NextQuestions')} for m in modules]
    blind['CrossRepository']=[{k:e[k] for k in ('From','To','Kind','Explanation')} for e in data.get('CrossRepository',[])]
    blind['ReadingOrder']=data['ReadingOrder']
    if 'Navigation' in data: blind['Navigation']=data['Navigation']
    for key in ('FocusGroups','RuntimeScenario','ConfigurationFindings','PriorityPolicy'):
        if key in data: blind[key]=data[key]
    for original, projected in zip(modules,blind['Modules']):
        for key in ('Priority','PriorityReason','AppliesTo','ConfigTriggers','ActivationStatus','UserQuestions'):
            if key in original:projected[key]=original[key]
    path.with_name(path.stem+'-reader.json').write_text(json.dumps(blind,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'modules':len(modules),'errors':errors,'coverage':{r:(v['Assigned'],v['Total']) for r,v in coverage.items()}},ensure_ascii=False))
    return data,report

if __name__=='__main__':
    _,report=validate(pathlib.Path(sys.argv[1]))
    sys.exit(1 if report['Errors'] else 0)
