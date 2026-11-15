"""Package generated, reviewed module prose without rewriting it."""
import argparse,json,pathlib,sys
from importlib.util import spec_from_file_location,module_from_spec
sys.dont_write_bytecode=True

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',type=pathlib.Path);a=p.parse_args();folder=a.folder.resolve()
 spec=spec_from_file_location('generation',pathlib.Path(__file__).with_name('generate-global-modules.py'));g=module_from_spec(spec);spec.loader.exec_module(g)
 overview=g.read(folder/'overview.json');bundle=g.read(folder/'modules.json');pins=bundle['Pins'];details={d['Module']['Id']:d for d in bundle['Modules']}
 graph={'Id':'vllm-ascend-global-main-20261115','Title':'vLLM / vLLM-Ascend 全局源码学习 · main','Description':overview['Overview'],'Nodes':[],'Edges':[],'Architecture':{'Scope':overview['Scope']+'\n'+overview['Overview'],'Modules':[],'Journey':[]}}
 lines=['# '+graph['Title'],'',overview['Overview'],'','所有说明由曳尔沐生成，各模块初始上下文互相隔离；每份模块/节点说明完成两轮独立读者提问与修订。','']
 def url(r):return f"https://github.com/vllm-project/{r['Repository']}/blob/{pins[r['Repository']]}/{r['Path']}#L{r['Start']}-L{r['End']}"
 for m in overview['Modules']:
  d=details[m['Id']];evidence=g.read(folder/m['Id']/'evidence.json');g.validate_initial(d,m,evidence)
  graph['Architecture']['Modules'].append({'Id':m['Id'],'Title':m['Title'],'Repository':m['Repository'],'ParentId':m['ParentId'],'Summary':d['Module']['Summary'],'Nodes':[n['Id'] for n in d['Nodes']]})
  lines+=['## '+m['Id']+' · '+m['Title']+' · '+m['Priority'],'',d['Module']['Summary'],'']
  for n in d['Nodes']:
   refs=n['References'];r=refs[0];source=next(f for f in evidence['Files'] if f['Repository']==r['Repository'] and f['Path']==r['Path'])
   code='\n'.join(source['Text'].splitlines()[r['Start']-1:r['End']])
   if len(code)>35000:raise ValueError('Source excerpt too long: '+n['Id'])
   graph['Nodes'].append({'Id':n['Id'],'Title':n['Title'],'Tier':{'P0':'CORE','P1':'S','P2':'B'}[m['Priority']],'Summary':n['Summary'],'Approach':n['Approach'],'Code':code,'Sources':'\n'.join(url(ref) for ref in refs),'Status':'源码学习；已完成两轮独立读者审读，未实机验证','Version':' / '.join(repo+'@'+pin for repo,pin in pins.items())})
   lines+=['### '+n['Id']+' · '+n['Title'],'',n['Summary'],'',n['Approach'],'']
   lines+=['- ['+r['Repository']+'/'+r['Path']+':'+str(r['Start'])+']('+url(r)+')' for r in refs];lines+=['']
  for e in d.get('Edges',[]):graph['Edges'].append({'From':e['From'],'To':e['To'],'Label':e['Label'],'Evidence':e['Explanation']+'\n'+'\n'.join(url(r) for r in e['References'])})
  if d.get('Unknowns'):lines+=['待深入或待核实：']+['- '+x for x in d['Unknowns']]+['']
 g.dump(folder/'graph.json',graph);(folder/'module-content.md').write_text('\n'.join(lines),encoding='utf-8')
 print(json.dumps({'Modules':len(graph['Architecture']['Modules']),'Nodes':len(graph['Nodes']),'Edges':len(graph['Edges'])}))
if __name__=='__main__':main()
