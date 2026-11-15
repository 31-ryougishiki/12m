"""Generate each accepted module with isolated Elyms calls and per-text reader review.

No target repository is imported/executed. Credentials remain in the PowerShell bridge.
All model calls and validations are checkpointed; rerunning resumes completed work.
"""
import argparse, concurrent.futures as cf, hashlib, io, json, pathlib, re, subprocess, tarfile, threading, time

ROOT=pathlib.Path(__file__).resolve().parent.parent
DEFAULT=ROOT/'artifacts/global-module-content/accepted-20261115'
LOCK=threading.Lock()

def read(p): return json.loads(pathlib.Path(p).read_text(encoding='utf-8-sig'))
def dump(p,x):
    p=pathlib.Path(p);p.parent.mkdir(parents=True,exist_ok=True)
    tmp=p.with_suffix(p.suffix+'.tmp');tmp.write_text(json.dumps(x,ensure_ascii=False,indent=2),encoding='utf-8');tmp.replace(p)
def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def log(text):
    with LOCK:print(time.strftime('%H:%M:%S')+' '+text,flush=True)
def bridge(input_path,output_path):
    result=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/module-model-call.ps1'),'-InputFile',str(input_path),'-OutputFile',str(output_path)],cwd=ROOT,capture_output=True,timeout=660)
    if result.returncode and not output_path.exists():raise RuntimeError(result.stderr.decode(errors='replace')[-1000:])
def model(folder,turns,validate,stage):
    accepted=folder/'accepted.json'
    if accepted.exists():
        data=read(accepted);validate(data);return data
    folder.mkdir(parents=True,exist_ok=True)
    failures=[]
    for attempt in range(1,7):
        request=folder/f'request-{attempt}.json';response=folder/f'response-{attempt}.json'
        payload={'Turns':turns,'Stage':stage,'HistoryTurns':0,'ExternalMemory':False}
        if failures:payload['Turns']=turns+[{'Role':'user','Content':'上次输出未通过结构检查，请重新完整输出并修正这些格式/引用问题。不要编造事实：'+str(failures[-1])}]
        if attempt>=5 and stage.endswith(' initial'):
            payload['Turns'][-1]['Content']+='\nJSON层级必须是根对象含Module、Nodes、Edges、Unknowns四个键。Module只含Id和Summary；Summary字符串结束后先关闭Module对象的大括号，再写根级Nodes。Nodes不能嵌在Module里。逐项检查所有大括号成对闭合。'
        if not request.exists():dump(request,payload)
        elif read(request)!=payload:raise RuntimeError('Checkpoint request changed: '+str(request))
        try:
            if not response.exists():bridge(request,response)
            result=read(response)
            if not result['Ok'] or result['Truncated'] or result['ResponseClipped']:raise ValueError('Incomplete model response: '+str(result.get('Error')))
            text=result['Text'].strip();text=re.sub(r'^```(?:json)?\s*|\s*```$','',text)
            data=json.loads(text);validate(data);dump(accepted,data);return data
        except Exception as e:
            failures.append(str(e));dump(folder/f'failure-{attempt}.json',{'Error':str(e)})
            log(stage+' retry: '+str(e)[:160])
    raise RuntimeError(stage+': '+str(failures[-1]))

EXTRA={
 'V01':['vllm/entrypoints/openai/api_server.py','vllm/entrypoints/openai/engine/serving.py'],
 'V02':['vllm/v1/engine/output_processor.py','vllm/v1/engine/__init__.py','vllm/v1/request.py'],
 'V03':['vllm/v1/core/block_pool.py','vllm/v1/core/sched/output.py','vllm/v1/request.py'],
 'V04':['vllm/v1/worker/gpu_worker.py','vllm/v1/outputs.py'],
 'V06':['vllm/distributed/kv_transfer/kv_connector/v1/mooncake_connector.py'],
 'V11':['vllm/config/vllm.py','vllm/config/parallel.py'],
 'V12':['vllm/distributed/parallel_state.py'],
 'V14':['vllm/renderers/base.py','vllm/reasoning/glm45_reasoning_parser.py'],
 'V16':['pyproject.toml','CMakeLists.txt','vllm/model_executor/models/registry.py'],
 'A01':['vllm_ascend/ascend_config.py','vllm_ascend/config_utils.py'],
 'A04':['vllm_ascend/worker/model_runner_v1.py'],
 'A05':['vllm_ascend/worker/v2/kvpp.py','vllm_ascend/worker/v2/pp_transport.py','vllm/v1/worker/gpu/model_runner.py'],
 'A06':['vllm_ascend/worker/v2/spec_decode/speculator.py'],
 'A13':['vllm_ascend/quantization/__init__.py','vllm_ascend/quantization/quant_config.py'],
 'A14':['vllm_ascend/platform.py'],
 'A15':['vllm_ascend/compilation/npugraph_ex.py'],
 'A17':['vllm_ascend/patch/__init__.py','vllm_ascend/patch/platform/__init__.py','vllm_ascend/patch/worker/__init__.py']}

def sources(pins):
    files={}
    for repo,pin in pins.items():
        raw=subprocess.check_output(['git','-C',str(pathlib.Path('D:/code/elyms-learning')/repo),'archive',pin])
        with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
            for member in archive:
                if member.isfile():
                    b=archive.extractfile(member).read()
                    if b'\x00' in b[:4096]:continue
                    try:files[(repo,member.name)]=b.decode('utf-8-sig')
                    except UnicodeDecodeError:pass
    return files

def prepare(out,overview,pins):
    files=sources(pins);modules=overview['Modules']
    for m in modules:
        folder=out/m['Id'];folder.mkdir(exist_ok=True)
        if (folder/'evidence.json').exists():continue
        repo=m['Repository'];owned=[]
        for r,p in files:
            if r!=repo:continue
            matches=[(len(prefix),m2['Id']) for m2 in modules if m2['Repository']==r for prefix in m2['Owns'] if p==prefix or p.startswith(prefix) and (prefix=='' or prefix.endswith('/'))]
            if matches and max(matches)[1]==m['Id']:owned.append(p)
        requested=[(repo,e['Path']) for e in m['EntryPoints']]
        for path in EXTRA.get(m['Id'],[]):
            r='vllm' if path.startswith('vllm/') else repo
            if (r,path) in files:requested.append((r,path))
        # Small neighboring declarations supply surrounding types and registration.
        candidates=sorted((p for p in owned if p.endswith(('.py','.cpp','.h','.cmake'))),key=lambda p:(p.count('/'),len(p),p))
        for p in candidates:
            if len(requested)>=8:break
            if (repo,p) not in requested and len(files[(repo,p)])<35000:requested.append((repo,p))
        selected=[];budget=650000
        for r,p in dict.fromkeys(requested):
            text=files[(r,p)];lines=text.splitlines()
            # Keep complete files, never silently splice methods. Huge files get an
            # explicitly bounded initial window and cannot be cited beyond it.
            limit=min(300000,budget)
            if limit<15000 and selected:continue
            count=0;end=0
            for line in lines:
                if count+len(line)+12>limit:break
                count+=len(line)+12;end+=1
            if end==0:continue
            selected.append({'Repository':r,'Path':p,'Commit':pins[r],'Start':1,'End':end,'TotalLines':len(lines),'Text':'\n'.join(lines[:end])})
            budget-=count
        evidence={'ModuleId':m['Id'],'OwnedTextFileCount':len(owned),'OwnedFileIndex':owned[:500],'IndexTruncated':len(owned)>500,'Files':selected}
        dump(folder/'evidence.json',evidence)
        body='\n\n'.join(f"FILE {f['Repository']}/{f['Path']} @ {f['Commit']} provided L1-L{f['End']} of {f['TotalLines']}\n"+'\n'.join(f'L{i}: {line}' for i,line in enumerate(f['Text'].splitlines(),1)) for f in selected)
        (folder/'evidence.txt').write_text(body,encoding='utf-8')
        dump(folder/'context-audit.json',{'ModuleId':m['Id'],'HistoryTurns':0,'OtherGeneratedModulesAttached':False,'ArchiveAttached':False,'OverviewSHA256':sha(out/'overview.json'),'EvidenceSHA256':sha(folder/'evidence.txt'),'Pins':pins})
    log('Prepared isolated source evidence for '+str(len(modules))+' modules')

def check_refs(refs,evidence):
    if not refs:raise ValueError('Missing source references')
    for ref in refs:
        found=next((f for f in evidence['Files'] if f['Repository']==ref.get('Repository') and f['Path']==ref.get('Path')),None)
        if not found or not isinstance(ref.get('Start'),int) or not isinstance(ref.get('End'),int) or not 1<=ref['Start']<=ref['End']<=found['End'] or ref['End']-ref['Start']>119:raise ValueError('Reference not in supplied source or longer than 120 lines: '+str(ref))
def validate_initial(data,m,evidence):
    if data['Module']['Id']!=m['Id'] or not data['Module']['Summary']:raise ValueError('Invalid module')
    nodes=data['Nodes'];ids=[n['Id'] for n in nodes]
    if not 1<=len(nodes)<=6 or len(set(ids))!=len(ids):raise ValueError('Invalid node count/IDs')
    for n in nodes:
        if not n['Id'].startswith(m['Id']+'-') or not all(n.get(k) for k in ('Title','Summary','Approach')):raise ValueError('Missing node content/ID prefix')
        check_refs(n['References'],evidence)
    for e in data.get('Edges',[]):
        if e['From'] not in ids or e['To'] not in ids or e['From']==e['To'] or not e['Label'] or len(e['Label'])>100:raise ValueError('Invalid edge')
        check_refs(e['References'],evidence)

def author_context(out,m,rules):
    system=(ROOT/'tools/prompts/global-module-content.txt').read_text(encoding='utf-8')+rules['Structure']
    user='已确认全局图谱：\n'+(out/'overview.json').read_text(encoding='utf-8')+'\n当前模块：\n'+json.dumps(m,ensure_ascii=False)+'\n只读源码：\n'+(out/m['Id']/'evidence.txt').read_text(encoding='utf-8')
    return [{'Role':'system','Content':system},{'Role':'user','Content':user}]

def initial(out,m,rules):
    folder=out/m['Id'];evidence=read(folder/'evidence.json')
    result=model(folder/'initial',author_context(out,m,rules),lambda d:validate_initial(d,m,evidence),m['Id']+' initial')
    log(m['Id']+' initial: '+str(len(result['Nodes']))+' nodes');return result

SOURCE_FEEDBACK={
 'V03':'覆盖检查：现在节点详细说明集中在RUNNING、块管理、抢占和输出，等待队列准入是常用主流程，不能只说在后续分支但没有节点解释。请依据Scheduler.schedule源码把WAITING请求如何选择、阻止准入的关键条件、缓存命中/外部KV等待、成功转RUNNING以及与running阶段预算的关系补入合适节点。保持5个节点可重组，不加入边缘模型分支的参数清单。',
 'A05':'事实检查：NPUModelRunner.__init__调用GPUModelRunner.__init__不能直接推论已经完成模型权重加载。核实构造、init_model_state/load_model及实际执行时机，明确仅哪些状态在构造中建立。逐项核对父类与子类方法归属、finish_execution/kvpp.complete_forward调用次序及条件。删去没有证据支持的推论。',
 'A14':'重点检查：这是学习A5/GLM常用代码，不能把Kimi、DSpark、Qwen、TurboQuant等特有布局的所有if分支参数罗列为必读主线。先说明KVCacheConfigBuilder和spec/manager注册的通用数据契约，再说明有证据的常用分支；其他模型分支简述存在并标记可选。保留P忽略recompute、D安装条件以及抢占/offload/recomputed输出的准确主体和触发条件，不丢失关键行为。'
}
def source_check(out,m,rules):
    folder=out/m['Id'];d=read(folder/'initial/accepted.json');evidence=read(folder/'evidence.json')
    turns=author_context(out,m,rules)+[{'Role':'assistant','Content':json.dumps(d,ensure_ascii=False)},{'Role':'user','Content':'源码复核反馈（请自行核实，不把问题当成事实）：'+SOURCE_FEEDBACK[m['Id']]+' 仍返回完整Module/Nodes/Edges/Unknowns JSON。'}]
    result=model(folder/'source-check',turns,lambda x:validate_initial(x,m,evidence),m['Id']+' source-check initial')
    log(m['Id']+' source-focused revision complete');return result
def candidate(out,m):
    folder=out/m['Id'];checked=folder/'source-check/accepted.json'
    return read(checked if checked.exists() else folder/'initial/accepted.json')

def validate_reader(data,visible):
    # Compare quotation text to the decoded human-readable prose, not JSON's
    # transport escaping (e.g. a literal quote versus backslash-quote).
    try:
        decoded=json.loads(visible)
        if isinstance(decoded,list) and all(isinstance(x,str) for x in decoded):visible='\n'.join(decoded)
    except (ValueError,TypeError):pass
    questions=data.get('Questions');decisions=data.get('Decisions')
    if not isinstance(questions,list) or len(questions)>6 or any(not isinstance(q,str) or not q.strip() or len(q)>1000 for q in questions) or not isinstance(decisions,list) or len(decisions)>6:raise ValueError('Invalid Questions/Decisions')
    for d in decisions:
        a=d.get('Action');t=d.get('Trigger');missing=d.get('Missing')
        if not isinstance(a,str) or not a or len(a)>200 or a not in visible or not isinstance(t,str) or len(t)>1000 or t and t not in visible:raise ValueError('Action/Trigger must quote CURRENT flow verbatim')
        if type(missing)!=bool or missing and (not d.get('Question') or d['Question'] not in questions) or not missing and not t:raise ValueError('Missing decision condition/question')

def review(out,m,item,rules,is_module=False):
    title=m['Title'] if is_module else item['Title'];ident='module' if is_module else item['Id']
    folder=out/m['Id']/'reviews'/ident
    draft={'Summary':item['Summary'],'Approach':'' if is_module else item['Approach']}
    context=author_context(out,m,rules);previous=None
    for round_no in (1,2):
        visible=json.dumps([title,draft['Summary'],draft['Approach']],ensure_ascii=False)
        focus='第一轮：优先检查关键行为触发/不触发、对象选择与结果。' if round_no==1 else '第二轮：核对上轮问题是否真正解决。上轮问题不是当前原文：'+json.dumps(previous['Questions'],ensure_ascii=False)
        reader_turns=[{'Role':'system','Content':rules['Reader']},{'Role':'user','Content':focus+'\n本轮流程原文：\n'+visible}]
        answer=model(folder/f'reader-{round_no}',reader_turns,lambda d:validate_reader(d,visible),m['Id']+'/'+ident+f' reader{round_no}')
        previous=answer
        revision=rules['Revision']+'\n当前流程标题：'+title+'。只返回 {"Summary":"完整介绍","Approach":"完整实现说明"}。'+('这是模块介绍，只改 Summary，Approach保持空字符串。' if is_module else '只修订当前节点，保留解释主体与具体行为条件。')
        author_turns=context+[{'Role':'system','Content':revision},{'Role':'assistant','Content':json.dumps(draft,ensure_ascii=False)},{'Role':'user','Content':'阅读反馈：'+json.dumps(answer,ensure_ascii=False)}]
        def validate_text(d):
            if not isinstance(d.get('Summary'),str) or not d['Summary'].strip() or len(d['Summary'])>6000 or not isinstance(d.get('Approach'),str) or len(d['Approach'])>18000 or not is_module and not d['Approach'].strip() or is_module and d['Approach']!='':raise ValueError('Invalid revised Summary/Approach')
        draft=model(folder/f'author-{round_no}',author_turns,validate_text,m['Id']+'/'+ident+f' author{round_no}')
    dump(folder/'final.json',draft);log(m['Id']+'/'+ident+' two-round review complete');return ident,draft

def assemble(out,overview):
    finals=[]
    for m in overview['Modules']:
        folder=out/m['Id'];d=candidate(out,m)
        d['Module']['Summary']=read(folder/'reviews/module/final.json')['Summary']
        for n in d['Nodes']:n.update(read(folder/'reviews'/n['Id']/'final.json'))
        validate_initial(d,m,read(folder/'evidence.json'));dump(folder/'final.json',d);finals.append(d)
    dump(out/'modules.json',{'OverviewSHA256':sha(out/'overview.json'),'Pins':read(out/'repository-pins.json'),'Modules':finals})
    count=sum(len(d['Nodes']) for d in finals)
    dump(out/'completion.json',{'Modules':len(finals),'Nodes':count,'ReaderAuthorRoundsPerText':2,'ReviewedTexts':count+len(finals),'InitialHistoryTurns':0,'OtherModuleDraftsAttached':False,'StructureValidation':'passed','RuntimeValidation':False})
    log('COMPLETE: '+str(len(finals))+' modules / '+str(count)+' nodes')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=pathlib.Path,default=DEFAULT);parser.add_argument('--workers',type=int,default=8);parser.add_argument('--prepare-only',action='store_true');args=parser.parse_args()
    out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    accepted=ROOT/'artifacts/global-overview-feedback/iteration-02-main'
    for name in ('overview.json','repository-pins.json'):
        target=out/name;source=accepted/name
        if target.exists() and target.read_bytes()!=source.read_bytes():raise RuntimeError('Accepted overview/pins changed. Use a new output directory.')
        if not target.exists():target.write_bytes(source.read_bytes())
    overview=read(out/'overview.json');pins=read(out/'repository-pins.json')
    export=out/'export-rules.json';dump(export,{'ExportRules':True})
    if not (out/'rules.json').exists():bridge(export,out/'rules.json')
    rules=read(out/'rules.json');prepare(out,overview,pins)
    if args.prepare_only:return
    modules=sorted(overview['Modules'],key=lambda m:(m['Priority'],overview['ReadingOrder'].index(m['Id']) if m['Id'] in overview['ReadingOrder'] else 100))
    errors=[]
    with cf.ThreadPoolExecutor(max_workers=args.workers) as pool:
        jobs={pool.submit(initial,out,m,rules):m for m in modules}
        for future in cf.as_completed(jobs):
            try:future.result()
            except Exception as e:errors.append(str(e));log('FAILED '+str(e))
    if errors:dump(out/'errors.json',errors);raise RuntimeError('Initial generation failures; resume after fixing: '+str(errors))
    with cf.ThreadPoolExecutor(max_workers=min(3,args.workers)) as pool:
        for future in cf.as_completed([pool.submit(source_check,out,m,rules) for m in modules if m['Id'] in SOURCE_FEEDBACK]):future.result()
    with cf.ThreadPoolExecutor(max_workers=args.workers) as pool:
        jobs=[]
        for m in modules:
            d=candidate(out,m)
            jobs.append(pool.submit(review,out,m,d['Module'],rules,True))
            jobs.extend(pool.submit(review,out,m,n,rules) for n in d['Nodes'])
        for future in cf.as_completed(jobs):
            try:future.result()
            except Exception as e:errors.append(str(e));log('FAILED '+str(e))
    if errors:dump(out/'errors.json',errors);raise RuntimeError('Review failures; resume after fixing: '+str(errors))
    assemble(out,overview)

if __name__=='__main__':main()
