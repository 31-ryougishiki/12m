"""Create a pinned, reproducible repository inventory without importing target code."""
import argparse, ast, collections, hashlib, io, json, pathlib, subprocess, tarfile

OUT = pathlib.Path('artifacts/global-overview-20261115')
PINS = {}

def main():
    global OUT, PINS
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=pathlib.Path,default=OUT)
    parser.add_argument('--repos',type=pathlib.Path,default=pathlib.Path('D:/code/elyms-learning'))
    parser.add_argument('--pins-file',type=pathlib.Path)
    args=parser.parse_args();OUT=args.output
    if args.pins_file: PINS=json.loads(args.pins_file.read_text(encoding='utf-8-sig'))
    else:
        for repo in ('vllm','vllm-ascend'):
            subprocess.run(['git','-C',str(args.repos/repo),'fetch','origin','main'],check=True)
            PINS[repo]=subprocess.check_output(['git','-C',str(args.repos/repo),'rev-parse','origin/main']).decode().strip()
    if (OUT/'evidence.txt').exists():
        raise SystemExit('Evidence already exists; choose a new --output to preserve the audit trail.')
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'repository-pins.json').write_text(json.dumps(PINS,indent=2),encoding='utf-8')
    inventory, evidence, manifest = {}, [], []
    for repo, pin in PINS.items():
        raw = subprocess.check_output(['git','-C',str(args.repos/repo),'archive',pin])
        files = {}
        with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
            for member in archive:
                if member.isfile():
                    files[member.name] = archive.extractfile(member).read()
                elif member.issym():
                    files[member.name] = ('SYMLINK -> '+member.linkname).encode('utf-8')
        inventory[repo] = sorted(files)
        package = 'vllm' if repo == 'vllm' else 'vllm_ascend'
        counts=collections.Counter('/'.join(p.split('/')[:2]) if '/' in p else p for p in files)
        evidence.append(f'REPOSITORY {repo} COMMIT {pin}\nAll top-level and second-level groups with file counts:\n'+json.dumps(counts,ensure_ascii=False)+'\nComplete production file paths:\n'+'\n'.join(p for p in sorted(files) if p.startswith(package+'/')))
        groups = collections.defaultdict(list)
        for path in files:
            if path.startswith(package+'/') and path.endswith('.py'):
                groups['/'.join(path.split('/')[:3]) if len(path.split('/'))>3 else '/'.join(path.split('/')[:2])].append(path)
        # Every production subdirectory is represented. This is a declaration index,
        # not evidence that every path is reached or any pair of symbols calls another.
        for group, paths in sorted(groups.items()):
            evidence.append('GROUP '+group)
            for path in sorted(paths):
                try: tree = ast.parse(files[path].decode('utf-8-sig'))
                except (SyntaxError, UnicodeDecodeError): continue
                symbols=[]
                for n in tree.body:
                    if isinstance(n,(ast.ClassDef,ast.FunctionDef,ast.AsyncFunctionDef)):
                        label=f'L{n.lineno}: '+('class ' if isinstance(n,ast.ClassDef) else 'def ')+n.name
                        if isinstance(n,ast.ClassDef):
                            label+='('+','.join(ast.unparse(b) for b in n.bases)+')'
                            label+=' methods: '+','.join(x.name for x in n.body if isinstance(x,(ast.FunctionDef,ast.AsyncFunctionDef)))[:180]
                        symbols.append(label)
                if symbols: evidence.append(path+'\n'+'\n'.join(symbols[:4]))
        keypaths = [p for p in files if p in ('setup.py','pyproject.toml','csrc/CMakeLists.txt','csrc/torch_binding.cpp','csrc/torch_bindings.cpp',package+'/__init__.py',package+'/platform.py',package+'/platforms/__init__.py',package+'/plugins/__init__.py')]
        keypaths += [p for p in files if p.endswith(('/v1/engine/core.py','/v1/engine/async_llm.py','/v1/executor/abstract.py','/worker/worker.py','/patch/__init__.py','/core/scheduler.py'))]
        keypaths += [p for p in files if p=='examples/disaggregated_prefill_v1/load_balance_proxy_server_example.py']
        for path in keypaths:
            lines=files[path].decode('utf-8',errors='replace').splitlines()
            # Include complete registration and platform bodies; bounded excerpts
            # of large runtime files, with omitted lines explicitly marked.
            limit=1800 if 'platform' in path or path.endswith('__init__.py') or path=='setup.py' or 'load_balance_proxy' in path else 420
            selected=lines[:limit]
            evidence.append(f'FILE {repo}/{path} @ {pin}\n'+'\n'.join(f'L{i}: {s}' for i,s in enumerate(selected,1))+f'\nEND EXCERPT ({len(selected)}/{len(lines)} lines)')
            manifest.append({'Repository':repo,'Commit':pin,'Path':path,'Lines':len(lines),'ReadLines':len(selected),'SHA256':hashlib.sha256(files[path]).hexdigest()})
    (OUT/'inventory.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding='utf-8')
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (OUT/'evidence.txt').write_text('\n\n'.join(evidence),encoding='utf-8')
    print(json.dumps({'files':{k:len(v) for k,v in inventory.items()},'evidence_chars':sum(map(len,evidence))}))

if __name__=='__main__': main()
