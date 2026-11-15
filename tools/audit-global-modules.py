"""Verify recorded isolation, per-text review lineage, source ranges and final fields."""
import argparse,importlib.util,json,pathlib,sys
sys.dont_write_bytecode=True

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',type=pathlib.Path);a=p.parse_args();out=a.folder.resolve()
 spec=importlib.util.spec_from_file_location('generation',pathlib.Path(__file__).with_name('generate-global-modules.py'));g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
 overview=g.read(out/'overview.json');rules=g.read(out/'rules.json');errors=[];counts={'Modules':0,'Nodes':0,'ReviewedTexts':0,'ModelCalls':0,'PromptTokens':0,'CompletionTokens':0}
 for m in overview['Modules']:
  try:
   folder=out/m['Id'];audit=g.read(folder/'context-audit.json');assert audit['OverviewSHA256']==g.sha(out/'overview.json');assert audit['EvidenceSHA256']==g.sha(folder/'evidence.txt');assert audit['OtherGeneratedModulesAttached'] is False
   context=g.author_context(out,m,rules);assert g.read(folder/'initial/request-1.json')['Turns']==context
   candidate=g.candidate(out,m);final=g.read(folder/'final.json');g.validate_initial(final,m,g.read(folder/'evidence.json'))
   for node in final['Nodes']:
    owners=[]
    for r in node['References']:
     matches=[(len(prefix),other['Id']) for other in overview['Modules'] if other['Repository']==r['Repository'] for prefix in other['Owns'] if r['Path']==prefix or r['Path'].startswith(prefix) and (prefix=='' or prefix.endswith('/'))]
     owners.append(max(matches)[1] if matches else None)
    assert m['Id'] in owners,'Node lacks evidence owned by its module: '+node['Id']
   assert [n['Id'] for n in final['Nodes']]==[n['Id'] for n in candidate['Nodes']]
   assert final.get('Edges',[])==candidate.get('Edges',[]);assert final.get('Unknowns',[])==candidate.get('Unknowns',[])
   items=[('module',m['Title'],candidate['Module'],final['Module'])]+[(n['Id'],n['Title'],n,f) for n,f in zip(candidate['Nodes'],final['Nodes'])]
   for ident,title,original,saved in items:
    is_module=ident=='module';previous=None;draft={'Summary':original['Summary'],'Approach':'' if is_module else original['Approach']}
    for round_no in (1,2):
     stage=folder/'reviews'/ident;visible=json.dumps([title,draft['Summary'],draft['Approach']],ensure_ascii=False)
     focus='第一轮：优先检查关键行为触发/不触发、对象选择与结果。' if round_no==1 else '第二轮：核对上轮问题是否真正解决。上轮问题不是当前原文：'+json.dumps(previous['Questions'],ensure_ascii=False)
     r=g.read(stage/f'reader-{round_no}/request-1.json');assert r['Turns']==[{'Role':'system','Content':rules['Reader']},{'Role':'user','Content':focus+'\n本轮流程原文：\n'+visible}]
     answer=g.read(stage/f'reader-{round_no}/accepted.json');g.validate_reader(answer,visible);previous=answer
     author=g.read(stage/f'author-{round_no}/request-1.json')['Turns'];assert author[:2]==context;assert len(author)==5;assert json.loads(author[3]['Content'])==draft;assert author[4]['Content']=='阅读反馈：'+json.dumps(answer,ensure_ascii=False)
     draft=g.read(stage/f'author-{round_no}/accepted.json')
    assert saved['Summary']==draft['Summary']
    if not is_module:
     assert saved['Approach']==draft['Approach']
     assert {k:v for k,v in original.items() if k not in ('Summary','Approach')}=={k:v for k,v in saved.items() if k not in ('Summary','Approach')}
    counts['ReviewedTexts']+=1
   counts['Modules']+=1;counts['Nodes']+=len(final['Nodes'])
  except Exception as e:errors.append(m['Id']+': '+type(e).__name__+': '+str(e))
 for f in out.glob('**/response-*.json'):
  r=g.read(f);counts['ModelCalls']+=1;counts['PromptTokens']+=r.get('PromptTokens',0);counts['CompletionTokens']+=r.get('CompletionTokens',0)
 report={'Errors':errors,'Counts':counts,'Verified':'Every initial input equals approved overview + current module + source; each reader sees only its current text and own preceding questions; each author retains only this module context; final text equals second revision; non-prose source/edge fields unchanged; references inside supplied source ranges.','Limits':'Not an exhaustive semantic proof or A5 runtime test.'}
 g.dump(out/'isolation-and-content-audit.json',report);print(json.dumps({'Errors':errors,'Counts':counts},ensure_ascii=False));raise SystemExit(1 if errors else 0)
if __name__=='__main__':main()
