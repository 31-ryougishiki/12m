"""Insert reviewed model text into the conversation's module navigator."""
import argparse,base64,gzip,json,pathlib

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',type=pathlib.Path);p.add_argument('fragment',type=pathlib.Path);a=p.parse_args()
 read=lambda file:json.loads(file.read_text(encoding='utf-8-sig'))
 overview=read(a.folder/'overview.json');bundle=read(a.folder/'modules.json');details={m['Module']['Id']:m for m in bundle['Modules']};modules=[]
 order=overview['ReadingOrder']
 for m in sorted(overview['Modules'],key=lambda m:(m['Priority'],order.index(m['Id']) if m['Id'] in order else 100)):
  d=details[m['Id']]
  modules.append({'Id':m['Id'],'Title':m['Title'],'Priority':m['Priority'],'Repository':m['Repository'],'OverviewSummary':m['Summary'],'Summary':d['Module']['Summary'],'Nodes':d['Nodes'],'Edges':d.get('Edges',[]),'Unknowns':d.get('Unknowns',[])})
 raw=json.dumps({'pins':bundle['Pins'],'modules':modules,'crossRepository':overview['CrossRepository']},ensure_ascii=False,separators=(',',':')).encode('utf-8')
 packed=gzip.compress(raw,mtime=0);assert gzip.decompress(packed)==raw
 data=json.dumps({'encoding':'gzip-base64','payload':base64.b64encode(packed).decode('ascii')})
 text=a.fragment.read_text(encoding='utf-8');assert text.count('__MODULE_DATA__')==1,'Use a fresh template, preserve already displayed versions.'
 text=text.replace('__MODULE_DATA__',data);assert len(text.encode('utf-8'))<1024*1024
 a.fragment.write_text(text,encoding='utf-8');print(json.dumps({'Modules':len(modules),'Nodes':sum(len(m['Nodes']) for m in modules),'FragmentBytes':len(text.encode('utf-8')),'Prose':'verbatim'}))
if __name__=='__main__':main()
